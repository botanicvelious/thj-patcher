using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace THJPatcher
{
    public static class ChangelogUtility
    {
        public static List<ChangelogInfo> ReadChangelogFromYaml(string filePath)
        {
            var result = new List<ChangelogInfo>();

            try
            {
                if (!File.Exists(filePath))
                {
                    return result;
                }

                var yaml = File.ReadAllText(filePath);
                var entries = IniLibrary.LoadChangelog();

                foreach (var entry in entries)
                {
                    if (entry.TryGetValue("timestamp", out var timestampStr) &&
                        entry.TryGetValue("author", out var author) &&
                        entry.TryGetValue("raw_content", out var rawContent) &&
                        entry.TryGetValue("message_id", out var messageId))
                    {
                        if (DateTime.TryParse(timestampStr, out var timestamp))
                        {
                            var changelogInfo = new ChangelogInfo
                            {
                                Timestamp = timestamp,
                                Author = author,
                                Raw_Content = rawContent,
                                Message_Id = messageId
                            };

                            // Add Raw field if available
                            if (entry.TryGetValue("raw", out var raw))
                            {
                                changelogInfo.Raw = raw;
                            }

                            result.Add(changelogInfo);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading changelog from YAML: {ex.Message}");
            }

            return result;
        }

        public static int GetChangelogCount(List<ChangelogInfo> changelogs)
        {
            return changelogs?.Count ?? 0;
        }

        public static async Task<List<ChangelogInfo>> LoadChangelogsFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return new List<ChangelogInfo>();

            try
            {
                using (var input = File.OpenText(filePath))
                {
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();

                    var entries = await Task.Run(() => deserializer.Deserialize<List<ChangelogInfo>>(input));
                    return entries ?? new List<ChangelogInfo>();
                }
            }
            catch (Exception)
            {
                return new List<ChangelogInfo>();
            }
        }

        public static async Task SaveChangelogsToFile(List<ChangelogInfo> changelogs, string filePath)
        {
            try
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                string yaml = await Task.Run(() => serializer.Serialize(changelogs));

                await File.WriteAllTextAsync(filePath, yaml);
            }
            catch (Exception)
            {
                // Handle exceptions
            }
        }
    }
}