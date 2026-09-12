using LaserBatcher.Services;
using Xunit;

namespace LaserBatcher.Tests;

public class ProgramNameTests
{
    [Theory]
    [InlineData("_x1", null, null, null, 1)]
    [InlineData("Widget_x4.svg", "Widget", null, null, 4)]
    [InlineData("Order A-10_x2", null, "A-10", null, 2)]
    [InlineData("Batch B.2_x3", null, null, "B.2", 3)]
    [InlineData("Widget_Order A_x4", "Widget", "A", null, 4)]
    [InlineData("Widget_Batch B_x4", "Widget", null, "B", 4)]
    [InlineData("Order A_Batch B_x4", null, "A", "B", 4)]
    [InlineData("Widget_Order A_Batch B_x4", "Widget", "A", "B", 4)]
    [InlineData("  __ Part (Rev.A)-1 __oRdEr AB-12__bAtCh Lot.3 __X0007.SvG  ", "Part (Rev.A)-1", "AB-12", "Lot.3", 7)]
    [InlineData("_X2147483647.SVG", null, null, null, int.MaxValue)]
    [InlineData("Part__Name_Order AB_ CD_Batch XZ_42_x1", "Part__Name", "AB_ CD", "XZ_42", 1)]
    public void ParsesAndRoundTrips(string filename, string? part, string? order, string? batch, int yield)
    {
        var expected = new ProgramMetadata(part, order, batch, yield);
        Assert.Equal(expected, ProgramNameParser.Parse(filename));
        Assert.Equal(expected, ProgramNameParser.Parse(ProgramNameParser.Format(expected)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Widget.svg")]
    [InlineData("x12.svg")]
    [InlineData("Widget_x0.svg")]
    [InlineData("Widget_x-1.svg")]
    [InlineData("Widget_x+1.svg")]
    [InlineData("Widget_x1.5.svg")]
    [InlineData("Widget_x2147483648.svg")]
    [InlineData("Widget_x9999999999999999999999.svg")]
    [InlineData("Widget_x١.svg")]
    [InlineData("Widget_x1.svg.svg")]
    [InlineData("Widget_x1.png")]
    [InlineData("Widget_x1.svg.extra")]
    [InlineData("Widget_x1_tail")]
    [InlineData("Widget_x1_x2.svg")]
    [InlineData("Widget_Order _x1")]
    [InlineData("Widget_Batch _x1")]
    [InlineData("Widget_Order 12_Order 13_x1")]
    [InlineData("Widget_Batch A_Batch B_x1")]
    [InlineData("Widget_Batch A_Order 12_x1")]
    [InlineData("Widget_Order Batch A_x1")]
    [InlineData("Widget_Batch x2_x1")]
    [InlineData("C:/Widget_x1.svg")]
    public void RejectsInvalidNames(string filename) => Assert.Throws<FormatException>(() => ProgramNameParser.Parse(filename));

    [Fact]
    public void OrdinaryFilenameHasEditableDefaultsButMalformedMetadataDoesNotSilentlyFallback()
    {
        Assert.Equal(new SourceProgram(new("Widget", null, null, 1), null), ProgramNameParser.FromSource("Widget.svg"));
        Assert.NotNull(ProgramNameParser.FromSource("Widget_batch_x2.svg").Error);
        Assert.NotNull(ProgramNameParser.FromSource("Widget_x0.svg").Error);
        Assert.Equal("Widget_Order A_Batch B_x12.svg", ProgramNameParser.Format(new("Widget", "A", "B", 12)));
        Assert.Throws<FormatException>(() => ProgramNameParser.Format(new("Widget_Order A", null, null, 1)));
    }

    [Fact]
    public void YieldCountsFinishedPartsAndChecksOverflow()
    {
        Assert.Equal("Widget_x12.svg", ProgramNameParser.Format(ProgramNameParser.Total(ProgramNameParser.Parse("Widget_x4.svg"), 3)));
        Assert.Throws<OverflowException>(() => ProgramNameParser.Total(new(null, null, null, int.MaxValue), 2));
        Assert.Throws<FormatException>(() => ProgramNameParser.Total(new(null, null, null, 1), 0));
    }
}
