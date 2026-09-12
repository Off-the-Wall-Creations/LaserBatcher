using System.Xml.Linq;
using LaserBatcher.Services;
using Xunit;

namespace LaserBatcher.Tests;

public class ViewportTests
{
    private static readonly XNamespace S = SvgBatchService.Svg;
    private static XDocument Flatten(string content) => SvgViewportNormalizer.Flatten(XDocument.Parse(content));
    [Theory]
    [InlineData("none", "matrix(2,0,0,1,-20,-20)")]
    [InlineData("xMidYMid meet", "matrix(1,0,0,1,40,-20)")]
    [InlineData("xMaxYMax slice", "matrix(2,0,0,2,-20,-140)")]
    public void ConvertsViewBoxOriginAndAspectRatio(string aspect, string matrix)
    {
        var source = $"<svg xmlns='{S}' width='4in' height='3in'><svg id='source' x='5' y='6' width='200' height='100' viewBox='10 20 100 100' preserveAspectRatio='{aspect}'><path id='art' d='M10 20h20'/></svg></svg>";
        var result = Flatten(source);
        Assert.Single(result.Descendants(S + "svg"));
        Assert.Contains(result.Descendants(S + "g"), e => e.Attribute("transform")?.Value == "translate(5,6)");
        Assert.Contains(result.Descendants(S + "g"), e => e.Attribute("transform")?.Value == matrix);
        Assert.Equal("M10 20h20", result.Descendants(S + "path").Single().Attribute("d")!.Value);
        Assert.Equal("source", result.Descendants(S + "g").First().Attribute("id")!.Value);
        Assert.Equal("10 20 100 100", result.Descendants(S + "g").First().Attribute("data-laserbatcher-source-viewbox")!.Value);
    }
    [Fact]
    public void HandlesMultipleNestedViewportsAndPercentGeometry()
    {
        var result = Flatten($"<svg xmlns='{S}' width='4in' height='3in' viewBox='0 0 400 300'><svg width='50%' height='100' viewBox='0 0 100 50'><svg x='10%' y='20%' width='50%' height='50%'><rect id='part' width='50%' height='50%'/></svg></svg></svg>");
        Assert.Single(result.Descendants(S + "svg"));
        var rect = result.Descendants(S + "rect").Single(e => e.Attribute("id")?.Value == "part");
        Assert.Equal("25", rect.Attribute("width")!.Value);
        Assert.Equal("12.5", rect.Attribute("height")!.Value);
        Assert.Contains(result.Descendants(S + "g"), e => e.Attribute("transform")?.Value == "translate(10,10)");
    }
    [Fact]
    public void KeepsSourceAndHiddenMetadataAndUsesUniqueClipIds()
    {
        var source = XDocument.Parse($"<svg xmlns='{S}' width='4in' height='3in'><svg id='old' width='96' height='96' transform='rotate(10)'><g id='lb-viewport-clip-1' style='display:none'><path id='secret'/></g></svg></svg>");
        var before = source.ToString();
        var result = SvgViewportNormalizer.Flatten(source);
        Assert.Equal(before, source.ToString());
        Assert.Equal("rotate(10)", result.Descendants(S + "g").First().Attribute("transform")!.Value);
        Assert.Contains(result.Descendants(S + "clipPath"), e => e.Attribute("id")?.Value == "lb-viewport-clip-2");
        Assert.Equal("display:none", result.Descendants(S + "g").Single(e => e.Attribute("id")?.Value == "lb-viewport-clip-1").Attribute("style")!.Value);
    }
    [Fact]
    public void ExplicitVisibleOverflowDoesNotIntroduceClipping()
    {
        var result = Flatten($"<svg xmlns='{S}' width='4in' height='3in'><svg width='1in' height='1in' overflow='visible'><path/></svg></svg>");
        Assert.Empty(result.Descendants(S + "clipPath"));
    }
    [Theory]
    [InlineData("viewBox='0 0 0 1'")]
    [InlineData("width='2em'")]
    [InlineData("preserveAspectRatio='invalid'")]
    [InlineData("style='width:2in'")]
    public void RejectsUnresolvedViewportSemantics(string attributes)
    {
        Assert.Throws<FormatException>(() => Flatten($"<svg xmlns='{S}' width='4in' height='3in'><svg {attributes}><path/></svg></svg>"));
    }
}
