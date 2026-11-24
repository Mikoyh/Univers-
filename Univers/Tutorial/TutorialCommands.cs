/*
 * =====================================================================
 *  TutorialCommands.cs - Perintah /tutorial & Setup Admin Region/Spawn
 *  Namespace: Univers.Tutorial (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Linq;
using Microsoft.Xna.Framework;
using TShockAPI;
using Univers.Aetherial;
using Univers.MultiWorld;

namespace Univers.Tutorial
{
    public static class TutorialCommands
    {
        public static void Register()
        {
            Commands.ChatCommands.Add(new Command("", TutorialMainCommand, "tutorial", "tut"));
        }

        private static void TutorialMainCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null) return;

            var cfg = TutorialModule.Config;

            if (args.Parameters.Count > 0)
            {
                string sub = args.Parameters[0].ToLowerInvariant();
                switch (sub)
                {
                    case "info":
                    case "status":
                        ShowTutorialInfo(player);
                        return;

                    case "help":
                    case "?":
                        ShowHelp(player);
                        return;

                    case "setspawn":
                        if (!RequireAdmin(player) || !RequireRealPlayer(player)) return;
                        cfg.TutorialSpawnX = player.TileX;
                        cfg.TutorialSpawnY = player.TileY;
                        TutorialModule.SaveConfig();
                        player.SendSuccessMessage($"[ OK ] Titik Spawn Tutorial disimpan ke koordinat tile ({cfg.TutorialSpawnX}, {cfg.TutorialSpawnY})!");
                        return;

                    case "setregion":
                        if (!RequireAdmin(player)) return;
                        HandleSetRegion(player, args);
                        return;

                    case "setaltar":
                        if (!RequireAdmin(player) || !RequireRealPlayer(player)) return;
                        HandleSetAltar(player, args);
                        return;

                    case "clearaltar":
                        if (!RequireAdmin(player)) return;
                        cfg.AltarCenterX = -1;
                        cfg.AltarCenterY = -1;
                        TutorialModule.SaveConfig();
                        player.SendSuccessMessage("[ OK ] Titik koordinat Altar langsung dihapus (hanya memakai TShock Region).");
                        return;

                    case "reset":
                        if (!RequireAdmin(player)) return;
                        HandleResetPlayer(player, args);
                        return;

                    case "stopstorm":
                    case "restoretime":
                        if (!RequireAdmin(player)) return;
                        if (TutorialModule.ForceStopTimeStorm(announce: true))
                        {
                            player.SendSuccessMessage("[ OK ] Distorsi waktu dihentikan dan waktu dunia dikembalikan seperti semula.");
                        }
                        else
                        {
                            player.SendInfoMessage("[ INFO ] Tidak ada badai waktu yang sedang aktif saat ini.");
                        }
                        return;

                    case "teststorm":
                        if (!RequireAdmin(player)) return;
                        HandleTestStorm(player, args);
                        return;

                    case "reload":
                        if (!RequireAdmin(player)) return;
                        TutorialModule.Reload();
                        player.SendSuccessMessage("[ OK ] Konfigurasi TutorialConfig.json berhasil dimuat ulang.");
                        return;
                }
            }

            if (!player.RealPlayer || !player.IsLoggedIn)
            {
                player.SendErrorMessage("[ TUTORIAL ] Perintah teleport tutorial hanya untuk pemain di dalam game. Ketik /tutorial info.");
                return;
            }

            if (!TutorialModule.IsTutorialActiveInCurrentWorld())
            {
                player.SendInfoMessage($"[ TUTORIAL ] Area Tutorial berada di world 'lobby' (saat ini kamu di '{MultiWorldModule.CurrentWorldId}'). Ketik /lobby terlebih dahulu.");
                return;
            }

            if (!cfg.HasTutorialSpawnConfigured())
            {
                player.SendErrorMessage("[ TUTORIAL ] Titik Spawn Tutorial belum diatur oleh Admin (/tutorial setspawn).");
                return;
            }

            var tutProfile = TutorialModule.Database.GetOrCreateProfile(player.Account.Name);
            tutProfile.TutorialVisits++;
            TutorialModule.Database.SaveProfile(tutProfile);

            TutorialModule.TeleportToTutorialSpawn(player, showGuide: false);

            var elemProfile = AetherialModule.Database.GetOrCreateProfile(player.Account.Name);
            if (elemProfile != null && !string.IsNullOrEmpty(elemProfile.AssignedElement))
            {
                player.SendSuccessMessage("[ TUTORIAL ] Kamu mengunjungi kembali Area Tutorial!");
                player.SendInfoMessage($"[ INFO ] Elemen jiwamu sudah terbangun ([c/facc15:{elemProfile.AssignedElement}]), sehingga Altar Pembangkitan Elemen tidak dapat dipakai lagi.");
            }
            else
            {
                player.SendSuccessMessage("[ TUTORIAL ] Kamu memasuki Area Tutorial! Ikuti jalur hingga ujung dan masuklah ke Altar Pembangkitan Elemen.");
            }
        }

        private static void HandleSetRegion(TSPlayer player, CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                player.SendErrorMessage("Format: /tutorial setregion <nama_region>");
                return;
            }

            string regionName = string.Join(" ", args.Parameters.Skip(1)).Trim();
            TutorialModule.Config.AwakenRegionName = regionName;
            TutorialModule.SaveConfig();

            bool exists = TShock.Regions?.Regions?.Any(r =>
                r != null && r.Name.Equals(regionName, StringComparison.OrdinalIgnoreCase)) ?? false;

            if (exists)
            {
                player.SendSuccessMessage($"[ OK ] Region Altar Pembangkitan Elemen ditautkan ke '{regionName}'!");
            }
            else
            {
                player.SendWarningMessage($"[ OK ] Nama region diatur ke '{regionName}', namun region tersebut belum dibuat. Gunakan /region set 1, /region set 2, lalu /region define {regionName}.");
            }
        }

        private static void HandleSetAltar(TSPlayer player, CommandArgs args)
        {
            int radius = TutorialModule.Config.AltarRadiusTiles;
            if (args.Parameters.Count >= 2 && int.TryParse(args.Parameters[1], out int parsed))
            {
                radius = Math.Clamp(parsed, 2, 25);
            }

            TutorialModule.Config.AltarCenterX = player.TileX;
            TutorialModule.Config.AltarCenterY = player.TileY;
            TutorialModule.Config.AltarRadiusTiles = radius;
            TutorialModule.SaveConfig();

            player.SendSuccessMessage($"[ OK ] Pusat Altar Pembangkitan diset di ({player.TileX}, {player.TileY}) dengan radius {radius} tile!");
        }

        private static void HandleResetPlayer(TSPlayer player, CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                player.SendErrorMessage("Format: /tutorial reset <nama_akun_player>");
                return;
            }

            string targetName = args.Parameters[1];
            var account = TShock.UserAccounts.GetUserAccountByName(targetName);
            string key = account != null ? account.Name : targetName;

            var prof = TutorialModule.Database.GetOrCreateProfile(key);
            prof.HasCompletedTutorial = false;
            prof.AwakenedElementSnapshot = string.Empty;
            prof.CompletedTutorialUtc = DateTime.MinValue;
            TutorialModule.Database.SaveProfile(prof);

            player.SendSuccessMessage($"[ OK ] Status tutorial '{key}' di-reset. (Gunakan '/resetelement {key}' juga jika ingin mengizinkannya gacha elemen ulang).");
        }

        private static void HandleTestStorm(TSPlayer player, CommandArgs args)
        {
            if (!TutorialModule.IsTutorialActiveInCurrentWorld())
            {
                player.SendErrorMessage("[ X ] Test storm hanya bisa dijalankan di world tutorial yang aktif.");
                return;
            }

            string tier = args.Parameters.Count >= 2 ? args.Parameters[1].ToLowerInvariant() : "common";
            string sampleElem = tier switch
            {
                "omnia" => "OMNIA",
                "epic" or "radiance" => "RADIANCE",
                "uncommon" or "rare" or "glacial" or "volt" => "VOLT",
                _ => "IGNIS"
            };

            var preset = TutorialModule.GetTimeDistortionPreset(sampleElem);
            TutorialModule.StartOrUpgradeWorldTimeStorm(player.Name, preset, DateTime.UtcNow);
            player.SendSuccessMessage($"[ OK ] Simulasi badai waktu '{preset.RarityLabel}' ({preset.ElementId}) dijalankan! Gunakan '/tutorial stopstorm' untuk menghentikan lebih awal.");
        }

        private static void ShowTutorialInfo(TSPlayer player)
        {
            var cfg = TutorialModule.Config;
            player.SendMessage("=== [ STATUS TUTORIAL & ALTAR TAKDIR ] ===", Color.Gold);
            player.SendMessage($"[ + ] World Aktif     : '{MultiWorldModule.CurrentWorldId}' -> {(TutorialModule.IsTutorialActiveInCurrentWorld() ? "AKTIF" : "STANDBY")}", Color.White);
            player.SendMessage($"[ + ] Spawn Tutorial  : {(cfg.HasTutorialSpawnConfigured() ? $"({cfg.TutorialSpawnX}, {cfg.TutorialSpawnY})" : "Belum diatur (/tutorial setspawn)")}", Color.White);
            player.SendMessage($"[ + ] Region Altar    : '{cfg.AwakenRegionName}'" + (cfg.HasDirectAltarConfigured() ? $" | Altar Direct: ({cfg.AltarCenterX},{cfg.AltarCenterY}) r={cfg.AltarRadiusTiles}" : ""), Color.White);
            player.SendMessage($"[ + ] Distorsi Waktu  : {TutorialModule.GetTimeStormSummary()}", new Color(125, 211, 252));

            if (player.RealPlayer && player.IsLoggedIn && player.Account != null)
            {
                var tutProf = TutorialModule.Database.GetOrCreateProfile(player.Account.Name);
                var elemProf = AetherialModule.Database.GetOrCreateProfile(player.Account.Name);
                string elemText = string.IsNullOrEmpty(elemProf?.AssignedElement) ? "Belum Terbangun" : elemProf.AssignedElement;
                player.SendMessage($"[ + ] Status Kamu     : Tutorial={(tutProf.HasCompletedTutorial ? "SELESAI" : "BELUM")} | Elemen={elemText}", new Color(167, 243, 208));
            }
        }

        private static void ShowHelp(TSPlayer player)
        {
            player.SendMessage("=== [ PANDUAN TUTORIAL UNIVERS ] ===", Color.Gold);
            player.SendInfoMessage("/tutorial | /tut - Teleport ke Area Tutorial (Altar tidak bisa dipakai ulang jika sudah punya elemen)");
            player.SendInfoMessage("/tutorial info   - Lihat status Tutorial, Region Altar & Distorsi Waktu");
            if (player.HasPermission("univers.tutorial.admin"))
            {
                player.SendMessage("[ ADMIN ] /tutorial setspawn           - Set titik spawn Tutorial di posisimu", Color.Yellow);
                player.SendMessage("[ ADMIN ] /tutorial setregion <nama>   - Tautkan region TShock untuk Altar Awaken", Color.Yellow);
                player.SendMessage("[ ADMIN ] /tutorial setaltar [radius]  - Set koordinat Altar langsung tanpa region", Color.Yellow);
                player.SendMessage("[ ADMIN ] /tutorial reset <player>     - Reset status selesai tutorial pemain", Color.Yellow);
                player.SendMessage("[ ADMIN ] /tutorial teststorm <common|uncommon|epic|omnia> | /tutorial stopstorm", Color.Yellow);
            }
        }

        private static bool RequireAdmin(TSPlayer player)
        {
            if (player.HasPermission("univers.tutorial.admin")) return true;
            player.SendErrorMessage("[ X ] Butuh permission 'univers.tutorial.admin' untuk perintah ini.");
            return false;
        }

        private static bool RequireRealPlayer(TSPlayer player)
        {
            if (player.RealPlayer) return true;
            player.SendErrorMessage("[ X ] Perintah ini harus dijalankan oleh karakter di dalam game.");
            return false;
        }
    }
}
