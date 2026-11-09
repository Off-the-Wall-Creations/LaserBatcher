using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LaserBatcher.Services;

public sealed record GlowforgeStep(string Class, int Order, IReadOnlyDictionary<string, string> RawAttributes,
    IReadOnlyList<string> ArtworkKeys, string? Suggestion, IReadOnlyList<string> ReportLines, XElement Definition);
public sealed record GlowforgeAnalysis(IReadOnlyList<GlowforgeStep> Steps, IReadOnlyList<string> ReportLines,
    IReadOnlyList<string> OriginalDefinitions, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
public sealed record GlowforgeStepMapping(string SourceClass, int SourceOrder, string OutputClass, int OutputOrder,
    int Copy, string Operation, IReadOnlyList<string> SourceKeys);
internal sealed record GlowforgeBinding(string Class, string SourceKey);

public static class GlowforgeSettings
{
    public static readonly XNamespace Namespace = "http://glowforge.com/namespaces/gf";
    private static readonly HashSet<string> Artwork = new() { "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "image", "use" };
    private static string Key(XElement e) => string.Join("/", e.AncestorsAndSelf().Reverse().Select(n => n.ElementsBeforeSelf().Count()));
    private static XElement CopyWithNamespaces(XElement source)
    {
        var copy = new XElement(source);
        // QName-valued vendor fields may depend on aliases declared on any ancestor.
        var declared = copy.Attributes().Where(a => a.IsNamespaceDeclaration).Select(a => a.Name).ToHashSet();
        foreach (var attribute in source.Ancestors().SelectMany(e => e.Attributes()).Where(a => a.IsNamespaceDeclaration))
            if (declared.Add(attribute.Name)) copy.Add(new XAttribute(attribute));
        return copy;
    }
    private static string? Stroke(XElement e)
    {
        foreach (var node in e.AncestorsAndSelf())
        {
            var matches = Regex.Matches((string?)node.Attribute("style") ?? "", @"(?:^|;)\s*stroke\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (matches.Count > 0) return matches[^1].Groups[1].Value.Trim();
            if (node.Attribute("stroke") is XAttribute stroke) return stroke.Value;
        }
        return null;
    }

    public static GlowforgeAnalysis Analyze(XDocument document)
    {
        var containers = document.Descendants(Namespace + "laserdefs").Where(e => !e.Ancestors(SvgBatchService.Svg + "metadata").Any()).ToArray();
        var definitions = containers.SelectMany(e => e.Elements(Namespace + "laserdef")).ToArray();
        var errors = new List<string>();
        var warnings = new List<string>();
        var original = containers.Select(e => CopyWithNamespaces(e).ToString(SaveOptions.DisableFormatting)).ToArray();
        var classes = definitions.Select(e => (string?)e.Attribute("class") ?? "").ToArray();
        if (classes.Any(c => !Regex.IsMatch(c, @"^[a-zA-Z_][a-zA-Z0-9_-]*$")) || classes.Distinct().Count() != classes.Length)
            errors.Add("Glowforge definitions must have unique, single class bindings.");
        if (containers.Any(e => e.DescendantsAndSelf().Attributes("id").Any()))
            errors.Add("ID-bearing Glowforge definitions cannot be safely split into ordered steps; resolve their references in the source.");
        var orders = new List<int>();
        foreach (var definition in definitions)
        {
            if (!int.TryParse((string?)definition.Attribute("order"), NumberStyles.None, CultureInfo.InvariantCulture, out var order))
                errors.Add("Glowforge step orders must be nonnegative integers.");
            else orders.Add(order);
        }
        if (orders.Distinct().Count() != orders.Count) errors.Add("Duplicate Glowforge step orders are ambiguous.");
        var artwork = document.Descendants().Where(e => e.Name.Namespace == SvgBatchService.Svg && Artwork.Contains(e.Name.LocalName) && !e.Ancestors().Any(a => a.Name == SvgBatchService.Svg + "metadata")).ToArray();
        foreach (var element in document.Descendants().Where(e => e.Name.Namespace == SvgBatchService.Svg))
        {
            var bindings = SvgClassStyles.Tokens(element).Intersect(classes).ToArray();
            if (bindings.Length > 1) errors.Add($"Artwork '{Key(element)}' has conflicting Glowforge bindings.");
            if (bindings.Length > 0 && (!Artwork.Contains(element.Name.LocalName) || element.Name.LocalName == "use"))
                errors.Add("Glowforge bindings on containers or clones require source correction before ordered export.");
        }

        var reports = new List<string>();
        var reportSteps = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var boundColors = artwork.Where(e => SvgClassStyles.Tokens(e).Intersect(classes).Any()).Select(Stroke)
            .Where(s => s != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (definitions.Length > 0)
        {
            foreach (var text in document.Descendants(SvgBatchService.Svg + "text"))
            {
                var lines = text.Elements(SvgBatchService.Svg + "tspan").Select(e => e.Value).ToArray();
                var stepColors = lines.Select(l => Regex.Match(l, @"^Step [0-9]+: (#[0-9a-fA-F]{6}) "))
                    .Where(m => m.Success).Select(m => m.Groups[1].Value).ToArray();
                var isReport = lines.FirstOrDefault() == "Glowforge® print settings" && lines.Any(l => l.StartsWith("Design id: ", StringComparison.Ordinal)) &&
                    lines.Any(l => l.StartsWith("Timestamp: ", StringComparison.Ordinal)) && lines.Contains("Version: 1.0") &&
                    stepColors.Length > 0 && stepColors.All(boundColors.Contains);
                if (!isReport) continue;
                reports.AddRange(lines);
                // A previously archived report describes its source, not regenerated steps.
                if (text.Attribute("data-laserbatcher-settings-report") == null)
                {
                    foreach (var line in lines)
                    {
                        var match = Regex.Match(line, @"^Step [0-9]+: (#[0-9a-fA-F]{6}) ");
                        if (!match.Success) continue;
                        var color = match.Groups[1].Value;
                        if (!reportSteps.ContainsKey(color)) reportSteps[color] = new();
                        reportSteps[color].Add(line);
                    }
                }
                text.SetAttributeValue("data-laserbatcher-settings-report", "glowforge");
                var style = (string?)text.Attribute("style") ?? "";
                if (!Regex.IsMatch(style, @"(?:^|;)\s*display\s*:\s*none\s*(?:;|$)", RegexOptions.IgnoreCase)) text.SetAttributeValue("style", style + ";display:none");
            }
        }

        var steps = new List<GlowforgeStep>();
        foreach (var definition in definitions)
        {
            var binding = (string?)definition.Attribute("class") ?? "";
            var linked = artwork.Where(e => SvgClassStyles.Tokens(e).Contains(binding)).ToArray();
            var colors = linked.Select(Stroke).Where(c => c != null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var lines = colors.Length == 1 && reportSteps.TryGetValue(colors[0]!, out var found) ? found.ToArray() : Array.Empty<string>();
            var setting = ((string?)definition.Attribute("settingid") ?? "").Split("::")[0].ToLowerInvariant();
            var type = ((string?)definition.Attribute("type") ?? "").ToLowerInvariant();
            var reported = lines.Select(l => Regex.Match(l, @"^Step [0-9]+: #[0-9a-fA-F]{6} (Score|Cut|Engrave)::", RegexOptions.IgnoreCase))
                .Where(m => m.Success).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToArray();
            var intent = setting is "score" or "cut" or "engrave" ? setting : type is "score" or "engrave" ? type : null;
            var disagreement = reported.Length > 1 || reported.Length == 1 && intent != null && reported[0] != intent ||
                type == "engrave" && (setting is "score" or "cut") || type == "score" && (setting is "engrave" or "cut");
            if (disagreement) warnings.Add($"Glowforge step '{binding}' has conflicting operation evidence; map it explicitly.");
            string? suggestion = !disagreement ? intent == "score" ? "Score" : intent == "engrave" ? "Engrave" : null : null;
            steps.Add(new(binding, int.TryParse((string?)definition.Attribute("order"), out var order) ? order : 0,
                definition.Attributes().Where(a => !a.IsNamespaceDeclaration).ToDictionary(a => a.Name.ToString(), a => a.Value),
                linked.Select(Key).ToArray(), suggestion, lines, CopyWithNamespaces(definition)));
        }
        return new(steps, reports, original, errors.Distinct().ToArray(), warnings.Distinct().ToArray());
    }

    internal static IReadOnlyList<GlowforgeStepMapping> Rewrite(XElement output, GlowforgeAnalysis analysis, List<string> warnings)
    {
        if (analysis.Steps.Count == 0) return Array.Empty<GlowforgeStepMapping>();
        var source = analysis.Steps.ToDictionary(s => s.Class);
        var usedClasses = output.DescendantsAndSelf().SelectMany(SvgClassStyles.Tokens).ToHashSet(StringComparer.Ordinal);
        var definitions = new XElement(Namespace + "laserdefs", new XAttribute(XNamespace.Xmlns + "gf", Namespace.NamespaceName));
        var mappings = new List<GlowforgeStepMapping>();
        foreach (var wrapper in output.Elements(SvgBatchService.Svg + "g"))
        {
            var elements = wrapper.Descendants().Where(e => e.Annotation<GlowforgeBinding>() != null).ToArray();
            foreach (var group in elements.GroupBy(e => e.Annotation<GlowforgeBinding>()!.Class))
            {
                var order = mappings.Count;
                var binding = "lb-gf-step-" + order;
                var suffix = 0;
                while (!usedClasses.Add(binding)) binding = "lb-gf-step-" + order + "-" + ++suffix;
                var step = source[group.Key];
                var definition = new XElement(step.Definition);
                definition.SetAttributeValue("class", binding);
                definition.SetAttributeValue("order", order);
                definitions.Add(definition);
                foreach (var element in group)
                    element.SetAttributeValue("class", string.Join(" ", SvgClassStyles.Tokens(element).Where(t => !source.ContainsKey(t)).Append(binding)));
                mappings.Add(new(step.Class, step.Order, binding, order, (int)wrapper.Attribute("data-laserbatcher-copy")!,
                    (string)wrapper.Attribute("data-laserbatcher-operation")!, group.Select(e => e.Annotation<GlowforgeBinding>()!.SourceKey).ToArray()));
            }
        }
        foreach (var original in output.Descendants(Namespace + "laserdefs").ToArray()) original.Remove();
        output.AddFirst(definitions);
        var prior = analysis.Steps.OrderBy(s => s.Order).Select(s => s.Class).ToArray();
        if (!prior.SequenceEqual(mappings.Select(m => m.SourceClass)) || mappings.Any(m => m.SourceOrder != m.OutputOrder))
            warnings.Add("Glowforge native step order was changed to match LaserBatcher's operation and copy ordering. Original definitions are preserved in vendor provenance.");
        return mappings;
    }
}
