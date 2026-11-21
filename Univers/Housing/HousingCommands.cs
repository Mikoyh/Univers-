/*
 * =====================================================================
 *  HousingCommands.cs - Perintah /house, /myhouse, /visit & /houseadmin
 *  Namespace: Univers.Housing (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Skynomi.Economy;
using TShockAPI;
using Univers.MultiWorld;

namespace Univers.Housing
{
    public static class HousingCommands
    {
        public static void Register()
        {
            Commands.ChatCommands.Add(new Command("univers.house.use", OnHouseCommand, "house", "housing", "plot"));
            Commands.ChatCommands.Add(new Command("univers.house.use", OnMyHouseShortcut, "myhouse", "rumah"));
            Commands.ChatCommands.Add(new Command("univers.house.use", OnVisitShortcut, "visit", "kunjung"));
            Commands.ChatCommands.Add(new Command("univers.house.admin", OnHouseAdminCommand, "houseadmin", "ha"));
        }

        private static bool EnsureActiveWorld(TSPlayer player)
        {
            if (HousingModule.IsHousingActiveInCurrentWorld()) return true;
            string allowed = string.Join(", ", HousingModule.Config.AllowedWorldIds);
            player.SendErrorMessage($"[HOUSING] Fitur perumahan hanya aktif di world: {allowed}. Ketik /lobby untuk kembali.");
            return false;
        }

        private static void OnMyHouseShortcut(CommandArgs args)
        {
            if (!EnsureActiveWorld(args.Player)) return;
            HandleHomeTeleport(args.Player, args.Parameters);
        }

        private static void OnVisitShortcut(CommandArgs args)
        {
            if (!EnsureActiveWorld(args.Player)) return;
            if (args.Parameters.Count == 0)
            {
                args.Player.SendErrorMessage("Format: /visit <nama_player / kode_kavling>");
                return;
            }
            HandleVisit(args.Player, string.Join(" ", args.Parameters));
        }

        private static void OnHouseCommand(CommandArgs args)
        {
            var player = args.Player;
            if (!EnsureActiveWorld(player)) return;

            if (args.Parameters.Count == 0)
            {
                SendHouseHelp(player);
                return;
            }

            string sub = args.Parameters[0].ToLowerInvariant();
            switch (sub)
            {
                case "list":
                    ShowPlotList(player);
                    break;
                case "auto":
                    HandleAutoClaim(player);
                    break;
                case "claim":
                case "buy":
                    HandleClaimStanding(player);
                    break;
                case "home":
                case "tp":
                    HandleHomeTeleport(player, args.Parameters.Skip(1).ToList());
                    break;
                case "visit":
                    if (args.Parameters.Count < 2)
                    {
                        player.SendErrorMessage("Format: /house visit <nama_player / kode_kavling>");
                        return;
                    }
                    HandleVisit(player, string.Join(" ", args.Parameters.Skip(1)));
                    break;
                case "setspawn":
                    HandleSetSpawn(player);
                    break;
                case "rename":
                    HandleRename(player, string.Join(" ", args.Parameters.Skip(1)));
                    break;
                case "desc":
                case "bio":
                    HandleDesc(player, string.Join(" ", args.Parameters.Skip(1)));
                    break;
                case "lock":
                    HandleLock(player, isLocked: true);
                    break;
                case "unlock":
                    HandleLock(player, isLocked: false);
                    break;
                case "trust":
                case "coowner":
                    if (args.Parameters.Count < 2) { player.SendErrorMessage("Format: /house trust <nama_player>"); return; }
                    HandleRoleChange(player, args.Parameters[1], isCoOwner: true, add: true);
                    break;
                case "untrust":
                    if (args.Parameters.Count < 2) { player.SendErrorMessage("Format: /house untrust <nama_player>"); return; }
                    HandleRoleChange(player, args.Parameters[1], isCoOwner: true, add: false);
                    break;
                case "member":
                case "addmember":
                    if (args.Parameters.Count < 2) { player.SendErrorMessage("Format: /house member <nama_player>"); return; }
                    HandleRoleChange(player, args.Parameters[1], isCoOwner: false, add: true);
                    break;
                case "unmember":
                case "removemember":
                    if (args.Parameters.Count < 2) { player.SendErrorMessage("Format: /house unmember <nama_player>"); return; }
                    HandleRoleChange(player, args.Parameters[1], isCoOwner: false, add: false);
                    break;
                case "kick":
                    if (args.Parameters.Count < 2) { player.SendErrorMessage("Format: /house kick <nama_player>"); return; }
                    HandleKickVisitor(player, args.Parameters[1]);
                    break;
                case "ban":
                    if (args.Parameters.Count < 2) { player.SendErrorMessage("Format: /house ban <nama_player>"); return; }
                    HandleBanVisitor(player, args.Parameters[1], ban: true);
                    break;
                case "unban":
                    if (args.Parameters.Count < 2) { player.SendErrorMessage("Format: /house unban <nama_player>"); return; }
                    HandleBanVisitor(player, args.Parameters[1], ban: false);
                    break;
                case "flag":
                    if (args.Parameters.Count < 3)
                    {
                        player.SendErrorMessage("Format: /house flag <pvp|chest|interact|build|cozy> <on|off>");
                        return;
                    }
                    HandleFlag(player, args.Parameters[1], args.Parameters[2]);
                    break;
                case "upgrade":
                    HandleUpgrade(player);
                    break;
                case "sell":
                    if (args.Parameters.Count < 2 || !long.TryParse(args.Parameters[1], out long resalePrice))
                    {
                        player.SendErrorMessage("Format: /house sell <harga_shards>");
                        return;
                    }
                    HandlePutForSale(player, resalePrice);
                    break;
                case "cancelsell":
                    HandlePutForSale(player, 0);
                    break;
                case "abandon":
                    HandleAbandon(player, args.Parameters.Skip(1).ToList());
                    break;
                case "like":
                case "rate":
                    HandleLikePlot(player);
                    break;
                case "top":
                    ShowTopPlots(player);
                    break;
                case "border":
                    HandleBorder(player);
                    break;
                case "info":
                    ShowStandingPlotInfo(player);
                    break;
                default:
                    SendHouseHelp(player);
                    break;
            }
        }

        private static HousingPlot? ResolveEditableOwnedPlot(TSPlayer player, out string errMsg)
        {
            errMsg = string.Empty;
            if (!player.IsLoggedIn)
            {
                errMsg = "Silakan /login terlebih dahulu.";
                return null;
            }

            var standing = HousingModule.Database.GetPlotAt(HousingModule.ActiveWorldId, player.TileX, player.TileY);
            if (standing != null && (standing.IsOwner(player.Account.Name) || player.HasPermission("univers.house.admin")))
            {
                return standing;
            }

            var owned = HousingModule.Database.GetOwnedPlots(player.Account.Name, HousingModule.ActiveWorldId);
            if (owned.Count == 1) return owned[0];
            if (owned.Count > 1)
            {
                errMsg = "Kamu punya lebih dari 1 rumah! Berdiri di dalam rumah yang ingin kamu atur terlebih dahulu.";
                return null;
            }

            errMsg = "Kamu belum memiliki rumah. Ketik /house auto atau klik Sign [KAVLING DIJUAL]!";
            return null;
        }

        private static void HandleAutoClaim(TSPlayer player)
        {
            if (!player.IsLoggedIn)
            {
                player.SendErrorMessage("Silakan /login terlebih dahulu.");
                return;
            }

            var freePlot = HousingModule.Database.GetFirstUnownedPlot(HousingModule.ActiveWorldId);
            if (freePlot == null)
            {
                player.SendErrorMessage("[HOUSING] Saat ini belum ada kavling kosong tersedia! Minta Admin membuat kavling via /ha grid.");
                return;
            }

            var (ok, msg) = HousingModule.TryClaimPlot(player, freePlot);
            if (!ok)
            {
                player.SendErrorMessage("[HOUSING] " + msg);
                return;
            }

            player.Teleport(freePlot.SpawnX * 16f, freePlot.SpawnY * 16f);
            player.SendSuccessMessage("[HOUSING] " + msg);
        }

        private static void HandleClaimStanding(TSPlayer player)
        {
            if (!player.IsLoggedIn)
            {
                player.SendErrorMessage("Silakan /login terlebih dahulu.");
                return;
            }

            var plot = HousingModule.Database.GetPlotAt(HousingModule.ActiveWorldId, player.TileX, player.TileY);
            if (plot == null)
            {
                player.SendErrorMessage("[HOUSING] Kamu tidak sedang berdiri di atas kavling mana pun! Gunakan /house auto untuk beli kavling otomatis.");
                return;
            }

            if (plot.IsOwned && plot.ForSaleByOwnerPrice > 0 && !plot.IsOwner(player.Account.Name))
            {
                var (okR, msgR) = HousingModule.TryBuyResalePlot(player, plot);
                if (okR) player.SendSuccessMessage("[HOUSING] " + msgR);
                else player.SendErrorMessage("[HOUSING] " + msgR);
                return;
            }

            var (ok, msg) = HousingModule.TryClaimPlot(player, plot);
            if (ok) player.SendSuccessMessage("[HOUSING] " + msg);
            else player.SendErrorMessage("[HOUSING] " + msg);
        }

        private static void HandleHomeTeleport(TSPlayer player, List<string> args)
        {
            if (!player.IsLoggedIn)
            {
                player.SendErrorMessage("Silakan /login terlebih dahulu.");
                return;
            }

            var owned = HousingModule.Database.GetOwnedPlots(player.Account.Name, HousingModule.ActiveWorldId);
            if (owned.Count == 0)
            {
                player.SendErrorMessage("[HOUSING] Kamu belum punya rumah di world ini! Ketik /house auto untuk membeli.");
                return;
            }

            HousingPlot? target = owned[0];
            if (args.Count > 0)
            {
                string q = string.Join(" ", args).Trim();
                if (int.TryParse(q, out int idx) && idx >= 1 && idx <= owned.Count)
                {
                    target = owned[idx - 1];
                }
                else
                {
                    target = owned.FirstOrDefault(p =>
                        p.Code.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                        p.CustomName.Contains(q, StringComparison.OrdinalIgnoreCase));
                    if (target == null)
                    {
                        player.SendErrorMessage($"[HOUSING] Rumah '{q}' tidak ditemukan di daftar milikmu.");
                        return;
                    }
                }
            }

            player.Teleport(target.SpawnX * 16f, target.SpawnY * 16f);
            player.SendSuccessMessage($"[HOUSING] Teleportasi ke '{target.EffectiveTitle}' ({target.Code}).");
        }

        private static void HandleVisit(TSPlayer player, string query)
        {
            var plot = HousingModule.Database.FindVisitTarget(query, HousingModule.ActiveWorldId);
            if (plot == null)
            {
                player.SendErrorMessage($"[HOUSING] Rumah atau kavling '{query}' tidak ditemukan.");
                return;
            }

            string accName = player.IsLoggedIn ? player.Account.Name : "";
            if (!player.HasPermission("univers.house.admin") && !plot.CanEnter(accName))
            {
                string reason = plot.IsBanned(accName) ? "Kamu diblokir dari properti ini." : "Rumah ini sedang DIKUNCI (Private).";
                player.SendErrorMessage($"[HOUSING] Tidak bisa mengunjungi '{plot.EffectiveTitle}': {reason}");
                return;
            }

            player.Teleport(plot.SpawnX * 16f, plot.SpawnY * 16f);
            player.SendSuccessMessage($"[HOUSING] Mengunjungi '{plot.EffectiveTitle}' ({plot.Code}).");
        }

        private static void HandleSetSpawn(TSPlayer player)
        {
            var standing = HousingModule.Database.GetPlotAt(HousingModule.ActiveWorldId, player.TileX, player.TileY);
            if (standing == null || (!standing.IsOwner(player.Account?.Name ?? "") && !player.HasPermission("univers.house.admin")))
            {
                player.SendErrorMessage("[HOUSING] Berdirilah di dalam rumah milikmu sendiri untuk mengatur titik spawn (/house setspawn).");
                return;
            }

            standing.SpawnX = player.TileX;
            standing.SpawnY = player.TileY;
            HousingModule.Database.SavePlot(standing);
            player.SendSuccessMessage($"[HOUSING] Titik masuk rumah '{standing.EffectiveTitle}' diset ke koordinat kamu saat ini.");
        }

        private static void HandleRename(TSPlayer player, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                player.SendErrorMessage("Format: /house rename <nama rumah RP>");
                return;
            }
            var plot = ResolveEditableOwnedPlot(player, out string err);
            if (plot == null) { player.SendErrorMessage("[HOUSING] " + err); return; }

            plot.CustomName = newName.Length > 36 ? newName.Substring(0, 36) : newName.Trim();
            HousingModule.Database.SavePlot(plot);
            HousingModule.QueueMainThread(() => HousingModule.RefreshPlotSign(plot));
            player.SendSuccessMessage($"[HOUSING] Nama rumah diubah menjadi '{plot.EffectiveTitle}'.");
        }

        private static void HandleDesc(TSPlayer player, string desc)
        {
            if (string.IsNullOrWhiteSpace(desc))
            {
                player.SendErrorMessage("Format: /house desc <deskripsi singkat RP>");
                return;
            }
            var plot = ResolveEditableOwnedPlot(player, out string err);
            if (plot == null) { player.SendErrorMessage("[HOUSING] " + err); return; }

            plot.Description = desc.Length > 80 ? desc.Substring(0, 80) : desc.Trim();
            HousingModule.Database.SavePlot(plot);
            player.SendSuccessMessage("[HOUSING] Bio RP rumah diperbarui!");
        }

        private static void HandleLock(TSPlayer player, bool isLocked)
        {
            var plot = ResolveEditableOwnedPlot(player, out string err);
            if (plot == null) { player.SendErrorMessage("[HOUSING] " + err); return; }

            plot.IsPublicVisit = !isLocked;
            HousingModule.Database.SavePlot(plot);
            HousingModule.QueueMainThread(() => HousingModule.RefreshPlotSign(plot));

            if (isLocked)
            {
                // Usir tamu yang bukan member
                HousingModule.QueueMainThread(() =>
                {
                    foreach (var p in TShock.Players)
                    {
                        if (p == null || !p.Active || !p.RealPlayer) continue;
                        if (plot.ContainsTile(p.TileX, p.TileY) && !p.HasPermission("univers.house.admin") && !plot.CanEnter(p.Account?.Name ?? ""))
                        {
                            HousingModule.EjectPlayerOutsidePlot(p, plot);
                        }
                    }
                });
                player.SendSuccessMessage($"[HOUSING] '{plot.EffectiveTitle}' sekarang DIKUNCI (Private). Hanya Owner/Member yang bisa masuk.");
            }
            else
            {
                player.SendSuccessMessage($"[HOUSING] '{plot.EffectiveTitle}' sekarang DIBUKA (Public). Semua player bisa berkunjung lewat /visit!");
            }
        }

        private static void HandleRoleChange(TSPlayer player, string targetQuery, bool isCoOwner, bool add)
        {
            var plot = ResolveEditableOwnedPlot(player, out string err);
            if (plot == null) { player.SendErrorMessage("[HOUSING] " + err); return; }

            string targetAcc = ResolveAccountName(targetQuery);
            if (string.IsNullOrWhiteSpace(targetAcc))
            {
                player.SendErrorMessage($"[HOUSING] Akun player '{targetQuery}' tidak ditemukan.");
                return;
            }
            if (plot.IsOwner(targetAcc))
            {
                player.SendErrorMessage("[HOUSING] Dia adalah pemilik utama rumah ini.");
                return;
            }

            var tier = HousingModule.Config.GetTier(plot.Level);
            if (add)
            {
                if (isCoOwner)
                {
                    if (plot.CoOwners.Any(x => x.Equals(targetAcc, StringComparison.OrdinalIgnoreCase)))
                    {
                        player.SendInfoMessage($"[HOUSING] {targetAcc} sudah menjadi Co-Owner.");
                        return;
                    }
                    if (plot.CoOwners.Count >= tier.MaxCoOwners)
                    {
                        player.SendErrorMessage($"[HOUSING] Batas Co-Owner untuk Tier {tier.Title} (Lv.{plot.Level}) penuh ({tier.MaxCoOwners}). Upgrade rumah dengan /house upgrade!");
                        return;
                    }
                    plot.Members.RemoveAll(x => x.Equals(targetAcc, StringComparison.OrdinalIgnoreCase));
                    plot.BannedPlayers.RemoveAll(x => x.Equals(targetAcc, StringComparison.OrdinalIgnoreCase));
                    plot.CoOwners.Add(targetAcc);
                    HousingModule.Database.SavePlot(plot);
                    player.SendSuccessMessage($"[HOUSING] {targetAcc} ditambahkan sebagai CO-OWNER (izin bangun + chest).");
                }
                else
                {
                    if (plot.Members.Any(x => x.Equals(targetAcc, StringComparison.OrdinalIgnoreCase)))
                    {
                        player.SendInfoMessage($"[HOUSING] {targetAcc} sudah menjadi Member.");
                        return;
                    }
                    if (plot.Members.Count >= tier.MaxMembers)
                    {
                        player.SendErrorMessage($"[HOUSING] Batas Member untuk Tier {tier.Title} (Lv.{plot.Level}) penuh ({tier.MaxMembers}). Upgrade rumah dengan /house upgrade!");
                        return;
                    }
                    plot.CoOwners.RemoveAll(x => x.Equals(targetAcc, StringComparison.OrdinalIgnoreCase));
                    plot.BannedPlayers.RemoveAll(x => x.Equals(targetAcc, StringComparison.OrdinalIgnoreCase));
                    plot.Members.Add(targetAcc);
                    HousingModule.Database.SavePlot(plot);
                    player.SendSuccessMessage($"[HOUSING] {targetAcc} ditambahkan sebagai MEMBER (izin bangun & dekorasi, tanpa chest).");
                }
            }
            else
            {
                int removed = isCoOwner
                    ? plot.CoOwners.RemoveAll(x => x.Equals(targetAcc, StringComparison.OrdinalIgnoreCase))
                    : plot.Members.RemoveAll(x => x.Equals(targetAcc, StringComparison.OrdinalIgnoreCase));
                HousingModule.Database.SavePlot(plot);
                player.SendSuccessMessage(removed > 0
                    ? $"[HOUSING] Izin {targetAcc} dicabut dari '{plot.EffectiveTitle}'."
                    : $"[HOUSING] {targetAcc} tidak terdaftar dalam role tersebut.");
            }
        }

        private static void HandleKickVisitor(TSPlayer player, string targetQuery)
        {
            var plot = ResolveEditableOwnedPlot(player, out string err);
            if (plot == null) { player.SendErrorMessage("[HOUSING] " + err); return; }

            var target = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.Name.Equals(targetQuery, StringComparison.OrdinalIgnoreCase));
            if (target == null || !plot.ContainsTile(target.TileX, target.TileY))
            {
                player.SendErrorMessage($"[HOUSING] Player '{targetQuery}' tidak sedang berada di dalam rumahmu.");
                return;
            }
            if (plot.IsOwner(target.Account?.Name ?? "") || target.HasPermission("univers.house.admin"))
            {
                player.SendErrorMessage("[HOUSING] Tidak bisa mengusir player tersebut.");
                return;
            }

            HousingModule.QueueMainThread(() => HousingModule.EjectPlayerOutsidePlot(target, plot));
            player.SendSuccessMessage($"[HOUSING] {target.Name} telah diusir keluar dari propertimu.");
        }

        private static void HandleBanVisitor(TSPlayer player, string targetQuery, bool ban)
        {
            var plot = ResolveEditableOwnedPlot(player, out string err);
            if (plot == null) { player.SendErrorMessage("[HOUSING] " + err); return; }

            string targetAcc = ResolveAccountName(targetQuery);
            if (string.IsNullOrWhiteSpace(targetAcc))
            {
                player.SendErrorMessage($"[HOUSING] Akun '{targetQuery}' tidak ditemukan.");
                return;
            }
            if (plot.IsOwner(targetAcc))
            {
                player.SendErrorMessage("[HOUSING] Tidak bisa ban diri sendiri!");
                return;
            }

            if (ban)
            {
                plot.CoOwners.RemoveAll(x => x.Equals(targetAcc, StringComparison.OrdinalIgnoreCase));
                plot.Members.RemoveAll(x => x.Equals(targetAcc, StringComparison.OrdinalIgnoreCase));
                if (!plot.IsBanned(targetAcc)) plot.BannedPlayers.Add(targetAcc);
                HousingModule.Database.SavePlot(plot);

                var online = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.IsLoggedIn && p.Account.Name.Equals(targetAcc, StringComparison.OrdinalIgnoreCase));
                if (online != null && plot.ContainsTile(online.TileX, online.TileY))
                {
                    HousingModule.QueueMainThread(() => HousingModule.EjectPlayerOutsidePlot(online, plot));
                }
                player.SendSuccessMessage($"[HOUSING] {targetAcc} diblokir dari rumah '{plot.EffectiveTitle}'.");
            }
            else
            {
                plot.BannedPlayers.RemoveAll(x => x.Equals(targetAcc, StringComparison.OrdinalIgnoreCase));
                HousingModule.Database.SavePlot(plot);
                player.SendSuccessMessage($"[HOUSING] Blokir untuk {targetAcc} telah dicabut.");
            }
        }

        private static void HandleFlag(TSPlayer player, string flagName, string valueStr)
        {
            var plot = ResolveEditableOwnedPlot(player, out string err);
            if (plot == null) { player.SendErrorMessage("[HOUSING] " + err); return; }

            bool? val = valueStr.Trim().ToLowerInvariant() switch
            {
                "on" or "true" or "yes" or "ya" => true,
                "off" or "false" or "no" or "tidak" => false,
                _ => null
            };
            if (!val.HasValue)
            {
                player.SendErrorMessage("Nilai flag harus 'on' atau 'off'.");
                return;
            }

            switch (flagName.ToLowerInvariant())
            {
                case "pvp": plot.FlagAllowPvp = val.Value; break;
                case "chest": case "chests": plot.FlagAllowGuestChests = val.Value; break;
                case "interact": case "door": plot.FlagAllowGuestInteract = val.Value; break;
                case "build": plot.FlagAllowGuestBuild = val.Value; break;
                case "cozy": case "heal": plot.FlagCozyRegen = val.Value; break;
                default:
                    player.SendErrorMessage("Flag valid: pvp, chest, interact, build, cozy");
                    return;
            }

            HousingModule.Database.SavePlot(plot);
            player.SendSuccessMessage($"[HOUSING] Flag '{flagName.ToLowerInvariant()}' di '{plot.EffectiveTitle}' diset ke {(val.Value ? "ON" : "OFF")}.");
        }

        private static void HandleUpgrade(TSPlayer player)
        {
            var plot = ResolveEditableOwnedPlot(player, out string err);
            if (plot == null) { player.SendErrorMessage("[HOUSING] " + err); return; }

            var (ok, msg) = HousingModule.TryUpgradePlot(player, plot);
            if (ok) player.SendSuccessMessage("[HOUSING] " + msg);
            else player.SendErrorMessage("[HOUSING] " + msg);
        }

        private static void HandlePutForSale(TSPlayer player, long price)
        {
            var plot = ResolveEditableOwnedPlot(player, out string err);
            if (plot == null) { player.SendErrorMessage("[HOUSING] " + err); return; }

            if (price < 0)
            {
                player.SendErrorMessage("Harga jual harus lebih dari 0.");
                return;
            }

            plot.ForSaleByOwnerPrice = price;
            HousingModule.Database.SavePlot(plot);
            HousingModule.QueueMainThread(() => HousingModule.RefreshPlotSign(plot));

            player.SendSuccessMessage(price == 0
                ? $"[HOUSING] Penjualan rumah '{plot.EffectiveTitle}' dibatalkan."
                : $"[HOUSING] Rumah '{plot.EffectiveTitle}' dipasang DIJUAL seharga {EconomyApi.Format(price)}!");
        }

        private static void HandleAbandon(TSPlayer player, List<string> args)
        {
            var plot = ResolveEditableOwnedPlot(player, out string err);
            if (plot == null) { player.SendErrorMessage("[HOUSING] " + err); return; }

            if (args.Count == 0 || !args[0].Equals("confirm", StringComparison.OrdinalIgnoreCase))
            {
                long refund = (long)Math.Floor(plot.PriceShards * (HousingModule.Config.AbandonRefundPercent / 100.0));
                player.SendWarningMessage($"[HOUSING] Melepas rumah '{plot.EffectiveTitle}' akan memberi refund {EconomyApi.Format(refund)}. Ketik /house abandon confirm untuk melanjutkan.");
                return;
            }

            var (ok, msg) = HousingModule.TryAbandonPlot(player, plot);
            if (ok) player.SendSuccessMessage("[HOUSING] " + msg);
            else player.SendErrorMessage("[HOUSING] " + msg);
        }

        private static void HandleLikePlot(TSPlayer player)
        {
            if (!player.IsLoggedIn)
            {
                player.SendErrorMessage("Silakan /login terlebih dahulu.");
                return;
            }

            var plot = HousingModule.Database.GetPlotAt(HousingModule.ActiveWorldId, player.TileX, player.TileY);
            if (plot == null || !plot.IsOwned)
            {
                player.SendErrorMessage("[HOUSING] Berdirilah di dalam rumah player yang ingin kamu Like!");
                return;
            }

            string acc = player.Account.Name;
            if (plot.IsOwner(acc))
            {
                player.SendErrorMessage("[HOUSING] Kamu tidak bisa me-Like rumahmu sendiri!");
                return;
            }

            if (plot.LikedBy.Any(x => x.Equals(acc, StringComparison.OrdinalIgnoreCase)))
            {
                player.SendInfoMessage($"[HOUSING] Kamu sudah pernah memberi Like untuk '{plot.EffectiveTitle}'.");
                return;
            }

            plot.LikedBy.Add(acc);
            plot.LikesCount = plot.LikedBy.Count;
            HousingModule.Database.SavePlot(plot);
            HousingModule.QueueMainThread(() => HousingModule.RefreshPlotSign(plot));

            player.SendSuccessMessage($"[HOUSING] Kamu memberi LIKE untuk '{plot.EffectiveTitle}'! (Total Likes: {plot.LikesCount})");
            var ownerOnline = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.IsLoggedIn && p.Account.Name.Equals(plot.OwnerAccount, StringComparison.OrdinalIgnoreCase));
            ownerOnline?.SendSuccessMessage($"[HOUSING] {player.Name} menyukai rumahmu '{plot.EffectiveTitle}'! (Likes: {plot.LikesCount})");
        }

        private static void ShowPlotList(TSPlayer player)
        {
            var plots = HousingModule.Database.GetAllPlots(HousingModule.ActiveWorldId);
            if (plots.Count == 0)
            {
                player.SendInfoMessage("[HOUSING] Belum ada kavling perumahan di world ini. Admin bisa membuatnya lewat /ha grid.");
                return;
            }

            int freeCount = plots.Count(p => !p.IsOwned);
            player.SendMessage($"=== [ DAFTAR KAVLING RP ({plots.Count} total, {freeCount} DIJUAL) ] ===", Color.Gold);
            foreach (var p in plots.Take(15))
            {
                if (!p.IsOwned)
                {
                    player.SendMessage($" - [{p.Code}] DIJUAL ({p.DistrictName}) | Harga: {EconomyApi.Format(p.PriceShards)}", Color.LightGreen);
                }
                else
                {
                    string status = p.ForSaleByOwnerPrice > 0
                        ? $"RESALE {EconomyApi.Format(p.ForSaleByOwnerPrice)}"
                        : (p.IsPublicVisit ? "OPEN" : "LOCKED");
                    player.SendMessage($" - [{p.Code}] {p.EffectiveTitle} (Milik: {p.OwnerDisplayName}) | Lv.{p.Level} | Likes:{p.LikesCount} | {status}", Color.Cyan);
                }
            }
            if (plots.Count > 15)
            {
                player.SendInfoMessage($"...dan {plots.Count - 15} kavling lainnya. Gunakan /house auto untuk beli kavling kosong otomatis.");
            }
        }

        private static void ShowTopPlots(TSPlayer player)
        {
            var top = HousingModule.Database.GetTopLikedPlots(HousingModule.ActiveWorldId, 10);
            player.SendMessage("=== [ TOP 10 RUMAH RP POPULER ] ===", Color.Gold);
            if (top.Count == 0)
            {
                player.SendInfoMessage("Belum ada rumah yang dimiliki saat ini.");
                return;
            }

            int rank = 1;
            foreach (var p in top)
            {
                player.SendMessage($" #{rank++} {p.EffectiveTitle} ({p.Code}) - Milik {p.OwnerDisplayName} | Likes: {p.LikesCount} | Visit: {p.VisitsCount} (/visit {p.Code})", Color.Aquamarine);
            }
        }

        private static void HandleBorder(TSPlayer player)
        {
            var plot = HousingModule.Database.GetPlotAt(HousingModule.ActiveWorldId, player.TileX, player.TileY)
                ?? (player.IsLoggedIn ? HousingModule.Database.GetOwnedPlots(player.Account.Name, HousingModule.ActiveWorldId).FirstOrDefault() : null);

            if (plot == null)
            {
                player.SendErrorMessage("[HOUSING] Berdiri di dalam kavling untuk melihat batas tanah.");
                return;
            }

            HousingModule.ShowPlotBorderMarkers(player, plot);
            int w = plot.MaxX - plot.MinX + 1;
            int h = plot.MaxY - plot.MinY + 1;
            player.SendInfoMessage($"[HOUSING] Batas '{plot.EffectiveTitle}' ({plot.Code}): X [{plot.MinX}..{plot.MaxX}] Y [{plot.MinY}..{plot.MaxY}] ({w}x{h} tiles).");
        }

        private static void ShowStandingPlotInfo(TSPlayer player)
        {
            var plot = HousingModule.Database.GetPlotAt(HousingModule.ActiveWorldId, player.TileX, player.TileY);
            if (plot == null)
            {
                player.SendInfoMessage("[HOUSING] Kamu tidak berada di dalam kavling perumahan mana pun.");
                return;
            }

            var tier = HousingModule.Config.GetTier(plot.Level);
            player.SendMessage($"=== Info Kavling [{plot.Code}] ===", Color.Cyan);
            if (!plot.IsOwned)
            {
                player.SendInfoMessage($"Status: DIJUAL | Zona: {plot.DistrictName} | Harga: {EconomyApi.Format(plot.PriceShards)} | Ketik /house claim");
                return;
            }

            player.SendInfoMessage($"Nama: {plot.EffectiveTitle} | Pemilik: {plot.OwnerDisplayName} | Tier: Lv.{plot.Level} ({tier.Title})");
            player.SendInfoMessage($"Likes: {plot.LikesCount} | Visits: {plot.VisitsCount} | Akses: {(plot.IsPublicVisit ? "OPEN" : "LOCKED")}");
            player.SendInfoMessage($"Co-Owner: {(plot.CoOwners.Count > 0 ? string.Join(", ", plot.CoOwners) : "-")} | Member: {(plot.Members.Count > 0 ? string.Join(", ", plot.Members) : "-")}");
            player.SendInfoMessage($"Flags: PvP={BoolText(plot.FlagAllowPvp)}, ChestTamu={BoolText(plot.FlagAllowGuestChests)}, InteraksiTamu={BoolText(plot.FlagAllowGuestInteract)}, BuildTamu={BoolText(plot.FlagAllowGuestBuild)}, Cozy={BoolText(plot.FlagCozyRegen)}");
        }

        #region Admin Commands (/houseadmin | /ha)

        private static void OnHouseAdminCommand(CommandArgs args)
        {
            var player = args.Player;
            if (!EnsureActiveWorld(player)) return;

            if (args.Parameters.Count == 0)
            {
                SendAdminHelp(player);
                return;
            }

            string sub = args.Parameters[0].ToLowerInvariant();
            switch (sub)
            {
                case "pos1":
                {
                    var sel = HousingModule.GetOrCreateSelection(player);
                    if (args.Parameters.Count > 1 && args.Parameters[1].Equals("here", StringComparison.OrdinalIgnoreCase))
                    {
                        sel.X1 = player.TileX;
                        sel.Y1 = player.TileY;
                        sel.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
                        player.SendSuccessMessage($"[HOUSING ADMIN] Pos 1 diset di posisi kamu ({player.TileX}, {player.TileY}).");
                    }
                    else
                    {
                        sel.PendingPosClick = 1;
                        sel.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2);
                        player.SendInfoMessage("[HOUSING ADMIN] Pukul/letakkan 1 blok di sudut KIRI-ATAS kavling (atau ketik /ha pos1 here).");
                    }
                    break;
                }

                case "pos2":
                {
                    var sel = HousingModule.GetOrCreateSelection(player);
                    if (args.Parameters.Count > 1 && args.Parameters[1].Equals("here", StringComparison.OrdinalIgnoreCase))
                    {
                        sel.X2 = player.TileX;
                        sel.Y2 = player.TileY;
                        sel.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
                        player.SendSuccessMessage($"[HOUSING ADMIN] Pos 2 diset di posisi kamu ({player.TileX}, {player.TileY}).");
                    }
                    else
                    {
                        sel.PendingPosClick = 2;
                        sel.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2);
                        player.SendInfoMessage("[HOUSING ADMIN] Pukul/letakkan 1 blok di sudut KANAN-BAWAH kavling (atau ketik /ha pos2 here).");
                    }
                    break;
                }

                case "create":
                {
                    if (args.Parameters.Count < 3 || !long.TryParse(args.Parameters[2], out long price))
                    {
                        player.SendErrorMessage("Format: /ha create <kode_unik> <harga_shards>");
                        return;
                    }
                    string code = args.Parameters[1].Trim().ToLowerInvariant();
                    if (HousingModule.Database.GetPlotByCode(code) != null)
                    {
                        player.SendErrorMessage($"Kode kavling '{code}' sudah ada.");
                        return;
                    }

                    var sel = HousingModule.GetOrCreateSelection(player);
                    int minX, minY, maxX, maxY;
                    if (sel.X1.HasValue && sel.Y1.HasValue && sel.X2.HasValue && sel.Y2.HasValue)
                    {
                        minX = Math.Min(sel.X1.Value, sel.X2.Value);
                        maxX = Math.Max(sel.X1.Value, sel.X2.Value);
                        minY = Math.Min(sel.Y1.Value, sel.Y2.Value);
                        maxY = Math.Max(sel.Y1.Value, sel.Y2.Value);
                    }
                    else
                    {
                        int hw = HousingModule.Config.DefaultPlotWidth / 2;
                        minX = Math.Max(10, player.TileX - hw);
                        maxX = minX + HousingModule.Config.DefaultPlotWidth;
                        maxY = player.TileY + 3;
                        minY = Math.Max(10, maxY - HousingModule.Config.DefaultPlotHeight);
                    }

                    var plot = new HousingPlot
                    {
                        Code = code,
                        WorldId = HousingModule.ActiveWorldId,
                        DistrictName = "custom",
                        MinX = minX,
                        MinY = minY,
                        MaxX = maxX,
                        MaxY = maxY,
                        SpawnX = (minX + maxX) / 2,
                        SpawnY = maxY - 3,
                        PriceShards = Math.Max(0, price),
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    HousingModule.Database.SavePlot(plot);
                    HousingModule.ShowPlotBorderMarkers(player, plot);
                    player.SendSuccessMessage($"[HOUSING ADMIN] Kavling '{code}' dibuat ({maxX - minX + 1}x{maxY - minY + 1} tiles, Harga: {EconomyApi.Format(price)}). Pasang Sign lalu ketik /ha linksign {code}.");
                    break;
                }

                case "grid":
                {
                    if (args.Parameters.Count < 3 || !int.TryParse(args.Parameters[2], out int count) || count < 1 || count > 20)
                    {
                        player.SendErrorMessage("Format: /ha grid <nama_zona> <jumlah 1-20> [harga] [lebar] [tinggi]");
                        return;
                    }
                    string district = args.Parameters[1].Trim().ToLowerInvariant();
                    long price = args.Parameters.Count > 3 && long.TryParse(args.Parameters[3], out long p) ? Math.Max(0, p) : HousingModule.Config.DefaultPlotPrice;
                    int width = args.Parameters.Count > 4 && int.TryParse(args.Parameters[4], out int w) ? Math.Clamp(w, 16, 120) : HousingModule.Config.DefaultPlotWidth;
                    int height = args.Parameters.Count > 5 && int.TryParse(args.Parameters[5], out int h) ? Math.Clamp(h, 12, 80) : HousingModule.Config.DefaultPlotHeight;

                    HousingModule.QueueMainThread(() =>
                    {
                        int built = HousingModule.BuildAutoPlotGrid(player, district, count, price, width, height);
                        player.SendSuccessMessage($"[HOUSING ADMIN] Berhasil membangun {built} kavling '{district}' lengkap dengan lantai batas & Sign DIJUAL ({EconomyApi.Format(price)})!");
                    });
                    break;
                }

                case "linksign":
                {
                    if (args.Parameters.Count < 2)
                    {
                        player.SendErrorMessage("Format: /ha linksign <kode_kavling>");
                        return;
                    }
                    string code = args.Parameters[1].Trim();
                    if (HousingModule.Database.GetPlotByCode(code) == null)
                    {
                        player.SendErrorMessage($"Kavling '{code}' tidak ditemukan.");
                        return;
                    }
                    var sel = HousingModule.GetOrCreateSelection(player);
                    sel.PendingLinkSignCode = code;
                    sel.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30);
                    player.SendInfoMessage($"[HOUSING ADMIN] Klik kanan Sign yang ingin dihubungkan ke kavling '{code}'.");
                    break;
                }

                case "delete":
                {
                    if (args.Parameters.Count < 3 || !args.Parameters[2].Equals("confirm", StringComparison.OrdinalIgnoreCase))
                    {
                        string k = args.Parameters.Count > 1 ? args.Parameters[1] : "<kode>";
                        player.SendErrorMessage($"Ketik: /ha delete {k} confirm");
                        return;
                    }
                    var plot = HousingModule.Database.GetPlotByCode(args.Parameters[1]);
                    if (plot == null) { player.SendErrorMessage("Kavling tidak ditemukan."); return; }
                    HousingModule.Database.DeletePlot(plot.Id);
                    player.SendSuccessMessage($"[HOUSING ADMIN] Kavling '{plot.Code}' dihapus permanen.");
                    break;
                }

                case "evict":
                {
                    if (args.Parameters.Count < 3 || !args.Parameters[2].Equals("confirm", StringComparison.OrdinalIgnoreCase))
                    {
                        string k = args.Parameters.Count > 1 ? args.Parameters[1] : "<kode>";
                        player.SendErrorMessage($"Ketik: /ha evict {k} confirm");
                        return;
                    }
                    var plot = HousingModule.Database.GetPlotByCode(args.Parameters[1]);
                    if (plot == null) { player.SendErrorMessage("Kavling tidak ditemukan."); return; }
                    HousingModule.ResetPlotToForSale(plot);
                    player.SendSuccessMessage($"[HOUSING ADMIN] Pemilik kavling '{plot.Code}' dikosongkan (status DIJUAL).");
                    break;
                }

                case "setprice":
                {
                    if (args.Parameters.Count < 3 || !long.TryParse(args.Parameters[2], out long newPrice) || newPrice < 0)
                    {
                        player.SendErrorMessage("Format: /ha setprice <kode> <harga>");
                        return;
                    }
                    var plot = HousingModule.Database.GetPlotByCode(args.Parameters[1]);
                    if (plot == null) { player.SendErrorMessage("Kavling tidak ditemukan."); return; }
                    plot.PriceShards = newPrice;
                    HousingModule.Database.SavePlot(plot);
                    HousingModule.QueueMainThread(() => HousingModule.RefreshPlotSign(plot));
                    player.SendSuccessMessage($"[HOUSING ADMIN] Harga dasar '{plot.Code}' diubah ke {EconomyApi.Format(newPrice)}.");
                    break;
                }

                default:
                    SendAdminHelp(player);
                    break;
            }
        }

        #endregion

        private static string ResolveAccountName(string query)
        {
            var online = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.IsLoggedIn &&
                (p.Name.Equals(query, StringComparison.OrdinalIgnoreCase) || p.Account.Name.Equals(query, StringComparison.OrdinalIgnoreCase)));
            if (online != null) return online.Account.Name;

            var acc = TShock.UserAccounts.GetUserAccountByName(query);
            return acc?.Name ?? string.Empty;
        }

        private static string BoolText(bool v) => v ? "ON" : "OFF";

        private static void SendHouseHelp(TSPlayer player)
        {
            player.SendMessage("=== [ PANDUAN RP HOUSING ] ===", Color.Gold);
            player.SendInfoMessage("/house auto | /house claim - Beli kavling otomatis atau kavling tempat berdiri");
            player.SendInfoMessage("/myhouse (/house home [nomor]) - Teleport ke rumahmu | /visit <player/kode> - Kunjungi rumah lain");
            player.SendInfoMessage("/house list | /house top | /house like | /house border | /house info");
            player.SendInfoMessage("/house rename <nama> | /house desc <bio> | /house setspawn | /house lock | /house unlock");
            player.SendInfoMessage("/house trust/untrust <p> (Co-Owner) | /house member/unmember <p> (Builder) | /house kick/ban <p>");
            player.SendInfoMessage("/house flag <pvp|chest|interact|build|cozy> <on|off> | /house upgrade | /house sell <harga> | /house abandon confirm");
        }

        private static void SendAdminHelp(TSPlayer player)
        {
            player.SendMessage("=== [ ADMIN HOUSING (/ha) ] ===", Color.Yellow);
            player.SendInfoMessage("/ha grid <zona> <jumlah> [harga] [lebar] [tinggi] - Buat deretan kavling RP + Sign DIJUAL otomatis");
            player.SendInfoMessage("/ha pos1 [here] & /ha pos2 [here] -> /ha create <kode> <harga> - Buat kavling ukuran custom");
            player.SendInfoMessage("/ha linksign <kode> - Hubungkan Sign ke kavling | /ha setprice <kode> <harga>");
            player.SendInfoMessage("/ha evict <kode> confirm | /ha delete <kode> confirm");
        }
    }
}
