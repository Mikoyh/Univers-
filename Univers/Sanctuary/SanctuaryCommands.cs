/*
 * =====================================================================
 *  SanctuaryCommands.cs - Perintah Lore, Status & Admin Segel Lobby
 *  Namespace: Univers.Sanctuary (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using Microsoft.Xna.Framework;
using TShockAPI;
using Univers.Bossing;
using Univers.MultiWorld;

namespace Univers.Sanctuary
{
    public static class SanctuaryCommands
    {
        public static void Register()
        {
            Commands.ChatCommands.Add(new Command("", SanctuaryMainCommand, "sanctuary", "ward", "pelindung", "shield"));
        }

        private static void SanctuaryMainCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null) return;

            var cfg = SanctuaryModule.Config;

            if (args.Parameters.Count > 0)
            {
                string sub = args.Parameters[0].ToLowerInvariant();
                switch (sub)
                {
                    case "pulse":
                    case "aura":
                    case "visual":
                        if (args.Parameters.Count == 1 || sub == "pulse" || sub == "aura")
                        {
                            if (!player.RealPlayer || !player.IsLoggedIn)
                            {
                                player.SendErrorMessage("[ SANCTUARY ] Perintah pulse visual hanya untuk pemain di dalam game.");
                                return;
                            }

                            if (!SanctuaryModule.IsSanctuaryActiveInCurrentWorld())
                            {
                                player.SendInfoMessage($"[ SANCTUARY ] Segel Pelindung tidak aktif di world '{MultiWorldModule.CurrentWorldId}'.");
                                return;
                            }

                            if (!SanctuaryModule.TryTriggerManualPulse(player, out int waitSec))
                            {
                                player.SendInfoMessage($"[ SANCTUARY ] Energi segel sedang memulihkan diri. Tunggu {waitSec} detik lagi.");
                                return;
                            }

                            player.SendSuccessMessage($"[ SANCTUARY ] Cahaya {cfg.BarrierName} beresonansi di sekitarmu!");
                            return;
                        }
                        break;

                    case "toggle":
                        if (!RequireAdmin(player)) return;
                        cfg.Enabled = !cfg.Enabled;
                        SanctuaryModule.SaveConfig();
                        player.SendSuccessMessage($"[ OK ] Proteksi Sanctuary sekarang: {(cfg.Enabled ? "AKTIF" : "NONAKTIF")}.");
                        return;

                    case "togglevisual":
                    case "visuals":
                        if (!RequireAdmin(player)) return;
                        cfg.ShowWardVisuals = !cfg.ShowWardVisuals;
                        SanctuaryModule.SaveConfig();
                        player.SendSuccessMessage($"[ OK ] Visual Sihir Pelindung sekarang: {(cfg.ShowWardVisuals ? "AKTIF" : "NONAKTIF")}.");
                        return;

                    case "sweep":
                    case "clean":
                        if (!RequireAdmin(player)) return;
                        int cleared = SanctuaryModule.ForceSanctuarySweep(out bool clearedEvent);
                        player.SendSuccessMessage($"[ OK ] Sapuan segel selesai: {cleared} entitas berbahaya diusir, event dibersihkan: {(clearedEvent ? "YA" : "TIDAK")}.");
                        return;

                    case "reload":
                        if (!RequireAdmin(player)) return;
                        SanctuaryModule.Reload();
                        player.SendSuccessMessage("[ OK ] Konfigurasi SanctuaryConfig.json berhasil dimuat ulang.");
                        return;
                }
            }

            bool isProtected = SanctuaryModule.IsSanctuaryActiveInCurrentWorld();
            bool isCenterSanctuary = BossingModule.IsBossingActiveInCurrentWorld();
            string statusText = isProtected
                ? "[c/4ade80:AKTIF MELINDUNGI PENUH]"
                : (isCenterSanctuary ? $"[c/38bdf8:ZONA TENGAH AKTIF (+/-{BossingModule.Config.CenterSanctuaryRadiusTiles} tile Spawn)]" : "[c/facc15:STANDBY / TIDAK AKTIF DI WORLD INI]");

            player.SendMessage($"=== [ {cfg.BarrierName.ToUpperInvariant()} ] ===", Color.Gold);
            player.SendMessage($"[ LORE ] \"{cfg.LoreSummary}\"", new Color(186, 230, 253));
            player.SendMessage($"[ + ] Penjaga Legenda : {cfg.GuardianName}", Color.White);
            player.SendMessage($"[ + ] World Ini       : '{MultiWorldModule.CurrentWorldId}' -> {statusText}", Color.White);
            if (isCenterSanctuary)
            {
                player.SendMessage($"[ + ] Arena Bossing   : Maks 1 Boss di KIRI & 1 Boss di KANAN Spawn (cek /bossing).", new Color(251, 191, 36));
            }
            player.SendMessage($"[ + ] Proteksi        : Boss={(cfg.BlockBosses ? "DITOLAK" : "OFF")} | Monster={(cfg.BlockHostileMobs ? "DITOLAK" : "OFF")} | Event={(cfg.BlockEventsAndInvasions ? "DITOLAK" : "OFF")}", Color.White);
            player.SendMessage($"[ + ] Statistik Segel : {SanctuaryModule.TotalBlockedAttempts} ancaman telah ditolak sejak server menyala.", new Color(167, 243, 208));

            if (isProtected && player.RealPlayer)
            {
                player.SendInfoMessage("[ INFO ] Ketik '/ward pulse' untuk membangkitkan resonansi cahaya segel di sekitarmu.");
            }

            if (player.HasPermission("univers.sanctuary.admin"))
            {
                player.SendMessage("[ ADMIN ] /sanctuary toggle | /sanctuary visuals | /sanctuary sweep | /sanctuary reload", Color.Yellow);
            }
        }

        private static bool RequireAdmin(TSPlayer player)
        {
            if (player.HasPermission("univers.sanctuary.admin")) return true;
            player.SendErrorMessage("[ X ] Kamu butuh permission 'univers.sanctuary.admin' untuk perintah ini.");
            return false;
        }
    }
}
