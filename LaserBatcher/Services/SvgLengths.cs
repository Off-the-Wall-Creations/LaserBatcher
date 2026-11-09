using System.Globalization;
using System.Text.RegularExpressions;

namespace LaserBatcher.Services;

public static class SvgLengths
{
    public static double PhysicalInches(string? text)
    {
        if (text == null || !Regex.IsMatch(text.Trim(), @"(?:in|mm|cm|pt|pc)$"))
            throw new FormatException("Source width and height must be positive dimensions with explicit in, mm, cm, pt, or pc units.");
        var result = Pixels(text, 0, 0) / 96;
        if (!double.IsFinite(result) || result <= 0) throw new FormatException("Source dimensions must be positive and finite.");
        return result;
    }

    public static double Pixels(string? text, double percentBasis, double fallback)
    {
        if (text == null) return fallback;
        var match = Regex.Match(text.Trim(), @"^([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)(px|in|mm|cm|pt|pc|%)?$");
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            throw new FormatException("SVG viewport lengths must use numbers, px, in, mm, cm, pt, pc, or percentages.");
        var result = number * (match.Groups[2].Value switch { "in" => 96, "mm" => 96 / 25.4, "cm" => 96 / 2.54, "pt" => 96 / 72d, "pc" => 16, "%" => percentBasis / 100, _ => 1 });
        if (!double.IsFinite(result)) throw new FormatException("SVG length exceeds the supported range.");
        return result;
    }
}
