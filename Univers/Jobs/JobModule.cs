/*  
 * =====================================================================  
 *  UNIVERS - JOBS & VOCATION SYSTEM (MODULAR COMPONENT)  
 *  Platform : TShock 6.x / Terraria 1.4.5 / .NET 9.0 (C# 13)  
 * =====================================================================  
 */  
using System; 
using System.IO; 
using System.Linq; 
using System.Timers; 
using System.Collections.Generic; 
using System.Collections.Concurrent; 
using Microsoft.Xna.Framework; 
using Terraria; 
using Terraria.ID; 
using TerrariaApi.Server; 
using TShockAPI; 
using Univers; 
using Univers.Exp; 
using Skynomi.Economy; 
using Skynomi.Ranks; 

namespace Univers.Jobs  
{  
    public class JobPlayerState  
    {  
        public TSPlayer Player { get; set; }  
        public DateTime LastReminderTime { get; set; } = DateTime.MinValue;  
        public JobPlayerState(TSPlayer player)  
        {  
            Player = player;  
        }  
    }  

    public static class JobModule  
    {  
        public static JobConfig Config { get; private set; } = new();  
        public static JobDatabase Database { get; set; } = null!;  
        public static ConcurrentDictionary<int, JobPlayerState> ActiveStates { get; } = new();  
        private static readonly ConcurrentDictionary<long, byte> PlacedTiles = new();  
        private const int MaxTrackedPlacedTiles = 250000;  
        private static System.Timers.Timer _cooldownHeartbeatTimer = null!;  

        public static void Initialize(string savePath)  
        {  
            string configDir = Path.Combine(savePath, "Univers");  
            Directory.CreateDirectory(configDir);  
            Config = JobConfig.Load(Path.Combine(configDir, "JobConfig.json"));  
            Database = new JobDatabase(Path.Combine(configDir, "univers_jobs.db"));  

            GetDataHandlers.TileEdit += OnTileEdit;  
            ServerApi.Hooks.NpcKilled.Register(UniversPlugin.Instance, OnNpcKilled);  
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnPlayerGreet);  

            _cooldownHeartbeatTimer = new System.Timers.Timer(10000);  
            _cooldownHeartbeatTimer.Elapsed += CheckShiftCooldownAlerts;  
            _cooldownHeartbeatTimer.AutoReset = true;  
            _cooldownHeartbeatTimer.Start();  

            JobCommands.Register();  
            TShock.Log.ConsoleInfo("[Univers.Jobs] Advanced Quota & Cooldown Jobs v2.0 loaded on .NET 9.0!");  
        }  

