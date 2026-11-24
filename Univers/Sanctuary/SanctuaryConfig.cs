/*
 * =====================================================================
 *  SanctuaryConfig.cs - Konfigurasi Segel Sihir Pelindung Lobby
 *  Namespace: Univers.Sanctuary (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TShockAPI;

namespace Univers.Sanctuary
{
    public class SanctuaryConfig
    {
        public bool Enabled { get; set; } = true;

        // World yang dilindungi oleh Segel Sihir Pelindung Sang Legenda
        public List<string> ProtectedWorldIds { get; set; } = new() { "lobby" };

        // Identitas & Lore Sang Legenda Pelindung Dunia
        public string GuardianName { get; set; } = "Archmage Aetherion";
        public string BarrierName { get; set; } = "Segel Sihir Pelindung Sang Legenda";
        public string LoreSummary { get; set; } =
            "Ribuan tahun lalu, Sang Legenda Archmage Aetherion menancapkan segel cahaya abadi di tanah Lobby agar para petualang memiliki tempat suci yang tak tertembus oleh boss, monster, maupun kutukan dunia.";

        // Aturan Proteksi
        public bool BlockBosses { get; set; } = true;
        public bool BlockHostileMobs { get; set; } = true;
        public bool BlockEventsAndInvasions { get; set; } = true;
        public bool BlockSummonItems { get; set; } = true;
        public bool BlockSpawnCommands { get; set; } = true;
        public bool AllowAdminCommandBypass { get; set; } = false;

        // Pesan & Visual
        public bool ShowLoreOnJoin { get; set; } = true;
        public bool ShowWardVisuals { get; set; } = true;
        public int AmbientBarrierPulseSeconds { get; set; } = 10;
        public float AmbientBarrierRadiusPixels { get; set; } = 110f;
        public float AmbientTriggerDistancePixels { get; set; } = 1600f;

        // Daftar perintah yang diblokir di world terproteksi (case-insensitive)
        public List<string> BlockedCommands { get; set; } = new()
        {
            "spawnboss",
            "sb",
            "spawnmob",
            "sm",
            "bloodmoon",
            "eclipse",
            "invade",
            "invasion",
            "slimerain",
            "pumpkinmoon",
            "frostmoon"
        };

        public bool IsWorldProtected(string? worldId)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(worldId) || ProtectedWorldIds == null || ProtectedWorldIds.Count == 0)
            {
                return false;
            }

            return ProtectedWorldIds.Any(id =>
                !string.IsNullOrWhiteSpace(id) &&
                id.Equals(worldId, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsCommandBlocked(string? cmdName)
        {
            if (!BlockSpawnCommands || string.IsNullOrWhiteSpace(cmdName) || BlockedCommands == null)
            {
                return false;
            }

            return BlockedCommands.Any(c =>
                !string.IsNullOrWhiteSpace(c) &&
                c.Equals(cmdName, StringComparison.OrdinalIgnoreCase));
        }

        public static SanctuaryConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonConvert.DeserializeObject<SanctuaryConfig>(json);
                    if (cfg != null)
                    {
                        if (cfg.ProtectedWorldIds == null || cfg.ProtectedWorldIds.Count == 0)
                        {
                            cfg.ProtectedWorldIds = new List<string> { "lobby" };
                        }
                        if (cfg.BlockedCommands == null)
                        {
                            cfg.BlockedCommands = new List<string>();
                        }
                        if (string.IsNullOrWhiteSpace(cfg.GuardianName))
                        {
                            cfg.GuardianName = "Archmage Aetherion";
                        }
                        if (string.IsNullOrWhiteSpace(cfg.BarrierName))
                        {
                            cfg.BarrierName = "Segel Sihir Pelindung Sang Legenda";
                        }
                        cfg.Save(path);
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.Sanctuary] Gagal membaca SanctuaryConfig.json, menggunakan default: {ex.Message}");
            }

            var fresh = new SanctuaryConfig();
            fresh.Save(path);
            return fresh;
        }

        public void Save(string path)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Univers.Sanctuary] Gagal menyimpan SanctuaryConfig.json: {ex.Message}");
            }
        }
    }
}
