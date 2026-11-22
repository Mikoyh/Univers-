/*
 * =====================================================================
 *  SurvivalCommands.cs - Perintah Status, HUD Toggle & Admin Don't Starve RPG
 *  Namespace: Univers.Survival (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using TShockAPI;
using Univers.MultiWorld;

namespace Univers.Survival
{
    public static class SurvivalCommands
    {
        public static void Register()
        {
            Commands.ChatCommands.Add(new Command("univers.survival.use", SurvivalMainCommand, "status", "survstat", "hunger", "sanity", "suhu"));
            Commands.ChatCommands.Add(new Command("univers.survival.use", ToggleHudShortcutCommand, "shud", "survivalhud"));
        }

        private static void ToggleHudShortcutCommand(CommandArgs args)
        {
            TogglePlayerHud(args.Player);
        }

        private static void SurvivalMainCommand(CommandArgs args)
        {
            var player = args.Player;
            if (!player.RealPlayer || !player.IsLoggedIn)
            {
                player.SendErrorMessage("[ SURVIVAL ] Perintah ini hanya bisa dipakai oleh pemain di dalam game.");
                return;
            }

            if (args.Parameters.Count > 0)
            {
                string sub = args.Parameters[0].ToLowerInvariant();
                switch (sub)
                {
                    case "hud":
                        TogglePlayerHud(player);
                        return;

                    case "help":
                    case "?":
                        SendHelp(player);
                        return;

                    case "reset":
                        if (!RequireAdmin(player)) return;
                        HandleAdminReset(args);
                        return;

                    case "set":
                        if (!RequireAdmin(player)) return;
                        HandleAdminSet(args);
                        return;
                }
            }

            if (!SurvivalModule.IsSurvivalActiveInCurrentWorld())
            {
                player.SendInfoMessage($"[ SURVIVAL ] Sistem Don't Starve sedang PAUSE di world '{MultiWorldModule.CurrentWorldId}'.");
                player.SendInfoMessage($"[ SURVIVAL ] Aktif otomatis saat kamu masuk ke world: {string.Join(", ", SurvivalModule.Config.ActiveWorldIds)}. Ketik /survival.");
                return;
            }

            var st = SurvivalModule.GetOrLoadState(player);
            if (st == null)
            {
                player.SendErrorMessage("[ SURVIVAL ] Data survival belum siap.");
                return;
            }

            var p = st.Profile;
            var cfg = SurvivalModule.Config;

            string hBar = SurvivalModule.BuildAsciiBar(p.Hunger, cfg.MaxHunger, 12);
            string sBar = SurvivalModule.BuildAsciiBar(p.Sanity, cfg.MaxSanity, 12);
            string wBar = SurvivalModule.BuildAsciiBar(p.Wetness, 100.0, 12);

            string hTag = p.Hunger >= 60 ? "4ade80" : (p.Hunger >= 30 ? "facc15" : "ef4444");
            string sTag = p.Sanity >= 60 ? "38bdf8" : (p.Sanity >= 30 ? "facc15" : "a855f7");
            string tTag = p.Temperature < cfg.ColdThreshold ? "38bdf8" : (p.Temperature > cfg.HotThreshold ? "f97316" : "4ade80");
            string tStatus = p.Temperature <= cfg.FreezingThreshold ? "BEKU"
                : (p.Temperature < cfg.ColdThreshold ? "Dingin"
                : (p.Temperature >= cfg.HeatstrokeThreshold ? "HEATSTROKE"
                : (p.Temperature > cfg.HotThreshold ? "Panas" : "Nyaman")));

            player.SendMessage("=== [ UNIVERS DON'T STARVE SURVIVAL ] ===", Color.Gold);
            player.SendMessage($"[ + ] Hunger  : [c/{hTag}:[{hBar}] {p.Hunger:0.0}/{cfg.MaxHunger:0}] (Makan item makanan Terraria untuk isi ulang)", Color.White);
            player.SendMessage($"[ + ] Sanity  : [c/{sTag}:[{sBar}] {p.Sanity:0.0}/{cfg.MaxSanity:0}] (Campfire, Kasur, Cahaya Terang)", Color.White);
            player.SendMessage($"[ + ] Suhu    : [c/{tTag}:{p.Temperature:0.0}C ({tStatus})] | Target Lingkungan: {st.TargetAmbientTemp:0.0}C", Color.White);
            player.SendMessage($"[ + ] Basah   : [c/60a5fa:[{wBar}] {p.Wetness:0}%] | Kondisi: {st.EnvironmentSummary}", Color.White);
            player.SendInfoMessage($"[ INFO ] HUD Layar: {(p.ShowStatusHud ? "ON" : "OFF")} (/shud) | Makanan Dimakan: {p.FoodsEaten} | Ketik /status help");
        }

        private static void TogglePlayerHud(TSPlayer player)
        {
            if (!player.RealPlayer || !player.IsLoggedIn)
            {
                player.SendErrorMessage("[ SURVIVAL ] Harus login sebagai player.");
                return;
            }

            var st = SurvivalModule.GetOrLoadState(player);
            if (st == null) return;

            st.Profile.ShowStatusHud = !st.Profile.ShowStatusHud;
            st.Dirty = true;
            SurvivalModule.Database.SaveProfile(st.Profile);

            if (st.Profile.ShowStatusHud)
            {
                player.SendSuccessMessage("[ OK ] Survival HUD Layar diaktifkan! Ketik /shud lagi untuk mematikan.");
                if (SurvivalModule.IsSurvivalActiveInCurrentWorld())
                {
                    SurvivalModule.SendStatusHudPacket(player, st);
                }
            }
            else
            {
                try
                {
                    NetMessage.SendData((int)PacketTypes.Status, player.Index, -1, NetworkText.FromLiteral(""), 0, 0f);
                }
                catch { }
                player.SendInfoMessage("[ OK ] Survival HUD Layar dimatikan.");
            }
        }

        private static void SendHelp(TSPlayer player)
        {
            player.SendMessage("=== [ PANDUAN SURVIVAL DON'T STARVE ] ===", Color.Gold);
            player.SendInfoMessage("/status | /hunger | /sanity | /suhu - Lihat bar Hunger, Sanity, Suhu & Kondisi Sekitar");
            player.SendInfoMessage("/shud | /status hud - Nyalakan/matikan HUD teks permanen di layar");
            player.SendInfoMessage("[ TIPS ] Hunger : Makan makanan Terraria (Fish, Soup, Pie, Fruit, Mushroom, dll)");
            player.SendInfoMessage("[ TIPS ] Sanity : Jangan berada di gelap total! Bawa Torch/Mining Helmet atau dekati Campfire");
            player.SendInfoMessage("[ TIPS ] Suhu   : Hangatkan diri di Campfire saat di Snow, minum air/jus di Desert/Underworld");
            if (player.HasPermission("univers.survival.admin"))
            {
                player.SendMessage("[ ADMIN ] /status reset <player> | /status set <player> <hunger|sanity|temp|wet> <nilai>", Color.Yellow);
            }
        }

        private static bool RequireAdmin(TSPlayer player)
        {
            if (player.HasPermission("univers.survival.admin")) return true;
            player.SendErrorMessage("[ X ] Butuh permission 'univers.survival.admin' untuk perintah ini.");
            return false;
        }

        private static void HandleAdminReset(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Format: /status reset <nama_player>");
                return;
            }

            var target = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.IsLoggedIn &&
                                                            p.Name.Equals(args.Parameters[1], StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                args.Player.SendErrorMessage($"[ X ] Player '{args.Parameters[1]}' tidak sedang online.");
                return;
            }

            var st = SurvivalModule.GetOrLoadState(target);
            if (st == null) return;

            st.Profile.Hunger = SurvivalModule.Config.MaxHunger;
            st.Profile.Sanity = SurvivalModule.Config.MaxSanity;
            st.Profile.Temperature = SurvivalModule.Config.IdealTemperature;
            st.Profile.Wetness = 0.0;
            st.Dirty = true;
            SurvivalModule.Database.SaveProfile(st.Profile);

            args.Player.SendSuccessMessage($"[ OK ] Status Survival '{target.Name}' telah di-reset penuh.");
            target.SendSuccessMessage("[ SURVIVAL ] Status Hunger, Sanity & Suhu kamu di-reset penuh oleh Admin.");
        }

        private static void HandleAdminSet(CommandArgs args)
        {
            if (args.Parameters.Count < 4 || !double.TryParse(args.Parameters[3], out double val))
            {
                args.Player.SendErrorMessage("Format: /status set <nama_player> <hunger|sanity|temp|wet> <nilai>");
                return;
            }

            var target = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.IsLoggedIn &&
                                                            p.Name.Equals(args.Parameters[1], StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                args.Player.SendErrorMessage($"[ X ] Player '{args.Parameters[1]}' tidak sedang online.");
                return;
            }

            var st = SurvivalModule.GetOrLoadState(target);
            if (st == null) return;

            var cfg = SurvivalModule.Config;
            string stat = args.Parameters[2].ToLowerInvariant();
            switch (stat)
            {
                case "hunger":
                    st.Profile.Hunger = Math.Clamp(val, 0.0, cfg.MaxHunger);
                    break;
                case "sanity":
                    st.Profile.Sanity = Math.Clamp(val, 0.0, cfg.MaxSanity);
                    break;
                case "temp":
                case "suhu":
                    st.Profile.Temperature = Math.Clamp(val, cfg.MinTemperature, cfg.MaxTemperature);
                    break;
                case "wet":
                case "basah":
                    st.Profile.Wetness = Math.Clamp(val, 0.0, 100.0);
                    break;
                default:
                    args.Player.SendErrorMessage("Stat tidak dikenal. Pilih: hunger, sanity, temp, wet");
                    return;
            }

            st.Dirty = true;
            SurvivalModule.Database.SaveProfile(st.Profile);
            args.Player.SendSuccessMessage($"[ OK ] {stat} milik '{target.Name}' diatur ke {val:0.0}.");
        }
    }
}
