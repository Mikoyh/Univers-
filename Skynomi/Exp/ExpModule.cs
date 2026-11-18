using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Skynomi.Modules;
using Skynomi.Ranks; 

namespace Skynomi.Exp
{
    public class ExpModule : IModule, IDependent, IDisposable, IReloadable
    {
        public string Name => "Experience & Leveling";
        public string Description => "Exponential player leveling system with custom chat badge & monster/chat rewards";
        public Version Version => new Version(2, 0, 0);
        public string Author => "Keyou & Kimoy";

        public IReadOnlyList<Type> RequiredModules => new[]
        {
            typeof(Utils.UtilsModule),
            typeof(Skynomi.Database.DatabaseModule),
            typeof(Skynomi.Ranks.RankModule) 
        };

        public static ExpModule Instance { get; private set; } = null!;
        public static ExpConfig Config { get; private set; } = new ExpConfig();
        public static ExpDatabase Database { get; private set; } = null!;

        private static readonly Random Rnd = new Random();

        public void Initialize()
        {
            Instance = this;
            Config = ExpConfig.Read();
            Database = new ExpDatabase();

            ServerApi.Hooks.ServerChat.Register(SkynomiPlugin.Instance, OnServerChat);
            ServerApi.Hooks.NetGreetPlayer.Register(SkynomiPlugin.Instance, OnPlayerGreet);
            
            // [SUNTIK BARU] Register hook kematian NPC di sini
            ServerApi.Hooks.NpcKilled.Register(SkynomiPlugin.Instance, OnNpcKilled);

            ExpCommands.Initialize();
            Log.Success("[Skynomi.Exp] Module EXP & Leveling 1.4.5 aktif dengan Akumulasi HP!");
        }

        public void Dispose()
        {
            ServerApi.Hooks.ServerChat.Deregister(SkynomiPlugin.Instance, OnServerChat);
            ServerApi.Hooks.NetGreetPlayer.Deregister(SkynomiPlugin.Instance, OnPlayerGreet);
            
            // [SUNTIK BARU] Cabut hook pas dibongkar
            ServerApi.Hooks.NpcKilled.Deregister(SkynomiPlugin.Instance, OnNpcKilled);
        }

        public void Reload(ReloadEventArgs e)
        {
            Config = ExpConfig.Read();
        }

        private void OnPlayerGreet(GreetPlayerEventArgs e)
        {
            var player = TShock.Players[e.Who];
            if (player == null || !player.Active || !player.IsLoggedIn) return;
            Database.GetOrCreate(player.Account.Name);
        }

        // [SUNTIK BARU] Handler hitung EXP dari darah monster (1000 HP = 1 EXP)
        private void OnNpcKilled(NpcKilledEventArgs e)
        {
            var npc = e.npc;
            if (npc == null || npc.friendly || npc.lifeMax <= 5) return;

            int lastHitPlayerIndex = npc.lastInteraction; 
            if (lastHitPlayerIndex < 0 || lastHitPlayerIndex >= TShock.Players.Length) return;

            var player = TShock.Players[lastHitPlayerIndex];
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            // 1000 HP = 1 EXP. Mob darah 1 tetep dapet pecahan desimal yang dibulatkan keatas
            double calculatedExp = (double)npc.lifeMax / 1000.0;
            int finalExpReward = (int)Math.Ceiling(calculatedExp); 

            if (finalExpReward > 0)
            {
                var data = Database.GetOrCreate(player.Account.Name);
                AddExp(player, data, finalExpReward, true);
            }
        }

