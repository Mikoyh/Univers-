/*
 * ==============================================================================
 * BossContributionTracker.cs - Engine Kontribusi Boss & Anti-Gendong (Anti-Carry)
 * Target: Terraria 1.4.5 Series / TShock 6.x (.NET 9.0 / C# 13)
 * ==============================================================================
 * Fungsi:
 *  1. Mencatat akumulasi damage real-time setiap player ke Boss (termasuk multi-part).
 *  2. Menghitung persentase kontribusi damage saat Boss mati.
 *  3. Validasi batas minimal kontribusi (default 50%) untuk syarat /rankup.
 *     Player di bawah 50% berstatus "Gendong" -> TIDAK dicatat di DefeatedBosses.
 *  4. Membagikan reward Shards (Economy) dan EXP (Level) secara adil & proporsional.
 *  5. Memberikan bonus reward +20% Shards & +25% EXP untuk pemain peringkat 1 (MVP).
 *  6. Mengirim rekap pertempuran transparan berbasis teks ASCII bersih (no emoji).
 * ==============================================================================
 */

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using Skynomi.Economy;
using Univers.Exp;

namespace Skynomi.Ranks
{
    public class BossFightInstance
    {
        public string BossKey { get; set; } = string.Empty;
        public string BossDisplayName { get; set; } = string.Empty;
        public int PrimaryNpcType { get; set; } = 0;
        public int MaxLifeRecorded { get; set; } = 0;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime LastHitTime { get; set; } = DateTime.UtcNow;

        public ConcurrentDictionary<string, long> PlayerDamage { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, string> PlayerDisplayNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddDamage(string accountName, string playerName, long damage)
        {
            if (damage <= 0 || string.IsNullOrWhiteSpace(accountName)) return;
            PlayerDisplayNames[accountName] = playerName;
            PlayerDamage.AddOrUpdate(accountName, damage, (_, current) => current + damage);
            LastHitTime = DateTime.UtcNow;
        }

        public long GetTotalDamage()
        {
            return PlayerDamage.Values.Sum();
        }
    }

    public class ContributorData
    {
        public string AccountName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public long Damage { get; set; } = 0;
        public double Percentage { get; set; } = 0.0;
    }

    public static class BossContributionTracker
    {
        public static readonly ConcurrentDictionary<string, BossFightInstance> ActiveFights = new(StringComparer.OrdinalIgnoreCase);
        private static DateTime _lastCleanup = DateTime.UtcNow;

        public static void Initialize()
        {
            ServerApi.Hooks.NpcStrike.Register(UniversPlugin.Instance, OnNpcStrike);
            ServerApi.Hooks.GameUpdate.Register(UniversPlugin.Instance, OnGameUpdate);
        }

        public static void Dispose()
        {
            ServerApi.Hooks.NpcStrike.Deregister(UniversPlugin.Instance, OnNpcStrike);
            ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate);
            ActiveFights.Clear();
        }

        public static bool IsBossOrPart(NPC npc)
        {
            if (npc == null || !npc.active) return false;
            if (npc.boss) return true;
            string key = IdentifyBossKey(npc);
            return !string.IsNullOrEmpty(key);
        }

