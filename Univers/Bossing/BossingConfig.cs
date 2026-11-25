/*
 * =====================================================================
 *  BossingConfig.cs - Konfigurasi Sistem Elden Ring Bossing Univers
 *  Namespace: Univers.Bossing (.NET 9.0 / TShock 6.x / .NET 9)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TShockAPI;

namespace Univers.Bossing
{
    public class BossingConfig
    {
        public bool Enabled { get; set; } = true;

        // World tempat sistem Elden Ring Bossing aktif (default: survival)
        public List<string> ActiveWorldIds { get; set; } = new() { "survival" };

        // Pengali HP Boss (10x untuk semua mode: Classic / Expert / Master), Damage tetap 1x bawaan mode!
        public double BossHpMultiplier { get; set; } = 10.0;

        // Jaga agar damage boss tetap murni bawaan mode dunia (tidak pernah dikali)
        public bool KeepBossDamageUnchanged { get; set; } = true;

        // Pengali hadiah Shards & EXP di BossContributionTracker (berbasis HP vanilla sebelum 10x agar ekonomi tetap seimbang)
        public double BossRewardMultiplier { get; set; } = 1.5;

        // Radius Zona Sanctuary Tengah di sekitar World Spawn (dalam satuan tile ke kiri & kanan Main.spawnTileX)
        public int CenterSanctuaryRadiusTiles { get; set; } = 120;

        // Jika false, pertarungan Underworld (Wall of Flesh) tetap bebas melintasi X tengah di lapisan Neraka
        public bool CenterSanctuaryIncludeUnderworld { get; set; } = false;

        // Jarak aman tambahan dari tepi Sanctuary Tengah untuk titik anchor spawn awal boss (tile)
        public int OriginSafeOffsetFromSanctuaryTiles { get; set; } = 18;

        // Maksimal boss aktif per sisi arena (1 Kiri + 1 Kanan = maksimal 2 boss di world survival)
        public int MaxBossesPerSide { get; set; } = 1;

        // Jika false (default), mini-boss event (Pumpkin/Frost Moon, Pirate, Martian, OOA) dan 4 Celestial Pillars
        // bebas batas 2 slot agar gelombang event & Lunar Event end-game tidak pernah terblokir
        public bool CountEventMiniBossesInArenaSlots { get; set; } = false;

        // Pengali HP khusus untuk Event Mini-Boss (default 1.0x agar Pumpkin/Frost Moon tetap adil sebelum pagi)
        public double EventMiniBossHpMultiplier { get; set; } = 1.0;

        // Pengali HP khusus untuk 4 Celestial Pillars pasca-Lunatic Cultist (default 1.0x sebelum Moon Lord 10x)
        public double CelestialPillarHpMultiplier { get; set; } = 1.0;

        // Jaga agar Event Mini-Boss & Celestial Pillars tidak masuk/spawn di dalam Zona Sanctuary Tengah
        public bool KeepEventBossesOutOfCenterSanctuary { get; set; } = true;

        // Anti-Despawn, Return-to-Origin & Delayed Gentle Regen saat tidak ada player hidup di arena
        public bool PreventBossDespawn { get; set; } = true;
        public bool ReturnToOriginWhenNoPlayers { get; set; } = true;
        public bool PreventRegenWhileIdle { get; set; } = true;
        public bool EnableDelayedIdleRegen { get; set; } = true;
        public double IdleRegenDelaySeconds { get; set; } = 15.0;
        public double IdleRegenPercentPerSecond { get; set; } = 0.25;

        // Kecepatan boss pulang ke titik spawn awal (pixel per tick; 16 px = 1 tile per tick)
        public float ReturnSpeedPixelsPerTick { get; set; } = 16.0f;

        // Jeda tunggu setelah semua player mati/keluar arena sebelum boss mulai pulang ke spawn awal (detik)
        public double NoChallengerGraceSeconds { get; set; } = 2.0;

        // Jarak player masih dianggap bertarung dengan boss yang sedang aktif (tile)
        public int ActiveCombatMaxDistanceTiles { get; set; } = 360;

        // Jarak player mendekati boss yang sedang diam menunggu untuk membangunkannya kembali (tile)
        public int WakeUpDistanceTiles { get; set; } = 135;

        // Visual & Notifikasi Chat
        public bool ShowCenterSanctuaryVisuals { get; set; } = true;
        public bool AnnounceBossEventsInChat { get; set; } = true;

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

        public void Normalize()
        {
            BossHpMultiplier = Math.Clamp(BossHpMultiplier, 1.0, 100.0);
            EventMiniBossHpMultiplier = Math.Clamp(EventMiniBossHpMultiplier, 1.0, 100.0);
            CelestialPillarHpMultiplier = Math.Clamp(CelestialPillarHpMultiplier, 1.0, 100.0);
            BossRewardMultiplier = Math.Clamp(BossRewardMultiplier, 0.5, 10.0);
            CenterSanctuaryRadiusTiles = Math.Clamp(CenterSanctuaryRadiusTiles, 20, 600);
            OriginSafeOffsetFromSanctuaryTiles = Math.Clamp(OriginSafeOffsetFromSanctuaryTiles, 5, 120);
            MaxBossesPerSide = Math.Clamp(MaxBossesPerSide, 1, 5);
            ReturnSpeedPixelsPerTick = Math.Clamp(ReturnSpeedPixelsPerTick, 4.0f, 48.0f);
            NoChallengerGraceSeconds = Math.Clamp(NoChallengerGraceSeconds, 0.5, 15.0);
            IdleRegenDelaySeconds = Math.Clamp(IdleRegenDelaySeconds, 2.0, 300.0);
            IdleRegenPercentPerSecond = Math.Clamp(IdleRegenPercentPerSecond, 0.05, 5.0);
            ActiveCombatMaxDistanceTiles = Math.Clamp(ActiveCombatMaxDistanceTiles, 120, 1200);
            WakeUpDistanceTiles = Math.Clamp(WakeUpDistanceTiles, 40, 400);

            if (ActiveWorldIds == null || ActiveWorldIds.Count == 0)
            {
                ActiveWorldIds = new List<string> { "survival" };
            }
        }

        public static BossingConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonConvert.DeserializeObject<BossingConfig>(json);
                    if (cfg != null)
                    {
                        cfg.Normalize();
                        cfg.Save(path);
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.Bossing] Gagal membaca BossingConfig.json, menggunakan default: {ex.Message}");
            }

            var fresh = new BossingConfig();
            fresh.Normalize();
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
                TShock.Log.ConsoleError($"[Univers.Bossing] Gagal menyimpan BossingConfig.json: {ex.Message}");
            }
        }
    }
}
