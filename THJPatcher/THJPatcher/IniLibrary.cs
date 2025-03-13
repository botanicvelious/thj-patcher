using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Windows.Forms;
using System.Diagnostics;

namespace THJPatcher
{
    class IniLibrary
    {
        public static IniLibrary instance;
        public string AutoPatch { get; set; }
        public string AutoPlay { get; set; }
        public VersionTypes ClientVersion { get; set; }
        public string LastPatchedVersion { get; set; }
        public string PatcherUrl { get; set; }
        public string FileName { get; set; }
        public string Version { get; set; }

        private static string GetConfigPath()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            Debug.WriteLine($"[DEBUG] Executable directory: {exeDir}");
            return Path.Combine(exeDir, "thjpatcher.yml");
        }

        private static string GetChangelogPath()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            return Path.Combine(exeDir, "changelog.yml");
        }

        public static void Save()
        {
            try
            {
                string configPath = GetConfigPath();
                Debug.WriteLine($"[DEBUG] Saving config to: {configPath}");
                Debug.WriteLine($"[DEBUG] LastPatchedVersion before save: {instance.LastPatchedVersion}");

                using (var writer = File.CreateText(configPath))
                {
                    var serializer = new SerializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    serializer.Serialize(writer, instance);
                }
                
                // Verify the file was written
                if (File.Exists(configPath))
                {
                    Debug.WriteLine($"[DEBUG] Config file saved successfully");
                    string contents = File.ReadAllText(configPath);
                    Debug.WriteLine($"[DEBUG] Config file contents: {contents}");
                }
                else
                {
                    Debug.WriteLine($"[DEBUG] Warning: Config file not found after save attempt");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DEBUG] Error saving config: {ex.Message}");
                Debug.WriteLine($"[DEBUG] Stack trace: {ex.StackTrace}");
            }
        }

        public static void Load()
        {
            string configPath = GetConfigPath();
            Debug.WriteLine($"[DEBUG] Loading config from: {configPath}");
            
            try {
                if (File.Exists(configPath))
                {
                    string contents = File.ReadAllText(configPath);
                    Debug.WriteLine($"[DEBUG] Existing config contents: {contents}");
                }
                else
                {
                    Debug.WriteLine($"[DEBUG] No existing config file found");
                }
                
                using (var input = File.OpenText(configPath))
                {
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    instance = deserializer.Deserialize<IniLibrary>(input);
                }

                if (instance == null) {
                    Debug.WriteLine($"[DEBUG] Deserialized instance is null, resetting defaults");
                    ResetDefaults();
                    Save();
                }
            } catch (FileNotFoundException e) {
                Debug.WriteLine($"[DEBUG] Config file not found: {e.Message}");
                ResetDefaults();
                Save();
            } catch (Exception e) {
                Debug.WriteLine($"[DEBUG] Error loading config: {e.Message}");
                Debug.WriteLine($"[DEBUG] Stack trace: {e.StackTrace}");
                ResetDefaults();
                Save();
            }

            if (instance.AutoPatch == null) instance.AutoPatch = "false";
            if (instance.AutoPlay == null) instance.AutoPlay = "false";
            if (instance.PatcherUrl == null) instance.PatcherUrl = "";
            if (instance.FileName == null) instance.FileName = "";
            if (instance.Version == null) instance.Version = "1.1.0";
            if (instance.LastPatchedVersion == null) instance.LastPatchedVersion = "";

            Debug.WriteLine($"[DEBUG] Loaded LastPatchedVersion: {instance.LastPatchedVersion}");

            // Check if filelist.yml exists, if not, force a patch
            string filelistPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "filelist.yml");
            if (!File.Exists(filelistPath))
            {
                Debug.WriteLine($"[DEBUG] filelist.yml not found, forcing LastPatchedVersion to empty to trigger patch");
                instance.LastPatchedVersion = "";
                Save();
            }
        }

        public static void ResetDefaults()
        {
            Debug.WriteLine($"[DEBUG] Resetting to default values");
            instance = new IniLibrary();
            instance.AutoPlay = "false";
            instance.AutoPatch = "false";
            instance.PatcherUrl = "";
            instance.FileName = "";
            instance.Version = "1.1.0";
            instance.LastPatchedVersion = "";
        }

        public static string GetLatestMessageId()
        {
            try
            {
                var entries = LoadChangelog();
                if (entries != null && entries.Count > 0)
                {
                    // Get the first entry (most recent) and return its message_id
                    var firstEntry = entries[0];
                    if (firstEntry.ContainsKey("message_id"))
                    {
                        return firstEntry["message_id"];
                    }
                }
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"[ERROR] Failed to get latest message_id: {ex.Message}");
            }
            return string.Empty;
        }

        public static void SaveChangelog(List<Dictionary<string, string>> entries)
        {
            try
            {
                string changelogPath = GetChangelogPath();
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                string contents = serializer.Serialize(entries);
                File.WriteAllText(changelogPath, contents);
            }
            catch (Exception)
            {
                // Failed to save changelog
            }
        }

        public static List<Dictionary<string, string>> LoadChangelog()
        {
            try
            {
                string changelogPath = GetChangelogPath();
                if (File.Exists(changelogPath))
                {
                    using (var input = File.OpenText(changelogPath))
                    {
                        var deserializer = new DeserializerBuilder()
                            .WithNamingConvention(CamelCaseNamingConvention.Instance)
                            .Build();
                        return deserializer.Deserialize<List<Dictionary<string, string>>>(input) ?? new List<Dictionary<string, string>>();
                    }
                }
            }
            catch (Exception)
            {
                // Failed to load changelog
            }
            return new List<Dictionary<string, string>>();
        }
    }
}
