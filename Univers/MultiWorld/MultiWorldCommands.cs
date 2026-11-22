/*
 * =====================================================================
 *  MultiWorldCommands.cs - Perintah Player & Admin Modular MultiWorld
 *  Namespace: Univers.MultiWorld (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using TShockAPI;

namespace Univers.MultiWorld
{
    public static class MultiWorldCommands
    {
        public static void Register()
        {
            Commands.ChatCommands.Add(new Command("univers.mw.use", MwCommand, "mw", "multiworld"));
            Commands.ChatCommands.Add(new Command("univers.mw.use", WorldShortcutCommand, "world", "worlds"));
            Commands.ChatCommands.Add(new Command("", LobbyShortcutCommand, "lobby", "hub"));
            Commands.ChatCommands.Add(new Command("univers.mw.use", GlobalOnlineCommand, "players", "glist"));
            Commands.ChatCommands.Add(new Command("univers.mw.use", SurvivalShortcutCommand, "survival", "surv"));
            Commands.ChatCommands.Add(new Command("univers.mw.use", RandomTeleportCommand, "rtp", "randomtp"));

        }

        private static void GlobalOnlineCommand(CommandArgs args)
        {
            _ = ShowGlobalOnlineAsync(args.Player);
        }

        private static void WorldShortcutCommand(CommandArgs args)
        {
            var player = args.Player;
            if (args.Parameters.Count == 0)
            {
                _ = ShowWorldListAsync(player);
                return;
            }

            string target = args.Parameters[0];
            if (target.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                _ = ShowWorldListAsync(player);
                return;
            }
            if (target.Equals("online", StringComparison.OrdinalIgnoreCase))
            {
                _ = ShowGlobalOnlineAsync(player);
                return;
            }

            _ = ExecuteTeleportAsync(player, target);
        }

        private static void LobbyShortcutCommand(CommandArgs args)
        {
            _ = ExecuteTeleportAsync(args.Player, MultiWorldModule.Config.HubWorldId);
        }

        private static void MwCommand(CommandArgs args)
        {
            var player = args.Player;
            if (args.Parameters.Count == 0)
            {
                _ = ShowWorldListAsync(player);
                return;
            }

            string sub = args.Parameters[0].ToLowerInvariant();
            switch (sub)
            {
                case "list":
                case "status":
                    _ = ShowWorldListAsync(player);
                    break;

                case "online":
                case "players":
                    _ = ShowGlobalOnlineAsync(player);
                    break;

                case "tp":
                case "go":
                case "join":
                    if (args.Parameters.Count < 2)
                    {
                        player.SendErrorMessage("Format: /mw tp <nama_world>");
                        return;
                    }
                    _ = ExecuteTeleportAsync(player, args.Parameters[1]);
                    break;

                case "info":
                    if (args.Parameters.Count < 2)
                    {
                        ShowCurrentWorldInfo(player);
                        return;
                    }
                    _ = ShowTargetWorldInfoAsync(player, args.Parameters[1]);
                    break;

                case "create":
                case "add":
                    if (!RequireAdmin(player)) return;
                    if (args.Parameters.Count < 2)
                    {
                        player.SendErrorMessage("Format: /mw create <id> [small|medium|large] [classic|expert|master|journey] [random|corrupt|crimson] [seed]");
                        return;
                    }
                    player.SendInfoMessage($"[MultiWorld] Memproses pembuatan world '{args.Parameters[1]}'...");
                    _ = RunAdminAsync(player, "create", args.Parameters.Skip(1).ToList());
                    break;

                case "delete":
                case "remove":
                    if (!RequireAdmin(player)) return;
                    if (args.Parameters.Count < 3 || !args.Parameters[2].Equals("confirm", StringComparison.OrdinalIgnoreCase))
                    {
                        string wid = args.Parameters.Count >= 2 ? args.Parameters[1] : "<id>";
                        player.SendErrorMessage($"[PERINGATAN] File .wld akan dihapus permanen! Ketik: /mw delete {wid} confirm");
                        return;
                    }
                    player.SendInfoMessage($"[MultiWorld] Menghapus world '{args.Parameters[1]}'...");
                    _ = RunAdminAsync(player, "delete", new List<string> { args.Parameters[1] });
                    break;

                case "start":
                case "wake":
                    if (!RequireAdmin(player)) return;
                    if (args.Parameters.Count < 2)
                    {
                        player.SendErrorMessage("Format: /mw start <id>");
                        return;
                    }
                    player.SendInfoMessage($"[MultiWorld] Menyalakan world '{args.Parameters[1]}'...");
                    _ = RunAdminAsync(player, "start", new List<string> { args.Parameters[1] });
                    break;

                case "stop":
                case "sleep":
                    if (!RequireAdmin(player)) return;
                    if (args.Parameters.Count < 2)
                    {
                        player.SendErrorMessage("Format: /mw sleep <id>");
                        return;
                    }
                    player.SendInfoMessage($"[MultiWorld] Menyimpan & mematikan world '{args.Parameters[1]}'...");
                    _ = RunAdminAsync(player, "sleep", new List<string> { args.Parameters[1] });
                    break;

                case "autosleep":
                    if (!RequireAdmin(player)) return;
                    if (args.Parameters.Count < 3)
                    {
                        player.SendErrorMessage("Format: /mw autosleep <id> <on|off|menit>");
                        return;
                    }
                    _ = RunAdminAsync(player, "autosleep", args.Parameters.Skip(1).ToList());
                    break;

                case "perm":
                case "setperm":
                    if (!RequireAdmin(player)) return;
                    if (args.Parameters.Count < 3)
                    {
                        player.SendErrorMessage("Format: /mw perm <id> <permission|none>");
                        return;
                    }
                    _ = RunAdminAsync(player, "perm", args.Parameters.Skip(1).ToList());
                    break;

                case "resetssc":
                    if (!RequireAdmin(player)) return;
                    {
                        TSPlayer? targetPlayer = args.Parameters.Count >= 2
                            ? TShock.Players.FirstOrDefault(p => p != null && p.Active && p.Name.Equals(args.Parameters[1], StringComparison.OrdinalIgnoreCase))
                            : (player.RealPlayer ? player : null);
                        if (targetPlayer == null)
                        {
                            player.SendErrorMessage("Player tidak ditemukan atau sedang tidak online di world ini. Format: /mw resetssc [nama_player]");
                            return;
                        }
                        if (MultiWorldModule.ResetOnlinePlayerSsc(targetPlayer, out string resetMsg))
                        {
                            player.SendSuccessMessage($"[MultiWorld] {resetMsg}");
                            if (!ReferenceEquals(player, targetPlayer))
                            {
                                targetPlayer.SendInfoMessage("[MultiWorld] Data karakter SSC kamu telah di-reset oleh Admin ke karakter awal.");
                            }
                        }
                        else
                        {
                            player.SendErrorMessage($"[MultiWorld] {resetMsg}");
                        }
                    }
                    break;

                case "help":
                    SendHelp(player);
                    break;

                default:
                    if (MultiWorldModule.Config.GetWorld(args.Parameters[0]) != null)
                    {
                        _ = ExecuteTeleportAsync(player, args.Parameters[0]);
                    }
                    else
                    {
                        SendHelp(player);
                    }
                    break;
            }
        }

        private static bool RequireAdmin(TSPlayer player)
        {
            if (player.HasPermission("univers.mw.admin")) return true;
            player.SendErrorMessage("Kamu membutuhkan permission 'univers.mw.admin' untuk perintah ini.");
            return false;
        }

        private static async Task ShowWorldListAsync(TSPlayer player)
        {
            var worlds = await MultiWorldModule.FetchAllWorldSnapshotsAsync();
            if (worlds.Count == 0)
            {
                player.SendErrorMessage("[MultiWorld] Belum ada world terdaftar.");
                return;
            }

            int totalOnline = worlds.Sum(w => w.PlayerCount);
            string proxyHint = MultiWorldModule.IsChildProcess
                ? $"Node: {MultiWorldModule.CurrentWorldId}"
                : (MultiWorldModule.Proxy != null && MultiWorldModule.Proxy.IsListening
                    ? $"Proxy: {MultiWorldModule.Proxy.ListenPort}"
                    : "Proxy: OFF");

            player.SendMessage($"=== [ UNIVERS MULTIWORLD ({proxyHint} | Total Online: {totalOnline}) ] ===", Color.Gold);
            foreach (var w in worlds)
            {
                bool here = w.Id.Equals(MultiWorldModule.CurrentWorldId, StringComparison.OrdinalIgnoreCase);
                string tag = w.IsHub ? "[HUB]" : $"[{w.State.ToUpperInvariant()}]";
                string hereMark = here ? " <- KAMU DISINI" : "";
                string names = w.OnlinePlayerNames.Count > 0
                    ? $" ({string.Join(", ", w.OnlinePlayerNames)})"
                    : "";
                string sleepInfo = w.IsHub
                    ? "Always On"
                    : (w.AutoSleep ? $"AutoSleep {w.AutoSleepMinutes}m" : "No AutoSleep");
                Color c = w.State == "Running"
                    ? (here ? Color.Cyan : Color.LightGreen)
                    : (w.State == "Booting" ? Color.Yellow : Color.LightGray);

                player.SendMessage(
                    $" - {w.Id} ({w.DisplayName}) {tag} | {w.PlayerCount} online{names} | {w.Size}/{w.Difficulty} | {sleepInfo}{hereMark}",
                    c);
            }
            player.SendInfoMessage("Pindah: /mw tp <id> | Online semua world: /mw online atau /players | Kembali: /lobby");
        }

        private static async Task ShowGlobalOnlineAsync(TSPlayer player)
        {
            var worlds = await MultiWorldModule.FetchAllWorldSnapshotsAsync();
            int total = worlds.Sum(w => w.PlayerCount);

            player.SendMessage($"=== [ PEMAIN ONLINE LINTAS WORLD (Total: {total}) ] ===", Color.Aquamarine);
            if (total == 0)
            {
                player.SendInfoMessage("Belum ada pemain aktif saat ini.");
                return;
            }

            foreach (var w in worlds.Where(w => w.PlayerCount > 0))
            {
                bool here = w.Id.Equals(MultiWorldModule.CurrentWorldId, StringComparison.OrdinalIgnoreCase);
                string hereTag = here ? " (World Kamu)" : "";
                string list = w.OnlinePlayerNames.Count > 0
                    ? string.Join(", ", w.OnlinePlayerNames)
                    : $"{w.PlayerCount} pemain";
                player.SendMessage($" - [{w.Id.ToUpperInvariant()}] {w.DisplayName}{hereTag}: {list}", here ? Color.Cyan : Color.LightGreen);
            }

            if (MultiWorldModule.Config.CrossWorldChat)
            {
                player.SendInfoMessage("Semua chat normal otomatis terkirim ke seluruh world di server.");
            }
        }

        private static void ShowCurrentWorldInfo(TSPlayer player)
        {
            var cur = MultiWorldModule.Config.GetWorld(MultiWorldModule.CurrentWorldId);
            string role = MultiWorldModule.IsChildProcess ? "Child World" : "Master Hub";
            player.SendInfoMessage($"[MultiWorld] Posisi saat ini: '{MultiWorldModule.CurrentWorldId}' ({cur?.DisplayName ?? "-"}) | Mode: {role}");
            if (!MultiWorldModule.IsChildProcess && MultiWorldModule.Proxy != null)
            {
                player.SendInfoMessage($"[MultiWorld] Gateway Proxy Listen Port: {MultiWorldModule.Proxy.ListenPort} | Master Port: {Terraria.Netplay.ListenPort}");
            }
        }

        private static async Task ShowTargetWorldInfoAsync(TSPlayer player, string worldId)
        {
            var worlds = await MultiWorldModule.FetchAllWorldSnapshotsAsync();
            var w = worlds.FirstOrDefault(x => x.Id.Equals(worldId, StringComparison.OrdinalIgnoreCase));
            if (w == null)
            {
                player.SendErrorMessage($"World '{worldId}' tidak ditemukan.");
                return;
            }

            player.SendMessage($"=== Info World: {w.DisplayName} ({w.Id}) ===", Color.Cyan);
            string onlineStr = w.OnlinePlayerNames.Count > 0 ? $" ({string.Join(", ", w.OnlinePlayerNames)})" : "";
            player.SendInfoMessage($"Status: {w.State} | Player: {w.PlayerCount}{onlineStr} | Port: {w.Port} | File .wld: {(w.WorldFileExists ? "Ada" : "Auto-Create saat masuk")}");
            player.SendInfoMessage($"Preset: Size={w.Size}, Difficulty={w.Difficulty}, Evil={w.Evil}");
            player.SendInfoMessage($"Auto-Sleep: {(w.AutoSleep ? $"Aktif ({w.AutoSleepMinutes} menit kosong, idle saat ini {w.IdleSeconds}d)" : "Nonaktif")}");
            if (!string.IsNullOrWhiteSpace(w.PermissionRequired))
            {
                player.SendInfoMessage($"Permission: {w.PermissionRequired}");
            }
        }

        private static readonly ConcurrentDictionary<string, DateTime> _pendingTeleports = new(StringComparer.OrdinalIgnoreCase);

        public static void ClearPendingTeleport(string playerName)
        {
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                _pendingTeleports.TryRemove(playerName, out _);
            }
        }

        public static void MarkArrivalCooldown(string playerName, int seconds = 10)
        {
            if (string.IsNullOrWhiteSpace(playerName) || seconds <= 0) return;
            DateTime targetUntil = DateTime.UtcNow.AddSeconds(seconds);
            _worldSwitchCooldowns.AddOrUpdate(
                playerName,
                targetUntil,
                (_, existing) => existing > targetUntil ? existing : targetUntil);
        }

        private static async Task ExecuteTeleportAsync(TSPlayer player, string targetWorldId)
        {
            if (!player.RealPlayer)
            {
                player.SendErrorMessage("Perintah teleport hanya bisa dipakai oleh player di dalam game.");
                return;
            }

            if (targetWorldId.Equals(MultiWorldModule.CurrentWorldId, StringComparison.OrdinalIgnoreCase))
            {
                player.SendErrorMessage("Kamu sudah berada di world ini.");
                return;
            }

            var target = MultiWorldModule.Config.GetWorld(targetWorldId);
            if (target == null)
            {
                player.SendErrorMessage($"World '{targetWorldId}' tidak ditemukan. Ketik /mw list untuk melihat daftar world.");
                return;
            }

            bool isAdmin = player.HasPermission("univers.mw.admin");
            int cooldownSec = isAdmin ? 3 : 10;
            if (_worldSwitchCooldowns.TryGetValue(player.Name, out var nextSwitch) &&
                DateTime.UtcNow < nextSwitch)
            {
                int sisa = Math.Max(1, (int)Math.Ceiling((nextSwitch - DateTime.UtcNow).TotalSeconds));
                player.SendErrorMessage($"[MultiWorld] Tunggu {sisa} detik lagi sebelum pindah world.");
                return;
            }

            if (_pendingTeleports.TryGetValue(player.Name, out var pendingUntil) && DateTime.UtcNow < pendingUntil)
            {
                player.SendErrorMessage("[MultiWorld] Kamu sedang dalam proses teleport! Harap diam di tempat.");
                return;
            }

            _pendingTeleports[player.Name] = DateTime.UtcNow.AddSeconds(25);

            try
            {
                int warmupSeconds = 5;
                MultiWorldModule.SetTransferLock(player.Name, warmupSeconds + 2);
                Vector2 startPos = player.TPlayer.position;
                player.SendInfoMessage($"[MultiWorld] Teleport ke '{target.DisplayName}' dalam {warmupSeconds} detik. JANGAN BERGERAK!");

                // Cek setiap 250ms selama 5 detik (20 kali pengecekan)
                int totalChecks = warmupSeconds * 4;
                for (int i = 0; i < totalChecks; i++)
                {
                    await Task.Delay(250);

                    if (!player.Active || player.TPlayer == null)
                    {
                        return;
                    }

                    // Toleransi gerak 16 pixel (1 tile) agar tidak batal cuma karena getaran kecil
                    if (Vector2.Distance(startPos, player.TPlayer.position) > 16f)
                    {
                        player.SendErrorMessage("[MultiWorld] Teleport DIBATALKAN karena kamu bergerak!");
                        return;
                    }
                }

                // Perpanjang sedikit expiry selama proses pindah world berlangsung
                _pendingTeleports[player.Name] = DateTime.UtcNow.AddSeconds(60);
                player.SendInfoMessage($"[MultiWorld] Memindahkan ke '{target.DisplayName}'...");
                _worldSwitchCooldowns[player.Name] = DateTime.UtcNow.AddSeconds(cooldownSec);
                var (ok, msg) = await MultiWorldModule.TransferPlayerAsync(
                    player,
                    player.Name,
                    player.Account?.Name ?? player.Name,
                    player.Group?.Name ?? "default",
                    player.UUID,
                    target.Id);

                if (!ok)
                {
                    player.SendErrorMessage($"[MultiWorld] {msg}");
                }
            }
            finally
            {
                MultiWorldModule.ClearTransferLock(player.Name);
                _pendingTeleports.TryRemove(player.Name, out _);
            }
        }

        private static readonly ConcurrentDictionary<string, DateTime> _worldSwitchCooldowns = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _rtpCooldowns = new(StringComparer.OrdinalIgnoreCase);

        private static void SurvivalShortcutCommand(CommandArgs args)
        {
            _ = ExecuteTeleportAsync(args.Player, "survival");
        }

        private static void RandomTeleportCommand(CommandArgs args)
        {
            var player = args.Player;
            if (!player.RealPlayer)
            {
                player.SendErrorMessage("Perintah /rtp hanya bisa dipakai oleh player di dalam game.");
                return;
            }

            if (!MultiWorldModule.CurrentWorldId.Equals("survival", StringComparison.OrdinalIgnoreCase))
            {
                player.SendErrorMessage("[RTP] Perintah /rtp hanya bisa digunakan di world Survival! Ketik /survival untuk masuk.");
                return;
            }

            // Cek cooldown 3 menit (180 detik) kecuali admin
            if (!player.HasPermission("univers.mw.admin") &&
                _rtpCooldowns.TryGetValue(player.Name, out var nextRtp) &&
                DateTime.UtcNow < nextRtp)
            {
                int sisa = (int)Math.Ceiling((nextRtp - DateTime.UtcNow).TotalSeconds);
                player.SendErrorMessage($"[RTP] Tunggu {sisa} detik lagi sebelum menggunakan /rtp kembali.");
                return;
            }

            if (TryFindSafeRtpSpot(out int tileX, out int tileY))
            {
                _rtpCooldowns[player.Name] = DateTime.UtcNow.AddMinutes(3);
                player.Teleport(tileX * 16f, tileY * 16f);
                string zone = tileY < Terraria.Main.worldSurface ? "Permukaan (Surface)" : "Bawah Tanah (Underground)";
                player.SendSuccessMessage($"[RTP] Berhasil teleport acak ke {zone} ({tileX}, {tileY})!");
            }
            else
            {
                player.SendErrorMessage("[RTP] Gagal menemukan titik aman, silakan coba ketik /rtp lagi.");
            }
        }

        private static bool TryFindSafeRtpSpot(out int safeX, out int safeY)
        {
            safeX = Terraria.Main.spawnTileX;
            safeY = Terraria.Main.spawnTileY;
            var rand = Random.Shared;

            int minX = 120;
            int maxX = Terraria.Main.maxTilesX - 120;
            if (maxX <= minX) return false;

            for (int attempt = 0; attempt < 60; attempt++)
            {
                int x = rand.Next(minX, maxX);
                // 70% cari di permukaan, 30% cari gua di bawah tanah
                bool underground = rand.Next(100) < 30;
                int startY = underground ? (int)Terraria.Main.worldSurface + 30 : 60;
                int endY = underground ? (int)Terraria.Main.rockLayer + 120 : (int)Terraria.Main.worldSurface + 25;
                endY = Math.Min(endY, Terraria.Main.maxTilesY - 150);

                for (int y = startY; y < endY; y++)
                {
                    if (IsSafeStandTile(x, y))
                    {
                        safeX = x;
                        safeY = y;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsSafeStandTile(int x, int y)
        {
            if (x < 50 || x >= Terraria.Main.maxTilesX - 50 || y < 50 || y >= Terraria.Main.maxTilesY - 50)
                return false;

            var floor = Terraria.Main.tile[x, y];
            if (floor == null || !floor.active() || !Terraria.Main.tileSolid[floor.type])
                return false;

            // Cek ruang kosong 2x3 tile di atas pijakan (tubuh player) & bebas cairan (lava/air)
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dy = 1; dy <= 3; dy++)
                {
                    var body = Terraria.Main.tile[x + dx, y - dy];
                    if (body == null) return false;
                    if (body.liquid > 0) return false;
                    if (body.active() && Terraria.Main.tileSolid[body.type] && !Terraria.Main.tileSolidTop[body.type])
                        return false;
                }
            }
            return true;
        }


        private static async Task RunAdminAsync(TSPlayer player, string subAction, List<string> args)
        {
            var res = await MultiWorldModule.ExecuteAdminActionAsync(subAction, args);
            if (res.Success)
            {
                player.SendSuccessMessage($"[MultiWorld] {res.Message}");
            }
            else
            {
                player.SendErrorMessage($"[MultiWorld] {res.Message}");
            }
        }

        private static void SendHelp(TSPlayer player)
        {
            player.SendMessage("=== [ Panduan Univers MultiWorld ] ===", Color.Gold);
            player.SendInfoMessage("/mw list | /world - Lihat daftar world, status & siapa saja yang online");
            player.SendInfoMessage("/mw online | /players - Lihat daftar player online di seluruh world");
            player.SendInfoMessage("/mw tp <id> | /world <id> - Pindah ke world (otomatis membangunkan world yang Sleep)");
            player.SendInfoMessage("/lobby | /hub - Kembali ke world Hub utama");
            if (player.HasPermission("univers.mw.admin"))
            {
                player.SendMessage("[Admin] /mw create <id> [small|medium|large] [classic|expert|master|journey] [random|corrupt|crimson] [seed]", Color.Yellow);
                player.SendMessage("[Admin] /mw delete <id> confirm - Hapus world permanen", Color.Yellow);
                player.SendMessage("[Admin] /mw start <id> | /mw sleep <id> - Nyalakan / tidurkan world manual", Color.Yellow);
                player.SendMessage("[Admin] /mw autosleep <id> <on|off|menit> - Atur Auto-Sleep saat world kosong", Color.Yellow);
                player.SendMessage("[Admin] /mw perm <id> <permission|none> - Batasi akses masuk world", Color.Yellow);
                player.SendMessage("[Admin] /mw resetssc [player] - Reset SSC player yang terlanjur tembus ke starter awal", Color.Yellow);
            }
        }
    }
}
