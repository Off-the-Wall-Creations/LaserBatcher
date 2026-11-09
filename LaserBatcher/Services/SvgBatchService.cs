using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LaserBatcher.Services;

public enum LaserOperation { Engrave, Score, InteriorCut, ExteriorCut }
public enum SvgAssignment { Engrave, Score, InteriorCut, ExteriorCut, Ignore }
public enum OrderingMode { WithinEachCopy, AcrossSheet }
public sealed record BatchSettings(double Width, double Height, double HorizontalMargin, double VerticalMargin, double Spacing, int Copies, OrderingMode Ordering);
public sealed record SvgItem(string Key, string? Id, string Label, string? Parent, string Element, bool Hidden, string Visibility, string? SemanticCategory, bool Production, LaserOperation? Operation, string? Suggestion);
public sealed record SvgAnalysis(XDocument Document, IReadOnlyList<SvgItem> Items, IReadOnlyList<string> Errors, double Width, double Height)
{
    public XDocument SourceDocument { get; init; } = Document;
    public ImportedMetadata? Metadata { get; init; }
    public GlowforgeAnalysis Glowforge { get; init; } = new(Array.Empty<GlowforgeStep>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
public sealed record BatchResult(XDocument Document, string Filename)
{
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public static class SvgBatchService
{
    public static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    public static readonly XNamespace Inkscape = "http://www.inkscape.org/namespaces/inkscape";
    public static readonly XNamespace MetadataNamespace = SvgMetadataService.Namespace;
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
        var source = SvgMetadataService.Parse(xml);
        var metadata = SvgMetadataService.Read(source);
        var doc = new XDocument(source);
        SvgClassStyles.Resolve(doc);
        var glowforge = GlowforgeSettings.Analyze(doc);
        var errors = new List<string>();
        errors.AddRange(glowforge.Errors);
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
                Category(e), production, operations.Length == 1 ? operations[0] : null,
                ancestry.Select(a => Alias(Label(a))).FirstOrDefault(a => a != null) ?? glowforge.Steps.FirstOrDefault(s => s.ArtworkKeys.Contains(Key(e)))?.Suggestion);
        }).ToArray();
        return new(doc, items, errors.Distinct().ToArray(), width, height)
        {
            SourceDocument = source, Metadata = metadata.Metadata, Glowforge = glowforge,
            Warnings = metadata.Warnings.Concat(glowforge.Warnings).ToArray()
        };
    }

    private static double Inches(string? value, List<string> errors)
    {
        try { return SvgLengths.PhysicalInches(value); }
        catch (FormatException ex) { errors.Add(ex.Message); }
        return 0;
    }

    public static IReadOnlyDictionary<string, SvgAssignment?> Resolve(SvgAnalysis analysis, IReadOnlyDictionary<string, SvgAssignment> mappings)
    {
        var byKey = analysis.Items.ToDictionary(i => i.Key);
        if (mappings.Values.Any(value => !Enum.IsDefined(typeof(SvgAssignment), value))) throw new FormatException("Unknown SVG assignment.");
        return analysis.Items.Where(i => i.Production).ToDictionary(i => i.Key, i =>
        {
            var item = i;
            while (true)
            {
                if (mappings.TryGetValue(item.Key, out var op)) return (SvgAssignment?)op;
                if (item.Parent == null) break;
                item = byKey[item.Parent];
            }
            return i.Operation == null ? null : (SvgAssignment?)i.Operation.Value;
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

    public static BatchResult Build(SvgAnalysis analysis, string sourceFilename, ProgramMetadata sourceProgram, BatchSettings settings, IReadOnlyDictionary<string, SvgAssignment> mappings, long? estimatedRuntimeSeconds = null)
    {
        EstimatedRuntime.Validate(estimatedRuntimeSeconds);
        if (analysis.Errors.Any()) throw new FormatException(string.Join(" ", analysis.Errors));
        var filename = ProgramNameParser.Format(ProgramNameParser.Total(sourceProgram, settings.Copies));
        var total = ProgramNameParser.Parse(filename);
        if (settings.Copies > Capacity(analysis, settings)) throw new FormatException("The requested copies do not fit within the sheet margins.");
        var assignments = Resolve(analysis, mappings);
        var ignored = assignments.Where(p => p.Value == SvgAssignment.Ignore).Select(p => p.Key).ToHashSet();
        var resolved = assignments.Where(p => p.Value != SvgAssignment.Ignore).ToDictionary(p => p.Key, p => p.Value);
        if (!resolved.Any()) throw new FormatException("Include at least one production element.");
        if (resolved.Any(p => p.Value == null)) throw new FormatException("Assign operations to all unresolved production elements before export.");
        var glowforgeBindings = analysis.Glowforge.Steps.SelectMany(s => s.ArtworkKeys.Select(k => (Key: k, s.Class))).ToDictionary(p => p.Key, p => p.Class);
        if (analysis.Glowforge.Steps.Count > 0 && resolved.Keys.Any(k => !glowforgeBindings.ContainsKey(k)))
            throw new FormatException("Visible artwork has no Glowforge settings binding. Configure it in the source before exporting native ordered steps.");
        var root = new XElement(analysis.Document.Root!);
        var keys = root.DescendantsAndSelf().ToDictionary(e => e, Key);
        bool Survives(XElement e) => !e.AncestorsAndSelf().Any(a => ignored.Contains(keys[a]));
        PreserveIgnoredChildContent(root, keys, ignored);
        PreserveIgnoredDependencies(root, keys, ignored);
        var activeBuckets = resolved.Values.Select(o => (int)o!.Value).Distinct().OrderBy(o => o).ToArray();
        var refs = root.DescendantsAndSelf().Where(Survives).SelectMany(ElementReferences).ToHashSet();
        var buckets = root.DescendantsAndSelf().ToDictionary(e => e, e => e.DescendantsAndSelf().Where(n => resolved.ContainsKey(keys[n])).Select(n => (int)resolved[keys[n]]!.Value).Distinct().ToArray());
        var retained = root.DescendantsAndSelf().ToDictionary(e => e, e => e.DescendantsAndSelf().Any(n => Survives(n) && buckets[n].Length == 0 && !n.Ancestors().Any(a => resolved.ContainsKey(keys[a]))));
        foreach (var e in root.DescendantsAndSelf().Where(e => buckets[e].Length > 0 && e.Descendants().Any(n => ignored.Contains(keys[n]))))
        {
            var clip = Property(e, "clip-path");
            if (Property(e, "transform-box") is "fill-box" or "stroke-box" || Property(e, "transform-origin") is string origin && origin != "0 0" ||
                Property(e, "filter") is string filter && filter != "none" ||
                clip != null && Url.Matches(clip).Any(m => root.Descendants().Any(d => (string?)d.Attribute("id") == m.Groups[1].Value && (string?)d.Attribute("clipPathUnits") == "objectBoundingBox")))
                throw new FormatException($"Ignoring artwork in '{Label(e)}' changes bounds used by a transform, filter or clip. Restore that artwork or resolve the effect in the source.");
        }
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
                    if (ignored.Contains(key)) return null;
                    if (resolved.TryGetValue(key, out var operation) && (int)operation!.Value != bucket) return null;
                    if (buckets[e].Length > 0 && !buckets[e].Contains(bucket) && !(bucket == activeBuckets[0] && retained[e])) return null;
                    if (buckets[e].Length == 0 && bucket != activeBuckets[0] && !e.Ancestors().Any(a => resolved.ContainsKey(keys[a])) ) return null;
                    var clone = new XElement(e.Name, e.Attributes());
                    if (resolved.ContainsKey(key) && glowforgeBindings.TryGetValue(key, out var binding)) clone.AddAnnotation(new GlowforgeBinding(binding, key));
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
                        referenceMappings.Add(new { copy = copy + 1, sourceId = e.Annotation<DependencyOrigin>()?.SourceId ?? id.Value, outputId });
                    }
                    else if (Hidden(e))
                    {
                        var outputId = Allocate($"lb-hidden-{key.Replace('/', '-')}-copy-{copy + 1}");
                        clone.SetAttributeValue("id", outputId);
                        elementOutputs[key] = new() { outputId };
                    }
                    if (e.Annotation<PreservedKey>() is PreservedKey preserved && clone.Attribute("id") is XAttribute preservedId)
                    {
                        if (!elementOutputs.TryGetValue(preserved.SourceKey, out var outputs)) elementOutputs[preserved.SourceKey] = outputs = new();
                        outputs.Add(preservedId.Value);
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
        var warnings = new List<string>();
        var stepMappings = GlowforgeSettings.Rewrite(output, analysis.Glowforge, warnings);
        var metadata = JsonSerializer.SerializeToNode(new { schemaVersion = 2, program = total, sourceFilename, sourceYield = sourceProgram.Yield, copyCount = settings.Copies, totalYield = total.Yield, orderingMode = settings.Ordering.ToString(), sheet = new { width = settings.Width, height = settings.Height, horizontalMargin = settings.HorizontalMargin, verticalMargin = settings.VerticalMargin, spacing = settings.Spacing, units = "in", svgUnitsPerInch = 96 }, operationMappings = resolved.Select(p => new { sourceKey = p.Key, operation = p.Value.ToString(), explicitlyAssigned = mappings.ContainsKey(p.Key) }), hiddenLayers = inventory, referenceMappings }, SvgMetadataService.JsonOptions)!.AsObject();
        SvgMetadataService.SetRuntime(metadata, estimatedRuntimeSeconds);
        if (ignored.Count > 0)
        {
            var items = analysis.Items.ToDictionary(i => i.Key);
            string AssignedBy(SvgItem item)
            {
                while (!mappings.ContainsKey(item.Key) && item.Parent != null) item = items[item.Parent];
                return item.Key;
            }
            metadata["ignoredElements"] = JsonSerializer.SerializeToNode(analysis.Items.Where(i => ignored.Contains(i.Key))
                .Select(i => new { sourceKey = i.Key, sourceId = i.Id, i.Label, assignedByKey = AssignedBy(i) }), SvgMetadataService.JsonOptions);
        }
        if (analysis.Glowforge.Steps.Count > 0)
            metadata["vendorProvenance"] = JsonSerializer.SerializeToNode(new { glowforge = new { originalDefinitions = analysis.Glowforge.OriginalDefinitions, reportLines = analysis.Glowforge.ReportLines, stepMappings } }, SvgMetadataService.JsonOptions);
        output.AddFirst(new XElement(Svg + "metadata", new XElement(MetadataNamespace + "batch", new XAttribute("schemaVersion", "2"), metadata.ToJsonString())));
        var outputIds = output.DescendantsAndSelf().Attributes("id").Select(a => a.Value).ToArray();
        if (outputIds.Length != outputIds.Distinct().Count() || References(output).Any(r => !outputIds.Contains(r))) throw new FormatException("Export reference validation failed.");
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null));
        foreach (var node in analysis.Document.Nodes()) document.Add(node is XElement ? output : CloneNode(node));
        return new(SvgViewportNormalizer.Flatten(document), filename) { Warnings = warnings };
    }

    private static XNode CloneNode(XNode node) => node switch { XCData c => new XCData(c.Value), XText t => new XText(t.Value), XComment c => new XComment(c.Value), XProcessingInstruction p => new XProcessingInstruction(p.Target, p.Data), _ => throw new FormatException("Unsupported XML node.") };
    private static IEnumerable<string> ElementReferences(XElement element) => element.Attributes().Where(a => !a.IsNamespaceDeclaration).SelectMany(a => a.Name.LocalName == "href" && a.Value.StartsWith('#') ? new[] { a.Value[1..] } : a.Name.LocalName is "aria-labelledby" or "aria-describedby" ? a.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) : Url.Matches(a.Value).Select(m => m.Groups[1].Value));
    private static IEnumerable<string> References(XElement root) => root.DescendantsAndSelf().SelectMany(ElementReferences);
    private sealed record DependencyOrigin(string SourceId);
    private sealed record PreservedKey(string SourceKey);

    private static void PreserveIgnoredChildContent(XElement root, Dictionary<XElement, string> keys, HashSet<string> ignored)
    {
        // A production text/shape can itself contain hidden text, licensing or
        // metadata. Keep only those child branches in inert definitions, retaining
        // ancestor context; ordinary visible text is not copied into this archive.
        var targets = root.Descendants().Where(e => ignored.Contains(keys[e]) && !e.Ancestors().Any(a => ignored.Contains(keys[a]))).ToArray();
        var definitions = new XElement(Svg + "defs", new XAttribute("data-laserbatcher-preserved-content", "ignored-descendants"));
        bool Protected(XElement e) => Hidden(e) || NonProduction(e) || e.Name.Namespace != Svg || e.Name.LocalName is "title" or "desc";
        XElement? Retain(XElement source, bool top = false)
        {
            if (!top && Protected(source))
            {
                var complete = new XElement(source);
                foreach (var pair in source.DescendantsAndSelf().Zip(complete.DescendantsAndSelf())) pair.Second.AddAnnotation(new PreservedKey(keys[pair.First]));
                return complete;
            }
            var children = source.Elements().Select(e => Retain(e)).Where(e => e != null).ToArray();
            if (children.Length == 0) return null;
            var shell = new XElement(source.Name, source.Attributes(), children);
            shell.AddAnnotation(new PreservedKey(keys[source]));
            return shell;
        }
        var used = root.DescendantsAndSelf().Attributes("id").Select(a => a.Value).ToHashSet();
        foreach (var target in targets)
        {
            var branch = Retain(target, true);
            if (branch == null) continue;
            foreach (var parent in target.Ancestors().TakeWhile(e => e != root)) branch = new XElement(parent.Name, parent.Attributes(), branch);
            var ids = new Dictionary<string, string>();
            foreach (var e in branch.DescendantsAndSelf())
                if (e.Attribute("id") is XAttribute id)
                {
                    var original = id.Value; var name = original + "-lb-preserved"; var n = 1;
                    while (!used.Add(name)) name = original + "-lb-preserved-" + n++;
                    ids[original] = name; id.Value = name; e.AddAnnotation(new DependencyOrigin(original));
                }
            RewriteReferences(branch, ids);
            definitions.Add(branch);
        }
        if (!definitions.HasElements) return;
        var index = 0;
        foreach (var e in definitions.DescendantsAndSelf()) keys[e] = "preserved/" + index++;
        root.AddFirst(definitions);
    }

    private static void PreserveIgnoredDependencies(XElement root, Dictionary<XElement, string> keys, HashSet<string> ignored)
    {
        if (ignored.Count == 0) return;
        var originals = root.DescendantsAndSelf().ToArray();
        var byId = originals.Where(e => e.Attribute("id") != null).ToDictionary(e => e.Attribute("id")!.Value);
        bool Affected(XElement e) => e.AncestorsAndSelf().Any(a => ignored.Contains(keys[a])) || e.Descendants().Any(d => ignored.Contains(keys[d]));
        var required = originals.Where(e => !e.AncestorsAndSelf().Any(a => ignored.Contains(keys[a]))).SelectMany(ElementReferences).ToHashSet();
        var templates = new Dictionary<string, (XElement Node, Dictionary<string, string> Ids)>();
        var used = byId.Keys.ToHashSet();
        string Allocate(string id) { var next = id + "-lb-definition"; var n = 1; while (!used.Add(next)) next = id + "-lb-definition-" + n++; return next; }
        var pending = new Queue<string>(required);
        while (pending.TryDequeue(out var id))
        {
            if (templates.ContainsKey(id) || !byId.TryGetValue(id, out var source) || !Affected(source)) continue;
            if (source == root || source.AncestorsAndSelf().Any(e => Property(e, "transform-box") is "fill-box" or "stroke-box" || Property(e, "transform-origin") is string origin && origin != "0 0"))
                throw new FormatException($"Ignored dependency '{Label(source)}' has a bounds-dependent transform. Restore it or ignore its dependent artwork.");
            if (source.Ancestors().Any(e => e != root && e.Name == Svg + "svg"))
                throw new FormatException($"Ignored dependency '{Label(source)}' is inside a nested SVG viewport. Flatten that viewport in the source, restore the artwork, or ignore its dependent artwork.");
            // Clone semantics inherit through the <use> instance, not the target's
            // original ancestors. Preserve the referenced subtree without baking
            // parent transforms/styles into it or applying occurrence assignments.
            var template = new XElement(source);
            var declared = template.Attributes().Where(a => a.IsNamespaceDeclaration).Select(a => a.Name).ToHashSet();
            foreach (var ns in source.Ancestors().SelectMany(e => e.Attributes()).Where(a => a.IsNamespaceDeclaration))
                if (declared.Add(ns.Name)) template.Add(new XAttribute(ns));
            var ids = new Dictionary<string, string>();
            foreach (var node in template.DescendantsAndSelf())
                if (node.Attribute("id") is XAttribute attr)
                {
                    var original = attr.Value;
                    ids[original] = Allocate(original); attr.Value = ids[original];
                    node.AddAnnotation(new DependencyOrigin(original));
                }
            templates[id] = (template, ids);
            foreach (var reference in References(source)) pending.Enqueue(reference);
        }
        if (templates.Count == 0) return;
        var redirects = templates.ToDictionary(p => p.Key, p => p.Value.Ids[p.Key]);
        RewriteReferences(root, redirects);
        var definitions = new XElement(Svg + "defs");
        foreach (var template in templates.Values)
        {
            var ids = new Dictionary<string, string>(redirects);
            foreach (var id in template.Ids) ids[id.Key] = id.Value;
            RewriteReferences(template.Node, ids);
            definitions.Add(template.Node);
        }
        var index = 0;
        foreach (var node in definitions.DescendantsAndSelf()) keys[node] = "dependency/" + index++;
        root.AddFirst(definitions);
    }
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
