/*
 * =====================================================================
 *  DiscordCommands.cs - Perintah In-Game Verifikasi Akun Discord
 *  Namespace: Univers.Discord (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using TShockAPI;
using Univers.MultiWorld;

namespace Univers.Discord
{
    public static class DiscordCommands
    {
        private static readonly System.Collections.Generic.List<Command> _registered = new();

        public static void Register()
        {
            Unregister();
            var c1 = new Command("", DiscordMainCommand, "discord", "dc");
            var c2 = new Command("", VerifyShortcutCommand, "verify", "link");
            Commands.ChatCommands.Add(c1);
            Commands.ChatCommands.Add(c2);
            _registered.Add(c1);
            _registered.Add(c2);
        }

        public static void Unregister()
        {
            foreach (var cmd in _registered)
            {
                Commands.ChatCommands.Remove(cmd);
            }
            _registered.Clear();
        }

        private static void VerifyShortcutCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null || !player.RealPlayer || !player.IsLoggedIn || player.Account == null)
            {
                args.Player?.SendErrorMessage("[ DISCORD ] Kamu harus login ke akun TShock terlebih dahulu untuk verifikasi Discord.");
                return;
            }

            RunPlayerRpc(player, "verify", args.Parameters.Count > 0 ? args.Parameters[0] : string.Empty);
        }

        private static void DiscordMainCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null) return;

            if (args.Parameters.Count == 0)
            {
                if (player.RealPlayer && player.IsLoggedIn && player.Account != null)
                {
                    RunPlayerRpc(player, "status", string.Empty);
                }
                else
                {
                    ShowHelp(player);
                }
                return;
            }

            string sub = args.Parameters[0].ToLowerInvariant();
            switch (sub)
            {
                case "help":
                case "?":
                    ShowHelp(player);
                    return;

                case "verify":
                case "verif":
                case "code":
                case "link":
                case "confirm":
                case "deny":
                case "unlink":
                case "status":
                    if (!player.RealPlayer || !player.IsLoggedIn || player.Account == null)
                    {
                        player.SendErrorMessage("[ DISCORD ] Perintah ini hanya bisa dipakai oleh pemain yang sudah login.");
                        return;
                    }
                    string arg = args.Parameters.Count >= 2 ? args.Parameters[1] : string.Empty;
                    RunPlayerRpc(player, sub, arg);
                    return;

                case "reload":
                    if (!RequireAdmin(player)) return;
                    if (MultiWorldModule.IsChildProcess)
                    {
                        player.SendInfoMessage("[ INFO ] Jalankan '/discord reload' dari world Master Hub (lobby) atau gunakan /mwexec lobby discord reload.");
                        return;
                    }
                    DiscordModule.Reload();
                    player.SendSuccessMessage("[ OK ] Konfigurasi DiscordConfig.json dimuat ulang & Gateway disegarkan.");
                    return;

                case "emojis":
                case "emoji":
                case "roles":
                    if (!RequireAdmin(player)) return;
                    if (MultiWorldModule.IsChildProcess)
                    {
                        player.SendInfoMessage("[ INFO ] Jalankan '/discord emojis' dari world Master Hub (lobby) atau gunakan /mwexec lobby discord emojis.");
                        return;
                    }
                    if (args.Parameters.Count >= 2 && args.Parameters[1].Equals("refresh", StringComparison.OrdinalIgnoreCase))
                    {
                        DiscordModule.RefreshGuildMetadataAsync();
                        player.SendInfoMessage("[ DISCORD ] Memuat ulang custom emoji & warna role dari server Discord...");
                        return;
                    }
                    player.SendInfoMessage(DiscordModule.GetCachedEmojiAndRoleSummary());
                    return;

                case "whois":
                case "check":
                    if (!RequireAdmin(player)) return;
                    if (args.Parameters.Count < 2)
                    {
                        player.SendErrorMessage("Format: /discord check <nama_akun_tshock|discord_id>");
                        return;
                    }
                    HandleAdminCheck(player, args.Parameters[1]);
                    return;

                case "forceunlink":
                    if (!RequireAdmin(player)) return;
                    if (args.Parameters.Count < 2)
                    {
                        player.SendErrorMessage("Format: /discord forceunlink <nama_akun_tshock>");
                        return;
                    }
                    HandleAdminForceUnlink(player, args.Parameters[1]);
                    return;

                default:
                    ShowHelp(player);
                    return;
            }
        }

        private static void RunPlayerRpc(TSPlayer player, string subAction, string arg)
        {
            string accName = player.Account.Name;
            string pName = player.Name;

            _ = Task.Run(async () =>
            {
                try
                {
                    var (ok, msg) = await DiscordModule.ExecutePlayerVerifyCommandAsync(accName, pName, subAction, arg);
                    DiscordModule.EnqueueMainThread(() =>
                    {
                        if (player != null && player.Active)
                        {
                            if (ok) player.SendSuccessMessage(msg);
                            else player.SendWarningMessage(msg);
                        }
                    });
                }
                catch (Exception ex)
                {
                    DiscordModule.EnqueueMainThread(() =>
                    {
                        if (player != null && player.Active)
                        {
                            player.SendErrorMessage($"[ DISCORD ] Gagal memproses permintaan: {ex.Message}");
                        }
                    });
                }
            });
        }

        private static void HandleAdminCheck(TSPlayer admin, string query)
        {
            if (MultiWorldModule.IsChildProcess || DiscordModule.Database == null)
            {
                admin.SendInfoMessage("[ INFO ] Gunakan perintah admin '/discord check' di Master Hub (lobby).");
                return;
            }

            var rec = DiscordModule.Database.GetByAccount(query) ?? DiscordModule.Database.GetByDiscordId(query);
            if (rec == null)
            {
                admin.SendInfoMessage($"[ DISCORD ] Tidak ditemukan tautan untuk '{query}'.");
                return;
            }

            admin.SendSuccessMessage($"[ DISCORD ] TShock: '{rec.AccountName}' <-> Discord: @{rec.DiscordUsername} (ID: {rec.DiscordUserId}) sejak {rec.LinkedAtUtc:yyyy-MM-dd HH:mm} UTC.");
        }

        private static void HandleAdminForceUnlink(TSPlayer admin, string accountQuery)
        {
            if (MultiWorldModule.IsChildProcess || DiscordModule.Database == null)
            {
                admin.SendInfoMessage("[ INFO ] Gunakan perintah admin '/discord forceunlink' di Master Hub (lobby).");
                return;
            }

            if (DiscordModule.Database.UnlinkByAccount(accountQuery))
            {
                admin.SendSuccessMessage($"[ OK ] Tautan Discord milik akun '{accountQuery}' telah dihapus.");
            }
            else
            {
                admin.SendErrorMessage($"[ X ] Akun '{accountQuery}' tidak memiliki tautan Discord.");
            }
        }

        private static void ShowHelp(TSPlayer player)
        {
            player.SendMessage("=== [ UNIVERS DISCORD AUTHENTICATOR & RELAY ] ===", Color.Gold);
            player.SendInfoMessage("/verify | /discord verify     - Buat kode verifikasi 6 karakter untuk Discord");
            player.SendInfoMessage("/discord confirm <KODE>       - Setujui permintaan !link dari Discord");
            player.SendInfoMessage("/discord status               - Cek status tautan akun & koneksi bot");
            player.SendInfoMessage("/discord unlink               - Putuskan tautan akun Discord kamu");
            if (player.HasPermission("univers.discord.admin"))
            {
                player.SendMessage("[ ADMIN ] /discord check <akun|id> | /discord forceunlink <akun> | /discord emojis [refresh] | /discord reload", Color.Yellow);
            }
        }

        private static bool RequireAdmin(TSPlayer player)
        {
            if (player.HasPermission("univers.discord.admin")) return true;
            player.SendErrorMessage("[ X ] Butuh permission 'univers.discord.admin' untuk perintah ini.");
            return false;
        }
    }
}
