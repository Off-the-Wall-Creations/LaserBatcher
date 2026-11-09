using System.Text.Json.Nodes;
using System.Xml.Linq;
using LaserBatcher.Services;
using Xunit;

namespace LaserBatcher.Tests;

public class IgnoreTests
{
    private static readonly XNamespace S = SvgBatchService.Svg;
    private static SvgAnalysis Source(string body) => SvgBatchService.Analyze($"<svg xmlns='{S}' width='1in' height='1in' viewBox='0 0 96 96'>{body}</svg>");
    private static BatchSettings Settings(OrderingMode order = OrderingMode.WithinEachCopy) => new(4, 3, 0, 0, .1, 2, order);
    private static BatchResult Build(SvgAnalysis source, Dictionary<string, SvgAssignment> assignments, OrderingMode order = OrderingMode.WithinEachCopy) => SvgBatchService.Build(source, "Part_x3.svg", new("Part", null, null, 3), Settings(order), assignments);
    private static string Key(SvgAnalysis source, string id) => source.Items.Single(i => i.Id == id).Key;
    private static JsonObject Metadata(BatchResult output) => JsonNode.Parse(output.Document.Root!.Element(S + "metadata")!.Element(SvgMetadataService.Namespace + "batch")!.Value)!.AsObject();

    [Theory]
    [InlineData(OrderingMode.WithinEachCopy)]
    [InlineData(OrderingMode.AcrossSheet)]
    public void IgnoreInheritsAndChildOverridesPreserveHiddenContent(OrderingMode mode)
    {
        var source = Source("<metadata><license xmlns='urn:custom'>Keep me</license></metadata><g id='parent'><path id='omit' d='M1 1h10'/><g id='nested'><path id='keep' d='M2 2h10'/></g><g style='display:none' id='hidden'><path d='M3 3h10'/></g></g>");
        var original = source.Document.ToString();
        var map = new Dictionary<string, SvgAssignment> { [Key(source, "parent")] = SvgAssignment.Ignore, [Key(source, "keep")] = SvgAssignment.Score };
        var resolved = SvgBatchService.Resolve(source, map);
        Assert.Equal(SvgAssignment.Ignore, resolved[Key(source, "omit")]);
        Assert.Equal(SvgAssignment.Score, resolved[Key(source, "keep")]);
        var result = Build(source, map, mode);
        Assert.DoesNotContain(result.Document.Descendants(), e => (string?)e.Attribute("id") == "omit");
        Assert.Equal(2, SvgBatchService.Analyze(result.Document.ToString()).Items.Count(i => i.Production));
        Assert.Equal(2, result.Document.Descendants().Count(e => e.Name.LocalName == "license"));
        Assert.Equal(2, result.Document.Descendants().Count(e => ((string?)e.Attribute("id"))?.StartsWith("hidden") == true));
        var metadata = Metadata(result);
        Assert.Equal(6, metadata["totalYield"]!.GetValue<int>());
        Assert.Equal(Key(source, "parent"), metadata["ignoredElements"]![0]!["assignedByKey"]!.GetValue<string>());
        Assert.DoesNotContain("Ignore", metadata["operationMappings"]!.ToJsonString());
        Assert.Equal(original, source.Document.ToString());
        Assert.Equal(SvgBatchService.Capacity(source, Settings()), SvgBatchService.Capacity(source, Settings(mode)));
        map.Remove(Key(source, "parent"));
        Assert.Null(SvgBatchService.Resolve(source, map)[Key(source, "omit")]);
    }

    [Fact]
    public void RootIgnoreRequiresAnIncludedChild()
    {
        var source = Source("<path id='Score' d='M1 1h10'/>");
        var map = new Dictionary<string, SvgAssignment> { ["0"] = SvgAssignment.Ignore };
        Assert.Contains("Include at least one", Assert.Throws<FormatException>(() => Build(source, map)).Message);
        map[Key(source, "Score")] = SvgAssignment.Score;
        Assert.Null(Metadata(Build(source, map))["ignoredElements"]);
    }

