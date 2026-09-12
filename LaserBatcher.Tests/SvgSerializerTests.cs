using System.Xml;
using System.Xml.Linq;
using LaserBatcher.Services;
using Xunit;

namespace LaserBatcher.Tests;

public class SvgSerializerTests
{
    [Theory]
    [InlineData("svg")]
    [InlineData("s")]
    [InlineData("drawing")]
    public void EmitsUnprefixedSvgDespitePreservedAliases(string alias)
    {
        var source = XDocument.Parse($"<svg xmlns='{SvgBatchService.Svg}'><{alias}:g xmlns:{alias}='{SvgBatchService.Svg}' id='group'><{alias}:path id='art' d='M0 0h1'/><{alias}:rect width='2' height='3'/></{alias}:g></svg>");
        var before = source.ToString();
        var xml = SvgSerializer.Serialize(source);
        AssertUnprefixedSvg(xml);
        Assert.Contains("<g ", xml);
        Assert.Contains("<path ", xml);
        Assert.Contains($"xmlns:{alias}=", xml);
        Assert.Equal(before, source.ToString());
        Assert.Equal("M0 0h1", XDocument.Parse(xml).Descendants(SvgBatchService.Svg + "path").Single().Attribute("d")!.Value);
    }

    [Fact]
    public void PreservesForeignMetadataNamespacedAttributesAndQNameValues()
    {
        var source = XDocument.Parse($"<svg xmlns='{SvgBatchService.Svg}' xmlns:ink='http://www.inkscape.org/namespaces/inkscape'><metadata><record xmlns='urn:custom' xmlns:s='{SvgBatchService.Svg}' type='s:path'><s:g ink:label='Hidden' style='display:none'><s:path id='hidden' d='M1 2h3'/></s:g><value>Keep &amp; preserve</value></record></metadata></svg>");
        var xml = SvgSerializer.Serialize(source);
        AssertUnprefixedSvg(xml);
        var parsed = XDocument.Parse(xml);
        var record = parsed.Descendants(XName.Get("record", "urn:custom")).Single();
        Assert.Equal("s:path", record.Attribute("type")!.Value);
        Assert.Equal(SvgBatchService.Svg, record.GetNamespaceOfPrefix("s"));
        Assert.Equal("Keep & preserve", record.Element(XName.Get("value", "urn:custom"))!.Value);
        Assert.Equal("Hidden", parsed.Descendants(SvgBatchService.Svg + "g").Single().Attribute(SvgBatchService.Inkscape + "label")!.Value);
    }

    [Fact]
    public void HandlesSvgNamespacedAttributesAndGeneratedNamespaces()
    {
        var source = new XDocument(new XElement(SvgBatchService.Svg + "svg",
            new XElement(SvgBatchService.Svg + "g", new XAttribute(SvgBatchService.Svg + "custom", "keep"),
                new XElement(SvgBatchService.Svg + "path", new XAttribute("d", "M0 0h1"))),
            new XElement(XName.Get("record", "urn:custom"), new XAttribute(XName.Get("flag", "urn:other"), "yes"))));
        var xml = SvgSerializer.Serialize(source);
        AssertUnprefixedSvg(xml);
        Assert.Equal("keep", XDocument.Parse(xml).Descendants(SvgBatchService.Svg + "g").Single().Attribute(SvgBatchService.Svg + "custom")!.Value);
    }

    [Fact]
    public void BatchExportWithSourceSvgAliasHasOneUnprefixedRootAndGeometry()
    {
        var source = SvgBatchService.Analyze($"<svg xmlns='{SvgBatchService.Svg}' xmlns:svg='{SvgBatchService.Svg}' width='1in' height='1in' viewBox='0 0 100 100'><svg:g id='Engrave'><svg:path d='M10 10h20'/></svg:g></svg>");
        var result = SvgBatchService.Build(source, "Part_x1.svg", new("Part", null, null, 1), new(4, 3, 0, 0, .1, 2, OrderingMode.WithinEachCopy), new Dictionary<string, LaserOperation>());
        var xml = SvgSerializer.Serialize(result.Document);
        AssertUnprefixedSvg(xml);
        var parsed = XDocument.Parse(xml);
        Assert.Single(parsed.Descendants(SvgBatchService.Svg + "svg"));
        Assert.Equal(2, parsed.Descendants(SvgBatchService.Svg + "path").Count());
    }

    private static void AssertUnprefixedSvg(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml));
        var count = 0;
        while (reader.Read())
            if (reader.NodeType == XmlNodeType.Element && reader.NamespaceURI == SvgBatchService.Svg.NamespaceName)
            {
                Assert.Equal("", reader.Prefix);
                count++;
            }
        Assert.True(count > 0);
    }
}
