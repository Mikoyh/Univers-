/*
 * =====================================================================
 *  MultiWorldConfig.cs - Konfigurasi Modular MultiWorld & Gateway Proxy
 *  Namespace: Univers.MultiWorld (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TShockAPI;

namespace Univers.MultiWorld
{
    public class WorldDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsHub { get; set; } = false;
        public int Port { get; set; } = 7779;
        public string WorldFileName { get; set; } = string.Empty;

        // "small" (1), "medium" (2), "large" (3)
        public string Size { get; set; } = "medium";

        // "classic" (0), "expert" (1), "master" (2), "journey" (3)
        public string Difficulty { get; set; } = "classic";

        // "random" (0), "corrupt" (1), "crimson" (2)
        public string Evil { get; set; } = "random";

        public string Seed { get; set; } = string.Empty;
        public bool AutoStart { get; set; } = false;
        public bool AutoSleep { get; set; } = true;

        // -1 = ikuti global EmptyShutdownMinutes
        public int EmptyShutdownMinutes { get; set; } = -1;

        public string PermissionRequired { get; set; } = string.Empty;
        public string ChatTag { get; set; } = string.Empty;
        public string ChatColorHex { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public int GetSizeArg()
        {
            return Size.Trim().ToLowerInvariant() switch
            {
                "small" or "s" or "1" => 1,
                "large" or "l" or "3" => 3,
                _ => 2
            };
        }

        public int GetDifficultyArg()
        {
            return Difficulty.Trim().ToLowerInvariant() switch
            {
                "expert" or "e" or "1" => 1,
                "master" or "m" or "2" => 2,
                "journey" or "j" or "3" => 3,
                _ => 0
            };
        }

        public string GetEvilArg()
        {
            return Evil.Trim().ToLowerInvariant() switch
            {
                "corrupt" or "corruption" or "1" => "corrupt",
                "crimson" or "2" => "crimson",
                _ => "random"
            };
        }
    }

    public class MultiWorldConfig
    {
        public bool Enabled { get; set; } = true;
        public bool ProxyEnabled { get; set; } = true;

        // Port utama Gateway Proxy agar player cukup konek ke 127.0.0.1:7777 / IP:7777
        public int ProxyPort { get; set; } = 7777;

        // Port cadangan jika Master TShock langsung berjalan di port 7777
        public int FallbackProxyPort { get; set; } = 7776;

        // Port default Master Hub saat dijalankan via build.bat / Start_MultiWorld
        public int MasterWorldPort { get; set; } = 7778;

        // Port awal alokasi otomatis Child World
        public int ChildPortStart { get; set; } = 7779;

        // Port kontrol IPC localhost antara Master Coordinator & Child Worlds
        public int ControlIpcPort { get; set; } = 7775;

        public string HubWorldId { get; set; } = "lobby";

        // Otomatis save & matikan child world jika kosong selama N menit
        public int EmptyShutdownMinutes { get; set; } = 5;

        // Batas tunggu saat membangunkan child world yang sedang tidur (detik)
        public int WakeTimeoutSeconds { get; set; } = 180;

        public int MaxPlayersPerChild { get; set; } = 32;

        // Jika true, world "rtp" otomatis dibuatkan entri default-nya (tetap Sleep sampai dimasuki)
        public bool SeedDefaultRtpWorld { get; set; } = true;

        // Fitur sosial lintas world: chat nyambung & indikator player di world lain
        public bool CrossWorldChat { get; set; } = true;
        public bool CrossWorldPresenceNotices { get; set; } = true;

        public List<WorldDefinition> Worlds { get; set; } = new();

        public static MultiWorldConfig Load(string path)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<MultiWorldConfig>(File.ReadAllText(path));
                    if (loaded != null)
                    {
                        if (loaded.WakeTimeoutSeconds <= 60)
                        {
                            loaded.WakeTimeoutSeconds = 180;
                        }
                        loaded.Normalize();
                        loaded.Save(path);
                        return loaded;
                    }
                }

                var def = new MultiWorldConfig();
                def.Normalize();
                def.Save(path);
                return def;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Univers.MultiWorld] Gagal memuat MultiWorldConfig.json: {ex.Message}");
                var fallback = new MultiWorldConfig();
                fallback.Normalize();
                return fallback;
            }
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
                TShock.Log.ConsoleError($"[Univers.MultiWorld] Gagal menyimpan MultiWorldConfig.json: {ex.Message}");
            }
        }

        public void Normalize()
        {
            ProxyPort = Math.Clamp(ProxyPort, 1024, 65500);
            FallbackProxyPort = Math.Clamp(FallbackProxyPort, 1024, 65500);
            MasterWorldPort = Math.Clamp(MasterWorldPort, 1024, 65500);
            ChildPortStart = Math.Clamp(ChildPortStart, 1024, 65000);
            ControlIpcPort = Math.Clamp(ControlIpcPort, 1024, 65500);
            EmptyShutdownMinutes = Math.Clamp(EmptyShutdownMinutes, 1, 1440);
            WakeTimeoutSeconds = Math.Clamp(WakeTimeoutSeconds, 30, 600);
            MaxPlayersPerChild = Math.Clamp(MaxPlayersPerChild, 2, 255);
            HubWorldId = SanitizeWorldId(string.IsNullOrWhiteSpace(HubWorldId) ? "lobby" : HubWorldId);
            if (string.IsNullOrWhiteSpace(HubWorldId)) HubWorldId = "lobby";

            Worlds ??= new List<WorldDefinition>();

            var hub = Worlds.FirstOrDefault(w => w.IsHub) ?? Worlds.FirstOrDefault(w => w.Id.Equals(HubWorldId, StringComparison.OrdinalIgnoreCase));
            if (hub == null)
            {
                Worlds.Insert(0, new WorldDefinition
                {
                    Id = HubWorldId,
                    DisplayName = "Grand Lobby",
                    Description = "World utama (Hub, Shop & Home Player)",
                    IsHub = true,
                    Port = MasterWorldPort,
                    WorldFileName = "",
                    Size = "medium",
                    Difficulty = "classic",
                    Evil = "random",
                    AutoStart = true,
                    AutoSleep = false,
                    EmptyShutdownMinutes = 0
                });
            }
            else
            {
                hub.Id = SanitizeWorldId(hub.Id);
                if (string.IsNullOrEmpty(hub.Id)) hub.Id = HubWorldId;
                HubWorldId = hub.Id;
                hub.IsHub = true;
                hub.AutoSleep = false;
                hub.AutoStart = true;
            }

            if (SeedDefaultRtpWorld && !Worlds.Any(w => w.Id.Equals("rtp", StringComparison.OrdinalIgnoreCase)))
            {
                Worlds.Add(new WorldDefinition
                {
                    Id = "rtp",
                    DisplayName = "Wilderness RTP",
                    Description = "World terpisah untuk farming, boss & eksplorasi",
                    IsHub = false,
                    Port = AllocateNextFreePort(),
                    WorldFileName = "rtp.wld",
                    Size = "medium",
                    Difficulty = "classic",
                    Evil = "random",
                    AutoStart = false,
                    AutoSleep = true,
                    EmptyShutdownMinutes = -1
                });
            }

            foreach (var w in Worlds)
            {
                w.Id = SanitizeWorldId(w.Id);
                if (string.IsNullOrWhiteSpace(w.DisplayName)) w.DisplayName = w.Id;
                if (!w.IsHub && string.IsNullOrWhiteSpace(w.WorldFileName))
                {
                    w.WorldFileName = $"{w.Id}.wld";
                }
                w.Size = NormalizeSize(w.Size);
                w.Difficulty = NormalizeDifficulty(w.Difficulty);
                w.Evil = NormalizeEvil(w.Evil);
                if (string.IsNullOrWhiteSpace(w.ChatTag))
                {
                    w.ChatTag = w.Id.ToUpperInvariant();
                }
                if (string.IsNullOrWhiteSpace(w.ChatColorHex))
                {
                    w.ChatColorHex = w.IsHub
                        ? "38bdf8"
                        : (w.Id.Equals("rtp", StringComparison.OrdinalIgnoreCase) ? "4ade80" : "facc15");
                }
            }
        }

        public WorldDefinition? GetWorld(string idOrAlias)
        {
            if (string.IsNullOrWhiteSpace(idOrAlias)) return null;
            string q = idOrAlias.Trim();

            return Worlds.FirstOrDefault(w => w.Id.Equals(q, StringComparison.OrdinalIgnoreCase))
                ?? Worlds.FirstOrDefault(w => w.DisplayName.Equals(q, StringComparison.OrdinalIgnoreCase));
        }

        public int AllocateNextFreePort()
        {
            var used = new HashSet<int>
            {
                ProxyPort,
                FallbackProxyPort,
                MasterWorldPort,
                ControlIpcPort
            };
            foreach (var w in Worlds)
            {
                used.Add(w.Port);
            }

            int candidate = ChildPortStart;
            while (used.Contains(candidate) && candidate < 65000)
            {
                candidate++;
            }
            return candidate;
        }

        public int GetEffectiveAutoSleepMinutes(WorldDefinition world)
        {
            if (world.IsHub || !world.AutoSleep) return 0;
            return world.EmptyShutdownMinutes > 0 ? world.EmptyShutdownMinutes : EmptyShutdownMinutes;
        }

        public static string SanitizeWorldId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string cleaned = Regex.Replace(raw.Trim().ToLowerInvariant(), @"[^a-z0-9_-]", "");
            return cleaned.Length > 24 ? cleaned.Substring(0, 24) : cleaned;
        }

        public static string NormalizeSize(string? raw)
        {
            return (raw ?? "").Trim().ToLowerInvariant() switch
            {
                "small" or "s" or "kecil" or "1" => "small",
                "large" or "l" or "besar" or "3" => "large",
                _ => "medium"
            };
        }

        public static string NormalizeDifficulty(string? raw)
        {
            return (raw ?? "").Trim().ToLowerInvariant() switch
            {
                "expert" or "e" or "1" => "expert",
                "master" or "m" or "2" => "master",
                "journey" or "j" or "3" => "journey",
                _ => "classic"
            };
        }

        public static string NormalizeEvil(string? raw)
        {
            return (raw ?? "").Trim().ToLowerInvariant() switch
            {
                "corrupt" or "corruption" or "1" => "corrupt",
                "crimson" or "2" => "crimson",
                _ => "random"
            };
        }
    }
}
