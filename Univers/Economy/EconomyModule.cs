// Original Skynomi Economy Core by Keyou (Univers Team)
using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Univers;
using Univers.Modules;
using Univers.Exp;
using Skynomi.Ranks;

namespace Skynomi.Economy
{
    public class EconomyModule : IModule, IDependent, IDisposable, IReloadable
    {
        public string Name => "Skynomi Economy Ledger";
        public string Description => "Modular digital currency with bank accounts, wallet limits, and player transactions";
        public Version Version => new Version(2, 0, 0);
        public string Author => "Univers Team";

        public IReadOnlyList<Type> RequiredModules => new[]
        {
            typeof(Univers.Utils.UtilsModule),
            typeof(Univers.Database.DatabaseModule)
        };

        public static EconomyModule Instance { get; private set; } = null!;
        public static EconomyConfig Config { get; private set; } = new EconomyConfig();
        public static EconomyDatabase Database { get; private set; } = null!;

        public void Initialize()
        {
            Instance = this;
            Config = EconomyConfig.Read();
            Database = new EconomyDatabase();

            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.NpcKilled.Register(UniversPlugin.Instance, OnNpcKilledDirect);

            EconomyCommands.Initialize();
            Log.Success("[Skynomi.Economy] Sistem Ekonomi Modular aktif!");
        }

        public void Dispose()
        {
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.NpcKilled.Deregister(UniversPlugin.Instance, OnNpcKilledDirect);
            Database?.Dispose();
        }

        public void Reload(ReloadEventArgs e)
        {
            Config = EconomyConfig.Read();
        }

        private void OnPlayerGreet(GreetPlayerEventArgs e)
        {
            var player = TShock.Players[e.Who];
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            Database.GetOrCreateAccount(player.Account.Name);
            EconomyApi.EnforceWalletCap(player);
        }

        private void OnNpcKilledDirect(NpcKilledEventArgs e)
        {
            if (e.npc == null || e.npc.friendly || e.npc.lifeMax < 1000) return;
            if (e.npc.boss || BossContributionTracker.IsBossOrPart(e.npc)) return;

            int killerId = e.npc.lastInteraction;
            if (killerId < 0 || killerId >= TShock.Players.Length) return;

            var player = TShock.Players[killerId];
            if (player != null && player.Active && player.IsLoggedIn)
            {
                double computedShard = e.npc.lifeMax / 1000.0;
                long shardToDatabase = (long)Math.Round(computedShard);

                EconomyApi.TryCredit(player.Account.Name, shardToDatabase, "Complex Monster Defeat Reward");

                string floatingText = $"+{computedShard.ToString("F2")} Shard";
                Color cosmicPurple = new Color(192, 132, 252);

                Random rnd = new Random();
                bool spawnLeft = rnd.Next(0, 2) == 0;

                float exactX = player.TPlayer.Center.X + (spawnLeft ? -64f : 42f);
                float exactY = player.TPlayer.Center.Y + 56f;

                player.SendData(
                    PacketTypes.CreateCombatTextExtended,
                    floatingText,
                    (int)cosmicPurple.PackedValue,
                    exactX,
                    exactY
                );

                try
                {
                    int earnedExp = e.npc.boss ? (int)(ExpModule.Config.MobExpMax * ExpModule.Config.BossExpMultiplier) : rnd.Next(ExpModule.Config.MobExpMin, ExpModule.Config.MobExpMax + 1);
                    var expData = ExpModule.Database.GetOrCreate(player.Account.Name);
                    ExpModule.AddExp(player, expData, earnedExp, false);
                }
                catch { }
            }
        }
    }
}
