/*
 * ==============================================================================
 * ChatConfig.cs - Konfigurasi Modul Chat, Mention (@tag) & Auto-AFK
 * Target: Terraria 1.4.5 Series / TShock 6.x (.NET 9.0 / C# 13)
 * ==============================================================================
 */

using System;
using System.IO;
using Newtonsoft.Json;
using TShockAPI;

namespace Univers.Chat
{
    public class ChatConfig
    {
        [JsonProperty("Enable Mention System")]
        public bool EnableMentionSystem { get; set; } = true;

        [JsonProperty("Enable Auto AFK")]
        public bool EnableAutoAfk { get; set; } = true;

        [JsonProperty("Auto AFK Minutes")]
        public int AutoAfkMinutes { get; set; } = 3;

        [JsonProperty("Mention Chat Color Hex")]
        public string MentionColorHex { get; set; } = "facc15";

        public static ChatConfig Read()
        {
            string path = Path.Combine(UniversPlugin.GetEffectiveSavePath(), "Univers", "ChatConfig.json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<ChatConfig>(File.ReadAllText(path));
                    if (loaded != null) return loaded;
                }
                var def = new ChatConfig();
                File.WriteAllText(path, JsonConvert.SerializeObject(def, Formatting.Indented));
                return def;
            }
            catch
            {
                return new ChatConfig();
            }
        }
    }
}
