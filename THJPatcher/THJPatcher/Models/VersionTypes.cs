namespace THJPatcher.Models
{
    public enum VersionTypes
    {
        Unknown,
        Titanium,
        Secrets_Of_Feydwer,
        Seeds_Of_Destruction,
        Rain_Of_Fear,
        Rain_Of_Fear_2,
        Underfoot,
        Broken_Mirror
    }

    public class ClientVersion
    {
        public string Name { get; set; }
        public string Suffix { get; set; }

        public ClientVersion(string name, string suffix)
        {
            Name = name;
            Suffix = suffix;
        }
    }
} 