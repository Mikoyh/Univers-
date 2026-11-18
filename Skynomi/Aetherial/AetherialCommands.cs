/*
 * =====================================================================
 *  AetherialCommands.cs - Interactive player commands for Elements
 * =====================================================================
 */

using System;
using TShockAPI;

namespace Skynomi.Aetherial
{
    public static class AetherialCommands
    {
        public static void Register()
        {
            // Command umum: Terbuka untuk semua pemain yang SUDAH LOGIN (pengecekan IsLoggedIn ada di HandleElementCommand)
            Commands.ChatCommands.Add(new Command("", HandleElementCommand, "element", "elemen", "aether")
            {
                HelpText = "Perintah elemen resonansi (/element, /element awaken, /element reactions)"
            });

            // Command awaken khusus (hanya dijalankan RegionCmd / Warp Tutorial atau yang punya izin)
            Commands.ChatCommands.Add(new Command("skynomi.element.awaken", HandleAwakenDirect, "awakenelement", "pilihelemen")
            {
                HelpText = "Membangkitkan elemen jiwa secara acak (digunakan oleh RegionCmd / Warp Tutorial)"
            });

            // Admin commands: HANYA SuperAdmin (su) yang bisa mengakses
            Commands.ChatCommands.Add(new Command("skynomi.element.admin", HandleAdminElement, "setelement")
            {
                HelpText = "Set elemen pemain: /setelement <player> <ignis|glacial|torrent|volt|bramble|radiance|omnia>"
            });

            Commands.ChatCommands.Add(new Command("skynomi.element.admin", HandleResetElement, "resetelement")
            {
                HelpText = "Reset elemen pemain: /resetelement <player>"
            });

            Commands.ChatCommands.Add(new Command("skynomi.element.admin", HandleSetElementLevel, "setelementlevel")
            {
                HelpText = "Set level elemen pemain: /setelementlevel <player> <1-1000>"
            });
        }


        private static void HandleAwakenDirect(CommandArgs args)
        {
            // Dedicated direct command for RegionCmd
            ExecuteAwakenLogic(args.Player, args.Parameters.Count > 0 ? args.Parameters[0] : null, args.Parameters.Contains("-s") || args.Parameters.Contains("-silent"));
        }

        private static void HandleElementCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null || (!player.IsLoggedIn && !player.RealPlayer))
            {
                args.Player.SendErrorMessage("[c/ef4444:Kamu harus login akun TShock untuk mengakses sistem Elemen!]");
                return;
            }

            var profile = player.IsLoggedIn ? AetherialModule.Database.GetOrCreateProfile(player.Account.Name) : null;

            if (args.Parameters.Count == 0)
            {
                if (profile != null) ShowElementStatus(player, profile);
                return;
            }

            string subCmd = args.Parameters[0].ToLowerInvariant();

