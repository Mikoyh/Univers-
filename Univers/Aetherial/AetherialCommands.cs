using System;
using System.Linq;
using TShockAPI;
using Univers.Exp;

namespace Univers.Aetherial
{
    public static class AetherialCommands
    {
        public static void Register()
        {
            Commands.ChatCommands.Add(new Command("", HandleElementCommand, "element", "elemen", "aether")
            {
                HelpText = "Perintah elemen resonansi (/element, /element list, /element reactions, /element awaken)"
            });

            Commands.ChatCommands.Add(new Command("univers.element.awaken", HandleAwakenDirect, "awakenelement", "pilihelemen")
            {
                HelpText = "Membangkitkan elemen jiwa secara acak (digunakan oleh RegionCmd / Warp Tutorial)"
            });

            Commands.ChatCommands.Add(new Command("univers.element.admin", HandleAdminElement, "setelement")
            {
                HelpText = "Set elemen pemain: /setelement <player> <ignis|glacial|torrent|volt|bramble|radiance|omnia>"
            });

            Commands.ChatCommands.Add(new Command("univers.element.admin", HandleResetElement, "resetelement")
            {
                HelpText = "Reset elemen pemain: /resetelement <player>"
            });

            Commands.ChatCommands.Add(new Command("univers.element.admin", HandleSetElementLevel, "setelementlevel")
            {
                HelpText = "Set level elemen & level utama pemain: /setelementlevel <player> <1-1000>"
            });
        }

        private static void HandleAwakenDirect(CommandArgs args)
        {
            if (args.Player == null) return;
            if (!args.Player.HasPermission("univers.element.awaken") && !args.Player.HasPermission("skynomi.element.awaken"))
            {
                args.Player.SendErrorMessage("[ ERROR ] Kamu tidak memiliki izin untuk perintah ini!");
                return;
            }
            ExecuteAwakenLogic(args.Player, args.Parameters.Count > 0 ? args.Parameters[0] : null, args.Parameters.Contains("-s") || args.Parameters.Contains("-silent"));
        }