        public static string IdentifyBossKey(NPC npc)
        {
            if (npc == null) return string.Empty;

            if (npc.type == NPCID.HallowBoss && Main.dayTime)
            {
                return "DaytimeEmpressOfLight";
            }

            return npc.type switch
            {
                NPCID.KingSlime => "KingSlime",
                NPCID.EyeofCthulhu => "EyeOfCthulhu",
                NPCID.EaterofWorldsHead or NPCID.EaterofWorldsBody or NPCID.EaterofWorldsTail => "EaterOfWorldsOrBrain",
                NPCID.BrainofCthulhu or NPCID.Creeper => "EaterOfWorldsOrBrain",
                NPCID.QueenBee => "QueenBee",
                NPCID.SkeletronHead or NPCID.SkeletronHand => "Skeletron",
                NPCID.Deerclops => "Deerclops",
                NPCID.WallofFlesh or NPCID.WallofFleshEye => "WallOfFlesh",
                NPCID.QueenSlimeBoss => "QueenSlime",
                NPCID.Retinazer or NPCID.Spazmatism => "TheTwins",
                NPCID.TheDestroyer or NPCID.TheDestroyerBody or NPCID.TheDestroyerTail => "TheDestroyer",
                NPCID.SkeletronPrime or NPCID.PrimeCannon or NPCID.PrimeSaw or NPCID.PrimeVice or NPCID.PrimeLaser => "SkeletronPrime",
                NPCID.Plantera or NPCID.PlanterasHook or NPCID.PlanterasTentacle => "Plantera",
                NPCID.Golem or NPCID.GolemHead or NPCID.GolemFistLeft or NPCID.GolemFistRight => "Golem",
                NPCID.DukeFishron => "DukeFishron",
                NPCID.Pumpking or NPCID.PumpkingBlade => "Pumpking",
                NPCID.MourningWood => "MourningWood",
                NPCID.IceQueen => "IceQueen",
                NPCID.MartianSaucer or NPCID.MartianSaucerCore or NPCID.MartianSaucerTurret or NPCID.MartianSaucerCannon => "MartianSaucer",
                NPCID.DD2Betsy => "Betsy",
                NPCID.LunarTowerSolar or NPCID.LunarTowerVortex or NPCID.LunarTowerNebula or NPCID.LunarTowerStardust => "CelestialPillars",
                NPCID.HallowBoss => "EmpressOfLight",
                NPCID.CultistBoss => "LunaticCultist",
                NPCID.MoonLordCore or NPCID.MoonLordHead or NPCID.MoonLordHand => "MoonLord",
                _ => npc.boss ? npc.FullName.Replace(" ", "") : string.Empty
            };
        }

        public static string GetBossDisplayName(string bossKey, NPC npc)
        {
            return bossKey switch
            {
                "KingSlime" => "King Slime",
                "EyeOfCthulhu" => "Eye of Cthulhu",
                "EaterOfWorldsOrBrain" => (npc.type == NPCID.BrainofCthulhu || npc.type == NPCID.Creeper) ? "Brain of Cthulhu" : "Eater of Worlds",
                "QueenBee" => "Queen Bee",
                "Skeletron" => "Skeletron",
                "Deerclops" => "Deerclops",
                "WallOfFlesh" => "Wall of Flesh",
                "QueenSlime" => "Queen Slime",
                "TheTwins" => "The Twins",
                "TheDestroyer" => "The Destroyer",
                "SkeletronPrime" => "Skeletron Prime",
                "Plantera" => "Plantera",
                "Golem" => "Golem",
                "DukeFishron" => "Duke Fishron",
                "Pumpking" => "Pumpking",
                "MourningWood" => "Mourning Wood",
                "IceQueen" => "Ice Queen",
                "MartianSaucer" => "Martian Saucer",
                "Betsy" => "Betsy",
                "CelestialPillars" => npc.FullName,
                "EmpressOfLight" => "Empress of Light",
                "DaytimeEmpressOfLight" => "Daytime Empress of Light",
                "LunaticCultist" => "Lunatic Cultist",
                "MoonLord" => "Moon Lord",
                _ => string.IsNullOrEmpty(npc.FullName) ? bossKey : npc.FullName
            };
        }

        public static bool IsBossDefeatNpc(NPC npc)
        {
            if (npc == null) return false;

            // Eater of Worlds: defeated when all parts are dead
            if (npc.type == NPCID.EaterofWorldsHead || npc.type == NPCID.EaterofWorldsBody || npc.type == NPCID.EaterofWorldsTail)
            {
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    var other = Main.npc[i];
                    if (other != null && other.active && other.whoAmI != npc.whoAmI &&
                        (other.type == NPCID.EaterofWorldsHead || other.type == NPCID.EaterofWorldsBody || other.type == NPCID.EaterofWorldsTail))
                    {
                        return false;
                    }
                }
                return true;
            }

            // The Twins: defeated when both Retinazer and Spazmatism are dead
            if (npc.type == NPCID.Retinazer || npc.type == NPCID.Spazmatism)
            {
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    var other = Main.npc[i];
                    if (other != null && other.active && other.whoAmI != npc.whoAmI &&
                        (other.type == NPCID.Retinazer || other.type == NPCID.Spazmatism))
                    {
                        return false;
                    }
                }
                return true;
            }

