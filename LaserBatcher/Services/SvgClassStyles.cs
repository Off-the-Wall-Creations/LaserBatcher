using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LaserBatcher.Services;

/// <summary>A deliberately bounded CSS subset; unsupported sheets are left for the export validator.</summary>
internal static class SvgClassStyles
{
    public static string[] Tokens(XElement element) => ((string?)element.Attribute("class") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();

    public static void Resolve(XDocument document)
    {
        var sheets = document.Descendants(SvgBatchService.Svg + "style").ToArray();
        var rules = new List<(string Class, string Name, string Value)>();
        foreach (var sheet in sheets)
        {
            if (sheet.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "type") ||
                sheet.Attribute("type") is XAttribute type && type.Value != "text/css") return;
            var css = Regex.Replace(sheet.Value, @"/\*.*?\*/", "", RegexOptions.Singleline);
            var position = 0;
            while (position < css.Length)
            {
                if (string.IsNullOrWhiteSpace(css[position..])) break;
                var rule = Regex.Match(css[position..], @"^\s*\.([a-zA-Z_][a-zA-Z0-9_-]*)\s*\{([^{}]*)\}");
                if (!rule.Success) return;
                var declarations = rule.Groups[2].Value.Split(';').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                if (declarations.Length == 0) return;
                foreach (var declaration in declarations)
                {
                    var value = Regex.Match(declaration, @"^\s*(stroke|fill)\s*:\s*(none|#[0-9a-f]{6}|#[0-9a-f]{3})\s*$", RegexOptions.IgnoreCase);
                    if (!value.Success) return;
                    rules.Add((rule.Groups[1].Value, value.Groups[1].Value.ToLowerInvariant(), value.Groups[2].Value));
                }
                position += rule.Length;
            }
        }
        if (sheets.Length == 0) return;
        foreach (var element in document.Descendants().Where(e => e.Name.Namespace == SvgBatchService.Svg))
        {
            var tokens = Tokens(element);
            var properties = new Dictionary<string, string>();
            foreach (var rule in rules) if (tokens.Contains(rule.Class)) properties[rule.Name] = rule.Value;
            // Class selectors override presentation attributes; existing inline declarations win.
            if (properties.Count > 0) element.SetAttributeValue("style", string.Join(";", properties.Select(p => p.Key + ":" + p.Value)) + ";" + (string?)element.Attribute("style"));
        }
        foreach (var sheet in sheets)
            sheet.ReplaceWith(new XElement(SvgBatchService.Svg + "metadata",
                new XElement(SvgMetadataService.Namespace + "sourceStylesheet", sheet.ToString(SaveOptions.DisableFormatting))));
    }
}
