using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LaserBatcher.Services;

/// <summary>Writes SVG element names without prefixes for importers that match literal tags.</summary>
public static class SvgSerializer
{
    public static string Serialize(XDocument document)
    {
        var text = new StringBuilder();
        using (var writer = XmlWriter.Create(text, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            NewLineHandling = NewLineHandling.None
        }))
        {
            foreach (var node in document.Nodes()) WriteNode(writer, node);
        }
        return text.ToString();
    }

    private static void WriteNode(XmlWriter writer, XNode node)
    {
        if (node is not XElement element)
        {
            node.WriteTo(writer);
            return;
        }

        // Do not let LINQ to XML choose an SVG alias from an inherited xmlns:s/ svg
        // declaration. An explicit empty prefix also resets a foreign default namespace.
        var prefix = element.Name.Namespace == SvgBatchService.Svg
            ? ""
            : element.GetPrefixOfNamespace(element.Name.Namespace) ?? "";
        writer.WriteStartElement(prefix, element.Name.LocalName, element.Name.NamespaceName);
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                if (attribute.Name.LocalName == "xmlns")
                {
                    // An unprefixed element must use its own namespace as the default.
                    // Descendants with a different namespace get their own declaration.
                    if (prefix.Length != 0 || attribute.Value == element.Name.NamespaceName)
                        writer.WriteAttributeString("xmlns", attribute.Value);
                }
                else writer.WriteAttributeString("xmlns", attribute.Name.LocalName, XNamespace.Xmlns.NamespaceName, attribute.Value);
            }
            else if (attribute.Name.Namespace == XNamespace.None)
                writer.WriteAttributeString(attribute.Name.LocalName, attribute.Value);
            else
            {
                var attributePrefix = element.GetPrefixOfNamespace(attribute.Name.Namespace);
                writer.WriteAttributeString(string.IsNullOrEmpty(attributePrefix) ? null : attributePrefix,
                    attribute.Name.LocalName, attribute.Name.NamespaceName, attribute.Value);
            }
        }
        foreach (var child in element.Nodes()) WriteNode(writer, child);
        writer.WriteEndElement();
    }
}
