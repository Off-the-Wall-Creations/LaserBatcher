using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LaserBatcher.Services;

public enum LaserOperation { Engrave, Score, InteriorCut, ExteriorCut }
public enum OrderingMode { WithinEachCopy, AcrossSheet }
public sealed record BatchSettings(double Width, double Height, double HorizontalMargin, double VerticalMargin, double Spacing, int Copies, OrderingMode Ordering);
public sealed record SvgItem(string Key, string? Id, string Label, string? Parent, string Element, bool Hidden, string Visibility, string? SemanticCategory, bool Production, LaserOperation? Operation, string? Suggestion);
public sealed record SvgAnalysis(XDocument Document, IReadOnlyList<SvgItem> Items, IReadOnlyList<string> Errors, double Width, double Height);
public sealed record BatchResult(XDocument Document, string Filename);

public static class SvgBatchService
{
    public static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    public static readonly XNamespace Inkscape = "http://www.inkscape.org/namespaces/inkscape";
    public static readonly XNamespace MetadataNamespace = "urn:laserbatcher:metadata:1";
    private static readonly HashSet<string> Geometry = new() { "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "image", "use" };
    private static readonly HashSet<string> Semantics = new() { "CustomizationZones", "MaterialSpecs", "DesignConstraints" };
    private static readonly Regex Url = new("url\\(\\s*['\"]?#([^)'\"\\s]+)['\"]?\\s*\\)", RegexOptions.IgnoreCase);
    private static string F(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static string Key(XElement node) => string.Join("/", node.AncestorsAndSelf().Reverse().Select(e => e.ElementsBeforeSelf().Count()));
    private static string Label(XElement node) => (string?)node.Attribute(Inkscape + "label") ?? (string?)node.Attribute("data-label") ?? (string?)node.Attribute("id") ?? node.Name.LocalName;
    private static Dictionary<string, string> Style(XElement e) => Regex.Replace((string?)e.Attribute("style") ?? "", @"/\*.*?\*/", "", RegexOptions.Singleline).Split(';').Select(s => s.Split(':', 2)).Where(p => p.Length == 2).GroupBy(p => p[0].Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Last()[1].Trim());
    private static string? Property(XElement e, string name) => Style(e).GetValueOrDefault(name) ?? (string?)e.Attribute(name);
    private static string? Category(XElement e) => e.AncestorsAndSelf().Select(Label).FirstOrDefault(Semantics.Contains);
    private static bool NonProduction(XElement e) => e.AncestorsAndSelf().Any(a => a.Name == Svg + "defs" || a.Name == Svg + "metadata" || a.Name == Svg + "clipPath" || a.Name == Svg + "mask" || a.Name == Svg + "symbol" || a.Name == Svg + "pattern" || a.Name == Svg + "marker");
    private static bool Hidden(XElement e) => Category(e) != null || e.AncestorsAndSelf().Any(a => string.Equals(Property(a, "display"), "none", StringComparison.OrdinalIgnoreCase)) || new[] { "hidden", "collapse" }.Contains(e.AncestorsAndSelf().Select(a => Property(a, "visibility")?.ToLowerInvariant()).FirstOrDefault(v => v != null));
    private static LaserOperation? Canonical(string label) => label switch
    {
        "Engrave" or "InteriorEngrave" or "ExteriorEngrave" => LaserOperation.Engrave,
        "Score" or "InteriorScore" or "ExteriorScore" => LaserOperation.Score,
        "InteriorCut" => LaserOperation.InteriorCut, "ExteriorCut" => LaserOperation.ExteriorCut, _ => null
    };
    private static string? Alias(string label) => label switch { "InlineCut" or "InsideCut" => "InteriorCut", "OutlineCut" or "OuterCut" => "ExteriorCut", "InlineEngrave" => "Engrave", _ => null };

    public static SvgAnalysis Analyze(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var errors = new List<string>();
        var root = doc.Root ?? throw new FormatException("SVG has no root element.");
        if (root.Name != Svg + "svg") throw new FormatException("The document must have an SVG root in the SVG namespace.");
        var elements = root.DescendantsAndSelf().ToArray();
        var ids = elements.Attributes("id").Select(a => a.Value).ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct().Count() != ids.Length) errors.Add("Empty or duplicate source IDs make references ambiguous.");
        if (doc.DescendantNodes().OfType<XProcessingInstruction>().Any(p => p.Target.Equals("xml-stylesheet", StringComparison.OrdinalIgnoreCase))) errors.Add("External stylesheets require resolved inline styles before export.");
        foreach (var e in elements)
        {
            if (new[] { "script", "foreignObject", "animate", "animateTransform", "animateMotion", "set", "switch" }.Contains(e.Name.LocalName) || e.Attributes().Any(a => a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase))) errors.Add("Active, animated, or conditional SVG content is not supported for ordered export.");
            if (e.Name == Svg + "style") errors.Add("Stylesheets require resolved styles before ordered export; convert rules to inline styles in the source.");
            foreach (var a in e.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                if (a.Name.Namespace == XNamespace.Xml && a.Name.LocalName == "base") errors.Add("XML base URLs must be resolved before export.");
                if ((a.Name.LocalName is "style" or "display" or "visibility") && (a.Value.Contains("!important", StringComparison.OrdinalIgnoreCase) || a.Value.Contains("var(") || Regex.IsMatch(a.Value, @"\b(inherit|unset|revert|initial)\b", RegexOptions.IgnoreCase))) errors.Add("Unresolved CSS inheritance or priority requires source cleanup before ordering.");
                if (a.Name.LocalName == "href" && !a.Value.StartsWith('#') && !(e.Name == Svg + "image" && a.Value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && !a.Value.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase))) errors.Add("External references must be embedded before preview/export.");
                if (Url.Replace(a.Value, "").Contains("url(", StringComparison.OrdinalIgnoreCase)) errors.Add("Only local fragment URL references are supported.");
            }
        }
        foreach (var id in References(root)) if (!ids.Contains(id)) errors.Add($"Broken reference: #{id}.");
        if (ids.Distinct().Count() == ids.Length)
        {
            var byId = elements.Where(e => e.Attribute("id") != null).ToDictionary(e => e.Attribute("id")!.Value);
            void InspectClone(XElement use, HashSet<string> visited)
            {
                var href = use.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value;
                if (href == null || !href.StartsWith('#') || !byId.TryGetValue(href[1..], out var target)) return;
                if (!visited.Add(href[1..])) { errors.Add("Cyclic clone references cannot be exported."); return; }
                var operations = target.DescendantsAndSelf().Select(e => Canonical(Label(e))).Where(o => o != null).Distinct().ToArray();
                if (operations.Length > 1 || target.DescendantsAndSelf().Any(e => Category(e) != null)) errors.Add("A clone contains mixed operations or semantic boundaries; resolve it in the source before export.");
                foreach (var nested in target.DescendantsAndSelf().Where(e => e.Name == Svg + "use")) InspectClone(nested, new(visited));
            }
            foreach (var use in elements.Where(e => e.Name == Svg + "use")) InspectClone(use, new());
        }
        var width = Inches(root.Attribute("width")?.Value, errors);
        var height = Inches(root.Attribute("height")?.Value, errors);
        var items = elements.Select(e =>
        {
            var ancestry = e.AncestorsAndSelf().ToArray();
            var operations = ancestry.Select(a => Canonical(Label(a))).Where(o => o != null).Distinct().ToArray();
            var hidden = Hidden(e);
            var production = e.Name.Namespace == Svg && Geometry.Contains(e.Name.LocalName) && !e.Ancestors().Any(a => a.Name == Svg + "text") && !NonProduction(e) && !hidden;
            return new SvgItem(Key(e), (string?)e.Attribute("id"), Label(e), e.Parent == null ? null : Key(e.Parent), e.Name.LocalName, hidden,
                elements.Any(a => a.Name == Svg + "style") ? "unresolved-stylesheet" : hidden ? Category(e) != null ? "semantic-container" : "hidden-by-self-or-ancestor" : "visible",
                Category(e), production, operations.Length == 1 ? operations[0] : null, ancestry.Select(a => Alias(Label(a))).FirstOrDefault(a => a != null));
        }).ToArray();
        return new(doc, items, errors.Distinct().ToArray(), width, height);
    }

    private static double Inches(string? value, List<string> errors)
    {
        if (value != null && value.EndsWith("in", StringComparison.Ordinal) && double.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n > 0) return n;
        errors.Add("Source width and height must be positive dimensions with explicit in units.");
        return 0;
    }

    public static IReadOnlyDictionary<string, LaserOperation?> Resolve(SvgAnalysis analysis, IReadOnlyDictionary<string, LaserOperation> mappings)
    {
        return analysis.Items.Where(i => i.Production).ToDictionary(i => i.Key, i =>
        {
            var item = i;
            while (true)
            {
                if (mappings.TryGetValue(item.Key, out var op)) return (LaserOperation?)op;
                if (item.Parent == null) break;
                item = analysis.Items.First(p => p.Key == item.Parent);
            }
            return i.Operation;
        });
    }

    public static int Capacity(SvgAnalysis analysis, BatchSettings settings)
    {
        ValidateSettings(settings);
        if (analysis.Width <= 0 || analysis.Height <= 0) throw new FormatException("Source dimensions are invalid.");
        var cols = Math.Max(0, Math.Floor((settings.Width - 2 * settings.HorizontalMargin + settings.Spacing + 1e-9) / (analysis.Width + settings.Spacing)));
        var rows = Math.Max(0, Math.Floor((settings.Height - 2 * settings.VerticalMargin + settings.Spacing + 1e-9) / (analysis.Height + settings.Spacing)));
        return checked((int)(cols * rows));
    }
    private static void ValidateSettings(BatchSettings s)
    {
        if (new[] { s.Width, s.Height, s.HorizontalMargin, s.VerticalMargin, s.Spacing }.Any(n => !double.IsFinite(n)) || s.Width <= 0 || s.Height <= 0 || s.HorizontalMargin < 0 || s.VerticalMargin < 0 || s.Spacing < 0 || s.Copies < 1) throw new FormatException("Sheet dimensions and copies must be positive; margins and spacing must be nonnegative.");
    }

    public static BatchResult Build(SvgAnalysis analysis, string sourceFilename, ProgramMetadata sourceProgram, BatchSettings settings, IReadOnlyDictionary<string, LaserOperation> mappings)
    {
        if (analysis.Errors.Any()) throw new FormatException(string.Join(" ", analysis.Errors));
        var filename = ProgramNameParser.Format(ProgramNameParser.Total(sourceProgram, settings.Copies));
        var total = ProgramNameParser.Parse(filename);
        if (settings.Copies > Capacity(analysis, settings)) throw new FormatException("The requested copies do not fit within the sheet margins.");
        var resolved = Resolve(analysis, mappings);
        if (!resolved.Any()) throw new FormatException("No visible production geometry was found.");
        if (resolved.Any(p => p.Value == null)) throw new FormatException("Assign operations to all unresolved production elements before export.");
        var root = analysis.Document.Root!;
        var activeBuckets = resolved.Values.Select(o => (int)o!.Value).Distinct().OrderBy(o => o).ToArray();
        var refs = References(root).ToHashSet();
        var keys = root.DescendantsAndSelf().ToDictionary(e => e, Key);
        var buckets = root.DescendantsAndSelf().ToDictionary(e => e, e => e.DescendantsAndSelf().Where(n => resolved.ContainsKey(keys[n])).Select(n => (int)resolved[keys[n]]!.Value).Distinct().ToArray());
        var retained = root.DescendantsAndSelf().ToDictionary(e => e, e => e.DescendantsAndSelf().Any(n => buckets[n].Length == 0 && !n.Ancestors().Any(a => resolved.ContainsKey(keys[a]))));
        foreach (var e in root.DescendantsAndSelf().Where(e => buckets[e].Length > 1 || buckets[e].Length == 1 && retained[e] && buckets[e][0] != activeBuckets[0]))
        {
            if (refs.Contains((string?)e.Attribute("id") ?? "")) throw new FormatException($"Referenced container '{Label(e)}' spans operations and cannot be safely split.");
            if (new[] { "opacity", "filter", "mask", "mix-blend-mode", "isolation" }.Any(p => Property(e, p) is string v && v != "none" && v != "1" && v != "normal" && v != "auto")) throw new FormatException($"Container '{Label(e)}' has compositing effects that prevent safe splitting.");
            if (Property(e, "transform-box") is "fill-box" or "stroke-box" || Property(e, "transform-origin") is string origin && origin != "0 0") throw new FormatException($"Container '{Label(e)}' has a bounds-dependent transform that prevents safe splitting.");
            var clip = Property(e, "clip-path");
            if (clip != null && Url.Matches(clip).Any(m => root.Descendants().Any(d => (string?)d.Attribute("id") == m.Groups[1].Value && (string?)d.Attribute("clipPathUnits") == "objectBoundingBox"))) throw new FormatException($"Container '{Label(e)}' uses bounding-box clipping that cannot be safely split.");
        }
        var output = new XElement(Svg + "svg", new XAttribute("width", F(settings.Width) + "in"), new XAttribute("height", F(settings.Height) + "in"), new XAttribute("viewBox", $"0 0 {F(settings.Width * 96)} {F(settings.Height * 96)}"));
        var usedIds = root.DescendantsAndSelf().Attributes("id").Select(a => a.Value).ToHashSet();
        string Allocate(string basis) { var name = basis; var index = 1; while (!usedIds.Add(name)) name = basis + "-" + index++; return name; }
        var pieces = new List<(int Copy, int Bucket, XElement Node)>();
        var inventory = new List<object>();
        var referenceMappings = new List<object>();
        var columns = (int)Math.Floor((settings.Width - 2 * settings.HorizontalMargin + settings.Spacing + 1e-9) / (analysis.Width + settings.Spacing));
        for (var copy = 0; copy < settings.Copies; copy++)
        {
            var idMap = new Dictionary<string, string>();
            var occurrences = new Dictionary<string, int>();
            var elementOutputs = new Dictionary<string, List<string>>();
            var copyPieces = new List<XElement>();
            foreach (var bucket in activeBuckets)
            {
                XElement? Partition(XElement e)
                {
                    var key = keys[e];
                    if (resolved.TryGetValue(key, out var operation) && (int)operation!.Value != bucket) return null;
                    if (buckets[e].Length > 0 && !buckets[e].Contains(bucket) && !(bucket == activeBuckets[0] && retained[e])) return null;
                    if (buckets[e].Length == 0 && bucket != activeBuckets[0] && !e.Ancestors().Any(a => resolved.ContainsKey(keys[a])) ) return null;
                    var clone = new XElement(e.Name, e.Attributes());
                    // Explicit mappings may override source semantics. Keep the original label
                    // as provenance, but do not export contradictory canonical operation labels.
                    if (buckets[e].Contains(bucket) && Canonical(Label(e)) is LaserOperation originalOperation && (int)originalOperation != bucket)
                    {
                        clone.SetAttributeValue("data-laserbatcher-original-label", Label(e));
                        clone.SetAttributeValue(e.Attribute(Inkscape + "label") != null ? Inkscape + "label" : XName.Get("data-label"), ((LaserOperation)bucket).ToString());
                    }
                    if (Category(e) != null && Semantics.Contains(Label(e)) && Property(e, "display") != "none") clone.SetAttributeValue("style", ((string?)clone.Attribute("style") ?? "") + ";display:none");
                    if (e.Attribute("id") is XAttribute id)
                    {
                        var count = occurrences.GetValueOrDefault(id.Value);
                        occurrences[id.Value] = count + 1;
                        var outputId = copy == 0 && count == 0 ? id.Value : Allocate($"{id.Value}-lb-copy-{copy + 1}-op-{bucket}");
                        clone.SetAttributeValue("id", outputId);
                        idMap.TryAdd(id.Value, outputId);
                        if (!elementOutputs.ContainsKey(key)) elementOutputs[key] = new();
                        elementOutputs[key].Add(outputId);
                        referenceMappings.Add(new { copy = copy + 1, sourceId = id.Value, outputId });
                    }
                    else if (Hidden(e))
                    {
                        var outputId = Allocate($"lb-hidden-{key.Replace('/', '-')}-copy-{copy + 1}");
                        clone.SetAttributeValue("id", outputId);
                        elementOutputs[key] = new() { outputId };
                    }
                    foreach (var node in e.Nodes())
                    {
                        if (node is XElement child) { var part = Partition(child); if (part != null) clone.Add(part); }
                        else clone.Add(CloneNode(node));
                    }
                    return clone;
                }
                var partition = Partition(root)!;
                // Retain viewport context during partitioning; convert it to group transforms below.
                var x = (settings.HorizontalMargin + copy % columns * (analysis.Width + settings.Spacing)) * 96;
                var y = (settings.VerticalMargin + copy / columns * (analysis.Height + settings.Spacing)) * 96;
                var operationLabel = ((LaserOperation)bucket).ToString();
                var wrapper = new XElement(Svg + "g", new XAttribute("transform", $"translate({F(x)},{F(y)})"), new XAttribute("data-laserbatcher-copy", copy + 1), new XAttribute("data-laserbatcher-operation", operationLabel), new XAttribute("data-label", operationLabel), partition);
                if (root.DescendantsAndSelf().Attributes().Any(a => a.Name.Namespace == Inkscape))
                {
                    wrapper.SetAttributeValue(Inkscape + "label", operationLabel);
                    wrapper.SetAttributeValue(Inkscape + "groupmode", "layer");
                }
                pieces.Add((copy, bucket, wrapper));
                copyPieces.Add(wrapper);
            }
            foreach (var piece in copyPieces) RewriteReferences(piece, idMap);
            foreach (var item in analysis.Items.Where(i => i.Hidden || i.SemanticCategory != null)) inventory.Add(new { copy = copy + 1, sourceKey = item.Key, sourceId = item.Id, outputIds = elementOutputs.GetValueOrDefault(item.Key) ?? new(), item.Label, item.Parent, item.Element, item.Hidden, item.Visibility, item.SemanticCategory,
                declaredDisplay = Property(root.DescendantsAndSelf().First(e => keys[e] == item.Key), "display"), declaredVisibility = Property(root.DescendantsAndSelf().First(e => keys[e] == item.Key), "visibility") });
        }
        foreach (var piece in settings.Ordering == OrderingMode.WithinEachCopy ? pieces.OrderBy(p => p.Copy).ThenBy(p => p.Bucket) : pieces.OrderBy(p => p.Bucket).ThenBy(p => p.Copy)) output.Add(piece.Node);
        var metadata = new { schemaVersion = 1, program = total, sourceFilename, sourceYield = sourceProgram.Yield, copyCount = settings.Copies, totalYield = total.Yield, orderingMode = settings.Ordering.ToString(), sheet = new { width = settings.Width, height = settings.Height, horizontalMargin = settings.HorizontalMargin, verticalMargin = settings.VerticalMargin, spacing = settings.Spacing, units = "in", svgUnitsPerInch = 96 }, operationMappings = resolved.Select(p => new { sourceKey = p.Key, operation = p.Value.ToString(), explicitlyAssigned = mappings.ContainsKey(p.Key) }), hiddenLayers = inventory, referenceMappings };
        output.AddFirst(new XElement(Svg + "metadata", new XElement(MetadataNamespace + "batch", new XAttribute("schemaVersion", "1"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))));
        var outputIds = output.DescendantsAndSelf().Attributes("id").Select(a => a.Value).ToArray();
        if (outputIds.Length != outputIds.Distinct().Count() || References(output).Any(r => !outputIds.Contains(r))) throw new FormatException("Export reference validation failed.");
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null));
        foreach (var node in analysis.Document.Nodes()) document.Add(node is XElement ? output : CloneNode(node));
        return new(SvgViewportNormalizer.Flatten(document), filename);
    }

    private static XNode CloneNode(XNode node) => node switch { XCData c => new XCData(c.Value), XText t => new XText(t.Value), XComment c => new XComment(c.Value), XProcessingInstruction p => new XProcessingInstruction(p.Target, p.Data), _ => throw new FormatException("Unsupported XML node.") };
    private static IEnumerable<string> References(XElement root) => root.DescendantsAndSelf().Attributes().Where(a => !a.IsNamespaceDeclaration).SelectMany(a => a.Name.LocalName == "href" && a.Value.StartsWith('#') ? new[] { a.Value[1..] } : a.Name.LocalName is "aria-labelledby" or "aria-describedby" ? a.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) : Url.Matches(a.Value).Select(m => m.Groups[1].Value));
    private static void RewriteReferences(XElement root, Dictionary<string, string> ids)
    {
        foreach (var a in root.DescendantsAndSelf().Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (a.Name.LocalName == "href" && a.Value.StartsWith('#')) a.Value = "#" + ids.GetValueOrDefault(a.Value[1..], a.Value[1..]);
            else if (a.Name.LocalName is "aria-labelledby" or "aria-describedby") a.Value = string.Join(" ", a.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(id => ids.GetValueOrDefault(id, id)));
            else a.Value = Url.Replace(a.Value, m => "url(#" + ids.GetValueOrDefault(m.Groups[1].Value, m.Groups[1].Value) + ")");
        }
    }
}
