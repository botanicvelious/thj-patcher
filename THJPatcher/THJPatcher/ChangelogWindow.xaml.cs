using System.Windows;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
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
        public bool IsAcknowledged { get; private set; }

        public ChangelogWindow(string changelogContent)
        {
            InitializeComponent();
            Owner = System.Windows.Application.Current.MainWindow;
            IsAcknowledged = false;

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
                        items.Add(new ChangelogItem
                        {
                            Date = currentDate,
                            Author = currentAuthor,
                            Content = string.Join("\n", currentContent),
                            DateVisibility = Visibility.Visible,
                            AuthorVisibility = Visibility.Visible,
                            ContentVisibility = Visibility.Visible,
                            SeparatorVisibility = Visibility.Visible
                        });
                        currentContent.Clear();
                    }

                    // Parse and format the date
                    var dateStr = line.Substring(2);
                    if (System.DateTime.TryParse(dateStr, out var date))
                    {
                        // Always convert to Eastern Time
                        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                        var estTime = TimeZoneInfo.ConvertTime(date, timeZone);
                        currentDate = $"{estTime:MMMM dd, yyyy}";
                    }
                    else
                    {
                        currentDate = dateStr;
                    }
                    continue;
                }

                if (line.StartsWith("## "))
                {
                    currentAuthor = line.Substring(3);
                    continue;
                }

                if (line.StartsWith("- "))
                {
                    currentContent.Add("• " + line.Substring(2));
                    continue;
                }

                if (line.StartsWith("---"))
                {
                    continue;
                }

                // Add other lines as is
                if (!string.IsNullOrWhiteSpace(line))
                {
                    currentContent.Add(line);
                }
            }

            // Add the last item if we have content
            if (currentContent.Any())
            {
                items.Add(new ChangelogItem
                {
                    Date = currentDate,
                    Author = currentAuthor,
                    Content = string.Join("\n", currentContent),
                    DateVisibility = Visibility.Visible,
                    AuthorVisibility = Visibility.Visible,
                    ContentVisibility = Visibility.Visible,
                    SeparatorVisibility = Visibility.Visible
                });
            }

            // Set the last item's separator visibility to collapsed
            if (items.Any())
            {
                items.Last().SeparatorVisibility = Visibility.Collapsed;
            }

            // Set the processed items as the ListView's items source
            ChangelogList.ItemsSource = items;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            IsAcknowledged = true;
            Close();
        }
    }
} 