        private void OnServerChat(ServerChatEventArgs e)
        {
            if (e.Handled) return;

            var player = TShock.Players[e.Who];
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            string rawText = e.Text;
            
            if (string.IsNullOrWhiteSpace(rawText) || rawText.StartsWith("/") || rawText.StartsWith(".")) return;
            if (int.TryParse(rawText, out _) && rawText.Length <= 3) return; 

            var data = Database.GetOrCreate(player.Account.Name);

            TimeSpan timeSinceLast = DateTime.UtcNow - data.LastChatExp;
            if (timeSinceLast.TotalSeconds >= Config.ChatCooldownSeconds)
            {
                int earned = Rnd.Next(Config.ChatExpMin, Config.ChatExpMax + 1);
                data.LastChatExp = DateTime.UtcNow;
                data.MessagesSent++;
                AddExp(player, data, earned, false);
            }

            if (Config.EnableChatFormat)
            {
                e.Handled = true; 

                int badgeId = Config.GetBadgeIdForLevel(data.Level);
                string levelColor = Config.GetLevelColorForLevel(data.Level);
                string tshockPrefix = player.Group.Prefix ?? "";

                string rankPrefix = "Mortal"; 
                try
                {
                    var rankData = Skynomi.Ranks.RankModule.Database.GetOrCreate(player.Account.Name); 
                    if (rankData != null)
                    {
                        var rankInfo = Skynomi.Ranks.RankModule.Config.GetRankByIndex(rankData.RankIndex);
                        if (rankInfo != null && !string.IsNullOrEmpty(rankInfo.Name))
                        {
                            rankPrefix = rankInfo.Name;
                        }
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[Skynomi.Exp] Gagal menjemput kasta rank untuk {player.Account.Name}: {ex.Message}");
                    rankPrefix = "Mortal"; 
                }

                string fullPrefix = $"[{rankPrefix}] {tshockPrefix.Trim()}".Trim();

                string formatted = Config.ChatFormat
                    .Replace("{BadgeId}", badgeId.ToString())
                    .Replace("{Level}", data.Level.ToString())
                    .Replace("{LevelColor}", levelColor)
                    .Replace("{Prefix}", fullPrefix) 
                    .Replace("{PlayerName}", player.Name)
                    .Replace("{Message}", rawText);

                foreach (var p in TShock.Players)
                {
                    if (p != null && p.Active)
                        p.SendMessage(formatted, Color.White);
                }
            }
        }

        public static void AddExp(TSPlayer player, PlayerExpData data, int amount, bool notifyCombatText)
        {
            if (data.Level >= 1000)
            {
                data.Level = 1000;
                data.CurrentExp = 0;
                Database.Save(data);
                return;
            }

            int maxLevelCap = 30; 
            try
            {
                var rankData = Skynomi.Ranks.RankModule.Database.GetOrCreate(player.Account.Name);
                if (rankData != null)
                {
                    var rankInfo = Skynomi.Ranks.RankModule.Config.GetRankByIndex(rankData.RankIndex);
                    if (rankInfo != null)
                    {
                        maxLevelCap = rankInfo.MaxLevelCap;
                    }
                }
            }
            catch 
            {
                maxLevelCap = 30;
            }

            if (data.Level >= maxLevelCap)
            {
                long requiredExp = Config.CalculateRequiredExp(data.Level);
                data.CurrentExp = Math.Min(requiredExp - 1, data.CurrentExp + amount);
                data.TotalExp += amount;
                Database.Save(data);
                return;
            }

            data.CurrentExp += amount;
            data.TotalExp += amount;

            bool leveledUp = false;
            int oldLevel = data.Level;

            while (true)
            {
                if (data.Level >= maxLevelCap || data.Level >= 1000)
                {
                    if (data.Level >= 1000) data.Level = 1000;
                    break;
                }

                long req = Config.CalculateRequiredExp(data.Level);
                if (data.CurrentExp >= req)
                {
                    data.CurrentExp -= req;
                    data.Level++;
                    leveledUp = true;
                }
                else
                {
                    break;
                }
            }

            Database.Save(data);
            
            if (leveledUp && data.Level > oldLevel) 
            {
                OnLevelUp(player, data, oldLevel);
            }
        }

        private static void OnLevelUp(TSPlayer player, PlayerExpData data, int oldLevel)
        {
            string color = Config.GetLevelColorForLevel(data.Level);
            int badgeId = Config.GetBadgeIdForLevel(data.Level);

            player.SendMessage($"==================================================", Color.Gold);
            player.SendMessage($"[c/22d3ee:★ LEVEL UP!] Selamat {player.Name}! Kamu telah naik ke [c/{color}:Level {data.Level}]!", Color.Cyan);
            player.SendMessage($"==================================================", Color.Gold);

            if (Config.EnableLevelUpBroadcast)
            {
                TShock.Utils.Broadcast($"[c/fbbf24:★] Pemain {player.Name} baru saja mencapai [c/{color}:Level {data.Level}]!", Color.Orange);
            }
        }
    }
}
