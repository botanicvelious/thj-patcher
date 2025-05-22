using System;
using System.IO;
using System.Windows;

namespace THJPatcher
{
    public partial class ChangelogWindow : Window
    {
        public ChangelogWindow() : this(null)
        {
        }

        public ChangelogWindow(string markdownContent)
        {
            InitializeComponent();
            LoadChangelog(markdownContent);
        }

        private void LoadChangelog(string markdownContent = null)
        {
            try
            {
                string markdown = null;

                // If markdown content was provided directly, use it
                if (!string.IsNullOrEmpty(markdownContent))
                {
                    markdown = markdownContent;
                }
                else
                {
                    // Get the root path (the directory above the executable directory)
                    string exeDir = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                    string rootPath = Directory.GetParent(exeDir)?.FullName ?? exeDir;

                    // Path to the CHANGELOG.md file
                    string changelogPath = Path.Combine(rootPath, "CHANGELOG.md");

                    // If not found in root, check the executable directory
                    if (!File.Exists(changelogPath))
                    {
                        changelogPath = Path.Combine(exeDir, "CHANGELOG.md");
                    }

                    // Check if the file exists
                    if (File.Exists(changelogPath))
                    {
                        // Read the markdown content
                        markdown = File.ReadAllText(changelogPath);

                        // Debug output
                        Console.WriteLine($"Loaded changelog from: {changelogPath}");
                        Console.WriteLine($"Content length: {markdown.Length} characters");
                    }
                    else
                    {
                        // Show error message if file not found
                        markdown = $"# Changelog File Not Found\n\nCould not find CHANGELOG.md at:\n- {changelogPath}";

                        Console.WriteLine("Changelog file not found");
                    }
                }

                // Process the markdown to fix common formatting issues
                markdown = ProcessMarkdown(markdown);

                // Display it
                MarkdownViewer.Markdown = markdown;
            }
            catch (Exception ex)
            {
                // Display any errors
                MarkdownViewer.Markdown = $"# Error Loading Changelog\n\n{ex.Message}";
                Console.WriteLine($"Error loading changelog: {ex.Message}");
            }
        }

        private string ProcessMarkdown(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return markdown;

            // Replace any Unicode circle bullets with standard Markdown dashes
            markdown = markdown.Replace("○", "- ");
            markdown = markdown.Replace("\u25CB", "- "); // Unicode circle character
            markdown = markdown.Replace("\u2022", "- "); // Unicode bullet character

            // Ensure proper spacing for nested list items
            markdown = System.Text.RegularExpressions.Regex.Replace(
                markdown,
                @"(\n- .*\n)(\s{0,2}- )",
                "$1  - "
            );

            // Ensure proper spacing after headers
            markdown = System.Text.RegularExpressions.Regex.Replace(
                markdown,
                @"(#{1,6}.*)\n([^\n])",
                "$1\n\n$2"
            );

            return markdown;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}