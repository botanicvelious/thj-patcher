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
            // Get the directory where filelist.yml is located, since that's our working directory
            string workingDir = Path.GetDirectoryName(Path.Combine(Application.StartupPath, "filelist.yml"));
            if (!Directory.Exists(workingDir))
            {
                workingDir = Path.GetDirectoryName(Application.ExecutablePath);
            }
            Debug.WriteLine($"[DEBUG] Working directory: {workingDir}");
            return Path.Combine(workingDir, "thjpatcher.yml");
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
            if (instance.Version == null) instance.Version = "1.0.0";
            if (instance.LastPatchedVersion == null) instance.LastPatchedVersion = "";

            Debug.WriteLine($"[DEBUG] Loaded LastPatchedVersion: {instance.LastPatchedVersion}");
        }

        public static void ResetDefaults()
        {
            Debug.WriteLine($"[DEBUG] Resetting to default values");
            instance = new IniLibrary();
            instance.AutoPlay = "false";
            instance.AutoPatch = "false";
            instance.PatcherUrl = "";
            instance.FileName = "";
            instance.Version = "1.0.0";
            instance.LastPatchedVersion = "";
        }
    }
}
