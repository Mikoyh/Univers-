using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Univers;
using Univers.Modules;
using Skynomi.Ranks;

namespace Univers.Exp
{
    public class ExpModule : IModule, IDependent, IDisposable, IReloadable
    {
        public string Name => "Experience & Leveling";
        public string Description => "Exponential player leveling system with custom chat badge & monster/chat rewards";
        public Version Version => new Version(2, 0, 0);
        public string Author => "Univers Team";

        public IReadOnlyList<Type> RequiredModules => new[]
        {
            typeof(Univers.Utils.UtilsModule),
            typeof(Univers.Database.DatabaseModule),
            typeof(RankModule)
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

            ServerApi.Hooks.ServerChat.Register(UniversPlugin.Instance, OnServerChat);
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.NpcKilled.Register(UniversPlugin.Instance, OnNpcKilled);

            ExpCommands.Initialize();
            Log.Success("[Univers.Exp] Module EXP & Leveling 1.4.5 aktif!");
        }

        public void Dispose()
        {
            ServerApi.Hooks.ServerChat.Deregister(UniversPlugin.Instance, OnServerChat);
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.NpcKilled.Deregister(UniversPlugin.Instance, OnNpcKilled);
            Database?.Dispose();
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

        private void OnNpcKilled(NpcKilledEventArgs e)
        {
            var npc = e.npc;
            if (npc == null || npc.friendly || npc.lifeMax <= 5) return;
            if (npc.boss || BossContributionTracker.IsBossOrPart(npc)) return;

            int lastHitPlayerIndex = npc.lastInteraction;
            if (lastHitPlayerIndex < 0 || lastHitPlayerIndex >= TShock.Players.Length) return;

            var player = TShock.Players[lastHitPlayerIndex];
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            int finalExpReward;
            if (npc.boss)
            {
                int bossScaled = (int)Math.Ceiling((Math.Sqrt(npc.lifeMax) * 3.0) + (npc.lifeMax / 40.0));
                finalExpReward = Math.Max((int)Math.Round(Config.BossExpMultiplier * 4.0), bossScaled);
            }
            else
            {
                int minMob = Math.Min(Config.MobExpMin, Config.MobExpMax);
                int maxMob = Math.Max(Config.MobExpMin, Config.MobExpMax);
                int baseMob = Rnd.Next(minMob, maxMob + 1);
                int hpBonus = (int)Math.Floor((Math.Sqrt(npc.lifeMax) * 0.8) + (npc.lifeMax / 60.0));
                finalExpReward = Math.Max(1, baseMob + hpBonus);
            }

            if (finalExpReward > 0)
            {
                var data = Database.GetOrCreate(player.Account.Name);
                data.MobsKilled++;
                if (npc.boss) data.BossesKilled++;
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
                string tshockPrefix = player.Group?.Prefix ?? "";

                string rankPrefix = "[c/E2E8F0:[][c/CCD4E0:m][c/B5C1D0:o][c/9FADC0:r][c/8D9CB2:t][c/7F8FA5:a][c/728198:l][c/64748B:]]";
                try
                {
                    var rankData = RankModule.Database?.GetOrCreate(player.Account.Name);
                    if (rankData != null)
                    {
                        var rankInfo = RankModule.Config?.GetRankByIndex(rankData.RankIndex);
                        if (rankInfo != null)
                        {
                            if (!string.IsNullOrWhiteSpace(rankInfo.Prefix))
                            {
                                rankPrefix = rankInfo.Prefix.Trim();
                            }
                            else if (!string.IsNullOrWhiteSpace(rankInfo.Name))
                            {
                                rankPrefix = $"[{rankInfo.Name}]";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[Univers.Exp] Gagal menjemput prefix rank untuk {player.Account.Name}: {ex.Message}");
                }

                string fullPrefix = string.IsNullOrWhiteSpace(tshockPrefix)
                    ? rankPrefix
                    : $"{rankPrefix} {tshockPrefix.Trim()}";

                string safeTemplate = Config.ChatFormat
                    .Replace("[c/{LevelColor}:{Prefix}]", "{Prefix}")
                    .Replace("[c/{LevelColor}:[{Level}][c/{LevelColor}:]]", "[c/{LevelColor}:[][c/{LevelColor}:{Level}][c/{LevelColor}:]]")
                    .Replace("[c/{LevelColor}:Lv.{Level}]", "[c/{LevelColor}:[][c/{LevelColor}:{Level}][c/{LevelColor}:]]")
                    .Replace("Lv.{Level}", "{Level}");

                string displayMessage = Univers.Chat.ChatModule.ProcessMentionsInMessage(player, rawText);

                string formattedPrefix = safeTemplate
                    .Replace("{BadgeId}", badgeId.ToString())
                    .Replace("{Level}", data.Level.ToString())
                    .Replace("{LevelColor}", levelColor)
                    .Replace("{Prefix}", fullPrefix)
                    .Replace("{PlayerName}", player.Name);

                string formatted = FormatChatMessageBody(formattedPrefix, displayMessage);

                if (Univers.MultiWorld.MultiWorldModule.IsCrossWorldChatActive)
                {
                    Univers.MultiWorld.MultiWorldModule.HandleLocalFormattedChat(player, formatted);
                }
                else
                {
                    foreach (var p in TShock.Players)
                    {
                        if (p != null && p.Active)
                            p.SendMessage(formatted, Color.White);
                    }
                }
            }
        }

        private static string FormatChatMessageBody(string templateWithMsgPlaceholder, string displayMessage)
        {
            var outerMatch = System.Text.RegularExpressions.Regex.Match(
                templateWithMsgPlaceholder,
                @"\[c/([0-9a-fA-F]{6}):\{Message\}\]");

            if (!outerMatch.Success)
            {
                return templateWithMsgPlaceholder.Replace("{Message}", displayMessage);
            }

            string outerHex = outerMatch.Groups[1].Value;
            string rebuilt = WrapPlainTextWithColor(displayMessage, outerHex);
            return templateWithMsgPlaceholder.Substring(0, outerMatch.Index)
                + rebuilt
                + templateWithMsgPlaceholder.Substring(outerMatch.Index + outerMatch.Length);
        }

        private static string WrapPlainTextWithColor(string text, string hexColor)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var tagRegex = new System.Text.RegularExpressions.Regex(@"(\[[a-zA-Z]{1,10}(?:/[^:\]]+)?:[^\]]*\])");
            var matches = tagRegex.Matches(text);
            if (matches.Count == 0)
            {
                return $"[c/{hexColor}:{text}]";
            }

            var sb = new System.Text.StringBuilder();
            int cursor = 0;
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (m.Index > cursor)
                {
                    string plain = text.Substring(cursor, m.Index - cursor);
                    if (!string.IsNullOrEmpty(plain))
                    {
                        sb.Append($"[c/{hexColor}:{plain}]");
                    }
                }
                sb.Append(m.Value);
                cursor = m.Index + m.Length;
            }
            if (cursor < text.Length)
            {
                string tail = text.Substring(cursor);
                if (!string.IsNullOrEmpty(tail))
                {
                    sb.Append($"[c/{hexColor}:{tail}]");
                }
            }
            return sb.ToString();
        }

        public static void AddExp(TSPlayer player, PlayerExpData data, int amount, bool notifyCombatText, bool applyRankMultiplier = true)
        {
            if (player == null || !player.Active || !player.IsLoggedIn || amount <= 0) return;

            if (data.Level >= 1000)
            {
                data.Level = 1000;
                data.CurrentExp = 0;
                Database.Save(data);
                return;
            }

            int maxLevelCap = 30;
            double rankExpMultiplier = 1.0;
            try
            {
                var rankData = RankModule.Database.GetOrCreate(player.Account.Name);
                if (rankData != null)
                {
                    var rankInfo = RankModule.Config.GetRankByIndex(rankData.RankIndex);
                    if (rankInfo != null)
                    {
                        maxLevelCap = rankInfo.MaxLevelCap;
                        if (rankInfo.ExpMultiplier > 1.0)
                        {
                            rankExpMultiplier = rankInfo.ExpMultiplier;
                        }
                    }
                }
            }
            catch
            {
                maxLevelCap = 30;
                rankExpMultiplier = 1.0;
            }

            int effectiveAmount = applyRankMultiplier
                ? Math.Max(1, (int)Math.Round(amount * rankExpMultiplier))
                : amount;

            if (data.Level >= maxLevelCap)
            {
                long requiredExp = Config.CalculateRequiredExp(data.Level);
                data.CurrentExp = Math.Min(requiredExp - 1, data.CurrentExp + effectiveAmount);
                data.TotalExp += effectiveAmount;
                Database.Save(data);
                return;
            }

            data.CurrentExp += effectiveAmount;
            data.TotalExp += effectiveAmount;

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
            if (player == null || !player.Active || player.TPlayer == null) return;

            string color = Config.GetLevelColorForLevel(data.Level);

            player.SendMessage($"[c/22d3ee:[ LEVEL UP ]] Lv.{oldLevel} -> [c/{color}:Lv.{data.Level}]", Color.Cyan);

            player.SendData(
                PacketTypes.CreateCombatTextExtended,
                $"LEVEL UP! Lv.{data.Level}",
                (int)Color.Cyan.PackedValue,
                player.X,
                player.Y - 24
            );

            if (Config.EnableLevelUpFireworks)
            {
                SpawnLevelUpFireworks(player);
            }

            // Panggil event bridge agar AetherialModule & modul lain langsung tersinkron
            ExpApi.InvokeLevelUp(player, player.Account.Name, oldLevel, data.Level, data.TotalExp);
            ExpApi.InvokeLevelChanged(player.Account.Name, data.Level, false);
        }

        private static void SpawnLevelUpFireworks(TSPlayer player)
        {
            if (player == null || !player.Active || player.TPlayer == null) return;

            // Efek sederhana: 2 semburan confetti ringan di atas kepala (tanpa roket ledakan)
            Vector2 origin = player.TPlayer.Top;
            SpawnCelebrationProjectile(origin, new Vector2(-1.8f, -4.5f), ProjectileID.ConfettiGun);
            SpawnCelebrationProjectile(origin, new Vector2(1.8f, -4.5f), ProjectileID.ConfettiGun);
        }

        private static void SpawnCelebrationProjectile(Vector2 pos, Vector2 vel, int projId, int? timeLeftOverride = null)
        {
            int idx = Projectile.NewProjectile(Projectile.GetNoneSource(), pos, vel, projId, 0, 0f, Main.myPlayer);
            if (idx >= 0 && idx < Main.maxProjectiles)
            {
                if (timeLeftOverride.HasValue)
                {
                    Main.projectile[idx].timeLeft = timeLeftOverride.Value;
                }
                NetMessage.SendData((int)PacketTypes.ProjectileNew, -1, -1, null, idx);
            }
        }
    }
}