            switch (subCmd)
            {
                case "awaken":
                case "bangkit":
                case "random":
                    // Check if player has permission or if executed by RegionCmd / Server
                    if (player.RealPlayer && !player.HasPermission("skynomi.element.awaken") && !player.HasPermission("skynomi.element"))
                    {
                        player.SendErrorMessage("[c/ef4444:Kamu tidak memiliki izin untuk memilih elemen jiwamu lagi!]");
                        return;
                    }

                    string targetPlayerName = args.Parameters.Count > 1 && !args.Parameters[1].StartsWith("-") ? args.Parameters[1] : null;
                    bool isSilent = args.Parameters.Contains("-s") || args.Parameters.Contains("-silent");
                    ExecuteAwakenLogic(player, targetPlayerName, isSilent);
                    break;

                case "reactions":
                case "reaksi":
                case "matriks":
                    ShowReactionsMatrix(player);
                    break;

                default:
                    if (profile != null) ShowElementStatus(player, profile);
                    break;
            }
        }

        // Ubah deklarasi string targetName menjadi string? targetName
        private static void ExecuteAwakenLogic(TSPlayer caller, string? targetName, bool silent)
        {
            TSPlayer target = caller;
            if (!string.IsNullOrEmpty(targetName))
            {
                var found = TSPlayer.FindByNameOrID(targetName);
                if (found.Count == 0)
                {
                    if (!silent) caller.SendErrorMessage($"[c/ef4444:Pemain '{targetName}' tidak ditemukan online!]");
                    return;
                }
                target = found[0];
            }

            if (target == null || !target.IsLoggedIn)
            {
                if (!silent) caller.SendErrorMessage("[c/ef4444:Target pemain harus login ke akun TShock terlebih dahulu!]");
                return;
            }

            var profile = AetherialModule.Database.GetOrCreateProfile(target.Account.Name);
            if (profile == null) return;

            // --- PROTEKSI GANDA / IDEMPOTENT GUARD ---
            // Jika pemain SUDAH memiliki elemen, batalkan eksekusi secara absolut!
            if (!string.IsNullOrEmpty(profile.AssignedElement))
            {
                if (!silent)
                {
                    target.SendWarningMessage($"[c/fbbf24:Kamu sudah memiliki Elemen: {profile.AssignedElement}! Elemen bersifat permanen dan tidak dapat diacak ulang.]");
                }
                return;
            }

            // Jalankan ritual acak elemen (15% proc standard, 0.000001% easter egg secret)
            AetherialModule.PerformAwakening(target);
        }

        private static void HandleResetElement(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendInfoMessage("Penggunaan: /resetelement <player>");
                return;
            }

            var matched = TShock.UserAccounts.GetUserAccountByName(args.Parameters[0]);
            if (matched == null)
            {
                args.Player.SendErrorMessage($"Pemain {args.Parameters[0]} tidak ditemukan di database akun!");
                return;
            }

            var profile = AetherialModule.Database.GetOrCreateProfile(matched.Name);
            profile.AssignedElement = "";
            profile.AwakenedAt = DateTime.MinValue;
            AetherialModule.Database.SaveProfile(profile);

            args.Player.SendSuccessMessage($"[c/4ade80:Berhasil me-reset elemen pemain {matched.Name}! Pemain kini dapat di-awaken kembali.]");
        }

        private static void ShowElementStatus(TSPlayer player, ElementUserProfile profile)
        {
            player.SendInfoMessage("=== [c/f59e0b:AETHERIAL RESONANCE SYSTEM] ===");
            if (string.IsNullOrEmpty(profile.AssignedElement))
            {
                player.SendWarningMessage("[c/fbbf24:Elemen jiwamu belum terbangun!]");
                player.SendInfoMessage("Selesaikan tutorial atau ketik [c/4ade80:/element awaken] untuk memilih elemen jiwamu secara acak!");
                return;
            }

            string elem = profile.AssignedElement.ToUpper();
            int lvl = Math.Max(1, profile.ElementLevel);
            var tier = AetherialModule.GetTierStatsForLevel(lvl);

            player.SendSuccessMessage($"Elemen Jiwamu: [c/f59e0b:{elem}]  |  [c/38bdf8:Level: Lv.{lvl}] ([c/{tier.TierColor}:Tier {tier.TierNumber} / 7])");
            player.SendInfoMessage($"Proc Chance: [c/f59e0b:{tier.TagProcChance}%] | Durasi Tag: [c/f59e0b:{tier.TagDurationSeconds}s] | Visual Stage: [c/{tier.TierColor}:Stage {tier.VisualStage}/5]");
            player.SendInfoMessage($"Benefit Solo: +{tier.WeaponSynergyBonusPercent}% Synergy Senjata, +{tier.SoloMarkedBonusPercent}% Marked DMG (Max +{tier.MaxSoloDamagePercent}%)");

            if (tier.HasLargeGemDisplay)
                player.SendInfoMessage("[c/4ade80:★ Large Gem Aktif: Kristal berputar di atas kepala saat tag aktif!]");
            else
                player.SendInfoMessage("[c/94a3b8:Large Gem terbuka mulai Tier 4 (Lv. 180+).]");


            if (elem == "OMNIA")
            {
                player.SendInfoMessage("[c/e879f9:★ ELEMEN EASTER EGG RAHASIA (0.000001% CHANCE) ★]");
                player.SendInfoMessage("[c/fdf4ff:Pasif: 5x Multiversal Damage + Universal Resonance dengan seluruh elemen lain!]");
            }

            player.SendInfoMessage("Ketik [c/38bdf8:/element reactions] untuk melihat panduan kombo 2 elemen!");
        }

        private static void ShowReactionsMatrix(TSPlayer player)
        {
            player.SendInfoMessage("=== [c/38bdf8:15 MATRIKS REAKSI ELEMEN RESONANSI] ===");
            player.SendInfoMessage("[c/f97316:Ignis + Glacial] = THERMAL SHOCK (+100%) | [c/fbbf24:Ignis + Torrent] = SCALDING (+85%)");
            player.SendInfoMessage("[c/f43f5e:Ignis + Volt] = PLASMA BLAST (+55% AoE) | [c/ea580c:Ignis + Bramble] = WILDFIRE (+50% Burn)");
            player.SendInfoMessage("[c/facc15:Ignis + Radiance] = SUPERNOVA (+95% Burst) | [c/38bdf8:Glacial + Torrent] = PERMAFROST (Stun)");
            player.SendInfoMessage("[c/a855f7:Glacial + Volt] = STATIC DECAY (-30% Def) | [c/2dd4bf:Glacial + Bramble] = FROSTBLIGHT (Slow)");
            player.SendInfoMessage("[c/fef08a:Glacial + Radiance] = PRISM REFRACTION | [c/06b6d4:Torrent + Volt] = IONIZED STREAM");
            player.SendInfoMessage("[c/10b981:Torrent + Bramble] = OVERGROWTH (Venom) | [c/fde047:Torrent + Radiance] = PURIFICATION (+20 HP)");
            player.SendInfoMessage("[c/a3e635:Volt + Bramble] = CATALYZE (+60%) | [c/eab308:Volt + Radiance] = RADIANT ARC (Crit)");
            player.SendInfoMessage("[c/84cc16:Bramble + Radiance] = PHOTOSYNTHESIS (+HP/Mana) | [c/e879f9:Omnia + Apapun] = INSTAKILL");
        }


        private static void HandleAdminElement(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendInfoMessage("Penggunaan: /setelement <player> <ignis|glacial|torrent|volt|bramble|omnia>");
                return;
            }

            var matched = TShock.UserAccounts.GetUserAccountByName(args.Parameters[0]);
            if (matched == null)
            {
                args.Player.SendErrorMessage($"Pemain {args.Parameters[0]} tidak ditemukan!");
                return;
            }

            string newElem = args.Parameters[1].ToUpperInvariant();
            var profile = AetherialModule.Database.GetOrCreateProfile(matched.Name);
            profile.AssignedElement = newElem;
            AetherialModule.Database.SaveProfile(profile);

            args.Player.SendSuccessMessage($"Berhasil mengubah elemen {matched.Name} menjadi {newElem}!");
        }

        private static void HandleSetElementLevel(CommandArgs args)
        {
            if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out int newLevel))
            {
                args.Player.SendInfoMessage("Penggunaan: /setelementlevel <player> <1-1000>");
                return;
            }

            var matched = TShock.UserAccounts.GetUserAccountByName(args.Parameters[0]);
            if (matched == null)
            {
                args.Player.SendErrorMessage($"Pemain {args.Parameters[0]} tidak ditemukan!");
                return;
            }

            newLevel = Math.Clamp(newLevel, 1, 1000);
            var profile = AetherialModule.Database.GetOrCreateProfile(matched.Name);
            profile.ElementLevel = newLevel;
            AetherialModule.Database.SaveProfile(profile);

            var tier = AetherialModule.GetTierStatsForLevel(newLevel);
            args.Player.SendSuccessMessage($"Berhasil mengubah Level Elemen {matched.Name} menjadi Lv.{newLevel} (Proc: {tier.TagProcChance}%, Synergy: +{tier.WeaponSynergyBonusPercent}%)!");
        }
    }
}
