using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace LaserBatcher.Services;

public sealed record ImportedMetadata(int SchemaVersion, ProgramMetadata Program, int SourceYield, int CopyCount,
    int? YieldOverride, long? EstimatedRuntimeSeconds);
public sealed record MetadataReadResult(ImportedMetadata? Metadata, IReadOnlyList<string> Warnings);
public sealed record ProgramSelection(ProgramMetadata Program, ProgramMetadata? FilenameCandidate, bool Conflict, string? Error);

public static class SvgMetadataService
{
    public static readonly XNamespace Namespace = "urn:laserbatcher:metadata:2";
    public static readonly XNamespace LegacyNamespace = "urn:laserbatcher:metadata:1";
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static XDocument Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != SvgBatchService.Svg + "svg") throw new FormatException("The document must have an SVG root in the SVG namespace.");
        return document;
    }

    private static XElement[] Records(XDocument document) => document.Root!.Elements(SvgBatchService.Svg + "metadata")
        .Elements().Where(e => e.Name.LocalName == "batch" && e.Name.NamespaceName.StartsWith("urn:laserbatcher:metadata:", StringComparison.Ordinal)).ToArray();

    public static MetadataReadResult Read(XDocument document)
    {
        var records = Records(document);
        if (records.Length == 0) return new(null, Array.Empty<string>());
        try
        {
            if (records.Length != 1) throw new FormatException("Multiple authoritative batch records are ambiguous.");
            var record = records[0];
            if (record.HasElements) throw new FormatException("A batch record must contain JSON text, not XML child elements.");
            var version = record.Name.Namespace == LegacyNamespace ? 1 : record.Name.Namespace == Namespace ? 2 : 0;
            if (version == 0) throw new FormatException("This batch metadata version is not supported.");
            if ((string?)record.Attribute("schemaVersion") != version.ToString(CultureInfo.InvariantCulture)) throw new FormatException("The metadata namespace and version disagree.");
            using var parsed = JsonDocument.Parse(record.Value);
            CheckUniqueProperties(parsed.RootElement);
            var json = JsonNode.Parse(record.Value) as JsonObject ?? throw new FormatException("Batch metadata must be a JSON object.");
            if (Integer(json, "schemaVersion") != version) throw new FormatException("The JSON schema version disagrees with the record.");
            var program = json["program"] as JsonObject ?? throw new FormatException("Program metadata is missing.");
            var value = new ProgramMetadata(OptionalString(program, "partName"), OptionalString(program, "orderNumber"), OptionalString(program, "batch"), Integer(program, "yield"));
            value = ProgramNameParser.Parse(ProgramNameParser.Format(value));
            var sourceYield = Integer(json, "sourceYield");
            var copies = Integer(json, "copyCount");
            var total = Integer(json, "totalYield");
            var calculated = checked(sourceYield * copies);
            int? overridden = json.ContainsKey("yieldOverride") ? Integer(json, "yieldOverride") : null;
            if (version == 1 && overridden != null) throw new FormatException("Yield overrides require metadata v2.");
            if (total != value.Yield || total != (overridden ?? calculated)) throw new FormatException("The program yield and batch counts disagree.");
            long? runtime = json.ContainsKey("estimatedRuntimeSeconds")
                ? json["estimatedRuntimeSeconds"]?.GetValue<long>() ?? throw new FormatException("Runtime must be whole seconds; remove the property for an unknown estimate.") : null;
            EstimatedRuntime.Validate(runtime);
            return new(new(version, value, sourceYield, copies, overridden, runtime), Array.Empty<string>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return new(null, new[] { "Embedded metadata was preserved but cannot be edited: " + ex.Message });
        }
    }

    private static int Integer(JsonObject json, string key)
    {
        var value = json[key]?.GetValue<int>() ?? throw new FormatException($"Missing {key}.");
        if (value < 1) throw new FormatException($"{key} must be a positive integer.");
        return value;
    }

    private static string? OptionalString(JsonObject json, string key) => json[key]?.GetValue<string>();

    private static void CheckUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new FormatException("Duplicate JSON properties make metadata ambiguous.");
                CheckUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) CheckUniqueProperties(child);
    }

    public static ProgramSelection SelectProgram(string filename, ImportedMetadata? metadata)
    {
        var fallback = ProgramNameParser.FromSource(filename);
        if (metadata == null) return new(fallback.Metadata, null, false, fallback.Error);
        ProgramMetadata? candidate = null;
        try { candidate = ProgramNameParser.Parse(filename); } catch (FormatException) { }
        return new(metadata.Program, candidate, candidate != null && candidate != metadata.Program, null);
    }

    public static BatchResult Update(XDocument source, ProgramMetadata program, long? estimatedRuntimeSeconds)
    {
        EstimatedRuntime.Validate(estimatedRuntimeSeconds);
        var read = Read(source);
        var existing = read.Metadata ?? throw new FormatException(read.Warnings.FirstOrDefault() ?? "A supported batch metadata record is required for metadata editing.");
        var filename = ProgramNameParser.Format(program);
        program = ProgramNameParser.Parse(filename);
        var document = new XDocument(source);
        var record = Records(document).Single();
        var json = JsonNode.Parse(record.Value)!.AsObject();
        var fields = json["program"]!.AsObject();
        fields["partName"] = program.PartName;
        fields["orderNumber"] = program.OrderNumber;
        fields["batch"] = program.Batch;
        fields["yield"] = program.Yield;
        json["schemaVersion"] = 2;
        json["totalYield"] = program.Yield;
        if (program.Yield == checked(existing.SourceYield * existing.CopyCount)) json.Remove("yieldOverride");
        else json["yieldOverride"] = program.Yield;
        SetRuntime(json, estimatedRuntimeSeconds);
        record.Name = Namespace + "batch";
        record.SetAttributeValue("schemaVersion", "2");
        // Patch JSON text while retaining comments/processing instructions in the owned record.
        var textNodes = record.Nodes().OfType<XText>().ToArray();
        if (textNodes.Length == 0) record.Add(new XText(json.ToJsonString()));
        else
        {
            textNodes[0].ReplaceWith(new XText(json.ToJsonString()));
            foreach (var text in textNodes.Skip(1)) text.Remove();
        }
        return new(document, filename);
    }

    internal static void SetRuntime(JsonObject json, long? seconds)
    {
        EstimatedRuntime.Validate(seconds);
        if (seconds == null) json.Remove("estimatedRuntimeSeconds");
        else json["estimatedRuntimeSeconds"] = seconds.Value;
    }
}

public static class EstimatedRuntime
{
    public static void Validate(long? seconds)
    {
        if (seconds != null && (seconds <= 0 || seconds > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond))
            throw new FormatException("Estimated runtime must be positive whole seconds within the supported duration range, or blank.");
    }

    public static long? Parse(string? hours, string? minutes, string? seconds)
    {
        if (new[] { hours, minutes, seconds }.All(string.IsNullOrWhiteSpace)) return null;
        long Part(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            if (!long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)) throw new FormatException("Runtime fields must contain nonnegative whole numbers.");
            return value;
        }
        var h = Part(hours); var m = Part(minutes); var s = Part(seconds);
        if (m > 59 || s > 59) throw new FormatException("Runtime minutes and seconds must be between 0 and 59.");
        long total;
        try { total = checked(h * 3600 + m * 60 + s); }
        catch (OverflowException) { throw new FormatException("Estimated runtime exceeds the supported duration range."); }
        Validate(total);
        return total;
    }
}
