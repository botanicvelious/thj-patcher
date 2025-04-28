using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace THJPatcher
{
    /// <summary>
    /// Interaction logic for LatestChangelogWindow.xaml
    /// </summary>
    public partial class LatestChangelogWindow : Window
    {
        public bool IsAcknowledged { get; private set; } = false;

        // Constructor that accepts raw markdown content
        public LatestChangelogWindow(string rawMarkdown)
        {
            InitializeComponent();

            // Process and set markdown content
            if (!string.IsNullOrEmpty(rawMarkdown))
            {
                // Process the markdown to fix formatting issues before displaying
                string processedMarkdown = ProcessMarkdown(rawMarkdown);
                MarkdownViewer.Markdown = processedMarkdown;
            }
            else
            {
                MarkdownViewer.Markdown = "# No changelog available\n\nNo changes were detected.";
            }
        }

        /// <summary>
        /// Processes the markdown content to ensure compatibility with wpfui.markdown renderer
        /// This is a generic processor that doesn't rely on specific text content
        /// </summary>
        private string ProcessMarkdown(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return markdown;

            // Create a new StringBuilder to build the final result
            StringBuilder result = new StringBuilder();

            // Split the markdown into individual lines for more precise control
            string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
            bool inList = false;
            bool inParagraph = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();

                // Skip empty lines but preserve them in the output
                if (string.IsNullOrWhiteSpace(line))
                {
                    result.AppendLine();
                    inList = false;
                    inParagraph = false;
                    continue;
                }

                // Handle headers (# header)
                if (line.StartsWith("#"))
                {
                    // Fix header formatting by removing any erroneous asterisks
                    line = Regex.Replace(line, @"\*([^*]+)\*", "$1");
                    line = Regex.Replace(line, @"([^*]+)\*", "$1");
                    line = Regex.Replace(line, @"\*([^*]+)", "$1");

                    result.AppendLine(line);

                    // Ensure headers have a blank line after them
                    if (i + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[i + 1]) && !lines[i + 1].StartsWith("#"))
                    {
                        result.AppendLine();
                    }
                    continue;
                }

                // Check if this line looks like a section title (not starting with # but followed by a colon)
                // This handles cases like "Key Changes:" which should be formatted as headers
                if (line.Contains(":") && !line.StartsWith("- ") && !line.StartsWith("* ") && !line.Contains("•"))
                {
                    string title = line.Replace(":", "").Trim();
                    // Remove any erroneous asterisks
                    title = Regex.Replace(title, @"\*([^*]+)\*", "$1");
                    title = Regex.Replace(title, @"([^*]+)\*", "$1");
                    title = Regex.Replace(title, @"\*([^*]+)", "$1");

                    // Format as a level 2 header
                    result.AppendLine($"## {title}");
                    result.AppendLine();
                    continue;
                }

                // Fix lines that start with asterisk but aren't proper markdown list items
                if (line.StartsWith("*") && !line.StartsWith("* "))
                {
                    // This is likely a misformatted heading, not a list item
                    line = line.TrimStart('*').Trim();
                    if (!string.IsNullOrEmpty(line))
                    {
                        if (i > 0 && string.IsNullOrWhiteSpace(lines[i - 1]) && !inParagraph)
                        {
                            // This appears to be a section heading
                            result.AppendLine($"## {line}");
                            result.AppendLine();
                        }
                        else
                        {
                            // This is regular text that was incorrectly started with an asterisk
                            result.AppendLine(line);
                            inParagraph = true;
                        }
                    }
                    continue;
                }

                // Handle bullet points and list items (• or * or -)
                if (line.Contains("•") || line.StartsWith("* ") || line.StartsWith("- "))
                {
                    // Extract the content after the bullet point
                    string content = line
                        .Replace("•\t", "")
                        .Replace("•", "")
                        .Replace("* ", "")
                        .Replace("- ", "")
                        .Trim();

                    // Fix any trailing or leading asterisks
                    content = Regex.Replace(content, @"([A-Za-z]+)\*", "$1");
                    content = Regex.Replace(content, @"\*([A-Za-z]+)", "$1");

                    // Determine list item indentation level
                    string prefix = "- ";

                    // Check if this is a sub-item by context
                    if (i > 0 && inList &&
                       (lines[i - 1].Contains("•") || lines[i - 1].StartsWith("* ") || lines[i - 1].StartsWith("- ")))
                    {
                        // Check if this appears to be a sub-item based on content
                        if (content.StartsWith("in ") || content.StartsWith("and ") ||
                            content.StartsWith("or ") || content.StartsWith("with ") ||
                            char.IsLower(content[0]))
                        {
                            prefix = "  - ";
                        }
                    }

                    result.AppendLine($"{prefix}{content}");
                    inList = true;
                    inParagraph = false;
                    continue;
                }

                // Fix any standalone lines with trailing or leading asterisks
                line = Regex.Replace(line, @"([A-Za-z]+)\*", "$1");
                line = Regex.Replace(line, @"\*([A-Za-z]+)", "$1");

                // Handle regular text lines
                if (inList && i > 0 && (lines[i - 1].Contains("•") || lines[i - 1].StartsWith("* ") || lines[i - 1].StartsWith("- ")))
                {
                    // If this is a continuation of a list item, indent it properly
                    result.AppendLine($"  {line}");
                }
                else
                {
                    result.AppendLine(line);
                    inParagraph = true;
                }
            }

            return result.ToString();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Set IsAcknowledged to true to indicate the user has seen the changelog
            IsAcknowledged = true;
            this.Close();
        }
    }
}