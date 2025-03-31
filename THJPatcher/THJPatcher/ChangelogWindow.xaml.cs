using System;
using System.IO;
using System.Windows;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Controls;
using System.Globalization;

namespace THJPatcher
{
    public class ChangelogItem
    {
        public string Date { get; set; }
        public string Author { get; set; }
        public string Content { get; set; }
        public Visibility DateVisibility { get; set; }
        public Visibility AuthorVisibility { get; set; }
        public Visibility ContentVisibility { get; set; }
        public Visibility SeparatorVisibility { get; set; }
    }

    public partial class ChangelogWindow : Window
    {
        public ChangelogWindow(string changelogContent = null)
        {
            InitializeComponent();
            Owner = System.Windows.Application.Current.MainWindow;

            try
            {
                if (changelogContent != null)
                {
                    LoadChangelogContent(changelogContent);
                }
                else
                {
                    string appPath = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                    string markdownPath = Path.Combine(appPath, "changelog.md");
                    
                    if (File.Exists(markdownPath))
                    {
                        LoadChangelogContent(File.ReadAllText(markdownPath));
                    }
                    else
                    {
                        LoadChangelogContent("No changelog entries available.");
                    }
                }
            }
            catch (Exception ex)
            {
                LoadChangelogContent($"Error loading changelog: {ex.Message}");
            }
        }

        private void LoadChangelogContent(string changelogContent)
        {
            try
            {
                // Split content into lines and remove empty ones
                var lines = changelogContent
                    .Split('\n')
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();

                var items = new List<ChangelogItem>();
                string currentDate = null;
                string currentAuthor = null;
                var currentContent = new List<string>();

                foreach (var line in lines)
                {
                    if (line.StartsWith("# "))
                    {
                        // If we have accumulated content, add it as an item
                        if (currentContent.Any())
                        {
                            items.Add(CreateChangelogItem(currentDate, currentAuthor, string.Join("\n", currentContent)));
                            currentContent.Clear();
                        }

                        // Parse and format the date
                        currentDate = line.Substring(2).Trim();
                        continue;
                    }

                    if (line.StartsWith("## "))
                    {
                        currentAuthor = line.Substring(3).Trim();
                        continue;
                    }

                    if (line.StartsWith("- "))
                    {
                        currentContent.Add("• " + line.Substring(2).Trim());
                        continue;
                    }

                    if (line.StartsWith("---"))
                    {
                        // If we have accumulated content, add it as an item
                        if (currentContent.Any())
                        {
                            items.Add(CreateChangelogItem(currentDate, currentAuthor, string.Join("\n", currentContent)));
                            currentContent.Clear();
                        }
                        continue;
                    }

                    // Add other lines as is if not empty
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        currentContent.Add(line.Trim());
                    }
                }

                // Add the last item if we have content
                if (currentContent.Any())
                {
                    items.Add(CreateChangelogItem(currentDate, currentAuthor, string.Join("\n", currentContent)));
                }

                // Set the last item's separator visibility to collapsed
                if (items.Any())
                {
                    items.Last().SeparatorVisibility = Visibility.Collapsed;
                }

                // Set the processed items as the ListView's items source
                ChangelogList.ItemsSource = items;
            }
            catch (Exception)
            {
                // If there's any error loading the content, show an empty list
                ChangelogList.ItemsSource = new List<ChangelogItem>();
            }
        }

        private ChangelogItem CreateChangelogItem(string date, string author, string content)
        {
            return new ChangelogItem
            {
                Date = date,
                Author = author,
                Content = content,
                DateVisibility = Visibility.Visible,
                AuthorVisibility = Visibility.Visible,
                ContentVisibility = Visibility.Visible,
                SeparatorVisibility = Visibility.Visible
            };
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Clear resources
                if (ChangelogList != null)
                {
                    ChangelogList.ItemsSource = null;
                }
                Owner = null;
            }
            catch (Exception)
            {
                // Ignore any errors during cleanup
            }
            finally
            {
                base.OnClosed(e);
            }
        }
    }
} 