    [Theory]
    [InlineData(OrderingMode.WithinEachCopy)]
    [InlineData(OrderingMode.AcrossSheet)]
    public void IncludedClonesKeepOriginalTemplatesAndValidReferences(OrderingMode mode)
    {
        var source = Source("<g id='original' fill='none' stroke='red'><path id='a' d='M1 1h10'/><path id='b' d='M2 2h10'/></g><use id='clone1' href='#original' x='30'/><use id='clone2' href='#a' x='40'/>");
        var map = new Dictionary<string, SvgAssignment> { ["0"] = SvgAssignment.Score, [Key(source, "original")] = SvgAssignment.Ignore, [Key(source, "b")] = SvgAssignment.Engrave };
        var result = Build(source, map, mode);
        var analyzed = SvgBatchService.Analyze(result.Document.ToString());
        Assert.Empty(analyzed.Errors);
        Assert.Equal(6, analyzed.Items.Count(i => i.Production)); // b plus two clones per copy
        var ids = result.Document.Descendants().Attributes("id").Select(a => a.Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        foreach (var use in result.Document.Descendants(S + "use"))
        {
            var target = result.Document.Descendants().Single(e => (string?)e.Attribute("id") == use.Attribute("href")!.Value[1..]);
            Assert.Contains(target.Ancestors(), e => e.Name == S + "defs");
        }
        Assert.Equal(4, result.Document.Descendants(S + "defs").Descendants(S + "path").Count(e => (string?)e.Attribute("d") == "M1 1h10"));
    }

    [Fact]
    public void MetadataEditingPreservesOmissionsButRebatchDoesNotReplayKeys()
    {
        var source = Source("<path id='a' d='M1 1h10'/><path id='b' d='M2 2h10'/>");
        var first = Build(source, new() { ["0"] = SvgAssignment.Score, [Key(source, "a")] = SvgAssignment.Ignore });
        var edited = SvgMetadataService.Update(first.Document, new("Edited", null, null, 6), 90061);
        Assert.Equal(Metadata(first)["ignoredElements"]!.ToJsonString(), Metadata(edited)["ignoredElements"]!.ToJsonString());
        var imported = SvgBatchService.Analyze(edited.Document.ToString());
        var second = SvgBatchService.Build(imported, edited.Filename, imported.Metadata!.Program, new(12, 12, 0, 0, 0, 1, OrderingMode.AcrossSheet), new Dictionary<string, SvgAssignment>());
        Assert.Null(Metadata(second)["ignoredElements"]);
        Assert.Equal(2, SvgBatchService.Analyze(second.Document.ToString()).Items.Count(i => i.Production));
    }

    [Fact]
    public void GlowforgeDropsIgnoredStepsAndRetainsSettings()
    {
        var source = SvgBatchService.Analyze(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "glowforge-x1.svg")));
        var map = new Dictionary<string, SvgAssignment> { ["0"] = SvgAssignment.Ignore };
        var included = source.Items.First(i => i.Production);
        map[included.Key] = SvgAssignment.Score;
        var result = SvgBatchService.Build(source, "Part.svg", new("Part", null, null, 1), new(100, 100, 0, 0, 0, 1, OrderingMode.WithinEachCopy), map);
        var definitions = result.Document.Descendants(GlowforgeSettings.Namespace + "laserdef").ToArray();
        Assert.Single(definitions);
        var original = source.Glowforge.Steps.Single(s => s.ArtworkKeys.Contains(included.Key));
        foreach (var attr in original.Definition.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName is not ("class" or "order")))
            Assert.Equal(attr.Value, (string?)definitions[0].Attribute(attr.Name));
        Assert.Single(Metadata(result)["operationMappings"]!.AsArray());
    }

    [Fact]
    public void IndividualIgnoreCanBeRemovedWithoutChangingSourceInheritance()
    {
        var source = Source("<defs><linearGradient id='paint'><stop stop-color='red'/></linearGradient><marker id='unrelated'/></defs><g id='Score'><path id='a' fill='url(#paint)' d='M1 1h10'/><path id='b' fill='url(#paint)' d='M2 2h10'/></g>");
        var map = new Dictionary<string, SvgAssignment> { [Key(source, "a")] = SvgAssignment.Ignore };
        var output = Build(source, map);
        Assert.Equal(2, output.Document.Descendants(S + "linearGradient").Count());
        Assert.Equal(2, output.Document.Descendants(S + "marker").Count());
        Assert.Empty(SvgBatchService.Analyze(output.Document.ToString()).Errors);
        Assert.Equal(Key(source, "a"), Metadata(output)["ignoredElements"]![0]!["assignedByKey"]!.GetValue<string>());
        map.Clear();
        Assert.Equal(SvgAssignment.Score, SvgBatchService.Resolve(source, map)[Key(source, "a")]);
        Assert.Equal(4, SvgBatchService.Analyze(Build(source, map).Document.ToString()).Items.Count(i => i.Production));
    }

    [Theory]
    [InlineData("style='transform-box:fill-box;transform-origin:50% 50%'", "")]
    [InlineData("clip-path='url(#clip)'", "<defs><clipPath id='clip' clipPathUnits='objectBoundingBox'><rect width='1' height='1'/></clipPath></defs>")]
    public void BoundsDependentEffectsRequireSourceCleanup(string effect, string definitions)
    {
        var source = Source(definitions + $"<g id='Score' {effect}><path id='omit' d='M1 1h10'/><path d='M20 1h10'/></g>");
        var ex = Assert.Throws<FormatException>(() => Build(source, new() { [Key(source, "omit")] = SvgAssignment.Ignore }));
        Assert.Contains("Restore that artwork or resolve the effect", ex.Message);
    }

    [Fact]
    public void ClonesFromAnIgnoredNestedViewportRequireFlattening()
    {
        var source = Source("<svg width='40' height='40' viewBox='0 0 20 20'><path id='target' d='M1 1h10'/></svg><use id='clone' href='#target'/>");
        var ex = Assert.Throws<FormatException>(() => Build(source, new() { ["0"] = SvgAssignment.Score, [Key(source, "target")] = SvgAssignment.Ignore }));
        Assert.Contains("Flatten that viewport", ex.Message);
    }

    [Fact]
    public void IgnoredTextRetainsItsHiddenChildrenAndLicenseWithoutVisibleText()
    {
        var source = Source("<g transform='translate(2,3)' font-size='12'><text id='omit'>IGNOREME<tspan id='hidden' display='none'>Keep hidden text</tspan><metadata><license xmlns='urn:license'>Keep license</license></metadata></text></g><path id='Score' d='M1 1h10'/>");
        var output = Build(source, new() { [Key(source, "omit")] = SvgAssignment.Ignore });
        var xml = output.Document.ToString();
        Assert.DoesNotContain("IGNOREME", xml);
        Assert.Contains("Keep hidden text", xml);
        Assert.Contains("Keep license", xml);
        var hidden = output.Document.Descendants(S + "tspan").ToArray();
        Assert.Equal(2, hidden.Length);
        Assert.All(hidden, e => Assert.Contains(e.Ancestors(), a => a.Name == S + "defs"));
        var inventory = Metadata(output)["hiddenLayers"]!.AsArray().Where(n => n!["sourceId"]?.GetValue<string>() == "hidden");
        Assert.All(inventory, entry => Assert.NotEmpty(entry!["outputIds"]!.AsArray()));
        var imported = SvgBatchService.Analyze(xml);
        Assert.Empty(imported.Errors);
        Assert.Equal(2, imported.Items.Count(i => i.Production));
    }
}