        private static void HandleElementCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null || (!player.IsLoggedIn && !player.RealPlayer))
            {
                args.Player?.SendErrorMessage("[ ERROR ] Kamu harus login akun TShock untuk mengakses sistem Elemen!");
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
                    if (player.RealPlayer &&
                        !player.HasPermission("univers.element.awaken") &&
                        !player.HasPermission("univers.element") &&
                        !player.HasPermission("skynomi.element.awaken") &&
                        !player.HasPermission("skynomi.element"))
                    {
                        player.SendErrorMessage("[ ERROR ] Kamu tidak memiliki izin untuk memilih elemen jiwamu lagi!");
                        return;
                    }

                    string? targetPlayerName = args.Parameters.Count > 1 && !args.Parameters[1].StartsWith("-") ? args.Parameters[1] : null;
                    bool isSilent = args.Parameters.Contains("-s") || args.Parameters.Contains("-silent");
                    ExecuteAwakenLogic(player, targetPlayerName, isSilent);
                    break;

                case "reactions":
                case "reaksi":
                case "matriks":
                    ShowReactionsMatrix(player);
                    break;

                case "list":
                case "daftar":
                case "info":
                    ShowElementCatalog(player);
                    break;

                default:
                    if (profile != null) ShowElementStatus(player, profile);
                    break;
            }
        }

        private static void ExecuteAwakenLogic(TSPlayer caller, string? targetName, bool silent)
        {
            TSPlayer target = caller;
            if (!string.IsNullOrEmpty(targetName))
            {
                var found = TSPlayer.FindByNameOrID(targetName);
                if (found.Count == 0)
                {
                    if (!silent) caller.SendErrorMessage($"[ ERROR ] Pemain '{targetName}' tidak ditemukan online!");
                    return;
                }
                target = found[0];
            }

            if (target == null || !target.IsLoggedIn)
            {
                if (!silent) caller.SendErrorMessage("[ ERROR ] Target pemain harus login ke akun TShock terlebih dahulu!");
                return;
            }

            var profile = AetherialModule.Database.GetOrCreateProfile(target.Account.Name);
            if (profile == null) return;

            if (!string.IsNullOrEmpty(profile.AssignedElement))
            {
                if (!silent)
                {
                    target.SendWarningMessage($"[ INFO ] Kamu sudah memiliki Elemen: {profile.AssignedElement}! Elemen bersifat permanen dan tidak dapat diacak ulang.");
                }
                return;
            }

            AetherialModule.PerformAwakening(target);
        }

        private static void HandleResetElement(CommandArgs args)
        {
            if (args.Player == null) return;
            if (!args.Player.HasPermission("univers.element.admin") && !args.Player.HasPermission("skynomi.element.admin"))
            {
                args.Player.SendErrorMessage("[ ERROR ] Kamu tidak memiliki izin!");
                return;
            }

            if (args.Parameters.Count < 1)
            {
                args.Player.SendInfoMessage("[ FORMAT ] Penggunaan: /resetelement <player>");
                return;
            }

            var matched = TShock.UserAccounts.GetUserAccountByName(args.Parameters[0]);
            if (matched == null)
            {
                args.Player.SendErrorMessage($"[ ERROR ] Pemain {args.Parameters[0]} tidak ditemukan di database akun!");
                return;
            }

            var profile = AetherialModule.Database.GetOrCreateProfile(matched.Name);
            profile.AssignedElement = "";
            profile.AwakenedAt = DateTime.MinValue;
            AetherialModule.Database.SaveProfile(profile);

            var onlinePlayer = TShock.Players.FirstOrDefault(p =>
                p != null && p.Active && p.IsLoggedIn &&
                string.Equals(p.Account?.Name, matched.Name, StringComparison.OrdinalIgnoreCase));
            if (onlinePlayer != null)
            {
                AetherialModule.ClearPlayerVisualState(onlinePlayer.Index);
                onlinePlayer.SendWarningMessage("[ INFO ] Elemen jiwamu telah di-reset oleh Admin. Kamu bisa mengikuti Ritual Awakening kembali.");
            }

            args.Player.SendSuccessMessage($"[ OK ] Berhasil me-reset elemen pemain {matched.Name}! Pemain kini dapat di-awaken kembali.");
        }

        private static void ShowElementStatus(TSPlayer player, ElementUserProfile profile)
        {
            player.SendInfoMessage("=== [c/f59e0b:AETHERIAL RESONANCE SYSTEM] ===");
            if (string.IsNullOrEmpty(profile.AssignedElement))
            {
                player.SendWarningMessage("[ INFO ] Elemen jiwamu belum terbangun!");
                player.SendInfoMessage("Selesaikan tutorial atau ketik [c/4ade80:/element awaken] untuk memilih elemen jiwamu secara acak!");
                player.SendInfoMessage("Ketik [c/38bdf8:/element list] untuk melihat daftar 7 elemen yang tersedia.");
                return;
            }

            string elem = profile.AssignedElement.ToUpperInvariant();
            var elemProf = AetherialModule.GetElementProfile(elem);
            string elemHex = elemProf?.HexColor ?? "f59e0b";

            int lvl = Math.Max(1, profile.ElementLevel);
            var tier = AetherialModule.GetTierStatsForLevel(lvl);

            player.SendSuccessMessage($"Elemen Jiwamu: [c/{elemHex}:{elem}]  |  [c/38bdf8:Level: Lv.{lvl}] ([c/{tier.TierColor}:Tier {tier.TierNumber} / 7])");
            player.SendInfoMessage($"Proc Chance: [c/f59e0b:{tier.TagProcChance}%] | Durasi Tag: [c/f59e0b:{tier.TagDurationSeconds}s] | Reaksi Terpicu: [c/a3e635:{profile.TotalReactionsTriggered}x]");
            player.SendInfoMessage($"Benefit Solo: +{tier.WeaponSynergyBonusPercent}% Synergy Senjata, +{tier.SoloMarkedBonusPercent}% Marked DMG (Max +{tier.MaxSoloDamagePercent}%)");

            if (tier.HasLargeGemDisplay)
                player.SendInfoMessage("[c/4ade80:[ + ] Large Gem Aktif: Kristal berputar di atas kepala saat tag aktif!]");
            else
                player.SendInfoMessage("[c/94a3b8:Large Gem terbuka mulai Tier 4 (Lv. 180+). Skill 10 detik terbuka mulai Tier 2 (Lv. 25+).]");

            if (elem == "OMNIA")
            {
                player.SendInfoMessage("[c/e879f9:[ + ] ELEMEN EASTER EGG RAHASIA (0.000001% CHANCE)]");
                player.SendInfoMessage("[c/fdf4ff:Pasif: Multiversal Shatter Instakill + Universal Resonance dengan seluruh elemen lain!]");
            }

            player.SendInfoMessage("Ketik [c/38bdf8:/element reactions] (15 Kombo) | [c/38bdf8:/element list] (Daftar 7 Elemen)");
        }

        private static void ShowElementCatalog(TSPlayer player)
        {
            player.SendInfoMessage("=== [c/38bdf8:DAFTAR 7 ELEMEN AETHERIAL] ===");
            player.SendInfoMessage("[c/f97316:1. IGNIS]    (Common)  - Api Oranye | Debuff: OnFire -> Hellfire -> Daybroken | Aura: Inferno Ring");
            player.SendInfoMessage("[c/38bdf8:2. GLACIAL]  (Rare)    - Es Kristal  | Debuff: Chilled -> Frostburn -> Frostbite | Aura: Ice Barrier");
            player.SendInfoMessage("[c/06b6d4:3. TORRENT]  (Common)  - Air Samudra | Debuff: Wet -> Slow -> Betsy's Curse | Aura: WaterWalk/Swift");
            player.SendInfoMessage("[c/a855f7:4. VOLT]     (Rare)    - Petir Ungu  | Debuff: Confused -> Electrified -> ShadowFlame | Aura: Swiftness");
            player.SendInfoMessage("[c/10b981:5. BRAMBLE]  (Common)  - Alam Hijau  | Debuff: Poisoned -> CursedInferno -> Venom | Aura: Dryad Leaf Ring");
            player.SendInfoMessage("[c/facc15:6. RADIANCE] (Epic 5%) - Cahaya Emas | Debuff: Midas -> Ichor -> Daybroken | Aura: Shine + Wrath");
            player.SendInfoMessage("[c/e879f9:7. OMNIA]    (Secret)  - Singularitas Cosmic (0.000001%) | Multiversal Shatter + Semua Aura");
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
            if (args.Player == null) return;
            if (!args.Player.HasPermission("univers.element.admin") && !args.Player.HasPermission("skynomi.element.admin"))
            {
                args.Player.SendErrorMessage("[ ERROR ] Kamu tidak memiliki izin!");
                return;
            }

            if (args.Parameters.Count < 2)
            {
                args.Player.SendInfoMessage("[ FORMAT ] Penggunaan: /setelement <player> <ignis|glacial|torrent|volt|bramble|radiance|omnia>");
                args.Player.SendInfoMessage("Pilihan valid: [c/f97316:ignis] | [c/38bdf8:glacial] | [c/06b6d4:torrent] | [c/a855f7:volt] | [c/10b981:bramble] | [c/facc15:radiance] | [c/e879f9:omnia]");
                return;
            }

            var matched = TShock.UserAccounts.GetUserAccountByName(args.Parameters[0]);
            if (matched == null)
            {
                args.Player.SendErrorMessage($"[ ERROR ] Pemain {args.Parameters[0]} tidak ditemukan!");
                return;
            }

            string rawElem = args.Parameters[1].Trim().ToUpperInvariant();
            if (!AetherialModule.ElementRegistry.TryGetValue(rawElem, out var elemProf))
            {
                args.Player.SendErrorMessage($"[ ERROR ] Elemen '{args.Parameters[1]}' tidak valid!");
                args.Player.SendInfoMessage("Opsi elemen: [c/f97316:ignis] | [c/38bdf8:glacial] | [c/06b6d4:torrent] | [c/a855f7:volt] | [c/10b981:bramble] | [c/facc15:radiance] | [c/e879f9:omnia]");
                return;
            }

            string newElem = elemProf.ElementId;
            var profile = AetherialModule.Database.GetOrCreateProfile(matched.Name);
            profile.AssignedElement = newElem;
            profile.ElementLevel = ExpApi.GetLevel(matched.Name);
            if (profile.AwakenedAt == DateTime.MinValue) profile.AwakenedAt = DateTime.UtcNow;
            AetherialModule.Database.SaveProfile(profile);

            var onlineTarget = TShock.Players.FirstOrDefault(p =>
                p != null && p.Active && p.IsLoggedIn &&
                string.Equals(p.Account?.Name, matched.Name, StringComparison.OrdinalIgnoreCase));
            if (onlineTarget != null)
            {
                AetherialModule.ClearPlayerVisualState(onlineTarget.Index);
                onlineTarget.SendSuccessMessage($"[ AETHERIAL ] Admin mengubah elemen jiwamu menjadi [c/{elemProf.HexColor}:{newElem}] (Lv.{profile.ElementLevel})!");
            }

            args.Player.SendSuccessMessage($"[ OK ] Berhasil mengubah elemen {matched.Name} menjadi [c/{elemProf.HexColor}:{newElem}]!");
        }

        private static void HandleSetElementLevel(CommandArgs args)
        {
            if (args.Player == null) return;
            if (!args.Player.HasPermission("univers.element.admin") && !args.Player.HasPermission("skynomi.element.admin"))
            {
                args.Player.SendErrorMessage("[ ERROR ] Kamu tidak memiliki izin!");
                return;
            }

            if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out int newLevel))
            {
                args.Player.SendInfoMessage("[ FORMAT ] Penggunaan: /setelementlevel <player> <1-1000>");
                return;
            }

            var matched = TShock.UserAccounts.GetUserAccountByName(args.Parameters[0]);
            if (matched == null)
            {
                args.Player.SendErrorMessage($"[ ERROR ] Pemain {args.Parameters[0]} tidak ditemukan!");
                return;
            }

            newLevel = Math.Clamp(newLevel, 1, 1000);
            var onlinePlayer = TShock.Players.FirstOrDefault(p =>
                p != null && p.Active && p.IsLoggedIn &&
                string.Equals(p.Account?.Name, matched.Name, StringComparison.OrdinalIgnoreCase));

            // Sinkronkan dua arah: ExpApi.SetLevel otomatis memicu OnLevelChanged -> update ElementLevel juga
            ExpApi.SetLevel(matched.Name, newLevel, onlinePlayer, true);

            var tier = AetherialModule.GetTierStatsForLevel(newLevel);
            args.Player.SendSuccessMessage($"[ OK ] Berhasil menyinkronkan Level Utama & Elemen {matched.Name} ke Lv.{newLevel} (Proc: {tier.TagProcChance}%, Synergy: +{tier.WeaponSynergyBonusPercent}%)!");
        }
    }
}