            return npc.type switch
            {
                NPCID.KingSlime => true,
                NPCID.EyeofCthulhu => true,
                NPCID.BrainofCthulhu => true,
                NPCID.QueenBee => true,
                NPCID.SkeletronHead => true,
                NPCID.Deerclops => true,
                NPCID.WallofFlesh => true,
                NPCID.QueenSlimeBoss => true,
                NPCID.TheDestroyer => true,
                NPCID.SkeletronPrime => true,
                NPCID.Plantera => true,
                NPCID.Golem => true,
                NPCID.DukeFishron => true,
                NPCID.Pumpking => true,
                NPCID.MourningWood => true,
                NPCID.IceQueen => true,
                NPCID.MartianSaucerCore => true,
                NPCID.DD2Betsy => true,
                NPCID.LunarTowerSolar or NPCID.LunarTowerVortex or NPCID.LunarTowerNebula or NPCID.LunarTowerStardust => true,
                NPCID.HallowBoss => true,
                NPCID.CultistBoss => true,
                NPCID.MoonLordCore => true,
                _ => npc.boss
            };
        }

        private static void OnNpcStrike(NpcStrikeEventArgs args)
        {
            if (args.Handled || args.Player == null || !args.Player.active) return;

            var player = TShock.Players[args.Player.whoAmI];
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            var npc = args.Npc;
            if (npc == null || !npc.active || npc.friendly || npc.lifeMax <= 5) return;

            string bossKey = IdentifyBossKey(npc);
            if (string.IsNullOrEmpty(bossKey)) return;

            // Clamp damage to remaining NPC health to prevent overkill score inflation
            long damageDealt = Math.Max(1, Math.Min(args.Damage, npc.life));

            var fight = ActiveFights.GetOrAdd(bossKey, key => new BossFightInstance
            {
                BossKey = key,
                BossDisplayName = GetBossDisplayName(key, npc),
                PrimaryNpcType = npc.type,
                MaxLifeRecorded = npc.lifeMax
            });

            fight.MaxLifeRecorded = Math.Max(fight.MaxLifeRecorded, npc.lifeMax);
            fight.AddDamage(player.Account.Name, player.Name, damageDealt);
        }

        private static void OnGameUpdate(EventArgs args)
        {
            if ((DateTime.UtcNow - _lastCleanup).TotalSeconds < 4) return;
            _lastCleanup = DateTime.UtcNow;

            // Despawn check: clean up fights whose boss is no longer active in the world
            foreach (var kvp in ActiveFights.ToArray())
            {
                string bossKey = kvp.Key;
                var fight = kvp.Value;

                bool hasLivingNpc = false;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    var npc = Main.npc[i];
                    if (npc != null && npc.active && IdentifyBossKey(npc).Equals(bossKey, StringComparison.OrdinalIgnoreCase))
                    {
                        hasLivingNpc = true;
                        break;
                    }
                }

                if (!hasLivingNpc && (DateTime.UtcNow - fight.LastHitTime).TotalSeconds > 8)
                {
                    ActiveFights.TryRemove(bossKey, out _);
                }
            }
        }

        public static void ProcessBossDefeat(NPC npc)
        {
            if (npc == null) return;
            string bossKey = IdentifyBossKey(npc);
            if (string.IsNullOrEmpty(bossKey)) return;

            if (!IsBossDefeatNpc(npc)) return;

            if (!ActiveFights.TryRemove(bossKey, out var fight))
            {
                return;
            }

            long totalDamage = fight.GetTotalDamage();
            if (totalDamage <= 0) return;

            string bossName = fight.BossDisplayName;
            double minPercent = RankModule.Config.MinBossContributionPercent; // Default 50.0%

            var contributors = fight.PlayerDamage
                .Select(kv => new ContributorData
                {
                    AccountName = kv.Key,
                    DisplayName = fight.PlayerDisplayNames.TryGetValue(kv.Key, out var name) ? name : kv.Key,
                    Damage = kv.Value,
                    Percentage = (double)kv.Value / totalDamage * 100.0
                })
                .OrderByDescending(c => c.Damage)
                .ToList();

            if (contributors.Count == 0) return;

            var mvp = contributors[0];

            int baseLife = Math.Max(1000, fight.MaxLifeRecorded);
            double totalBaseShards = Math.Max(10.0, (baseLife / 1000.0) * 15.0);
            int totalBaseExp = Math.Max(150, (int)Math.Ceiling(Math.Sqrt(baseLife) * 4.0 + (baseLife / 30.0)));

            var recapLines = new List<string>
            {
                $"=== [ BOSS DEFEATED: {bossName.ToUpperInvariant()} ] ===",
                $"Total Damage: {totalDamage:N0} DMG | Peserta: {contributors.Count} Pemain"
            };

            foreach (var c in contributors)
            {
                bool isMvp = (c == mvp);
                bool isQualified = c.Percentage >= minPercent;

                double shardShare = totalBaseShards * (c.Percentage / 100.0);
                int expShare = (int)Math.Round(totalBaseExp * (c.Percentage / 100.0));

                if (isMvp)
                {
                    shardShare *= (1.0 + (RankModule.Config.BossMvpShardBonusPercent / 100.0));
                    expShare = (int)Math.Round(expShare * (1.0 + (RankModule.Config.BossMvpExpBonusPercent / 100.0)));
                }

                long awardedShards = Math.Max(1, (long)Math.Round(shardShare));
                int awardedExp = Math.Max(10, expShare);

                var player = TShock.Players.FirstOrDefault(p =>
                    p != null && p.Active && p.IsLoggedIn &&
                    p.Account.Name.Equals(c.AccountName, StringComparison.OrdinalIgnoreCase));

                if (player != null)
                {
                    EconomyApi.TryCredit(player.Account.Name, awardedShards, $"Boss Defeat: {bossName}");

                    var expData = ExpModule.Database.GetOrCreate(player.Account.Name);
                    expData.MobsKilled++;
                    expData.BossesKilled++;
                    ExpModule.AddExp(player, expData, awardedExp, true);

                    string mvpTag = isMvp ? "[ MVP ] " : "";
                    Color cColor = isMvp ? Color.Gold : (isQualified ? Color.LightGreen : Color.Orange);
                    player.SendData(
                        PacketTypes.CreateCombatTextExtended,
                        $"{mvpTag}+{awardedShards} Shard ({c.Percentage:F1}%)",
                        (int)cColor.PackedValue,
                        player.X,
                        player.Y - 20
                    );

                    if (isQualified)
                    {
                        var rankData = RankModule.Database.GetOrCreate(player.Account.Name);
                        if (!rankData.DefeatedBosses.Contains(bossKey))
                        {
                            rankData.DefeatedBosses.Add(bossKey);
                            RankModule.Database.Update(rankData);
                            player.SendSuccessMessage($"[ + PROGRES PERSONAL ] Kamu telah menumbangkan {bossName}! Kontribusi: {c.Percentage:F1}% (>= {minPercent:F0}%). Tercatat di /rankup!");
                        }
                        else
                        {
                            player.SendInfoMessage($"[ OK ] Kontribusi ke {bossName}: {c.Percentage:F1}%. Hadiah berhasil diterima!");
                        }
                    }
                    else
                    {
                        player.SendErrorMessage($"[ NOTICE ] Kontribusi ke {bossName} hanya {c.Percentage:F1}% (< {minPercent:F0}%). Tidak memenuhi syarat /rankup (Status: Gendong/Terbawa). Tingkatkan damagemu!");
                    }
                }
                else
                {
                    EconomyApi.TryCredit(c.AccountName, awardedShards, $"Boss Defeat: {bossName}");
                    if (isQualified)
                    {
                        var rankData = RankModule.Database.GetOrCreate(c.AccountName);
                        if (!rankData.DefeatedBosses.Contains(bossKey))
                        {
                            rankData.DefeatedBosses.Add(bossKey);
                            RankModule.Database.Update(rankData);
                        }
                    }
                }

                string statusBadge = isQualified ? "[ QUALIFIED ]" : $"[ < {minPercent:F0}% GENDONG ]";
                string roleTag = isMvp ? "[ MVP ]" : "[ - ]";
                recapLines.Add($"{roleTag} {c.DisplayName}: {c.Damage:N0} DMG ({c.Percentage:F1}%) -> +{awardedShards} Shards, +{awardedExp} EXP {statusBadge}");
            }

            if (RankModule.Config.EnableBossContributionRecap)
            {
                foreach (var line in recapLines)
                {
                    TSPlayer.All.SendMessage(line, Color.Cyan);
                }
            }
        }
    }
}
