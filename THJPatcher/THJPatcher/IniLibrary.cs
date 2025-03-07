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

        public static void Save()
        {
            try
            {
                using (var writer = File.CreateText($"{System.IO.Path.GetDirectoryName(Application.ExecutablePath)}\\thjpatcher.yml"))
                {
                    var serializer = new SerializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    serializer.Serialize(writer, instance);
                }
                Debug.WriteLine($"Saved version: {instance.Version}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        public static void Load()
        {
            try {
                using (var input = File.OpenText($"{System.IO.Path.GetDirectoryName(Application.ExecutablePath)}\\thjpatcher.yml"))
                {
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    instance = deserializer.Deserialize<IniLibrary>(input);
                }

                if (instance == null) {
                    ResetDefaults();
                    Save();
                }
            } catch (FileNotFoundException e) {
                Console.WriteLine($"Failed loading config: {e.Message}");
                ResetDefaults();
                Save();
            }

            if (instance.AutoPatch == null) instance.AutoPatch = "false";
            if (instance.AutoPlay == null) instance.AutoPlay = "false";
            if (instance.PatcherUrl == null) instance.PatcherUrl = "";
            if (instance.FileName == null) instance.FileName = "";
            if (instance.Version == null) instance.Version = "1.0.0";
            if (instance.LastPatchedVersion == null) instance.LastPatchedVersion = "";

            Debug.WriteLine($"Loaded version: {instance.Version}");
        }

        public static void ResetDefaults()
        {
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