        public static void Dispose()  
        {  
            if (_cooldownHeartbeatTimer != null)  
            {  
                _cooldownHeartbeatTimer.Stop();  
                _cooldownHeartbeatTimer.Dispose();  
            }  
            GetDataHandlers.TileEdit -= OnTileEdit;  
            ServerApi.Hooks.NpcKilled.Deregister(UniversPlugin.Instance, OnNpcKilled);  
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnPlayerGreet);  
            PlacedTiles.Clear();  
            ActiveStates.Clear();  
            Database?.Dispose();  
        }  

        private static void OnPlayerGreet(GreetPlayerEventArgs args)  
        {  
            var player = TShock.Players[args.Who];  
            if (player == null || !player.Active) return;  
            if (Univers.MultiWorld.MultiWorldModule.IsPlayerTransferring(player)) return;  

            System.Threading.Tasks.Task.Delay(3500).ContinueWith(_ =>  
            {  
                if (player == null || !player.Active || !player.IsLoggedIn) return;  
                var profile = Database.GetOrCreateProfile(player.Account.Name);  
                if (profile == null) return;  
                if (profile.ActiveJobs.Count == 0)  
                {  
                    player.SendInfoMessage("[i:73] [c/38bdf8:=== UNIVERS JOBS SYSTEM ===]");  
                    player.SendInfoMessage("[c/f59e0b:Halo " + player.Name + "! Kamu belum memilih pekerjaan server!]");  
                    player.SendInfoMessage("[c/94a3b8:Ketik ][c/4ade80:/job list][c/94a3b8: untuk memilih profesi (Miner, Fisher, Hunter) dan dapatkan upah Shard serta EXP!]");  
                }  
                else  
                {  
                    foreach (var jobId in profile.ActiveJobs)  
                    {  
                        var shift = profile.GetOrCreateShift(jobId);  
                        if (shift.IsOnCooldown)  
                        {  
                            var rem = shift.ShiftCooldownUntil - DateTime.UtcNow;  
                            player.SendInfoMessage($"[i:73] [c/38bdf8:UNIVERS] Pekerjaan [c/f59e0b:{jobId.ToUpper()}]: Sedang istirahat ({rem.Minutes}m {rem.Seconds}s lagi).");  
                        }  
                        else  
                        {  
                            player.SendSuccessMessage($"[i:73] [c/4ade80:UNIVERS] Pekerjaan [c/f59e0b:{jobId.ToUpper()}]: SHIFT SIAP DIKERJAKAN! Target: {shift.CurrentCount}/{shift.RequiredCount} {shift.TargetName}. Ketik /jobs untuk info.");  
                        }  
                    }  
                }  
            });  
        }  

        private static void CheckShiftCooldownAlerts(object? sender, ElapsedEventArgs e)  
        {  
            foreach (var player in TShock.Players.Where(p => p != null && p.Active && p.IsLoggedIn))  
            {  
                var profile = Database.GetOrCreateProfile(player.Account.Name);  
                if (profile == null || profile.ActiveJobs.Count == 0) continue;  
                bool profileUpdated = false;  
                foreach (var jobId in profile.ActiveJobs)  
                {  
                    if (profile.ActiveShifts.TryGetValue(jobId, out var shift))  
                    {  
                        if (!shift.IsOnCooldown && shift.NeedsCooldownAlert)  
                        {  
                            shift.NeedsCooldownAlert = false;  
                            profileUpdated = true;  
                            player.SendSuccessMessage($"[i:73] [c/4ade80:UNIVERS JOBS] Waktu istirahat selesai! Pekerjaan [c/f59e0b:{jobId.ToUpper()}] kini siap dikerjakan kembali!");  
                            player.SendInfoMessage($"[c/38bdf8:Target Baru:] {shift.TargetName} ({shift.RequiredCount}x) | Upah: {FormatCoinsShort(shift.RewardCoins)} + {shift.RewardExp} EXP. Ketik /jobs untuk detail.");  
                        }  
                    }  
                }  
                if (profileUpdated)  
                {  
                    Database.SaveProfile(profile);  
                }  
            }  
        }  

        private static void OnTileEdit(object? sender, GetDataHandlers.TileEditEventArgs e)  
        {  
            if (e.Handled || e.Player == null || !e.Player.IsLoggedIn) return;  
            long tileCoordKey = ((long)e.X << 32) | (uint)e.Y;  

            if (e.Action == GetDataHandlers.EditAction.PlaceTile)  
            {  
                if (PlacedTiles.Count > MaxTrackedPlacedTiles)  
                    PlacedTiles.Clear();  
                PlacedTiles.TryAdd(tileCoordKey, 1);  
                return;  
            }  

            if (e.Action == GetDataHandlers.EditAction.KillTile || e.Action == GetDataHandlers.EditAction.KillTileNoItem)  
            {  
                if (PlacedTiles.TryRemove(tileCoordKey, out _))  
                {  
                    return;  
                }  
                ITile tile = Main.tile[e.X, e.Y];  
                if (tile == null) return;  
                int tileType = tile.type;  
                var profile = Database.GetOrCreateProfile(e.Player.Account.Name);  
                if (profile == null) return;  

                if (profile.HasJob("miner"))  
                {  
                    ProcessShiftProgress(e.Player, profile, "miner", tileType);  
                }  
                if (profile.HasJob("lumberjack"))  
                {  
                    ProcessShiftProgress(e.Player, profile, "lumberjack", tileType);  
                }  
                if (profile.HasJob("farmer"))  
                {  
                    ProcessShiftProgress(e.Player, profile, "farmer", tileType);  
                }  
            }  
        }  

        public static void OnPlayerCaughtFish(TSPlayer player, int itemId, int stack)  
        {             
            if (player == null || !player.IsLoggedIn) return;  
            var profile = Database.GetOrCreateProfile(player.Account.Name);  
            if (profile == null || !profile.HasJob("fisher")) return;  
            for (int i = 0; i < stack; i++)  
            {  
                ProcessShiftProgress(player, profile, "fisher", itemId);  
            }  
        }  

        private static void OnNpcKilled(NpcKilledEventArgs args)
        {
            NPC npc = args.npc;
            if (npc == null || npc.friendly || npc.lifeMax <= 5) return;

            int lastHitPlayerIndex = npc.lastInteraction;
            if (lastHitPlayerIndex < 0 || lastHitPlayerIndex >= TShock.Players.Length) return;

            TSPlayer killer = TShock.Players[lastHitPlayerIndex];
            if (killer == null || !killer.Active || !killer.IsLoggedIn) return;

            var profile = Database.GetOrCreateProfile(killer.Account.Name);
            if (profile == null) return;

            if (profile.HasJob("hunter"))
            {
                ProcessShiftProgress(killer, profile, "hunter", npc.netID);
            }
        }

        private static void ProcessShiftProgress(TSPlayer player, JobUserProfile profile, string jobId, int actionTargetId)
        {
            var shift = profile.GetOrCreateShift(jobId);

            if (shift.IsOnCooldown)
            {
                return;
            }

            if (!IsTargetMatching(jobId, shift.TargetId, actionTargetId))
            {
                return;
            }

            if (shift.CurrentCount < shift.RequiredCount)
            {
                shift.CurrentCount++;

                if (shift.CurrentCount % 5 == 0 || shift.CurrentCount == shift.RequiredCount)
                {
                    player.SendInfoMessage($"[c/38bdf8:[{jobId.ToUpper()} SHIFT]] Progres: [c/f59e0b:{shift.CurrentCount}/{shift.RequiredCount}] {shift.TargetName}");
                }

                Database.SaveProfile(profile);
            }

            if (shift.CurrentCount >= shift.RequiredCount)
            {
                long finalCoins = shift.RewardCoins;
                long finalExp = shift.RewardExp;

                int currentJobLvl = profile.GetJobLevel(jobId);
                double bonusPct = Math.Clamp(Config.LevelBonusPercent, 0.0, 5.0);
                double multiplier = 1.0 + Math.Max(0, currentJobLvl - 1) * (bonusPct / 100.0);

                finalCoins = (long)Math.Max(1, Math.Round(finalCoins * multiplier));
                finalExp = (long)Math.Max(1, Math.Round(finalExp * multiplier));

                PayCoinsToPlayer(player, finalCoins);

                try
                {
                    ExpApi.AddExp(player, (int)finalExp);
                }
                catch { }

                bool leveledUp = profile.AddJobExp(jobId, finalExp, out int newJobLevel);
                profile.TotalCoinsEarned += finalCoins;
                profile.TotalActionsCount += shift.RequiredCount;

                EconomyApi.TryCredit(player.Account.Name, finalCoins, $"Job Shift: {jobId}");

                try
                {
                    var rankData = RankModule.Database?.GetOrCreate(player.Account.Name);
                    if (rankData != null)
                    {
                        rankData.HasJobSelected = true;
                        rankData.JobsCompleted++;
                        RankModule.Database!.Update(rankData);
                    }
                }
                catch { }

                int cooldownSeconds = GetEffectiveShiftCooldownSeconds(player);
                DateTime cooldownEnd = cooldownSeconds > 0
                    ? DateTime.UtcNow.AddSeconds(cooldownSeconds)
                    : DateTime.MinValue;
                profile.RollNextShift(jobId, newJobLevel);

                var nextShift = profile.GetOrCreateShift(jobId);
                nextShift.ShiftCooldownUntil = cooldownEnd;
                nextShift.NeedsCooldownAlert = cooldownSeconds > 0;

                Database.SaveProfile(profile);

                player.SendSuccessMessage($"[i:73] [c/4ade80:=== SHIFT SELESAI! ===]");
                player.SendSuccessMessage($"Kamu menyelesaikan kuota {shift.RequiredCount}x {shift.TargetName}!");
                player.SendSuccessMessage($"[c/fbbf24:Gaji Diterima:] {finalCoins:N0} Shard | [c/38bdf8:EXP:] +{finalExp:N0} EXP");
                if (cooldownSeconds > 0)
                {
                    int cdMins = cooldownSeconds / 60;
                    int cdSecs = cooldownSeconds % 60;
                    string cdLabel = cdSecs > 0 ? $"{cdMins}m {cdSecs}s" : $"{cdMins} menit";
                    player.SendInfoMessage($"[c/94a3b8:Shift istirahat selama {cdLabel}. Kamu akan diberi tahu di chat begitu shift berikutnya siap!]");
                }
                else
                {
                    player.SendInfoMessage("[c/4ade80:Bonus Kasta Univers: Shift berikutnya langsung siap tanpa cooldown!]");
                }

                if (Config.EnableCombatTextFeedback)
                {
                    ShowCombatText(player, finalCoins, jobId);
                }

                JobApi.InvokeShiftCompleted(player, jobId, newJobLevel);

                if (leveledUp)
                {
                    JobApi.InvokeLevelUp(player, jobId, newJobLevel);
                    player.SendSuccessMessage($"[i:273] [c/f59e0b:SELAMAT!] Karir [c/38bdf8:{jobId.ToUpper()}] kamu naik ke Level {newJobLevel}!");
                    player.SendInfoMessage($"[c/a3e635:+ Misi tingkat berikutnya terbuka & bonus gaji naik menjadi +{(newJobLevel - 1) * bonusPct:0.#}%!]");
                }
            }
        }

        private static int GetEffectiveShiftCooldownSeconds(TSPlayer player)
        {
            int baseSeconds = Math.Max(60, Config.TaskCooldownMinutes * 60);
            int rankIndex = 0;
            try
            {
                rankIndex = RankApi.GetRankIndex(player);
            }
            catch { }

            if (rankIndex >= 8) return 0; // Univers: Instant Job Delay 0m
            if (rankIndex >= 6) return Math.Max(30, baseSeconds / 2); // Astral/Cosmic: -50%
            if (rankIndex >= 2) return Math.Max(30, baseSeconds - 30); // Transcendant+: -30s
            return baseSeconds;
        }

        private static bool IsTargetMatching(string jobId, int expectedId, int actualId)
        {
            if (expectedId == actualId) return true;

            if (jobId == "miner")
            {
                if (expectedId == TileID.Copper && actualId == TileID.Tin) return true;
                if (expectedId == TileID.Iron && actualId == TileID.Lead) return true;
                if (expectedId == TileID.Silver && actualId == TileID.Tungsten) return true;
                if (expectedId == TileID.Gold && actualId == TileID.Platinum) return true;
                if (expectedId == TileID.Hellstone && (actualId == TileID.Cobalt || actualId == TileID.Palladium)) return true;
                if (expectedId == TileID.Demonite && actualId == TileID.Crimtane) return true;
                if (expectedId == TileID.Cobalt && actualId == TileID.Palladium) return true;
            }

            if (jobId == "lumberjack")
            {
                return actualId == TileID.Trees ||
                       actualId == 323 || // PalmTree
                       actualId == 72 ||  // MushroomTrees
                       actualId == 596 || // VanityTreeSakura
                       actualId == 616 || // VanityTreeYellowWillow
                       actualId == 634;   // TreeAsh
            }

            if (jobId == "farmer")
            {
                return actualId == 82 || // ImmatureHerbs
                       actualId == 83 || // MatureHerbs
                       actualId == 84 || // BloomingHerbs
                       actualId == 71 || // Mushrooms
                       actualId == 80 || // Cactus
                       actualId == 3;    // Wild Plants / Flowers
            }

            if (jobId == "hunter")
            {
                if (expectedId == NPCID.GreenSlime)
                {
                    NPC mockNpc = new NPC();
                    mockNpc.SetDefaults(actualId);

                    string npcName = mockNpc.TypeName ?? "";

                    if (npcName.Contains("Slime") || actualId == NPCID.Zombie || actualId == NPCID.BaldZombie)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ShowCombatText(TSPlayer player, long coinsEarned, string jobId)
        {
            if (player == null || player.TPlayer == null) return;

            string cleanText = $"+{coinsEarned} Shards";

            player.SendData(
                PacketTypes.CreateCombatTextExtended,
                cleanText,
                (int)Color.Purple.PackedValue,
                player.TPlayer.position.X,
                player.TPlayer.position.Y - 20
            );
        }

        private static void PayCoinsToPlayer(TSPlayer player, long coins)
        {
            if (player == null || !player.Active || player.TPlayer == null) return;
        }

        public static string FormatCoinsShort(long amount)
        {
            return EconomyApi.FormatColored(amount);
        }

        public static int GetMaxAllowedJobsForPlayer(TSPlayer player)
        {
            int rankIndex = 0;
            try
            {
                rankIndex = RankApi.GetRankIndex(player);
            }
            catch { }
            if (rankIndex >= 7) return 3;
            if (rankIndex >= 4) return 2;
            return Config.DefaultMaxJobsPerPlayer;
        }
    }
}
