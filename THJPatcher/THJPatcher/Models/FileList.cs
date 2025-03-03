using System.Collections.Generic;

namespace THJPatcher.Models
{
    public class FileList
    {
        public string version { get; set; }
        public List<FileEntry> deletes { get; set; }
        public string downloadprefix { get; set; }
        public List<FileEntry> downloads { get; set; }
        public List<FileEntry> unpacks { get; set; }
    }

    public class FileEntry
    {
        public string name { get; set; }
        public string md5 { get; set; }
        public string date { get; set; }
        public string zip { get; set; }
        public int size { get; set; }
    }
}