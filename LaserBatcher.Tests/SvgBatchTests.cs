using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using LaserBatcher.Services;
using Xunit;

namespace LaserBatcher.Tests;

public class SvgBatchTests
{
    private static readonly XNamespace S = SvgBatchService.Svg;
    private static SvgAnalysis Fixture() => SvgBatchService.Analyze(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "semantic.svg")));
    private static SvgAnalysis Analyze(string body, string attributes = "") => SvgBatchService.Analyze($"<svg xmlns='{S}' width='1in' height='1in' {attributes}>{body}</svg>");
    private static BatchSettings Settings(OrderingMode mode = OrderingMode.WithinEachCopy, int copies = 2) => new(4, 3, .25, .25, .1, copies, mode);
    private static BatchResult Build(SvgAnalysis a, OrderingMode mode = OrderingMode.WithinEachCopy, int copies = 2, Dictionary<string, LaserOperation>? map = null) => SvgBatchService.Build(a, "Widget_x4.svg", new("Widget", null, null, 4), Settings(mode, copies), map ?? new());

    [Theory]
    [InlineData(OrderingMode.WithinEachCopy, "Engrave,Score,InteriorCut,ExteriorCut,Engrave,Score,InteriorCut,ExteriorCut")]
    [InlineData(OrderingMode.AcrossSheet, "Engrave,Engrave,Score,Score,InteriorCut,InteriorCut,ExteriorCut,ExteriorCut")]
    public void OrdersIndividualGeometryAndPreservesHiddenData(OrderingMode mode, string expected)
    {
        var a = Fixture();
        Assert.Empty(a.Errors);
        var before = a.Document.ToString();
        var result = Build(a, mode);
        var root = XDocument.Parse(result.Document.ToString()).Root!;
        Assert.Equal(expected, string.Join(",", root.Elements(S + "g").Select(e => e.Attribute("data-laserbatcher-operation")!.Value)));
        Assert.Equal(before, a.Document.ToString());
        var ids = root.DescendantsAndSelf().Attributes("id").Select(a => a.Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        foreach (var id in a.Document.Descendants().Attributes("id")) Assert.Contains(id.Value, ids);
        Assert.Equal(2, root.Descendants(S + "circle").Count());
        Assert.All(root.Descendants(S + "circle"), e => Assert.Contains(e.Ancestors(), p => p.Attribute("style")?.Value.Contains("display:none") == true));
        Assert.Equal(2, root.Descendants().Count(e => e.Name.LocalName == "license"));
        Assert.Equal(2, root.Descendants(S + "path").Count(p => p.Attribute("fill-rule")?.Value == "evenodd"));
        Assert.All(root.Descendants(S + "use"), e => Assert.Contains(e.Attribute("href")!.Value[1..], ids));
        Assert.Equal("4in", root.Attribute("width")!.Value);
        Assert.Equal("0 0 384 288", root.Attribute("viewBox")!.Value);
        Assert.Contains(root.Elements(S + "g"), e => e.Attribute("transform")?.Value == "translate(129.6,24)");
        Assert.Empty(root.Descendants(S + "svg"));
        Assert.Contains(root.Descendants(S + "g"), e => e.Attribute("data-laserbatcher-source-viewbox")?.Value == "0 0 100 100");
        var json = JsonDocument.Parse(root.Element(S + "metadata")!.Element(SvgBatchService.MetadataNamespace + "batch")!.Value).RootElement;
        Assert.Equal(8, json.GetProperty("totalYield").GetInt32());
        Assert.Equal("in", json.GetProperty("sheet").GetProperty("units").GetString());
        Assert.Contains(json.GetProperty("hiddenLayers").EnumerateArray(), e => e.GetProperty("sourceId").GetString() == "hidden-circle");
        Assert.Equal("Widget_x8.svg", result.Filename);
    }

    [Fact]
    public void UnresolvedAliasesAndConflictsRequireMappingWithIndividualOverride()
    {
        var a = Analyze("<g id='InsideCut'><path id='a' d='M0 0h1'/><path id='b' d='M1 1h1'/></g>");
        Assert.All(a.Items.Where(i => i.Production), i => Assert.Null(i.Operation));
        Assert.Throws<FormatException>(() => Build(a));
        var group = a.Items.Single(i => i.Id == "InsideCut");
        var b = a.Items.Single(i => i.Id == "b");
        var map = new Dictionary<string, LaserOperation> { [group.Key] = LaserOperation.InteriorCut, [b.Key] = LaserOperation.Engrave };
        Assert.Equal(LaserOperation.Engrave, SvgBatchService.Resolve(a, map)[b.Key]);
        Assert.NotNull(Build(a, map: map));
        var conflict = Analyze("<g id='Engrave'><path id='ExteriorCut' d='M0 0h1'/></g>");
        Assert.Null(conflict.Items.Single(i => i.Production).Operation);
    }

    [Theory]
    [InlineData("<g style='display:none'><path id='secret'/></g><path id='Engrave'/>")]
    [InlineData("<g visibility='hidden'><path id='secret'/></g><path id='Engrave'/>")]
    [InlineData("<g style='display:/* hidden design */NONE'><path id='secret'/></g><path id='Engrave'/>")]
    [InlineData("<g data-label='CustomizationZones'><rect id='secret'/></g><path id='Engrave'/>")]
    public void HiddenGeometryNeverRequiresProductionMapping(string body)
    {
        var a = Analyze(body);
        Assert.Single(a.Items.Where(i => i.Production));
        var result = Build(a);
        Assert.Equal(2, result.Document.Descendants().Count(e => e.Attribute("id")?.Value.StartsWith("secret") == true));
    }

    [Fact]
    public void ExplicitVisibilityCanOverrideHiddenParent()
    {
        var a = Analyze("<g visibility='hidden'><path id='Engrave' visibility='visible'/></g>");
        Assert.Single(a.Items.Where(i => i.Production));
        Assert.NotNull(Build(a));
    }

    [Theory]
    [InlineData("<path id='a'/><path id='a'/>")]
    [InlineData("<use id='Engrave' href='#missing'/>")]
    [InlineData("<style>.cut{stroke:red}</style><path id='Engrave' class='cut'/>")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<image id='Engrave' href='https://example.com/a.png'/>")]
    [InlineData("<defs><g id='mixed'><path id='Engrave'/><path id='ExteriorCut'/></g></defs><use id='Score' href='#mixed'/>")]
    [InlineData("<defs><use id='a' href='#b'/><use id='b' href='#a'/></defs><use id='Engrave' href='#a'/>")]
    [InlineData("<switch><path id='Engrave'/><path id='ExteriorCut'/></switch>")]
    [InlineData("<defs><pattern id='local'/></defs><path id='Engrave' style='fill:url(#local);stroke:url(https://example.com/paint)'/>")]
    public void RejectsUnsafeOrAmbiguousDocuments(string body)
    {
        var a = Analyze(body);
        Assert.NotEmpty(a.Errors);
        Assert.Throws<FormatException>(() => Build(a));
    }

    [Fact]
    public void GroupCompositingAndReferencesBlockUnsafeSplits()
    {
        var a = Analyze("<g opacity='.5'><path id='Engrave'/><path id='ExteriorCut'/></g>");
        Assert.Throws<FormatException>(() => Build(a));
        var referenced = Analyze("<g id='shared'><path id='Engrave'/><path id='ExteriorCut'/></g><use id='Score' href='#shared'/>");
        Assert.Throws<FormatException>(() => Build(referenced));
        var clipping = Analyze("<defs><clipPath id='clip' clipPathUnits='objectBoundingBox'><rect width='1' height='1'/></clipPath></defs><g clip-path='url(#clip)'><path id='Engrave'/><path id='ExteriorCut'/></g>");
        Assert.Throws<FormatException>(() => Build(clipping));
    }

    [Fact]
    public void MetadataUsesCanonicalFilenameValuesAndIdsForUnlabelledHiddenElements()
    {
        var a = Analyze("<g style='display:none'><path/></g><path id='Engrave'/>");
        var result = SvgBatchService.Build(a, "Source.svg", new(" _ Widget _ ", " A ", null, 1), Settings(), new Dictionary<string, LaserOperation>());
        var json = JsonDocument.Parse(result.Document.Root!.Element(S + "metadata")!.Element(SvgBatchService.MetadataNamespace + "batch")!.Value).RootElement;
        Assert.Equal("Widget", json.GetProperty("program").GetProperty("partName").GetString());
        Assert.Equal("A", json.GetProperty("program").GetProperty("orderNumber").GetString());
        Assert.All(json.GetProperty("hiddenLayers").EnumerateArray(), e => Assert.NotEmpty(e.GetProperty("outputIds").EnumerateArray()));
    }

    [Fact]
    public void PreservesDocumentLevelCommentsAndDoesNotTreatUnknownTextAsCss()
    {
        var a = SvgBatchService.Analyze($"<!--license--><svg xmlns='{S}' width='1in' height='1in' data-note='inherited design'><path id='Engrave'/></svg>");
        Assert.Empty(a.Errors);
        Assert.IsType<XComment>(Build(a).Document.FirstNode);
    }

    [Fact]
    public void CopiesRemainUniqueWhenSourceIdsResembleGeneratedNames()
    {
        var a = Analyze("<path id='art' data-label='Engrave'/><metadata id='art-lb-copy-2-op-0'/>");
        var ids = Build(a).Document.Descendants().Attributes("id").Select(a => a.Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Contains("art-lb-copy-2-op-0-1", ids);
    }

    [Fact]
    public void ExplicitOverridesDoNotExportConflictingCanonicalLabels()
    {
        var source = Analyze("<g id='Engrave'><path id='ExteriorCut'/></g>");
        var leaf = source.Items.Single(i => i.Production);
        var result = Build(source, map: new() { [leaf.Key] = LaserOperation.Score });
        Assert.Contains(result.Document.Descendants(), e => (string?)e.Attribute("data-laserbatcher-original-label") == "ExteriorCut");
        var reimported = SvgBatchService.Analyze(result.Document.ToString());
        Assert.All(reimported.Items.Where(i => i.Production), i => Assert.Equal(LaserOperation.Score, i.Operation));
        Assert.Equal("ExteriorCut", source.Items.Single(i => i.Production).Label);
    }

    [Fact]
    public void TextRasterAndUnknownMetadataArePreserved()
    {
        var a = Analyze("<g id='Engrave'><text id='text'>Hello<tspan> world</tspan></text><image href='data:image/png;base64,AA==' width='1' height='1'/></g><metadata><record xmlns='urn:test'>keep</record></metadata>");
        var result = Build(a);
        Assert.Equal(2, result.Document.Descendants(S + "text").Count());
        Assert.All(result.Document.Descendants(S + "text"), e => Assert.Equal("Hello world", e.Value));
        Assert.Equal(2, result.Document.Descendants(S + "image").Count());
        Assert.Equal(2, result.Document.Descendants(XName.Get("record", "urn:test")).Count());
    }

    [Fact]
    public void CapacityAndPlacementUseInvariantUnits()
    {
        var a = Fixture();
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(6, SvgBatchService.Capacity(a, Settings()));
            Assert.Equal("translate(24,24)", Build(a).Document.Root!.Elements(S + "g").First().Attribute("transform")!.Value);
            Assert.Throws<FormatException>(() => Build(a, copies: 7));
            Assert.Throws<FormatException>(() => SvgBatchService.Capacity(a, Settings() with { Spacing = -1 }));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Assert.Empty(SvgBatchService.Analyze($"<svg xmlns='{S}' width='25.4mm' height='1in'/>").Errors);
    }
}
