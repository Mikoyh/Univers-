/*
 * =====================================================================
 *  SurvivalModule.cs - Engine Inti Don't Starve RPG (Hunger, Sanity, Suhu, Wetness)
 *  Namespace: Univers.Survival (.NET 9.0 / TShock 6.x / .NET 9)
 *  Aktif khusus di world 'survival' (otomatis pause & bersih di 'lobby')
 * =====================================================================
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;
using Univers.MultiWorld;

namespace Univers.Survival
{
    public class SurvivalRuntimeState
    {
        public SurvivalProfile Profile { get; set; } = null!;
        public Vector2 LastPosition { get; set; }
        public bool HasLastPosition { get; set; }
        public bool Dirty { get; set; }

        public DateTime LastHungerDamageUtc { get; set; } = DateTime.MinValue;
        public DateTime LastThermalDamageUtc { get; set; } = DateTime.MinValue;
        public DateTime LastDarknessDamageUtc { get; set; } = DateTime.MinValue;
        public DateTime LastShadowSpawnUtc { get; set; } = DateTime.MinValue;
        public DateTime LastBuffSyncUtc { get; set; } = DateTime.MinValue;
        public DateTime LastHudSyncUtc { get; set; } = DateTime.MinValue;
        public DateTime LastWarningChatUtc { get; set; } = DateTime.MinValue;
        public DateTime LastEatTriggerUtc { get; set; } = DateTime.MinValue;
        public DateTime LastItemDropUtc { get; set; } = DateTime.MinValue;
        public int LastItemDropType { get; set; }
        public DateTime LastTrashUtc { get; set; } = DateTime.MinValue;
        public int LastTrashType { get; set; }
        public DateTime LastChestMoveUtc { get; set; } = DateTime.MinValue;
        public DateTime LastItemUseUtc { get; set; } = DateTime.MinValue;
        public DateTime LastBuffOrHpUtc { get; set; } = DateTime.MinValue;

        public int DarknessSeconds { get; set; }
        public int LastHungerStage { get; set; } = -1;   // 0=Full, 1=Normal, 2=Peckish, 3=Hungry, 4=Starving
        public int LastTempStage { get; set; } = 0;      // -2=Freezing, -1=Cold, 0=Normal, 1=Hot, 2=Heatstroke
        public int LastSanityStage { get; set; } = 0;    // 0=Calm, 1=Anxious, 2=Insane
        public bool LastDarknessWarned { get; set; }

        // Snapshot lingkungan terakhir untuk perintah /survival
        public string EnvironmentSummary { get; set; } = "Normal";
        public bool NearCampfire { get; set; }
        public bool NearLight { get; set; }
        public bool Indoors { get; set; }
        public bool InTotalDarkness { get; set; }
        public double TargetAmbientTemp { get; set; } = 25.0;

        // Tracking slot (0..58 termasuk cursor) untuk anti-exploit & anti-false-trigger konsumsi makanan
        public readonly Dictionary<short, (int NetId, int Stack)> TrackedFoodSlots = new();
    }

    internal readonly record struct PendingFoodConsumption(
        int PlayerIndex,
        int ItemType,
        int ExpectedMaxTotal,
        DateTime ExpiresUtc
    );

    public static class SurvivalModule
    {
        public static SurvivalConfig Config { get; private set; } = new();
        public static SurvivalDatabase Database { get; private set; } = null!;
        public static ConcurrentDictionary<int, SurvivalRuntimeState> States { get; } = new();

        private static readonly ConcurrentQueue<PendingFoodConsumption> _pendingConsumptions = new();
        private static readonly Random _rng = new();

        private static string _configPath = string.Empty;
        private static bool _initialized = false;
        private static DateTime _lastTickUtc = DateTime.UtcNow;
        private static DateTime _lastSaveUtc = DateTime.UtcNow;

        public static bool IsSurvivalActiveInCurrentWorld()
        {
            return _initialized && Config.IsWorldActive(MultiWorldModule.CurrentWorldId);
        }

        public static void Initialize(string savePath)
        {
            if (_initialized) return;

            string universDir = Path.Combine(savePath, "Univers");
            Directory.CreateDirectory(universDir);

            _configPath = Path.Combine(universDir, "SurvivalConfig.json");
            Config = SurvivalConfig.Load(_configPath);
            Database = new SurvivalDatabase(Path.Combine(universDir, "univers_survival.db"));

            ServerApi.Hooks.GameUpdate.Register(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.ServerLeave.Register(UniversPlugin.Instance, OnServerLeave);
            ServerApi.Hooks.NetGetData.Register(UniversPlugin.Instance, OnNetGetData);

            GetDataHandlers.TileEdit += OnTileEdit;
            GetDataHandlers.KillMe += OnPlayerKillMe;
            GetDataHandlers.PlayerSpawn += OnPlayerSpawn;

            SurvivalCommands.Register();

            _lastTickUtc = DateTime.UtcNow;
            _lastSaveUtc = DateTime.UtcNow;
            _initialized = true;

            string worldInfo = IsSurvivalActiveInCurrentWorld()
                ? $"AKTIF di world '{MultiWorldModule.CurrentWorldId}'"
                : $"PAUSE di world '{MultiWorldModule.CurrentWorldId}' (Aktif di: {string.Join(", ", Config.ActiveWorldIds)})";
            TShock.Log.ConsoleInfo($"[Univers.Survival] Don't Starve RPG Engine dimuat ({worldInfo}).");
        }

        public static void Reload()
        {
            if (!string.IsNullOrEmpty(_configPath))
            {
                Config = SurvivalConfig.Load(_configPath);
            }
            TShock.Log.ConsoleInfo("[Univers.Survival] Konfigurasi SurvivalConfig.json dimuat ulang.");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            SaveAllDirtyProfiles();

            ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.ServerLeave.Deregister(UniversPlugin.Instance, OnServerLeave);
            ServerApi.Hooks.NetGetData.Deregister(UniversPlugin.Instance, OnNetGetData);

            GetDataHandlers.TileEdit -= OnTileEdit;
            GetDataHandlers.KillMe -= OnPlayerKillMe;
            GetDataHandlers.PlayerSpawn -= OnPlayerSpawn;

            States.Clear();
            while (_pendingConsumptions.TryDequeue(out _)) { }
            Database?.Dispose();
        }

        public static SurvivalRuntimeState? GetOrLoadState(TSPlayer player)
        {
            if (!_initialized || player == null || !player.Active || !player.IsLoggedIn || player.Account == null)
            {
                return null;
            }

            return States.GetOrAdd(player.Index, _ =>
            {
                var profile = Database.GetOrCreateProfile(player.Account.Name, Config);
                var st = new SurvivalRuntimeState
                {
                    Profile = profile,
                    LastPosition = player.TPlayer.position,
                    HasLastPosition = true
                };
                SnapshotFoodSlots(player, st);
                return st;
            });
        }

        private static void SnapshotFoodSlots(TSPlayer player, SurvivalRuntimeState st)
        {
            st.TrackedFoodSlots.Clear();
            var inv = player.TPlayer.inventory;
            int limit = Math.Min(inv.Length, 59); // 0..58 (termasuk slot cursor 58)
            for (short s = 0; s < limit; s++)
            {
                var it = inv[s];
                if (it != null && it.type > 0 && it.stack > 0 && SurvivalFoodRegistry.TryGetFoodNutrition(it.type, out _))
                {
                    st.TrackedFoodSlots[s] = (it.type, it.stack);
                }
            }
        }

        private static int CountTrackedFoodType(SurvivalRuntimeState st, int itemType)
        {
            int total = 0;
            foreach (var kv in st.TrackedFoodSlots)
            {
                if (kv.Value.NetId == itemType && kv.Value.Stack > 0)
                {
                    total += kv.Value.Stack;
                }
            }
            return total;
        }

        private static int CountFoodInInventory(Player tp, int itemType)
        {
            int total = 0;
            var inv = tp.inventory;
            if (inv == null) return 0;
            int limit = Math.Min(inv.Length, 59); // 0..58 termasuk cursor 58
            for (int i = 0; i < limit; i++)
            {
                var it = inv[i];
                if (it != null && !it.IsAir && it.type == itemType && it.stack > 0)
                {
                    total += it.stack;
                }
            }
            return total;
        }

        private static void OnPlayerGreet(GreetPlayerEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null || !player.Active || !player.IsLoggedIn) return;
            if (MultiWorldModule.IsPlayerTransferring(player)) return;

            if (!IsSurvivalActiveInCurrentWorld())
            {
                if (Config.ClearSurvivalDebuffsOutsideActiveWorlds)
                {
                    ClearSurvivalDebuffs(player);
                }
                return;
            }

            var st = GetOrLoadState(player);
            if (st == null) return;

            SnapshotFoodSlots(player, st);

            player.SendMessage(
                $"[ SURVIVAL ] Mode Don't Starve AKTIF di '{MultiWorldModule.CurrentWorldId}'! Hunger: {st.Profile.Hunger:0}% | Sanity: {st.Profile.Sanity:0}% | Suhu: {st.Profile.Temperature:0.0}C",
                Color.Gold);
            player.SendInfoMessage("[ SURVIVAL ] Ketik /status untuk cek detail bar, atau /shud untuk HUD layar.");
        }

        private static void OnServerLeave(LeaveEventArgs args)
        {
            if (States.TryRemove(args.Who, out var st) && st.Profile != null)
            {
                Database?.SaveProfile(st.Profile);
            }
        }

        private static void OnPlayerKillMe(object? sender, GetDataHandlers.KillMeEventArgs e)
        {
            if (!IsSurvivalActiveInCurrentWorld()) return;
            var player = e.Player;
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            if (States.TryGetValue(player.Index, out var st))
            {
                st.DarknessSeconds = 0;
                st.LastDarknessWarned = false;
            }
        }

        private static void OnPlayerSpawn(object? sender, GetDataHandlers.SpawnEventArgs e)
        {
            if (!IsSurvivalActiveInCurrentWorld()) return;
            var player = e.Player;
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            if (States.TryGetValue(player.Index, out var st))
            {
                st.Profile.Hunger = Math.Max(st.Profile.Hunger, Config.RespawnHunger);
                st.Profile.Sanity = Math.Max(st.Profile.Sanity, Config.RespawnSanity);
                st.Profile.Temperature = Config.IdealTemperature;
                st.Profile.Wetness = 0.0;
                st.DarknessSeconds = 0;
                st.LastDarknessWarned = false;
                st.Dirty = true;
                Database?.SaveProfile(st.Profile);
            }
        }

        private static void OnTileEdit(object? sender, GetDataHandlers.TileEditEventArgs e)
        {
            if (e.Handled || !IsSurvivalActiveInCurrentWorld()) return;
            var player = e.Player;
            if (player == null || !player.Active || !player.IsLoggedIn || player.Dead) return;

            if (e.Action == GetDataHandlers.EditAction.KillTile ||
                e.Action == GetDataHandlers.EditAction.KillTileNoItem ||
                e.Action == GetDataHandlers.EditAction.PlaceTile)
            {
                if (States.TryGetValue(player.Index, out var st))
                {
                    st.Profile.Hunger = Math.Max(0.0, st.Profile.Hunger - Config.ActivityHungerDrain);
                    st.Dirty = true;
                }
            }
        }

        private static void OnNetGetData(GetDataEventArgs e)
        {
            if (e.Handled || !IsSurvivalActiveInCurrentWorld()) return;

            var player = TShock.Players[e.Msg.whoAmI];
            if (player == null || !player.Active || !player.IsLoggedIn || player.Dead) return;

            if (!States.TryGetValue(player.Index, out var st)) return;

            int msgId = (int)e.MsgID;

            if (msgId == 41) // PlayerAnimation
            {
                st.LastItemUseUtc = DateTime.UtcNow;
                return;
            }

            if (e.MsgID == PacketTypes.PlayerBuff ||
                msgId == 55 || // PlayerAddBuff
                e.MsgID == PacketTypes.PlayerHp)
            {
                st.LastBuffOrHpUtc = DateTime.UtcNow;
                return;
            }

            if (e.MsgID == PacketTypes.ChestItem ||
                msgId == 85 || // ForceItemIntoNearestChest (quick-stack)
                msgId == 133)  // FoodPlatterTryPlacing
            {
                st.LastChestMoveUtc = DateTime.UtcNow;
                return;
            }

            if (e.MsgID == PacketTypes.ItemDrop)
            {
                // Catat item yang baru dijatuhkan pemain agar trik drop-item tidak dianggap makan
                try
                {
                    using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                    short itemIndex = reader.ReadInt16();
                    reader.ReadSingle(); // posX
                    reader.ReadSingle(); // posY
                    reader.ReadSingle(); // velX
                    reader.ReadSingle(); // velY
                    short stack = reader.ReadInt16();
                    reader.ReadByte();   // prefix
                    reader.ReadByte();   // noDelay
                    short netId = reader.ReadInt16();
                    if (netId > 0 && stack > 0)
                    {
                        st.LastItemDropType = netId;
                        st.LastItemDropUtc = DateTime.UtcNow;
                    }
                }
                catch { }
                return;
            }

            if (e.MsgID == PacketTypes.PlayerSlot)
            {
                try
                {
                    using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                    byte playerId = reader.ReadByte();
                    short slot = reader.ReadInt16();
                    short newStack = reader.ReadInt16();
                    byte prefix = reader.ReadByte();
                    short newNetId = reader.ReadInt16();

                    if (playerId != player.Index) return;

                    // Slot 179 = trashItem; membuang makanan ke trash bukan makan
                    if (slot == 179)
                    {
                        if (newNetId > 0 && newStack > 0)
                        {
                            st.LastTrashType = newNetId;
                            st.LastTrashUtc = DateTime.UtcNow;
                        }
                        return;
                    }

                    // Slot 99..259 (kecuali trash 179) = piggy bank / safe / defender forge / void vault
                    if (slot >= 99 && slot < 260)
                    {
                        st.LastChestMoveUtc = DateTime.UtcNow;
                        return;
                    }

                    // Hanya pantau slot inventory 0..58 (termasuk coins 50..53, ammo 54..57, dan cursor 58)
                    if (slot < 0 || slot > 58) return;

                    bool hadTracked = st.TrackedFoodSlots.TryGetValue(slot, out var oldInfo);
                    int oldTotal = hadTracked ? CountTrackedFoodType(st, oldInfo.NetId) : 0;

                    if (newNetId > 0 && newStack > 0 && SurvivalFoodRegistry.TryGetFoodNutrition(newNetId, out _))
                    {
                        st.TrackedFoodSlots[slot] = (newNetId, newStack);
                    }
                    else
                    {
                        st.TrackedFoodSlots.Remove(slot);
                    }

                    // Deteksi pengurangan tepat 1 item makanan secara total di luar chest/pindah slot
                    if (hadTracked && player.TPlayer.chest == -1)
                    {
                        bool sameItemMinusOne = (newNetId == oldInfo.NetId && oldInfo.Stack - newStack == 1) ||
                                                (newStack == 0 && oldInfo.Stack == 1);

                        if (sameItemMinusOne)
                        {
                            int newTotal = CountTrackedFoodType(st, oldInfo.NetId);
                            if (newTotal == oldTotal - 1)
                            {
                                // Tunggu 250ms di main thread agar jika pindah ke slot lain / cursor / trash / drop langsung batal
                                _pendingConsumptions.Enqueue(new PendingFoodConsumption(
                                    player.Index,
                                    oldInfo.NetId,
                                    newTotal,
                                    DateTime.UtcNow.AddMilliseconds(250)));
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static void OnGameUpdate(EventArgs args)
        {
            if (!_initialized) return;

            DateTime frameNow = DateTime.UtcNow;
            if (IsSurvivalActiveInCurrentWorld())
            {
                foreach (var player in TShock.Players)
                {
                    if (player == null || !player.Active || !player.IsLoggedIn || !player.RealPlayer || player.Dead) continue;
                    if (States.TryGetValue(player.Index, out var st))
                    {
                        var tp = player.TPlayer;
                        if (tp != null && (tp.controlUseItem || tp.itemAnimation > 0))
                        {
                            st.LastItemUseUtc = frameNow;
                        }
                    }
                }
            }

            ProcessPendingFoodConsumptions();

            if (!IsSurvivalActiveInCurrentWorld()) return;

            DateTime now = DateTime.UtcNow;
            if ((now - _lastTickUtc).TotalSeconds < 1.0) return;

            double dt = Math.Clamp((now - _lastTickUtc).TotalSeconds, 0.5, 3.0);
            _lastTickUtc = now;

            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active || !player.IsLoggedIn || !player.RealPlayer) continue;
                UpdatePlayerSurvivalTick(player, dt, now);
            }

            if ((now - _lastSaveUtc).TotalSeconds >= Config.AutoSaveIntervalSeconds)
            {
                _lastSaveUtc = now;
                SaveAllDirtyProfiles();
            }
        }

        private static void ProcessPendingFoodConsumptions()
        {
            DateTime now = DateTime.UtcNow;
            int guard = 0;
            while (guard < 20 && _pendingConsumptions.TryPeek(out var pending))
            {
                if (now < pending.ExpiresUtc) break;
                _pendingConsumptions.TryDequeue(out _);
                guard++;

                var player = TShock.Players[pending.PlayerIndex];
                if (player == null || !player.Active || !player.IsLoggedIn || player.Dead) continue;
                if (!States.TryGetValue(player.Index, out var st)) continue;

                // Anti-exploit: abaikan jika barusan menjatuhkan item tersebut (drop / buang item)
                if (st.LastItemDropType == pending.ItemType && (now - st.LastItemDropUtc).TotalMilliseconds <= 550)
                {
                    continue;
                }

                // Abaikan jika item masuk ke slot trash (179)
                if (st.LastTrashType == pending.ItemType && (now - st.LastTrashUtc).TotalMilliseconds <= 550)
                {
                    continue;
                }

                // Abaikan jika sedang membuka chest atau memindahkan item ke chest/piggy/safe/void bag
                if (player.TPlayer.chest != -1 || (now - st.LastChestMoveUtc).TotalMilliseconds <= 550) continue;

                // Pastikan total item tersebut di seluruh inventory (0..58 termasuk cursor) benar-benar berkurang (bukan dipindah slot!)
                int currentTotal = CountFoodInInventory(player.TPlayer, pending.ItemType);
                if (currentTotal > pending.ExpectedMaxTotal)
                {
                    continue;
                }

                // Pastikan memang ada aksi penggunaan item / buff / heal (bukan sekadar drag antar slot di mobile/PC)
                bool usedOrBuffed = (now - st.LastItemUseUtc).TotalMilliseconds <= 1200 ||
                                    (now - st.LastBuffOrHpUtc).TotalMilliseconds <= 1200;
                if (!usedOrBuffed)
                {
                    continue;
                }

                // Debounce singkat agar tidak terhitung ganda
                if ((now - st.LastEatTriggerUtc).TotalMilliseconds < 350) continue;

                if (!SurvivalFoodRegistry.TryGetFoodNutrition(pending.ItemType, out var food)) continue;

                st.LastEatTriggerUtc = now;
                ApplyFoodConsumption(player, st, food);
            }
        }

        private static void ApplyFoodConsumption(TSPlayer player, SurvivalRuntimeState st, FoodNutrition food)
        {
            var p = st.Profile;
            p.Hunger = Math.Clamp(p.Hunger + food.HungerGain, 0.0, Config.MaxHunger);
            p.Sanity = Math.Clamp(p.Sanity + food.SanityGain, 0.0, Config.MaxSanity);
            p.Temperature = Math.Clamp(p.Temperature + food.TemperatureDelta, Config.MinTemperature, Config.MaxTemperature);
            p.FoodsEaten++;
            st.Dirty = true;

            string signSan = food.SanityGain >= 0 ? $"+{food.SanityGain:0}" : $"{food.SanityGain:0}";
            string combatText = $"+{food.HungerGain:0} Hunger | {signSan} Sanity";
            player.SendData(
                PacketTypes.CreateCombatTextExtended,
                combatText,
                (int)Color.LightGreen.PackedValue,
                player.X,
                player.Y - 28f);

            string tempNote = food.TemperatureDelta < 0
                ? $" | Suhu {food.TemperatureDelta:0}C"
                : (food.TemperatureDelta > 0 ? $" | Suhu +{food.TemperatureDelta:0}C" : "");

            player.SendSuccessMessage($"[ + MAKAN ] {food.Label}: Hunger {p.Hunger:0}% | Sanity {p.Sanity:0}%{tempNote}");
            SyncSurvivalBuffs(player, st, DateTime.UtcNow, force: true);
        }

        private static void UpdatePlayerSurvivalTick(TSPlayer player, double dt, DateTime now)
        {
            var st = GetOrLoadState(player);
            if (st == null || player.Dead) return;

            var prof = st.Profile;
            var tp = player.TPlayer;

            // 1. Pindai lingkungan ringan (12x12 sekitar player, 1x per detik di Main Thread)
            int tx = Math.Clamp((int)(player.X / 16f), 15, Main.maxTilesX - 15);
            int ty = Math.Clamp((int)(player.Y / 16f), 15, Main.maxTilesY - 15);
            ScanEnvironment(player, st, tx, ty);

            // 2. Hitung pergerakan untuk pengurasan Hunger
            double moveMultiplier = 1.0;
            if (st.HasLastPosition)
            {
                float moved = Vector2.Distance(st.LastPosition, tp.position);
                if (moved > 32f)
                {
                    moveMultiplier = Config.MovingHungerMultiplier;
                }
            }
            st.LastPosition = tp.position;
            st.HasLastPosition = true;

            // 3. Update HUNGER
            double hungerDrain = Config.PassiveHungerDrainPerSecond * moveMultiplier * dt;
            if (st.Profile.Temperature < Config.ColdThreshold)
            {
                // Tubuh kedinginan membakar kalori sedikit lebih cepat
                hungerDrain *= 1.25;
            }
            prof.Hunger = Math.Clamp(prof.Hunger - hungerDrain, 0.0, Config.MaxHunger);

            // 4. Update WETNESS (Kebasahan)
            bool inWater = tp.wet && !tp.lavaWet && !tp.honeyWet;
            bool inRainOutdoors = Main.raining && ty <= Main.worldSurface && !st.Indoors;

            if (inWater)
            {
                prof.Wetness = Math.Clamp(prof.Wetness + Config.WaterWetnessGainPerSecond * dt, 0.0, 100.0);
            }
            else if (inRainOutdoors)
            {
                prof.Wetness = Math.Clamp(prof.Wetness + Config.RainWetnessGainPerSecond * dt, 0.0, 100.0);
            }
            else
            {
                double drySpeed = Config.DryRatePerSecond * (st.NearCampfire ? Config.CampfireDryMultiplier : 1.0);
                prof.Wetness = Math.Clamp(prof.Wetness - drySpeed * dt, 0.0, 100.0);
            }

            // 5. Update TEMPERATURE (Suhu)
            double targetTemp = ComputeAmbientTemperature(player, st, tx, ty, inRainOutdoors);
            st.TargetAmbientTemp = targetTemp;
            double tempStep = Config.TemperatureAdaptRatePerSecond * dt;
            if (prof.Temperature < targetTemp)
            {
                prof.Temperature = Math.Min(targetTemp, prof.Temperature + tempStep);
            }
            else if (prof.Temperature > targetTemp)
            {
                prof.Temperature = Math.Max(targetTemp, prof.Temperature - tempStep);
            }
            prof.Temperature = Math.Clamp(prof.Temperature, Config.MinTemperature, Config.MaxTemperature);

            // 6. Update SANITY (Kewarasan)
            double sanityDelta = 0.0;
            bool nearBoss = IsBossNearby(player.X, player.Y);

            if (st.InTotalDarkness)
            {
                sanityDelta -= Config.DarknessSanityDrainPerSecond;
                st.DarknessSeconds++;
            }
            else
            {
                st.DarknessSeconds = 0;
                st.LastDarknessWarned = false;
            }

            if (inRainOutdoors) sanityDelta -= Config.RainSanityDrainPerSecond;
            if (nearBoss) sanityDelta -= Config.BossSanityDrainPerSecond;
            if (prof.Wetness >= 60.0) sanityDelta -= 0.08;

            if (st.NearCampfire) sanityDelta += Config.CampfireSanityRegenPerSecond;
            if (HasTileOrBuffBed(st)) sanityDelta += Config.BedSanityRegenPerSecond;
            if (HasPlayerBuff(tp, BuffID.Sunflower)) sanityDelta += Config.SunflowerSanityRegenPerSecond;
            if (!st.InTotalDarkness && Main.dayTime && ty <= Main.worldSurface && !Main.raining && !nearBoss)
            {
                sanityDelta += Config.DaylightSanityRegenPerSecond;
            }

            prof.Sanity = Math.Clamp(prof.Sanity + sanityDelta * dt, 0.0, Config.MaxSanity);
            st.Dirty = true;

            // 7. Efek Bahaya & Konsekuensi
            ApplySurvivalConsequences(player, st, nearBoss, now);

            // 8. Sinkronisasi Buff Vanilla Terraria (Hemat Paket!)
            SyncSurvivalBuffs(player, st, now, force: false);

            // 9. Optional On-Screen Status Bar (PacketTypes.Status)
            if (prof.ShowStatusHud && (now - st.LastHudSyncUtc).TotalSeconds >= Config.StatusHudRefreshSeconds)
            {
                st.LastHudSyncUtc = now;
                SendStatusHudPacket(player, st);
            }
        }

        private static bool HasTileOrBuffBed(SurvivalRuntimeState st)
        {
            return st.EnvironmentSummary.Contains("Kasur", StringComparison.OrdinalIgnoreCase);
        }

        private static void ScanEnvironment(TSPlayer player, SurvivalRuntimeState st, int tx, int ty)
        {
            int r = Math.Clamp(Config.EnvironmentScanRadiusTiles, 6, 16);
            int minX = Math.Max(5, tx - r);
            int maxX = Math.Min(Main.maxTilesX - 5, tx + r);
            int minY = Math.Max(5, ty - (r - 2));
            int maxY = Math.Min(Main.maxTilesY - 5, ty + (r - 2));

            bool nearCampfire = false;
            bool nearLight = false;
            bool nearBed = false;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    var tile = Main.tile[x, y];
                    if (tile == null || !tile.active()) continue;

                    ushort t = tile.type;
                    if (t == TileID.Campfire || t == TileID.Fireplace ||
                        t == TileID.Furnaces || t == TileID.Hellforge || t == TileID.AdamantiteForge)
                    {
                        nearCampfire = true;
                        nearLight = true;
                    }
                    else if (Main.tileLighted[t])
                    {
                        nearLight = true;
                    }
                    else if (t == TileID.Beds)
                    {
                        nearBed = true;
                    }
                }
            }

            // Cek personal light source (torch di tangan, Mining Helmet, Shine Potion, Light Pet)
            var tp = player.TPlayer;
            int heldType = player.SelectedItem?.type ?? 0;
            bool holdingTorchOrGlow = heldType > 0 && heldType < ItemID.Sets.Torches.Length && ItemID.Sets.Torches[heldType];
            bool hasLightArmor = tp.armor[0] != null &&
                                 (tp.armor[0].type == ItemID.MiningHelmet || tp.armor[0].type == ItemID.UltrabrightHelmet);
            bool hasLightBuff = HasPlayerBuff(tp, BuffID.Shine) || HasPlayerBuff(tp, BuffID.Campfire);

            bool hasLight = nearLight || holdingTorchOrGlow || hasLightArmor || hasLightBuff;

            // Cek atap / dinding belakang untuk status Indoors (terlindung dari hujan)
            bool hasBackWall = Main.tile[tx, ty] != null && Main.tile[tx, ty].wall > 0;
            bool hasRoof = false;
            for (int dy = 1; dy <= 18; dy++)
            {
                int checkY = ty - dy;
                if (checkY < 5) break;
                var roof = Main.tile[tx, checkY];
                if (roof != null && roof.active() && Main.tileSolid[roof.type])
                {
                    hasRoof = true;
                    break;
                }
            }
            bool indoors = hasRoof || (hasBackWall && ty > Main.worldSurface);

            bool isDaySurface = Main.dayTime && ty <= Main.worldSurface && !Main.eclipse;
            bool inTotalDarkness = !isDaySurface && !hasLight;

            st.NearCampfire = nearCampfire;
            st.NearLight = hasLight;
            st.Indoors = indoors;
            st.InTotalDarkness = inTotalDarkness;

            var notes = new List<string>();
            if (nearCampfire) notes.Add("Dekat Api Hangat");
            if (nearBed) notes.Add("Dekat Kasur");
            if (inTotalDarkness) notes.Add("Gelap Total!");
            else if (hasLight) notes.Add("Terang");
            if (indoors) notes.Add("Dalam Ruangan");
            else if (Main.raining && ty <= Main.worldSurface) notes.Add("Hujan Terbuka");

            st.EnvironmentSummary = notes.Count > 0 ? string.Join(" | ", notes) : "Terbuka";
        }

        private static double ComputeAmbientTemperature(TSPlayer player, SurvivalRuntimeState st, int tx, int ty, bool inRainOutdoors)
        {
            var tp = player.TPlayer;
            double temp = Main.dayTime ? 26.0 : 17.0;

            if (tp.ZoneUnderworldHeight || ty >= Main.UnderworldLayer)
            {
                temp = 50.0;
            }
            else if (tp.ZoneSnow)
            {
                temp = ty > Main.worldSurface ? -2.0 : (Main.dayTime ? 4.0 : -7.0);
            }
            else if (tp.ZoneDesert)
            {
                temp = ty > Main.worldSurface ? 32.0 : (Main.dayTime ? 43.0 : 15.0);
            }
            else if (tp.ZoneJungle)
            {
                temp = Main.dayTime ? 32.0 : 24.0;
            }
            else if (ty > Main.worldSurface)
            {
                temp = 17.0;
            }

            if (inRainOutdoors) temp -= 5.0;
            temp -= (st.Profile.Wetness / 100.0) * 10.0;

            if (st.NearCampfire)
            {
                // Api unggun menormalkan hawa dingin menuju suhu hangat nyaman (~27C)
                temp = Math.Max(temp + 14.0, 27.0);
                temp = Math.Min(temp, 34.0);
            }

            if (tp.lavaWet) temp += 28.0;

            // Perlindungan potion / armor
            if (HasPlayerBuff(tp, BuffID.Warmth) && temp < 14.0)
            {
                temp = 14.0;
            }
            if (HasPlayerBuff(tp, BuffID.ObsidianSkin) && temp > 34.0)
            {
                temp = 34.0;
            }

            return Math.Clamp(temp, Config.MinTemperature, Config.MaxTemperature);
        }

        private static bool IsBossNearby(float px, float py)
        {
            const float maxDistSq = 2400f * 2400f;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var npc = Main.npc[i];
                if (npc == null || !npc.active || !npc.boss) continue;

                float dx = npc.Center.X - px;
                float dy = npc.Center.Y - py;
                if ((dx * dx) + (dy * dy) <= maxDistSq)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ApplySurvivalConsequences(TSPlayer player, SurvivalRuntimeState st, bool nearBoss, DateTime now)
        {
            if (player.GodMode) return;
            var prof = st.Profile;

            // A. Kegelapan Total (Charlie Effect!)
            if (st.InTotalDarkness)
            {
                if (!st.LastDarknessWarned)
                {
                    st.LastDarknessWarned = true;
                    player.SendWarningMessage("[ ! SANITY ] Gelap total! Pegang obor atau nyalakan api agar kewarasan tidak drop!");
                    player.SendData(PacketTypes.CreateCombatTextExtended, "Gelap! Butuh Cahaya", (int)Color.MediumPurple.PackedValue, player.X, player.Y - 30f);
                }

                if (st.DarknessSeconds >= Config.DarknessGraceSeconds && (now - st.LastDarknessDamageUtc).TotalSeconds >= 4.0)
                {
                    st.LastDarknessDamageUtc = now;
                    player.DamagePlayer(Config.DarknessDamage);
                    player.SendData(PacketTypes.CreateCombatTextExtended, "Charlie Attack!", (int)Color.DarkViolet.PackedValue, player.X, player.Y - 24f);
                }
            }

            // B. Kelaparan Akut (Hunger <= 0)
            if (prof.Hunger <= 0.01 && (now - st.LastHungerDamageUtc).TotalSeconds >= 3.0)
            {
                st.LastHungerDamageUtc = now;
                player.DamagePlayer(Config.StarvingDamage);
                player.SendData(PacketTypes.CreateCombatTextExtended, "KELAPARAN!", (int)Color.OrangeRed.PackedValue, player.X, player.Y - 24f);
            }

            // C. Suhu Ekstrem (Beku / Heatstroke)
            if ((prof.Temperature <= Config.FreezingThreshold || prof.Temperature >= Config.HeatstrokeThreshold) &&
                (now - st.LastThermalDamageUtc).TotalSeconds >= 3.0)
            {
                st.LastThermalDamageUtc = now;
                player.DamagePlayer(Config.ThermalDamage);
                string txt = prof.Temperature <= Config.FreezingThreshold ? "HIPOTERMIA!" : "KEPANASAN!";
                Color col = prof.Temperature <= Config.FreezingThreshold ? Color.Cyan : Color.Orange;
                player.SendData(PacketTypes.CreateCombatTextExtended, txt, (int)col.PackedValue, player.X, player.Y - 24f);
            }

            // D. Halusinasi Bayangan saat Sanity Kritis
            if (prof.Sanity <= Config.ShadowMonsterThreshold &&
                (now - st.LastShadowSpawnUtc).TotalSeconds >= Config.ShadowMonsterCooldownSeconds &&
                !nearBoss)
            {
                st.LastShadowSpawnUtc = now;
                prof.ShadowEncounters++;
                st.Dirty = true;

                int offsetTilesX = _rng.Next(2) == 0 ? -24 : 24;
                int spawnX = (int)Math.Clamp(player.X + (offsetTilesX * 16), 320, (Main.maxTilesX * 16) - 320);
                int spawnY = (int)Math.Clamp(player.Y - 64, 320, (Main.maxTilesY * 16) - 320);

                int spawnTileX = Math.Clamp((int)(spawnX / 16f), 20, Main.maxTilesX - 20);
                int spawnTileY = Math.Clamp((int)(spawnY / 16f), 20, Main.maxTilesY - 20);
                var shadowNpc = new NPC();
                shadowNpc.SetDefaults(NPCID.Wraith);
                TSPlayer.Server.SpawnNPC(shadowNpc.type, shadowNpc.FullName, 1, spawnTileX, spawnTileY, 4, 4);

                player.SendErrorMessage("[ SANITY KRITIS ] Pikiranmu terguncang... Bayangan gelap muncul mengejarmu!");
                player.SendData(PacketTypes.CreateCombatTextExtended, "SHADOW CREATURE!", (int)Color.MediumPurple.PackedValue, player.X, player.Y - 36f);
            }

            // E. Peringatan Chat Saat Perubahan Stage Penting (anti-spam!)
            SendStageTransitionWarnings(player, st, now);
        }

        private static void SendStageTransitionWarnings(TSPlayer player, SurvivalRuntimeState st, DateTime now)
        {
            var prof = st.Profile;

            int hungerStage = prof.Hunger >= 80 ? 0
                : (prof.Hunger >= Config.PeckishThreshold ? 1
                : (prof.Hunger >= Config.HungryThreshold ? 2
                : (prof.Hunger > 0 ? 3 : 4)));

            if (hungerStage != st.LastHungerStage)
            {
                int prev = st.LastHungerStage;
                st.LastHungerStage = hungerStage;
                if (prev != -1 && (now - st.LastWarningChatUtc).TotalSeconds >= 2)
                {
                    if (hungerStage == 3)
                    {
                        st.LastWarningChatUtc = now;
                        player.SendWarningMessage("[ ! HUNGER ] Perutmu keroncongan! Segera cari & makan makanan.");
                    }
                    else if (hungerStage == 4)
                    {
                        st.LastWarningChatUtc = now;
                        player.SendErrorMessage("[ X KELAPARAN ] Hunger 0%! Kamu mulai kehilangan HP terus-menerus!");
                    }
                }
            }

            int tempStage = prof.Temperature <= Config.FreezingThreshold ? -2
                : (prof.Temperature < Config.ColdThreshold ? -1
                : (prof.Temperature >= Config.HeatstrokeThreshold ? 2
                : (prof.Temperature > Config.HotThreshold ? 1 : 0)));

            if (tempStage != st.LastTempStage)
            {
                st.LastTempStage = tempStage;
                if (tempStage == -1)
                    player.SendWarningMessage($"[ ! SUHU ] Terasa dingin ({prof.Temperature:0.0}C). Dekati Campfire atau nyalakan api.");
                else if (tempStage == -2)
                    player.SendErrorMessage($"[ X BEKU ] Suhu membeku ({prof.Temperature:0.0}C)! Segera cari api unggun!");
                else if (tempStage == 1)
                    player.SendWarningMessage($"[ ! SUHU ] Terlalu panas ({prof.Temperature:0.0}C). Minum air/jus dingin atau berteduh.");
                else if (tempStage == 2)
                    player.SendErrorMessage($"[ X HEATSTROKE ] Suhu sangat panas ({prof.Temperature:0.0}C)! Segera dinginkan tubuh!");
            }

            int sanityStage = prof.Sanity <= Config.ShadowMonsterThreshold ? 2
                : (prof.Sanity <= Config.LowSanityThreshold ? 1 : 0);

            if (sanityStage != st.LastSanityStage)
            {
                st.LastSanityStage = sanityStage;
                if (sanityStage == 1)
                {
                    player.SendWarningMessage($"[ ! SANITY ] Kewarasan menurun ({prof.Sanity:0}%). Dekati Campfire atau beristirahat di cahaya terang.");
                }
                else if (sanityStage == 2)
                {
                    player.SendErrorMessage($"[ X SANITY KRITIS ] Sanity {prof.Sanity:0}%! Waspadai halusinasi bayangan gelap!");
                }
            }
        }

        private static void SyncSurvivalBuffs(TSPlayer player, SurvivalRuntimeState st, DateTime now, bool force)
        {
            if (!force && (now - st.LastBuffSyncUtc).TotalSeconds < 5.0) return;
            st.LastBuffSyncUtc = now;

            var prof = st.Profile;
            var tp = player.TPlayer;

            // 1. Buff Hunger resmi Terraria 1.4.3+ (The Constant)
            if (prof.Hunger <= Config.StarvingThreshold)
            {
                ApplyBuffIfLow(player, tp, BuffID.Starving, 360);
            }
            else if (prof.Hunger < Config.HungryThreshold)
            {
                ApplyBuffIfLow(player, tp, BuffID.Hunger, 360);
            }
            else if (prof.Hunger < Config.PeckishThreshold)
            {
                ApplyBuffIfLow(player, tp, BuffID.NeutralHunger, 360);
            }
            else if (prof.Hunger >= 85.0 && !HasPlayerBuff(tp, BuffID.WellFed3) && !HasPlayerBuff(tp, BuffID.WellFed2))
            {
                ApplyBuffIfLow(player, tp, BuffID.WellFed, 360);
            }

            // 2. Buff Suhu & Kebasahan
            if (prof.Temperature < Config.ColdThreshold)
            {
                ApplyBuffIfLow(player, tp, BuffID.Chilled, 360);
                if (prof.Temperature <= Config.FreezingThreshold)
                {
                    ApplyBuffIfLow(player, tp, BuffID.Frozen, 60);
                }
            }
            else if (prof.Temperature >= Config.HeatstrokeThreshold)
            {
                ApplyBuffIfLow(player, tp, BuffID.OnFire, 180);
            }

            if (prof.Wetness >= 30.0)
            {
                ApplyBuffIfLow(player, tp, BuffID.Wet, 360);
            }

            // 3. Buff Sanity / Darkness
            if (prof.Sanity < Config.LowSanityThreshold || st.InTotalDarkness)
            {
                ApplyBuffIfLow(player, tp, BuffID.Darkness, 240);
            }
        }

        private static bool HasPlayerBuff(Player? tp, int buffId)
        {
            return tp != null && tp.FindBuffIndex(buffId) != -1;
        }

        private static void ApplyBuffIfLow(TSPlayer player, Player tp, int buffId, int ticks)
        {
            int idx = tp.FindBuffIndex(buffId);
            if (idx == -1 || tp.buffTime[idx] < 120)
            {
                player.SetBuff(buffId, ticks);
            }
        }

        private static void ClearSurvivalDebuffs(TSPlayer player)
        {
            try
            {
                var tp = player.TPlayer;
                int[] toClear = { BuffID.NeutralHunger, BuffID.Hunger, BuffID.Starving, BuffID.Chilled, BuffID.Frozen, BuffID.Wet, BuffID.Darkness };
                bool changed = false;
                foreach (int bid in toClear)
                {
                    int idx = tp.FindBuffIndex(bid);
                    if (idx != -1)
                    {
                        tp.DelBuff(idx);
                        changed = true;
                    }
                }
                if (changed)
                {
                    NetMessage.SendData((int)PacketTypes.PlayerBuff, -1, -1, null, player.Index);
                }
            }
            catch { }
        }

        public static void SendStatusHudPacket(TSPlayer player, SurvivalRuntimeState st)
        {
            try
            {
                var p = st.Profile;
                string hBar = BuildAsciiBar(p.Hunger, Config.MaxHunger, 8);
                string sBar = BuildAsciiBar(p.Sanity, Config.MaxSanity, 8);
                string wetStr = p.Wetness >= 15.0 ? $" | Wet:{p.Wetness:0}%" : "";

                string hud = $"\n\n\n\n\n\n[SURVIVAL] Hunger [{hBar}] {p.Hunger:0}%  |  Sanity [{sBar}] {p.Sanity:0}%  |  Suhu {p.Temperature:0.0}C{wetStr}";
                NetMessage.SendData((int)PacketTypes.Status, player.Index, -1, NetworkText.FromLiteral(hud), 1, 1f);
            }
            catch { }
        }

        public static string BuildAsciiBar(double val, double max, int width = 10)
        {
            double ratio = max <= 0 ? 0 : Math.Clamp(val / max, 0.0, 1.0);
            int filled = (int)Math.Round(ratio * width);
            return new string('#', filled) + new string('.', Math.Max(0, width - filled));
        }

        private static void SaveAllDirtyProfiles()
        {
            if (Database == null) return;
            foreach (var st in States.Values)
            {
                if (st.Dirty && st.Profile != null)
                {
                    st.Dirty = false;
                    Database.SaveProfile(st.Profile);
                }
            }
        }
    }
}
