using System.Globalization;
using System.Text.RegularExpressions;

namespace LaserBatcher.Services;

public sealed record ProgramMetadata(string? PartName, string? OrderNumber, string? Batch, int Yield);
public sealed record SourceProgram(ProgramMetadata Metadata, string? Error);

public static class ProgramNameParser
{
    private static readonly Regex Suffix = new(@"_x([0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Marker = new(@"^(Order|Batch)(?:\s+(.*))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex YieldLike = new(@"^x(?:\s|[0-9+\-.]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static string? Clean(string? value)
    {
        var cleaned = Regex.Replace(value ?? "", @"^[\s_]+|[\s_]+$", "");
        return cleaned.Length == 0 ? null : cleaned;
    }

    public static ProgramMetadata Parse(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
            throw new FormatException("Enter a program filename, not a path.");
        var stem = filename.Trim();
        if (stem.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];
        var suffix = Suffix.Match(stem);
        if (!suffix.Success || !int.TryParse(suffix.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var yield) || yield < 1)
            throw new FormatException("The filename must end in _xYield (1–2147483647), optionally followed by .svg.");
        var values = new List<string>[] { new(), new(), new() };
        var stage = 0;
        foreach (var raw in stem[..suffix.Index].Split('_'))
        {
            var token = raw.Trim();
            if (token.Length == 0) { values[stage].Add(raw); continue; }
            if (YieldLike.IsMatch(token)) throw new FormatException("Duplicated or misplaced yield marker.");
            var marker = Marker.Match(token);
            if (marker.Success)
            {
                var next = marker.Groups[1].Value.Equals("Order", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                if (next <= stage || string.IsNullOrWhiteSpace(marker.Groups[2].Value))
                    throw new FormatException("Order and Batch must be nonempty, unique, and in that order.");
                stage = next;
                token = marker.Groups[2].Value.Trim();
                if (Marker.IsMatch(token) || YieldLike.IsMatch(token)) throw new FormatException("Ambiguous metadata value.");
            }
            values[stage].Add(marker.Success ? token : raw);
        }
        return new(Clean(string.Join("_", values[0])), Clean(string.Join("_", values[1])), Clean(string.Join("_", values[2])), yield);
    }

    public static string Format(ProgramMetadata metadata)
    {
        var normalized = new ProgramMetadata(Clean(metadata.PartName), Clean(metadata.OrderNumber), Clean(metadata.Batch), metadata.Yield);
        var fields = new List<string>();
        if (normalized.PartName != null) fields.Add(normalized.PartName);
        if (normalized.OrderNumber != null) fields.Add("Order " + normalized.OrderNumber);
        if (normalized.Batch != null) fields.Add("Batch " + normalized.Batch);
        var result = string.Join("_", fields) + "_x" + metadata.Yield.ToString(CultureInfo.InvariantCulture) + ".svg";
        if (result.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0 || result.Any(char.IsControl))
            throw new FormatException("Metadata contains characters that cannot be used in a filename.");
        if (Parse(result) != normalized) throw new FormatException("Metadata is ambiguous under the filename grammar.");
        return result;
    }

    public static SourceProgram FromSource(string filename)
    {
        try { return new(Parse(filename), null); }
        catch (FormatException ex)
        {
            var stem = filename.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? filename[..^4] : filename;
            var looksLikeMetadata = stem.Split('_').Any(t => Marker.IsMatch(t.Trim()) || YieldLike.IsMatch(t.Trim()));
            return new(new(Clean(stem), null, null, 1), looksLikeMetadata ? ex.Message : null);
        }
    }

    public static ProgramMetadata Total(ProgramMetadata source, int copies)
    {
        if (copies < 1 || source.Yield < 1) throw new FormatException("Yield and copies must be positive integers.");
        return source with { Yield = checked(source.Yield * copies) };
    }
}
