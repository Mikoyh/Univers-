/*
 * =====================================================================
 *  BossingCommands.cs - Perintah Status & Admin Sistem Bossing Elden Ring
 *  Namespace: Univers.Bossing (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI;
using Univers.MultiWorld;

namespace Univers.Bossing
{
    public static class BossingCommands
    {
        private static readonly System.Collections.Generic.List<Command> _registered = new();

        public static void Register()
        {
            Unregister();
            var cmd = new Command("", BossingMainCommand, "bossing", "boss", "eldenboss");
            Commands.ChatCommands.Add(cmd);
            _registered.Add(cmd);
        }

        public static void Unregister()
        {
            foreach (var cmd in _registered)
            {
                Commands.ChatCommands.Remove(cmd);
            }
            _registered.Clear();
        }

        private static void BossingMainCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null) return;

            var cfg = BossingModule.Config;

            if (args.Parameters.Count > 0)
            {
                string sub = args.Parameters[0].ToLowerInvariant();
                switch (sub)
                {
                    case "toggle":
                        if (!RequireAdmin(player)) return;
                        cfg.Enabled = !cfg.Enabled;
                        BossingModule.SaveConfig();
                        player.SendSuccessMessage($"[ OK ] Sistem Bossing sekarang: {(cfg.Enabled ? "AKTIF" : "NONAKTIF")}.");
                        return;

                    case "reload":
                        if (!RequireAdmin(player)) return;
                        BossingModule.Reload();
                        player.SendSuccessMessage("[ OK ] Konfigurasi BossingConfig.json berhasil dimuat ulang.");
                        return;

                    case "setradius":
                    case "radius":
                        if (!RequireAdmin(player)) return;
                        if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out int newRadius) || newRadius < 20 || newRadius > 600)
                        {
                            player.SendErrorMessage("[ X ] Format: /bossing setradius <20-600> (dalam satuan tile dari Spawn).");
                            return;
                        }
                        cfg.CenterSanctuaryRadiusTiles = newRadius;
                        cfg.Normalize();
                        BossingModule.SaveConfig();
                        player.SendSuccessMessage($"[ OK ] Radius Zona Sanctuary Tengah diubah menjadi +/-{cfg.CenterSanctuaryRadiusTiles} tile dari World Spawn.");
                        return;

                    case "sethpmult":
                    case "hpmult":
                        if (!RequireAdmin(player)) return;
                        if (args.Parameters.Count < 2 || !double.TryParse(args.Parameters[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double newMult) || newMult < 1.0 || newMult > 100.0)
                        {
                            player.SendErrorMessage("[ X ] Format: /bossing sethpmult <1.0-100.0>");
                            return;
                        }
                        cfg.BossHpMultiplier = newMult;
                        cfg.Normalize();
                        BossingModule.SaveConfig();
                        player.SendSuccessMessage($"[ OK ] Pengali HP Boss Utama sekarang: {cfg.BossHpMultiplier:0.#}x (Damage tetap normal 1x).");
                        return;

                    case "seteventhpmult":
                    case "eventhpmult":
                        if (!RequireAdmin(player)) return;
                        if (args.Parameters.Count < 2 || !double.TryParse(args.Parameters[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double newEvMult) || newEvMult < 1.0 || newEvMult > 100.0)
                        {
                            player.SendErrorMessage("[ X ] Format: /bossing seteventhpmult <1.0-100.0>");
                            return;
                        }
                        cfg.EventMiniBossHpMultiplier = newEvMult;
                        cfg.Normalize();
                        BossingModule.SaveConfig();
                        player.SendSuccessMessage($"[ OK ] Pengali HP Event Mini-Boss (Pumpkin/Frost Moon, Pirate, Martian, OOA) sekarang: {cfg.EventMiniBossHpMultiplier:0.#}x.");
                        return;

                    case "setpillarhpmult":
                    case "pillarhpmult":
                        if (!RequireAdmin(player)) return;
                        if (args.Parameters.Count < 2 || !double.TryParse(args.Parameters[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double newPilMult) || newPilMult < 1.0 || newPilMult > 100.0)
                        {
                            player.SendErrorMessage("[ X ] Format: /bossing setpillarhpmult <1.0-100.0>");
                            return;
                        }
                        cfg.CelestialPillarHpMultiplier = newPilMult;
                        cfg.Normalize();
                        BossingModule.SaveConfig();
                        player.SendSuccessMessage($"[ OK ] Pengali HP 4 Celestial Pillars sekarang: {cfg.CelestialPillarHpMultiplier:0.#}x.");
                        return;

                    case "stoppillars":
                    case "resetpillars":
                    case "stoplunar":
                    case "clearpillars":
                        if (!RequireAdmin(player)) return;
                        int lunarCleared = BossingModule.AdminStopLunarEvent();
                        player.SendSuccessMessage($"[ OK ] Lunar Event & 4 Celestial Pillars direset ({lunarCleared} entitas dihapus, Moon Lord countdown dihentikan).");
                        return;

                    case "clear":
                    case "reset":
                        if (!RequireAdmin(player)) return;
                        BossArenaSide? targetSide = null;
                        if (args.Parameters.Count >= 2)
                        {
                            string s = args.Parameters[1].ToLowerInvariant();
                            if (s == "left" || s == "kiri") targetSide = BossArenaSide.Left;
                            else if (s == "right" || s == "kanan") targetSide = BossArenaSide.Right;
                            else if (s != "all" && s != "semua")
                            {
                                player.SendErrorMessage("[ X ] Format: /bossing clear [left|right|all]");
                                return;
                            }
                        }

                        int removed = BossingModule.AdminClearArena(targetSide);
                        string scope = targetSide.HasValue ? BossingModule.FormatSideLabel(targetSide.Value) : "KIRI & KANAN";
                        player.SendSuccessMessage($"[ OK ] Berhasil membersihkan {removed} entitas boss dari Arena {scope}.");
                        return;
                }
            }

            bool isActive = BossingModule.IsBossingActiveInCurrentWorld();
            string worldState = isActive ? "[c/4ade80:AKTIF]" : "[c/facc15:STANDBY]";
            string modeName = Main.masterMode ? "Master" : (Main.expertMode ? "Expert" : "Classic");

            player.SendMessage("=== [ UNIVERS ELDEN BOSSING SYSTEM ] ===", Color.Gold);
            player.SendMessage($"[ + ] World          : '{MultiWorldModule.CurrentWorldId}' ({modeName}) -> {worldState}", Color.White);
            player.SendMessage($"[ + ] Skala Boss     : HP x{cfg.BossHpMultiplier:0.#} | Damage x1 (Normal sesuai mode)", Color.White);
            player.SendMessage($"[ + ] Event & Pillars: Bebas batas 2 slot (HP Event x{cfg.EventMiniBossHpMultiplier:0.#} | HP 4 Pillars x{cfg.CelestialPillarHpMultiplier:0.#})", new Color(165, 243, 252));
            player.SendMessage($"[ + ] Sanctuary Spawn: +/-{cfg.CenterSanctuaryRadiusTiles} tile sekitar Spawn (X={Main.spawnTileX})", new Color(186, 230, 253));

            if (player.RealPlayer && player.Active && isActive)
            {
                BossArenaSide myZone = BossingModule.ResolveArenaSidePx(player.X, player.Y);
                string zoneLabel = myZone switch
                {
                    BossArenaSide.Left => "[c/facc15:ARENA KIRI (Boleh Summon)]",
                    BossArenaSide.Right => "[c/facc15:ARENA KANAN (Boleh Summon)]",
                    _ => "[c/38bdf8:ZONA SANCTUARY TENGAH (Aman / Dilarang Boss)]"
                };
                player.SendMessage($"[ + ] Posisimu       : {zoneLabel}", Color.White);
            }

            if (isActive)
            {
                var encounters = BossingModule.GetActiveEncountersSnapshot();
                PrintArenaSlot(player, "ARENA KIRI", encounters.FirstOrDefault(e => e.CountsTowardArenaSlot && e.Side == BossArenaSide.Left));
                PrintArenaSlot(player, "ARENA KANAN", encounters.FirstOrDefault(e => e.CountsTowardArenaSlot && e.Side == BossArenaSide.Right));

                var (eventCount, pillarCount) = BossingModule.GetActiveTransientSummary();
                if (eventCount > 0 || pillarCount > 0)
                {
                    player.SendMessage($"[ EVENT / PILLARS ] Aktif bebas slot: {eventCount} Event Mini-Boss | {pillarCount} Celestial Pillars", new Color(125, 211, 252));
                }
            }
            else
            {
                player.SendInfoMessage($"[ INFO ] Sistem ini aktif di world: {string.Join(", ", cfg.ActiveWorldIds)}.");
            }

            if (player.HasPermission("univers.bossing.admin"))
            {
                player.SendMessage("[ ADMIN ] /bossing clear [left|right|all] | stoppillars | setradius <tile> | sethpmult <n> | seteventhpmult <n> | setpillarhpmult <n> | toggle | reload", Color.Yellow);
            }
        }

        private static void PrintArenaSlot(TSPlayer player, string label, TrackedBossEncounter? enc)
        {
            if (enc == null)
            {
                player.SendMessage($"[ {label} ] Kosong (Siap untuk boss baru)", new Color(167, 243, 208));
                return;
            }

            var (curHp, maxHp) = BossingModule.GetEncounterHealthSummary(enc);
            double pct = maxHp > 0 ? (double)curHp / maxHp * 100.0 : 0.0;
            int ox = (int)(enc.OriginPosition.X / 16f);
            int oy = (int)(enc.OriginPosition.Y / 16f);

            string stateText;
            if (enc.IsIdleAtOrigin)
            {
                var cfg = BossingModule.Config;
                if (cfg.EnableDelayedIdleRegen && enc.IdleSinceUtc.HasValue)
                {
                    double elapsed = (DateTime.UtcNow - enc.IdleSinceUtc.Value).TotalSeconds;
                    if (elapsed < cfg.IdleRegenDelaySeconds)
                    {
                        int waitSec = Math.Max(1, (int)Math.Ceiling(cfg.IdleRegenDelaySeconds - elapsed));
                        stateText = $"DIAM MENUNGGU (Regen dalam {waitSec}s) di ({ox}, {oy})";
                    }
                    else if (enc.IsRegenerating)
                    {
                        stateText = $"DIAM & REGEN PERLAHAN (+{cfg.IdleRegenPercentPerSecond:0.##}%/s) di ({ox}, {oy})";
                    }
                    else
                    {
                        stateText = $"DIAM MENUNGGU (HP Penuh) di ({ox}, {oy})";
                    }
                }
                else
                {
                    stateText = $"DIAM MENUNGGU (No-Regen) di ({ox}, {oy})";
                }
            }
            else
            {
                stateText = enc.IsReturningToOrigin ? $"PULANG KE SPAWN ({ox}, {oy})" : "SEDANG BERTARUNG";
            }

            player.SendMessage(
                $"[ {label} ] {enc.DisplayName} | HP: {curHp:N0}/{maxHp:N0} ({pct:0.0}%) | Status: {stateText}",
                new Color(251, 191, 36));
        }

        private static bool RequireAdmin(TSPlayer player)
        {
            if (player.HasPermission("univers.bossing.admin")) return true;
            player.SendErrorMessage("[ X ] Kamu butuh permission 'univers.bossing.admin' untuk perintah ini.");
            return false;
        }
    }
}
