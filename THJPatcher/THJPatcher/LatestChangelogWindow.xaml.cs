using System.Windows;
using System.Collections.Generic;

namespace THJPatcher
{
    public partial class LatestChangelogWindow : Window
    {
        public bool IsAcknowledged { get; private set; }

        public LatestChangelogWindow(ChangelogInfo latestChangelog)
        {
            InitializeComponent();
            Owner = System.Windows.Application.Current.MainWindow;
            IsAcknowledged = false;

            // Convert timestamp to Eastern Time
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var estTime = TimeZoneInfo.ConvertTime(latestChangelog.Timestamp, timeZone);

            // Set the content
            DateText.Text = $"{estTime:MMMM dd, yyyy}";
            AuthorText.Text = latestChangelog.Author;
            ContentText.Text = FormatContent(latestChangelog.Formatted_Content);
        }

        private string FormatContent(string content)
        {
            var lines = content.Split('\n');
            var formattedLines = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("# ") || line.StartsWith("## ") || line.StartsWith("---"))
                    continue;

                if (line.StartsWith("- "))
                    formattedLines.Add("• " + line.Substring(2));
                else if (!string.IsNullOrWhiteSpace(line))
                    formattedLines.Add(line);
            }

            return string.Join("\n", formattedLines);
        }

        private void AcknowledgeButton_Click(object sender, RoutedEventArgs e)
        {
            IsAcknowledged = true;
            Close();
        }
    }
} 