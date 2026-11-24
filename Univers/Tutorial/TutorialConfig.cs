/*
 * =====================================================================
 *  TutorialConfig.cs - Konfigurasi Area Tutorial & Distorsi Waktu Takdir
 *  Namespace: Univers.Tutorial (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TShockAPI;

namespace Univers.Tutorial
{
    public class TutorialConfig
    {
        public bool Enabled { get; set; } = true;

        // World tempat tutorial berada (default: lobby)
        public List<string> ActiveWorldIds { get; set; } = new() { "lobby" };

        // Titik Spawn Tutorial (set via /tutorial setspawn). -1 berarti belum diatur.
        public int TutorialSpawnX { get; set; } = -1;
        public int TutorialSpawnY { get; set; } = -1;

        // Nama Region TShock untuk Altar Pembangkitan Elemen (set via /tutorial setregion <nama>)
        public string AwakenRegionName { get; set; } = "tutorial_awaken";

        // Altar koordinat opsional jika tidak memakai TShock Region (set via /tutorial setaltar [radius])
        public int AltarCenterX { get; set; } = -1;
        public int AltarCenterY { get; set; } = -1;
        public int AltarRadiusTiles { get; set; } = 5;

        // Aturan Routing Pemain Baru
        public bool TeleportNewPlayersOnLogin { get; set; } = true;
        public bool LockUnawakenedPlayersInTutorial { get; set; } = true;
        public bool BlockWorldWarpBeforeAwaken { get; set; } = true;
        public bool AutoMarkPlayersWhoAlreadyHaveElement { get; set; } = true;

        // Durasi Ritual
        public int ChannelingSeconds { get; set; } = 6;
        public int PostAwakenCountdownSeconds { get; set; } = 5;

        // Kecepatan Putaran Waktu Dunia saat Persiapan (Channeling)
        public double ChannelingStartTicksPerSecond { get; set; } = 7200.0;
        public double ChannelingMaxTicksPerSecond { get; set; } = 26000.0;

        // Kecepatan & Durasi Putaran Waktu Dunia Berdasarkan Rarity Elemen
        // (1 siklus siang+malam penuh Terraria = 86.400 ticks)
        public int CommonTimeDurationSeconds { get; set; } = 10;       // IGNIS, TORRENT, BRAMBLE
        public double CommonTimeTicksPerSecond { get; set; } = 10800.0;

        public int UncommonTimeDurationSeconds { get; set; } = 20;     // GLACIAL, VOLT
        public double UncommonTimeTicksPerSecond { get; set; } = 28800.0;

        public int EpicTimeDurationSeconds { get; set; } = 45;         // RADIANCE
        public double EpicTimeTicksPerSecond { get; set; } = 64800.0;

        public int OmniaTimeDurationSeconds { get; set; } = 3600;      // OMNIA (1 Jam Realtime!)
        public double OmniaTimeTicksPerSecond { get; set; } = 129600.0;

        public bool BroadcastTimeDistortion { get; set; } = true;
        public bool ShowChaosVisualEffects { get; set; } = true;

        public bool IsWorldActive(string? worldId)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(worldId) || ActiveWorldIds == null || ActiveWorldIds.Count == 0)
            {
                return false;
            }

            return ActiveWorldIds.Any(id =>
                !string.IsNullOrWhiteSpace(id) &&
                id.Equals(worldId, StringComparison.OrdinalIgnoreCase));
        }

        public bool HasTutorialSpawnConfigured()
        {
            return TutorialSpawnX > 10 && TutorialSpawnY > 10;
        }

        public bool HasDirectAltarConfigured()
        {
            return AltarCenterX > 10 && AltarCenterY > 10 && AltarRadiusTiles > 0;
        }

        public static TutorialConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonConvert.DeserializeObject<TutorialConfig>(json);
                    if (cfg != null)
                    {
                        if (cfg.ActiveWorldIds == null || cfg.ActiveWorldIds.Count == 0)
                        {
                            cfg.ActiveWorldIds = new List<string> { "lobby" };
                        }
                        if (cfg.ChannelingSeconds < 2) cfg.ChannelingSeconds = 2;
                        if (cfg.PostAwakenCountdownSeconds < 2) cfg.PostAwakenCountdownSeconds = 2;
                        if (cfg.AltarRadiusTiles < 1) cfg.AltarRadiusTiles = 5;
                        cfg.Save(path);
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.Tutorial] Gagal membaca TutorialConfig.json, menggunakan default: {ex.Message}");
            }

            var fresh = new TutorialConfig();
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
                TShock.Log.ConsoleError($"[Univers.Tutorial] Gagal menyimpan TutorialConfig.json: {ex.Message}");
            }
        }
    }
}
