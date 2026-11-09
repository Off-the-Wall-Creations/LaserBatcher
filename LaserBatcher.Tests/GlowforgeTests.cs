using System.Text.Json.Nodes;
using System.Xml.Linq;
using LaserBatcher.Services;
using Xunit;

namespace LaserBatcher.Tests;

public class GlowforgeTests
{
    private static readonly XNamespace S = SvgBatchService.Svg;
    private static readonly XNamespace G = GlowforgeSettings.Namespace;
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".svg"));
    private static Dictionary<string, SvgAssignment> Map(SvgAnalysis analysis) => analysis.Items.Where(i => i.Production).ToDictionary(i => i.Key,
        i => i.Suggestion == "Score" ? SvgAssignment.Score : i.Id is "one" or "three" or "relief" or "slot" or "hinge" or "openings" ? SvgAssignment.InteriorCut : SvgAssignment.ExteriorCut);
    private static BatchResult Build(SvgAnalysis a, OrderingMode mode = OrderingMode.WithinEachCopy, int copies = 2, Dictionary<string, SvgAssignment>? mappings = null) =>
        SvgBatchService.Build(a, "Fixture_x1.svg", new("Fixture", null, null, 1), new(50, 50, .2, .2, .1, copies, mode), mappings ?? Map(a));

    [Theory]
    [InlineData("master-face")][InlineData("master-hook")][InlineData("master-shelf")][InlineData("master-back")]
    public void MillimeterMastersPreserveGeometryLicenseNamespacesAndWatermarks(string name)
    {
        var xml = Fixture(name);
        var a = SvgBatchService.Analyze(xml);
        Assert.Empty(a.Errors);
        var result = Build(a);
        var parsed = SvgMetadataService.Parse(SvgSerializer.Serialize(result.Document));
        Assert.Equal(a.SourceDocument.ToString(), SvgMetadataService.Parse(xml).ToString());
        foreach (var path in a.SourceDocument.Descendants(S + "path"))
            Assert.Equal(2, parsed.Descendants(S + "path").Count(e => (string?)e.Attribute("d") == (string?)path.Attribute("d")));
        var license = a.SourceDocument.Descendants().Single(e => e.Name.LocalName == "license");
        Assert.Equal(2, parsed.Descendants(license.Name).Count(e => XNode.DeepEquals(e, license)));
        var watermark = a.SourceDocument.Descendants().Single(e => (string?)e.Attribute("id") == "license-watermark");
        Assert.Equal(2, parsed.Descendants(watermark.Name).Count(e => ((string?)e.Attribute("id"))?.StartsWith("license-watermark") == true && (string?)e.Attribute("style") == "display:none"));
        Assert.Contains(parsed.Descendants(S + "g"), e => (string?)e.Attribute("data-laserbatcher-source-width") == (string?)a.SourceDocument.Root!.Attribute("width"));
        Assert.Empty(SvgMetadataService.Read(parsed).Warnings);
    }

    [Fact]
    public void RecognizesScoreWithoutConvertingRawSpeedsAndHidesOnlyReport()
    {
        var a = SvgBatchService.Analyze(Fixture("glowforge-x1"));
        Assert.Empty(a.Errors);
        Assert.Equal(2, a.Items.Count(i => i.Production));
        var score = a.Glowforge.Steps.Single(s => s.Class == "score-step");
        Assert.Equal("cut", score.RawAttributes["type"]);
        Assert.Equal("2903.125", score.RawAttributes["speed"]);
        Assert.Equal("Score", score.Suggestion);
        Assert.Contains(score.ReportLines, l => l.EndsWith("Speed=400"));
        Assert.Single(a.Document.Descendants(S + "text").Where(e => e.Attribute("data-laserbatcher-settings-report") != null));
        Assert.Null(a.SourceDocument.Descendants(S + "text").Single().Attribute("style"));
        var b = SvgBatchService.Analyze(Fixture("glowforge-x2"));
        Assert.All(b.Glowforge.Steps, s => Assert.Null(s.Suggestion));
        Assert.All(b.Glowforge.Steps, s => Assert.Equal("2.997", s.RawAttributes["focal_offset"]));
        var ordinary = SvgMetadataService.Parse(Fixture("glowforge-x1"));
        ordinary.Root!.Add(new XElement(S + "text", "Ordinary text"));
        Assert.Equal(3, SvgBatchService.Analyze(ordinary.ToString()).Items.Count(i => i.Production));
    }

    [Theory]
    [InlineData(OrderingMode.WithinEachCopy, "Score,ExteriorCut,Score,ExteriorCut")]
    [InlineData(OrderingMode.AcrossSheet, "Score,Score,ExteriorCut,ExteriorCut")]
    public void NativeStepsFollowBatchOrderAndKeepMachineValues(OrderingMode mode, string expected)
    {
        var a = SvgBatchService.Analyze(Fixture("glowforge-x1"));
        var before = a.Document.ToString();
        var result = Build(a, mode);
        Assert.Equal(before, a.Document.ToString());
        Assert.NotEmpty(result.Warnings);
        var output = SvgMetadataService.Parse(SvgSerializer.Serialize(result.Document));
        var definitions = output.Descendants(G + "laserdef").ToArray();
        Assert.Equal(4, definitions.Length);
        var wrappers = output.Root!.Elements(S + "g").ToArray();
        Assert.Equal(expected, string.Join(",", wrappers.Select(e => (string?)e.Attribute("data-laserbatcher-operation"))));
        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            Assert.Equal(index.ToString(), (string?)definition.Attribute("order"));
            var token = (string)definition.Attribute("class")!;
            var paths = wrappers[index].Descendants(S + "path").ToArray();
            Assert.All(paths, p => Assert.Contains(token, ((string)p.Attribute("class")!).Split(' ')));
            var source = a.Glowforge.Steps.Single(s => s.RawAttributes["settingid"] == (string?)definition.Attribute("settingid"));
            foreach (var attribute in source.Definition.Attributes().Where(attr => !attr.IsNamespaceDeclaration && attr.Name.LocalName is not ("class" or "order")))
                Assert.Equal(attribute.Value, (string?)definition.Attribute(attribute.Name));
        }
        var json = JsonNode.Parse(output.Root.Element(S + "metadata")!.Element(SvgMetadataService.Namespace + "batch")!.Value)!;
        Assert.Equal(4, json["vendorProvenance"]!["glowforge"]!["stepMappings"]!.AsArray().Count);
        Assert.Contains("score-step", json["vendorProvenance"]!["glowforge"]!["originalDefinitions"]![0]!.GetValue<string>());
        Assert.Empty(output.Descendants(S + "style"));
        Assert.NotEmpty(output.Descendants(SvgMetadataService.Namespace + "sourceStylesheet"));
        Assert.Empty(SvgBatchService.Analyze(output.ToString()).Errors);
    }

    [Fact]
    public void ASharedSettingSplitsAcrossOperationBucketsAndClassNamesCannotCollide()
    {
        var doc = SvgMetadataService.Parse(Fixture("glowforge-x2"));
        doc.Root!.SetAttributeValue("class", "lb-gf-step-0");
        var a = SvgBatchService.Analyze(doc.ToString());
        var map = Map(a);
        map[a.Items.Single(i => i.Id == "three").Key] = SvgAssignment.ExteriorCut;
        var result = Build(a, copies: 1, mappings: map);
        var defs = result.Document.Descendants(G + "laserdef").ToArray();
        Assert.Equal(3, defs.Length);
        Assert.DoesNotContain(defs, e => (string?)e.Attribute("class") == "lb-gf-step-0");
        var first = result.Document.Descendants(S + "path").Single(e => (string?)e.Attribute("id") == "one");
        var third = result.Document.Descendants(S + "path").Single(e => (string?)e.Attribute("id") == "three");
        Assert.NotEqual((string?)first.Attribute("class"), (string?)third.Attribute("class"));
        Assert.Contains(first.Ancestors(), e => (string?)e.Attribute("data-laserbatcher-operation") == "InteriorCut");
        Assert.Contains(third.Ancestors(), e => (string?)e.Attribute("data-laserbatcher-operation") == "ExteriorCut");
        Assert.All(defs, e => Assert.Equal("2.997", (string?)e.Attribute("focal_offset")));
    }

    [Fact]
    public void InheritedVendorNamespaceAliasesSurviveRegenerationAndArchiving()
    {
        var doc = SvgMetadataService.Parse(Fixture("glowforge-x1"));
        doc.Root!.SetAttributeValue(XNamespace.Xmlns + "vendor", "urn:example:vendor");
        doc.Descendants(G + "laserdef").First().SetAttributeValue("kind", "vendor:material");
        var result = Build(SvgBatchService.Analyze(doc.ToString()));
        var parsed = SvgMetadataService.Parse(SvgSerializer.Serialize(result.Document));
        var definitions = parsed.Descendants(G + "laserdef").Where(e => e.Attribute("kind") != null);
        Assert.All(definitions, e => Assert.Equal("urn:example:vendor", e.GetNamespaceOfPrefix("vendor")!.NamespaceName));
        var json = JsonNode.Parse(parsed.Root!.Element(S + "metadata")!.Element(SvgMetadataService.Namespace + "batch")!.Value)!;
        var archive = XElement.Parse(json["vendorProvenance"]!["glowforge"]!["originalDefinitions"]![0]!.GetValue<string>());
        Assert.Equal("urn:example:vendor", archive.GetNamespaceOfPrefix("vendor")!.NamespaceName);
    }

    [Fact]
    public void UncorroboratedReportIsNotHiddenAndConflictingEvidenceIsNotSuggested()
    {
        var doc = SvgMetadataService.Parse(Fixture("glowforge-x1"));
        var text = doc.Descendants(S + "text").Single();
        foreach (var line in text.Elements()) line.Value = line.Value.Replace("#000000", "#999999");
        var a = SvgBatchService.Analyze(doc.ToString());
        Assert.Equal(3, a.Items.Count(i => i.Production));
        Assert.Empty(a.Glowforge.ReportLines);
        doc = SvgMetadataService.Parse(Fixture("glowforge-x1"));
        doc.Descendants(G + "laserdef").First().SetAttributeValue("settingid", "cut::different");
        a = SvgBatchService.Analyze(doc.ToString());
        Assert.NotEmpty(a.Warnings);
        Assert.Null(a.Glowforge.Steps.First().Suggestion);
    }

    [Fact]
    public void SupportedCssHonorsSourceOrderInlineStylesAndPresentationAttributes()
    {
        var a = SvgBatchService.Analyze($"<svg xmlns='{S}' width='1in' height='1in'><style>.a{{stroke:#000000;fill:none}}.b{{stroke:#ff0000}}.a{{stroke:#00ff00}}</style><path id='Engrave' class='a a b' stroke='#ffffff' style='fill:#123456'/></svg>");
        Assert.Empty(a.Errors);
        var style = (string)a.Document.Descendants(S + "path").Single().Attribute("style")!;
        Assert.Contains("stroke:#00ff00", style);
        Assert.EndsWith("fill:#123456", style);
    }

    [Theory]
    [InlineData("selector")][InlineData("priority")][InlineData("duplicate")][InlineData("conflict")][InlineData("order")][InlineData("unbound")]
    public void AmbiguousBindingsAndUnsupportedCssCannotExport(string kind)
    {
        var doc = SvgMetadataService.Parse(Fixture("glowforge-x1"));
        if (kind == "selector") doc.Descendants(S + "style").Single().Value = ".score-step:hover {stroke:#000000}";
        if (kind == "priority") doc.Descendants(S + "style").Single().Value = ".score-step {stroke:#000000!important}";
        if (kind == "duplicate") doc.Descendants(G + "laserdef").Last().SetAttributeValue("class", "score-step");
        if (kind == "conflict") doc.Descendants(S + "path").First().SetAttributeValue("class", "score-step cut-step");
        if (kind == "order") doc.Descendants(G + "laserdef").Last().SetAttributeValue("order", "0");
        if (kind == "unbound") doc.Root!.Add(new XElement(S + "path", new XAttribute("d", "M0 0h10")));
        var a = SvgBatchService.Analyze(doc.ToString());
        Assert.Throws<FormatException>(() => Build(a));
    }
}
