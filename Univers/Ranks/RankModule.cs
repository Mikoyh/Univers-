// Original Skynomi Rank Core by Keyou (Univers Team)
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Univers;
using Univers.Modules;
using Skynomi.Economy;

namespace Skynomi.Ranks
{
    public class RankModule : IModule, IDependent, IDisposable, IReloadable
    {
        public string Name => "Rank Progression & Cosmic Economy";
        public string Description => "9 Cosmic-Themed ranks with playtime, boss tracker, item tier lock, and wallet cap";
        public Version Version => new Version(2, 0, 0);
        public string Author => "Univers Team";

        public IReadOnlyList<Type> RequiredModules => new[]
        {
            typeof(Univers.Utils.UtilsModule),
            typeof(Univers.Database.DatabaseModule),
            typeof(Univers.Exp.ExpModule),
            typeof(Skynomi.Economy.EconomyModule)
        };

        public static RankModule Instance { get; private set; } = null!;
        public static RankConfig Config { get; private set; } = new RankConfig();
        public static RankDatabase Database { get; private set; } = null!;

        private static System.Timers.Timer? _playtimeTimer;

        public void Initialize()
        {
            Instance = this;
            Config = RankConfig.Read();
            Database = new RankDatabase();

            ServerApi.Hooks.NpcKilled.Register(UniversPlugin.Instance, OnNpcKilled);
            ServerApi.Hooks.NetGetData.Register(UniversPlugin.Instance, OnGetData);
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.GamePostInitialize.Register(UniversPlugin.Instance, OnGamePostInitialize);
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;

            BossContributionTracker.Initialize();

            _playtimeTimer = new System.Timers.Timer(30000);
            _playtimeTimer.Elapsed += HeartbeatTick;
            _playtimeTimer.AutoReset = true;
            _playtimeTimer.Start();

            RankCommands.Initialize();

            Log.Success("[Skynomi.Ranks] Module Cosmic Rank Progression & Wallet Cap aktif untuk TShock 6.x (.NET 9.0)!");
        }

        public void Dispose()
        {
            BossContributionTracker.Dispose();

            ServerApi.Hooks.NpcKilled.Deregister(UniversPlugin.Instance, OnNpcKilled);
            ServerApi.Hooks.NetGetData.Deregister(UniversPlugin.Instance, OnGetData);
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.GamePostInitialize.Deregister(UniversPlugin.Instance, OnGamePostInitialize);
            PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;

            _playtimeTimer?.Stop();
            _playtimeTimer?.Dispose();

            Log.Info("[Skynomi.Ranks] Module Cosmic Rank Progression dibongkar (Disposed).");
        }

        public void Reload(ReloadEventArgs e)
        {
            Config = RankConfig.Read();
            EnsureBasicPermissionsForGroup(TShock.Groups.GetGroupByName("default"), "server reload");
            Log.Info("[Skynomi.Ranks] Konfigurasi Cosmic Rank Progression dimuat ulang.");
        }

        private void OnGamePostInitialize(EventArgs e)
        {
            EnsureBasicPermissionsForGroup(TShock.Groups.GetGroupByName("default"), "startup");
        }

        private void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
        {
            EnsurePlayerPermissionsAndProfile(e.Player);
        }

        private void OnPlayerGreet(GreetPlayerEventArgs e)
        {
            var player = TShock.Players[e.Who];
            EnsurePlayerPermissionsAndProfile(player);
        }

        private void EnsurePlayerPermissionsAndProfile(TSPlayer? player)
        {
            if (player == null || !player.Active || !player.IsLoggedIn || player.Account == null) return;

            var isNewPlayer = RankModule.Database.GetOrCreate(player.Account.Name);
            EnsureBasicPermissionsForGroup(player.Group, $"login {player.Name}");
            EnforceWalletCap(player);
        }

