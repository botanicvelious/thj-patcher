using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace THJPatcher.Models
{
    /// <summary>
    /// Represents a changelog data response
    /// </summary>
    public class ChangelogData
    {
        public string Status { get; set; }
        public bool Found { get; set; }
        public ChangelogInfo Changelog { get; set; }
    }

    /// <summary>
    /// Represents a changelog API response
    /// </summary>
    public class ChangelogResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }
        
        public int Total { get; set; }
        
        public List<ChangelogInfo> Changelogs { get; set; }
    }

    /// <summary>
    /// Represents a single changelog entry
    /// </summary>
    public class ChangelogInfo
    {
        public string Id { get; set; }
        
        [JsonPropertyName("date")]
        public string Date { get; set; }
        
        public string DateDisplay => !string.IsNullOrEmpty(Date) ? 
            DateTime.Parse(Date).ToString("MMM dd, yyyy") : string.Empty;
        
        [JsonPropertyName("author")]
        public string Author { get; set; }
        
        [JsonPropertyName("content")]
        public string Content { get; set; }
    }
} 