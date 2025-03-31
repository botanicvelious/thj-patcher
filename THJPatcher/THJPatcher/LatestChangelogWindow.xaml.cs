using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.IO;
using System.Linq;

namespace THJPatcher
{
    public partial class LatestChangelogWindow : Window
    {
        private static bool _hasShownChangelog = false;
        public bool IsAcknowledged { get; private set; }

        public class ChangelogItemViewModel
        {
            public string Date { get; set; }
            public string Author { get; set; }
            public string Content { get; set; }
            public Visibility DateVisibility { get; set; }
            public Visibility AuthorVisibility { get; set; }
            public Visibility ContentVisibility { get; set; }
            public Visibility SeparatorVisibility { get; set; }
        }

        public LatestChangelogWindow(string changelogContent)
        {
            if (_hasShownChangelog)
            {
                Close();
                return;
            }

            InitializeComponent();
            Owner = System.Windows.Application.Current.MainWindow;

            // Enable smooth scrolling and hardware acceleration
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            UseLayoutRounding = true;

            LoadChangelogContent(changelogContent ?? "No new changelog entries available.");
            _hasShownChangelog = true;
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

                var items = new List<ChangelogItemViewModel>();
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
                Title = $"Latest Changes ({items.Count} update{(items.Count > 1 ? "s" : "")})";
            }
            catch (Exception)
            {
                // If there's any error loading the content, show an empty list
                ChangelogList.ItemsSource = new List<ChangelogItemViewModel>();
            }
        }

        private ChangelogItemViewModel CreateChangelogItem(string date, string author, string content)
        {
            return new ChangelogItemViewModel
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

        private void AcknowledgeButton_Click(object sender, RoutedEventArgs e)
        {
            IsAcknowledged = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            IsAcknowledged = true;
            ChangelogList.ItemsSource = null;
            base.OnClosed(e);
        }

        public static void ResetChangelogFlag()
        {
            _hasShownChangelog = false;
        }
    }
} 