        private static void EnsureBasicPermissionsForGroup(Group? group, string contextLabel)
        {
            if (group == null || string.IsNullOrEmpty(group.Name)) return;

            var basicPermissions = new List<string>
            {
                "skynomi.rank.use",
                "skynomi.rank.up",
                "skynomi.eco.user",
                "univers.job.use",
                "univers.job",
                "univers.exp.use",
                "univers.element.use",
                "univers.element.awaken",
                "univers.element",
                "univers.shop.use",
                "univers.ah.use",
                "univers.mw.use",
                "univers.house.use",
                "univers.survival.use"
            };

            List<string> permsToAdd = new List<string>();
            foreach (var perm in basicPermissions)
            {
                if (!group.HasPermission(perm))
                {
                    permsToAdd.Add(perm);
                }
            }

            if (permsToAdd.Count > 0)
            {
                try
                {
                    TShock.Groups.AddPermissions(group.Name, permsToAdd);
                    TShock.Log.ConsoleInfo($"[Skynomi.Automation] Berhasil otomatis menyuntik {permsToAdd.Count} perizinan ke grup '{group.Name}' via {contextLabel}.");
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[Skynomi.Automation] Gagal menyuntik permission ke grup {group.Name}: {ex.Message}");
                }
            }
        }

        #region Playtime Tracker & Economy Heartbeat

        private void HeartbeatTick(object? sender, ElapsedEventArgs e)
        {
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active && player.IsLoggedIn)
                {
                    try
                    {
                        var data = Database.GetOrCreate(player.Account.Name);
                        data.PlaytimeSeconds += 30;
                        Database.Update(data);

                        if (Config.EnableWalletCap)
                        {
                            EnforceWalletCap(player);
                        }
                    }
                    catch { }
                }
            }
        }

        #endregion

        #region Skynomi Modular Economy Integration

        public static long GetPlayerBalance(TSPlayer player)
        {
            if (player == null || !player.IsLoggedIn) return 0;
            return EconomyApi.GetBalance(player.Account.Name);
        }

        public static bool TryDeductShards(TSPlayer player, long costShards, string reason = "RankUp")
        {
            if (player == null || !player.IsLoggedIn) return false;
            return EconomyApi.TryDebit(player.Account.Name, costShards, reason);
        }

        public static bool TryDeductCredits(TSPlayer player, long costCredits, string reason = "RankUp") => TryDeductShards(player, costCredits, reason);

        public static void EnforceWalletCap(TSPlayer player)
        {
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            var rankData = Database.GetOrCreate(player.Account.Name);
            var rank = Config.GetRankByIndex(rankData.RankIndex);
            long maxLimit = rank.MaxMoneyLimitShards;

            if (maxLimit <= 0) return;

            EconomyApi.SetWalletLimit(player.Account.Name, maxLimit);
            EconomyApi.EnforceWalletCap(player);
        }

        public static string FormatMoneyString(long shards)
        {
            return EconomyApi.FormatColored(shards);
        }

        #endregion

        #region Personal Boss Defeat Tracker & Anti-Carry Engine

        private void OnNpcKilled(NpcKilledEventArgs e)
        {
            var npc = e.npc;
            if (npc == null) return;
            BossContributionTracker.ProcessBossDefeat(npc);
        }

        public static string IdentifyBossKey(NPC npc) => BossContributionTracker.IdentifyBossKey(npc);

        #endregion

        #region Item Restriction & Tier Lock Engine

        private void OnGetData(GetDataEventArgs e)
        {
            if (!Config.EnableItemLock) return;

            var player = TShock.Players[e.Msg.whoAmI];
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            if (e.MsgID == PacketTypes.PlayerSlot)
            {
                using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                byte playerId = reader.ReadByte();
                short slot = reader.ReadInt16();
                short stack = reader.ReadInt16();
                byte prefix = reader.ReadByte();
                short netId = reader.ReadInt16();

                if (netId <= 0) return;

                int requiredTier = Config.GetItemTier(netId);
                var rankData = Database.GetOrCreate(player.Account.Name);
                var currentRank = Config.GetRankByIndex(rankData.RankIndex);

                if (requiredTier > currentRank.MaxItemTierAllowed)
                {
                    e.Handled = true;
                    player.SendData(PacketTypes.PlayerSlot, "", player.Index, slot);

                    var reqRank = Config.GetRankForTier(requiredTier);
                    player.SendErrorMessage($"[ X ITEM LOCKED ] Item ini adalah Tier {requiredTier}. Kamu butuh Rank '{reqRank.Name}' untuk memakainya!");
                    player.SendData(PacketTypes.CreateCombatTextExtended, $"LOCKED! Butuh {reqRank.Name}", (int)Color.Red.PackedValue, player.X, player.Y - 20);
                }
            }
        }

        #endregion
    }
}
