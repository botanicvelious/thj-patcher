using System;
using System.Text.Json.Serialization;

namespace THJPatcher.Models
{
    public class ServerStatus
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("found")]
        public bool Found { get; set; }

        [JsonPropertyName("server")]
        public ServerInfo Server { get; set; }
    }

    public class ServerInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("players_online")]
        public int PlayersOnline { get; set; }

        [JsonPropertyName("last_updated")]
        public DateTime LastUpdated { get; set; }
    }

    public class ExpBonusStatus
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("found")]
        public bool Found { get; set; }

        [JsonPropertyName("exp_boost")]
        public string ExpBoost { get; set; }

        [JsonPropertyName("channel_id")]
        public string ChannelId { get; set; }
    }
} 