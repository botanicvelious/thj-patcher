using System;
using System.IO;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Xml;
using HtmlAgilityPack;

namespace THJPatcher
{
    public static class HtmlToFlowDocumentConverter
    {
        public static FlowDocument Convert(string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                throw new ArgumentException("HTML content cannot be null or empty.", nameof(htmlContent));
            }

            try
            {
                // Use HtmlAgilityPack to clean and process the HTML content
                var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                htmlDoc.LoadHtml(htmlContent);

                // Simplify the HTML structure for compatibility with XAML
                var bodyNode = htmlDoc.DocumentNode.SelectSingleNode("//body") ?? htmlDoc.DocumentNode;

                // Remove unsupported tags and attributes
                CleanHtmlNode(bodyNode);

                string simplifiedHtml = bodyNode.InnerHtml;

                // Wrap the simplified HTML content in a basic FlowDocument structure
                string xamlContent = $"<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">{simplifiedHtml}</FlowDocument>";

                using (StringReader stringReader = new StringReader(xamlContent))
                using (XmlReader xmlReader = XmlReader.Create(stringReader))
                {
                    return (FlowDocument)XamlReader.Load(xmlReader);
                }
            }
            catch (Exception ex)
            {
                // Log the error details to a file for debugging
                File.WriteAllText("HtmlToFlowDocumentConverter_Error.log", $"Exception: {ex.Message}\nStack Trace: {ex.StackTrace}");
                throw new InvalidOperationException("Failed to convert HTML to FlowDocument.", ex);
            }
        }

        private static void CleanHtmlNode(HtmlAgilityPack.HtmlNode node)
        {
            // Remove unsupported tags
            var unsupportedTags = new[] { "script", "style", "iframe", "object", "embed" };
            foreach (var tag in unsupportedTags)
            {
                var nodesToRemove = node.SelectNodes($"//{tag}");
                if (nodesToRemove != null)
                {
                    foreach (var n in nodesToRemove)
                    {
                        n.Remove();
                    }
                }
            }

            // Remove unsupported attributes
            foreach (var child in node.Descendants())
            {
                child.Attributes.RemoveAll();
            }
        }
    }
}