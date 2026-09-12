using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LaserBatcher.Services;

/// <summary>Replaces nested SVG viewports with ordinary groups without changing their coordinate systems.</summary>
public static class SvgViewportNormalizer
{
    private static readonly XNamespace Svg = SvgBatchService.Svg;
    private static readonly HashSet<string> ViewportAttributes = new() { "x", "y", "width", "height", "viewBox", "preserveAspectRatio", "version", "baseProfile", "overflow" };
    private static string F(double n) => n.ToString("G17", CultureInfo.InvariantCulture);

    public static XDocument Flatten(XDocument source)
    {
        var result = new XDocument(source);
        var root = result.Root ?? throw new FormatException("SVG has no root.");
        var ids = root.DescendantsAndSelf().Attributes("id").Select(a => a.Value).ToHashSet();
        var counter = 0;
        string NextId() { string id; do { id = "lb-viewport-clip-" + ++counter; } while (!ids.Add(id)); return id; }
        var rootWidth = Length(root.Attribute("width")?.Value, 0, 0);
        var rootHeight = Length(root.Attribute("height")?.Value, 0, 0);
        var rootBox = ViewBox(root);
        var viewportWidth = rootBox?[2] ?? rootWidth;
        var viewportHeight = rootBox?[3] ?? rootHeight;

        void Visit(XElement parent, double parentWidth, double parentHeight)
        {
            foreach (var child in parent.Elements().ToArray())
            {
                if (child.Name != Svg + "svg") { Visit(child, parentWidth, parentHeight); continue; }
                var style = child.Attribute("style")?.Value ?? "";
                if (Regex.IsMatch(style, @"(?:^|;)\s*(?:x|y|width|height|transform|transform-origin|transform-box)\s*:", RegexOptions.IgnoreCase))
                    throw new FormatException("SVG viewport dimensions/transforms declared in CSS must be converted to attributes before group export.");
                var x = Length(child.Attribute("x")?.Value, parentWidth, 0);
                var y = Length(child.Attribute("y")?.Value, parentHeight, 0);
                var width = Length(child.Attribute("width")?.Value, parentWidth, parentWidth);
                var height = Length(child.Attribute("height")?.Value, parentHeight, parentHeight);
                if (width <= 0 || height <= 0) throw new FormatException("Nested SVG viewport dimensions must be positive.");
                var box = ViewBox(child);
                var userWidth = box?[2] ?? width;
                var userHeight = box?[3] ?? height;
                // Percentage geometry refers to the nearest viewport. Once it becomes a group,
                // leaving percentages untouched would refer to the sheet instead.
                ResolvePercentages(child, userWidth, userHeight);
                Visit(child, userWidth, userHeight);
                var sx = box == null ? 1 : width / box[2];
                var sy = box == null ? 1 : height / box[3];
                var tx = box == null ? 0 : -box[0] * sx;
                var ty = box == null ? 0 : -box[1] * sy;
                var aspect = (child.Attribute("preserveAspectRatio")?.Value ?? "xMidYMid meet").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (aspect.FirstOrDefault() == "defer") aspect.RemoveAt(0);
                var align = aspect.FirstOrDefault() ?? "xMidYMid";
                if (aspect.Count > 2 || (aspect.Count == 2 && aspect[1] is not ("meet" or "slice")) ||
                    !Regex.IsMatch(align, @"^(none|x(Min|Mid|Max)Y(Min|Mid|Max))$"))
                    throw new FormatException("Unsupported preserveAspectRatio value.");
                if (box != null && align != "none")
                {
                    sx = sy = aspect.LastOrDefault() == "slice" ? Math.Max(sx, sy) : Math.Min(sx, sy);
                    tx = -box[0] * sx + (width - box[2] * sx) * (align.Contains("xMax") ? 1 : align.Contains("xMid") ? .5 : 0);
                    ty = -box[1] * sy + (height - box[3] * sy) * (align.Contains("YMax") ? 1 : align.Contains("YMid") ? .5 : 0);
                }
                var group = new XElement(Svg + "g", child.Attributes().Where(a => a.IsNamespaceDeclaration || a.Name.Namespace != XNamespace.None || !ViewportAttributes.Contains(a.Name.LocalName)));
                foreach (var name in ViewportAttributes)
                    if (child.Attribute(name) is XAttribute value) group.SetAttributeValue("data-laserbatcher-source-" + name.ToLowerInvariant(), value.Value);
                // Keep an existing transform on the outer group; viewport placement and viewBox
                // mapping happen inside it, in the same order as the original nested SVG.
                var position = new XElement(Svg + "g", new XAttribute("transform", $"translate({F(x)},{F(y)})"));
                var content = new XElement(Svg + "g", new XAttribute("transform", $"matrix({F(sx)},0,0,{F(sy)},{F(tx)},{F(ty)})"), child.Nodes().ToArray());
                var overflow = Regex.Match(style, @"(?:^|;)\s*overflow\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                var overflowValue = overflow.Success ? overflow.Groups[1].Value.Trim() : child.Attribute("overflow")?.Value ?? "hidden";
                if (!overflowValue.Equals("visible", StringComparison.OrdinalIgnoreCase))
                {
                    // A viewport's implicit clip must not expose previously cropped artwork.
                    var id = NextId();
                    position.Add(new XElement(Svg + "defs", new XElement(Svg + "clipPath", new XAttribute("id", id), new XAttribute("clipPathUnits", "userSpaceOnUse"),
                        new XElement(Svg + "rect", new XAttribute("width", F(width)), new XAttribute("height", F(height))))));
                    position.Add(new XElement(Svg + "g", new XAttribute("clip-path", $"url(#{id})"), content));
                }
                else position.Add(content);
                group.Add(position);
                child.ReplaceWith(group);
            }
        }
        Visit(root, viewportWidth, viewportHeight);
        return result;
    }

    private static void ResolvePercentages(XElement viewport, double width, double height)
    {
        var horizontal = new HashSet<string> { "x", "x1", "x2", "cx", "rx", "width", "dx" };
        var vertical = new HashSet<string> { "y", "y1", "y2", "cy", "ry", "height", "dy" };
        foreach (var node in viewport.Descendants().Where(e => e.Ancestors().FirstOrDefault(a => a.Name == Svg + "svg") == viewport))
        {
            if (node.Name == Svg + "svg") continue; // Its own viewport resolver handles its attributes.
            if (node.Ancestors().Any(a => a != viewport && a.Name.Namespace == Svg && a.Name.LocalName is "defs" or "symbol" or "pattern" or "marker" or "clipPath" or "mask")) continue;
            foreach (var attribute in node.Attributes().Where(a => a.Name.Namespace == XNamespace.None && a.Value.Contains('%')).ToArray())
            {
                var name = attribute.Name.LocalName;
                if (horizontal.Contains(name)) attribute.Value = F(Length(attribute.Value, width, 0));
                else if (vertical.Contains(name)) attribute.Value = F(Length(attribute.Value, height, 0));
                else if (name is "r" or "stroke-width") attribute.Value = F(Length(attribute.Value, Math.Sqrt((width * width + height * height) / 2), 0));
                else if (name == "style") throw new FormatException("Percentage lengths in inline styles must be converted to explicit SVG units before group export.");
            }
        }
    }

    private static double[]? ViewBox(XElement node)
    {
        if (node.Attribute("viewBox") is not XAttribute attr) return null;
        var parts = Regex.Split(attr.Value.Trim(), @"[\s,]+");
        if (parts.Length != 4 || parts.Any(p => !double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)))
            throw new FormatException("SVG viewBox must contain four finite numbers.");
        var box = parts.Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        if (box[2] <= 0 || box[3] <= 0) throw new FormatException("SVG viewBox dimensions must be positive.");
        return box;
    }

    private static double Length(string? text, double percentBasis, double fallback)
    {
        if (text == null) return fallback;
        var match = Regex.Match(text.Trim(), @"^([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)(px|in|mm|cm|pt|pc|%)?$");
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            throw new FormatException("SVG viewport lengths must use numbers, px, in, mm, cm, pt, pc, or percentages.");
        return number * (match.Groups[2].Value switch { "in" => 96, "mm" => 96 / 25.4, "cm" => 96 / 2.54, "pt" => 96 / 72d, "pc" => 16, "%" => percentBasis / 100, _ => 1 });
    }
}
