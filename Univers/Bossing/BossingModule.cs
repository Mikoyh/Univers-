/*
 * =====================================================================
 *  BossingModule.cs - Elden Ring Style Bossing Engine
 *  Namespace: Univers.Bossing (.NET 9.0 / TShock 6.x)
 *  Fitur:
 *   1. HP Boss 10x sesuai mode (Classic/Expert/Master), Damage tetap 1x.
 *   2. World survival dibatasi max 2 boss: 1 Arena Kiri + 1 Arena Kanan.
 *   3. Zona tengah sekitar Spawn dilindungi Sanctuary (anti summon & anti masuk).
 *   4. Tidak pernah despawn saat tidak ada player; boss pulang ke spawn awal.
 *   5. Saat idle menunggu player, boss diam & tidak regen HP sama sekali.
 * =====================================================================
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Univers.MultiWorld;

namespace Univers.Bossing
{
    public enum BossArenaSide
    {
        None = 0,
        Left = 1,
        Right = 2
    }

    public enum BossCategory
    {
        None = 0,
        MainBoss = 1,
        EventMiniBoss = 2,
        CelestialPillar = 3
    }

    public class TransientBossNpcState
    {
        public int NpcIndex { get; set; }
        public int NpcType { get; set; }
        public BossCategory Category { get; set; }
        public BossArenaSide Side { get; set; }
        public int BaseLifeMax { get; set; }
        public int ScaledLifeMax { get; set; }
        public double AppliedMultiplier { get; set; } = 1.0;
        public bool HasSharedRealLife { get; set; }
        public int SyncCountdownTicks { get; set; } = 2;
    }

    public class TrackedBossNpcState
    {
        public int NpcIndex { get; set; }
        public int NpcType { get; set; }
        public string BossKey { get; set; } = string.Empty;
        public string EncounterId { get; set; } = string.Empty;
        public int BaseLifeMax { get; set; }
        public int ScaledLifeMax { get; set; }
        public int LastValidLife { get; set; }
        public int LockedIdleLife { get; set; }
        public Vector2 LastPosition { get; set; }
        public Vector2 IdleAnchorOffset { get; set; }
        public float[] SavedAi { get; } = new float[4];
        public float[] SavedLocalAi { get; } = new float[4];
        public bool HasSavedAi { get; set; }
        public bool HasSharedRealLife { get; set; }
        public int SyncCountdownTicks { get; set; } = 2;
        public bool NeedsVisualBarSync { get; set; }
        public DateTime LastBarSyncUtc { get; set; } = DateTime.MinValue;
    }

    public class TrackedBossEncounter
    {
        public string EncounterId { get; set; } = string.Empty;
        public string BossKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public BossArenaSide Side { get; set; }
        public bool CountsTowardArenaSlot { get; set; } = true;
        public bool IsUnderworldEncounter { get; set; }
        public bool IsNightOnlyBoss { get; set; }
        public Vector2 OriginPosition { get; set; }
        public int PrimaryNpcIndex { get; set; } = -1;
        public List<int> NpcIndices { get; } = new();
        public bool IsReturningToOrigin { get; set; }
        public bool IsIdleAtOrigin { get; set; }
        public DateTime? NoChallengerSinceUtc { get; set; }
        public DateTime? IdleSinceUtc { get; set; }
        public DateTime LastRegenTickUtc { get; set; } = DateTime.MinValue;
        public bool IsRegenerating { get; set; }
        public bool HasAnnouncedRegen { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public int ReturnSyncTickCounter { get; set; }
        public int IdleSyncTickCounter { get; set; }
        public DateTime LastReturnNoticeUtc { get; set; } = DateTime.MinValue;
    }

    public static class BossingModule
    {
        public static BossingConfig Config { get; private set; } = new();

        private static string _configPath = string.Empty;
        private static bool _initialized;
        private static bool _internalRecovering;
        [ThreadStatic]
        private static bool _isSendingMappedNpcPacket;

        private static readonly object _syncRoot = new();
        private static readonly Dictionary<string, TrackedBossEncounter> _encounters = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, TrackedBossNpcState> _npcStates = new();
        private static readonly Dictionary<int, TransientBossNpcState> _transientBossStates = new();
        private static readonly HashSet<int> _genuinelyKilledSlots = new();
        private static readonly ConcurrentDictionary<int, DateTime> _lastPlayerNoticeUtc = new();
        private static DateTime _lastBarrierVisualUtc = DateTime.MinValue;
        private static DateTime _lastBarrierChatUtc = DateTime.MinValue;
        private static DateTime _lastPillarSpawnNoticeUtc = DateTime.MinValue;
        private static DateTime _lastLunarPillarsClearedUtc = DateTime.MinValue;
        private static DateTime _lastNightSyncUtc = DateTime.MinValue;
        private static DateTime _lastNightNoticeUtc = DateTime.MinValue;
        private static int _transientTickCounter;

        // Semua NPC Boss & bagian multi-part resmi Terraria
        private static readonly HashSet<int> _allBossAndPartTypes = new()
        {
            NPCID.KingSlime,
            NPCID.EyeofCthulhu,
            NPCID.EaterofWorldsHead,
            NPCID.EaterofWorldsBody,
            NPCID.EaterofWorldsTail,
            NPCID.BrainofCthulhu,
            NPCID.Creeper,
            NPCID.QueenBee,
            NPCID.SkeletronHead,
            NPCID.SkeletronHand,
            NPCID.Deerclops,
            NPCID.WallofFlesh,
            NPCID.WallofFleshEye,
            NPCID.QueenSlimeBoss,
            NPCID.Retinazer,
            NPCID.Spazmatism,
            NPCID.TheDestroyer,
            NPCID.TheDestroyerBody,
            NPCID.TheDestroyerTail,
            NPCID.SkeletronPrime,
            NPCID.PrimeCannon,
            NPCID.PrimeSaw,
            NPCID.PrimeVice,
            NPCID.PrimeLaser,
            NPCID.Plantera,
            NPCID.PlanterasHook,
            NPCID.PlanterasTentacle,
            NPCID.Golem,
            NPCID.GolemHead,
            NPCID.GolemFistLeft,
            NPCID.GolemFistRight,
            NPCID.DukeFishron,
            NPCID.Pumpking,
            NPCID.PumpkingBlade,
            NPCID.MourningWood,
            NPCID.Everscream,
            NPCID.SantaNK1,
            NPCID.IceQueen,
            NPCID.PirateShip,
            NPCID.PirateShipCannon,
            NPCID.MartianSaucer,
            NPCID.MartianSaucerCore,
            NPCID.MartianSaucerTurret,
            NPCID.MartianSaucerCannon,
            NPCID.DD2DarkMageT1,
            NPCID.DD2DarkMageT3,
            NPCID.DD2OgreT2,
            NPCID.DD2OgreT3,
            NPCID.DD2Betsy,
            NPCID.LunarTowerSolar,
            NPCID.LunarTowerVortex,
            NPCID.LunarTowerNebula,
            NPCID.LunarTowerStardust,
            NPCID.HallowBoss,
            NPCID.CultistBoss,
            NPCID.MoonLordCore,
            NPCID.MoonLordHead,
            NPCID.MoonLordHand
        };

        // Bagian tubuh/segmen/lengan dari boss utama yang tidak dihitung sebagai boss baru terpisah
        private static readonly HashSet<int> _strictSegmentPartTypes = new()
        {
            NPCID.EaterofWorldsBody,
            NPCID.EaterofWorldsTail,
            NPCID.Creeper,
            NPCID.SkeletronHand,
            NPCID.WallofFleshEye,
            NPCID.TheDestroyerBody,
            NPCID.TheDestroyerTail,
            NPCID.PrimeCannon,
            NPCID.PrimeSaw,
            NPCID.PrimeVice,
            NPCID.PrimeLaser,
            NPCID.PlanterasHook,
            NPCID.PlanterasTentacle,
            NPCID.GolemHead,
            NPCID.GolemFistLeft,
            NPCID.GolemFistRight,
            NPCID.PumpkingBlade,
            NPCID.PirateShipCannon,
            NPCID.MartianSaucerCore,
            NPCID.MartianSaucerTurret,
            NPCID.MartianSaucerCannon,
            NPCID.MoonLordHead,
            NPCID.MoonLordHand
        };

        // 4 Celestial Pillars pasca-Lunatic Cultist (bebas batas 2 slot & menuju Moon Lord)
        private static readonly HashSet<int> _celestialPillarTypes = new()
        {
            NPCID.LunarTowerSolar,
            NPCID.LunarTowerVortex,
            NPCID.LunarTowerNebula,
            NPCID.LunarTowerStardust
        };

        // Boss malam hari yang AI-nya langsung kabur ke langit / enrage jika waktu berubah menjadi siang
        // (Empress of Light / HallowBoss sengaja TIDAK dimasukkan agar Daytime EoL untuk Terraprisma tetap bisa!)
        private static readonly HashSet<int> _nightOnlyBossTypes = new()
        {
            NPCID.EyeofCthulhu,
            NPCID.SkeletronHead,
            NPCID.SkeletronHand,
            NPCID.Retinazer,
            NPCID.Spazmatism,
            NPCID.TheDestroyer,
            NPCID.TheDestroyerBody,
            NPCID.TheDestroyerTail,
            NPCID.SkeletronPrime,
            NPCID.PrimeCannon,
            NPCID.PrimeSaw,
            NPCID.PrimeVice,
            NPCID.PrimeLaser
        };

        // Mini-boss gelombang invasi / event (Pumpkin Moon, Frost Moon, Pirate, Martian, Old One's Army)
        private static readonly HashSet<int> _eventMiniBossTypes = new()
        {
            NPCID.Pumpking,
            NPCID.PumpkingBlade,
            NPCID.MourningWood,
            NPCID.Everscream,
            NPCID.SantaNK1,
            NPCID.IceQueen,
            NPCID.PirateShip,
            NPCID.PirateShipCannon,
            NPCID.MartianSaucer,
            NPCID.MartianSaucerCore,
            NPCID.MartianSaucerTurret,
            NPCID.MartianSaucerCannon,
            NPCID.DD2DarkMageT1,
            NPCID.DD2DarkMageT3,
            NPCID.DD2OgreT2,
            NPCID.DD2OgreT3,
            NPCID.DD2Betsy
        };

        // Item pemanggil boss untuk deteksi dini saat player mengayunkan item summon
        private static readonly HashSet<int> _bossSummonItemIds = new()
        {
            ItemID.SlimeCrown,
            ItemID.SuspiciousLookingEye,
            ItemID.WormFood,
            ItemID.BloodySpine,
            ItemID.Abeemination,
            ItemID.DeerThing,
            ItemID.GuideVoodooDoll,
            ItemID.ClothierVoodooDoll,
            ItemID.QueenSlimeCrystal,
            ItemID.MechanicalEye,
            ItemID.MechanicalWorm,
            ItemID.MechanicalSkull,
            ItemID.TruffleWorm,
            ItemID.EmpressButterfly,
            ItemID.LihzahrdPowerCell,
            ItemID.CelestialSigil
        };

        public static bool IsBossingActiveInCurrentWorld()
        {
            return _initialized && Config.IsWorldActive(MultiWorldModule.CurrentWorldId);
        }

        public static void Initialize(string savePath)
        {
            if (_initialized) return;

            string universDir = Path.Combine(savePath, "Univers");
            Directory.CreateDirectory(universDir);

            _configPath = Path.Combine(universDir, "BossingConfig.json");
            Config = BossingConfig.Load(_configPath);

            ServerApi.Hooks.NpcSpawn.Register(UniversPlugin.Instance, OnNpcSpawn);
            ServerApi.Hooks.NpcTransform.Register(UniversPlugin.Instance, OnNpcTransform);
            ServerApi.Hooks.NpcKilled.Register(UniversPlugin.Instance, OnNpcKilled);
            ServerApi.Hooks.NpcStrike.Register(UniversPlugin.Instance, OnNpcStrike);
            ServerApi.Hooks.NetGetData.Register(UniversPlugin.Instance, OnNetGetData);
            ServerApi.Hooks.NetSendData.Register(UniversPlugin.Instance, OnNetSendData);
            ServerApi.Hooks.GameUpdate.Register(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.ServerCommand.Register(UniversPlugin.Instance, OnServerCommand);
            PlayerHooks.PlayerCommand += OnPlayerCommand;

            BossingCommands.Register();

            _initialized = true;

            string status = IsBossingActiveInCurrentWorld()
                ? $"AKTIF di world '{MultiWorldModule.CurrentWorldId}' (HP {Config.BossHpMultiplier:0.#}x | Max {Config.MaxBossesPerSide} Kiri + {Config.MaxBossesPerSide} Kanan | Sanctuary Tengah +/-{Config.CenterSanctuaryRadiusTiles} tile)"
                : $"STANDBY di world '{MultiWorldModule.CurrentWorldId}' (Aktif di: {string.Join(", ", Config.ActiveWorldIds)})";
            TShock.Log.ConsoleInfo($"[Univers.Bossing] Elden Ring Bossing Engine dimuat ({status}).");
        }

        public static void Reload()
        {
            if (!string.IsNullOrEmpty(_configPath))
            {
                Config = BossingConfig.Load(_configPath);
            }
            TShock.Log.ConsoleInfo("[Univers.Bossing] Konfigurasi BossingConfig.json dimuat ulang.");
        }

        public static void SaveConfig()
        {
            if (!string.IsNullOrEmpty(_configPath))
            {
                Config.Save(_configPath);
            }
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            try { ServerApi.Hooks.NpcSpawn.Deregister(UniversPlugin.Instance, OnNpcSpawn); } catch { }
            try { ServerApi.Hooks.NpcTransform.Deregister(UniversPlugin.Instance, OnNpcTransform); } catch { }
            try { ServerApi.Hooks.NpcKilled.Deregister(UniversPlugin.Instance, OnNpcKilled); } catch { }
            try { ServerApi.Hooks.NpcStrike.Deregister(UniversPlugin.Instance, OnNpcStrike); } catch { }
            try { ServerApi.Hooks.NetGetData.Deregister(UniversPlugin.Instance, OnNetGetData); } catch { }
            try { ServerApi.Hooks.NetSendData.Deregister(UniversPlugin.Instance, OnNetSendData); } catch { }
            try { ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate); } catch { }
            try { ServerApi.Hooks.ServerCommand.Deregister(UniversPlugin.Instance, OnServerCommand); } catch { }
            try { PlayerHooks.PlayerCommand -= OnPlayerCommand; } catch { }
            try { BossingCommands.Unregister(); } catch { }

            lock (_syncRoot)
            {
                _encounters.Clear();
                _npcStates.Clear();
                _transientBossStates.Clear();
                _genuinelyKilledSlots.Clear();
            }
            _lastPlayerNoticeUtc.Clear();
        }

        #region Arena Geometry & Public Helpers

        public static (float LeftLimitPx, float RightLimitPx) GetCenterSanctuaryBoundsPx()
        {
            float spawnX = Main.spawnTileX * 16f;
            float radiusPx = Config.CenterSanctuaryRadiusTiles * 16f;
            return (spawnX - radiusPx, spawnX + radiusPx);
        }

        public static bool IsInsideCenterSanctuaryPx(float worldX, float worldY)
        {
            if (!Config.CenterSanctuaryIncludeUnderworld && (worldY / 16f) >= Main.UnderworldLayer)
            {
                return false;
            }

            var (leftLimit, rightLimit) = GetCenterSanctuaryBoundsPx();
            return worldX >= leftLimit && worldX <= rightLimit;
        }

        public static BossArenaSide ResolveArenaSidePx(float worldX, float worldY)
        {
            if (IsInsideCenterSanctuaryPx(worldX, worldY))
            {
                return BossArenaSide.None;
            }

            float spawnX = Main.spawnTileX * 16f;
            return worldX < spawnX ? BossArenaSide.Left : BossArenaSide.Right;
        }

        public static string FormatSideLabel(BossArenaSide side)
        {
            return side switch
            {
                BossArenaSide.Left => "KIRI",
                BossArenaSide.Right => "KANAN",
                _ => "SANCTUARY TENGAH"
            };
        }

        public static BossArenaSide GetEncounterSideForNpc(int npcWhoAmI)
        {
            if (!IsBossingActiveInCurrentWorld()) return BossArenaSide.None;

            lock (_syncRoot)
            {
                if (_npcStates.TryGetValue(npcWhoAmI, out var st) &&
                    _encounters.TryGetValue(st.EncounterId, out var enc))
                {
                    return enc.Side;
                }

                if (_transientBossStates.TryGetValue(npcWhoAmI, out var tst))
                {
                    return tst.Side;
                }
            }
            return BossArenaSide.None;
        }

        public static int AdjustRecordedMaxLifeForRewards(int npcWhoAmI, int recordedMaxLife, int npcType = 0)
        {
            if (!IsBossingActiveInCurrentWorld())
            {
                return recordedMaxLife;
            }

            lock (_syncRoot)
            {
                if (_npcStates.TryGetValue(npcWhoAmI, out var st) && st.BaseLifeMax > 0)
                {
                    return Math.Max(1000, (int)Math.Round(st.BaseLifeMax * Config.BossRewardMultiplier));
                }

                if (_transientBossStates.TryGetValue(npcWhoAmI, out var tst) && tst.BaseLifeMax > 0)
                {
                    return Math.Max(1000, (int)Math.Round(tst.BaseLifeMax * Config.BossRewardMultiplier));
                }
            }

            double appliedMult = GetConfiguredHpMultiplierForNpcType(npcType);
            if (appliedMult <= 1.0)
            {
                return Math.Max(1000, (int)Math.Round(recordedMaxLife * Config.BossRewardMultiplier));
            }

            double normalized = recordedMaxLife / Math.Max(1.0, appliedMult);
            return Math.Max(1000, (int)Math.Round(normalized * Config.BossRewardMultiplier));
        }

        public static List<TrackedBossEncounter> GetActiveEncountersSnapshot()
        {
            lock (_syncRoot)
            {
                return _encounters.Values.ToList();
            }
        }

        public static (int EventMiniBossCount, int CelestialPillarCount) GetActiveTransientSummary()
        {
            lock (_syncRoot)
            {
                int eventCount = 0;
                int pillarCount = 0;
                foreach (var kv in _transientBossStates)
                {
                    int idx = kv.Key;
                    if (idx < 0 || idx >= Main.maxNPCs) continue;
                    var npc = Main.npc[idx];
                    if (npc == null || !npc.active) continue;

                    if (kv.Value.Category == BossCategory.CelestialPillar)
                    {
                        pillarCount++;
                    }
                    else if (kv.Value.Category == BossCategory.EventMiniBoss && !_strictSegmentPartTypes.Contains(npc.type))
                    {
                        eventCount++;
                    }
                }
                return (eventCount, pillarCount);
            }
        }

        public static (long CurrentHp, long MaxHp) GetEncounterHealthSummary(TrackedBossEncounter encounter)
        {
            lock (_syncRoot)
            {
                long cur = 0;
                long max = 0;
                var seenSharedRoots = new HashSet<int>();

                foreach (int idx in encounter.NpcIndices)
                {
                    if (idx < 0 || idx >= Main.maxNPCs) continue;
                    var npc = Main.npc[idx];
                    if (npc == null || !npc.active) continue;

                    int root = (npc.realLife >= 0 && npc.realLife < Main.maxNPCs) ? npc.realLife : npc.whoAmI;
                    if (!seenSharedRoots.Add(root)) continue;

                    var rootNpc = Main.npc[root];
                    if (rootNpc != null && rootNpc.active)
                    {
                        cur += Math.Max(0, rootNpc.life);
                        max += Math.Max(1, rootNpc.lifeMax);
                    }
                }
                return (cur, max);
            }
        }

        public static int AdminClearArena(BossArenaSide? targetSide)
        {
            int cleared = 0;
            lock (_syncRoot)
            {
                var targets = _encounters.Values
                    .Where(e => !targetSide.HasValue || e.Side == targetSide.Value)
                    .ToList();

                foreach (var enc in targets)
                {
                    foreach (int idx in enc.NpcIndices.ToList())
                    {
                        _genuinelyKilledSlots.Add(idx);
                        _npcStates.Remove(idx);

                        if (idx >= 0 && idx < Main.maxNPCs)
                        {
                            var npc = Main.npc[idx];
                            if (npc != null && npc.active)
                            {
                                npc.active = false;
                                npc.life = 0;
                                NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                                cleared++;
                            }
                        }
                    }

                    _encounters.Remove(enc.EncounterId);
                }

                var transientTargets = _transientBossStates.Values
                    .Where(t => !targetSide.HasValue || t.Side == targetSide.Value)
                    .ToList();

                foreach (var tst in transientTargets)
                {
                    int idx = tst.NpcIndex;
                    _transientBossStates.Remove(idx);
                    if (idx >= 0 && idx < Main.maxNPCs)
                    {
                        var npc = Main.npc[idx];
                        if (npc != null && npc.active)
                        {
                            npc.active = false;
                            npc.life = 0;
                            NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                            cleared++;
                        }
                    }
                }
            }
            return cleared;
        }

        public static int AdminStopLunarEvent()
        {
            int cleared = 0;
            lock (_syncRoot)
            {
                _lastLunarPillarsClearedUtc = DateTime.MinValue;

                SetStaticBoolMember(typeof(NPC), "LunarApocalypseIsUp", false);
                SetStaticBoolMember(typeof(NPC), "TowerActiveSolar", false);
                SetStaticBoolMember(typeof(NPC), "TowerActiveVortex", false);
                SetStaticBoolMember(typeof(NPC), "TowerActiveNebula", false);
                SetStaticBoolMember(typeof(NPC), "TowerActiveStardust", false);
                SetStaticIntMember(typeof(NPC), "ShieldStrengthTowerSolar", 0);
                SetStaticIntMember(typeof(NPC), "ShieldStrengthTowerVortex", 0);
                SetStaticIntMember(typeof(NPC), "ShieldStrengthTowerNebula", 0);
                SetStaticIntMember(typeof(NPC), "ShieldStrengthTowerStardust", 0);
                SetStaticIntMember(typeof(NPC), "MoonLordCountdown", 0);
                SetStaticIntMember(typeof(WorldGen), "MoonLordCountdown", 0);

                // Bersihkan semua 4 Celestial Pillars dari tracking & dunia
                foreach (int idx in _transientBossStates.Keys.ToList())
                {
                    if (_transientBossStates.TryGetValue(idx, out var tst) && tst.Category == BossCategory.CelestialPillar)
                    {
                        _transientBossStates.Remove(idx);
                    }
                }

                // Bersihkan encounter Moon Lord jika sedang countdown / aktif
                foreach (var enc in _encounters.Values.Where(e => e.BossKey.Equals("MoonLord", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    foreach (int idx in enc.NpcIndices.ToList())
                    {
                        _genuinelyKilledSlots.Add(idx);
                        _npcStates.Remove(idx);
                        if (idx >= 0 && idx < Main.maxNPCs && Main.npc[idx] != null && Main.npc[idx].active)
                        {
                            Main.npc[idx].active = false;
                            Main.npc[idx].life = 0;
                            NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                            cleared++;
                        }
                    }
                    _encounters.Remove(enc.EncounterId);
                }

                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    var npc = Main.npc[i];
                    if (npc == null || !npc.active) continue;

                    bool isPillarOrMoonLord = _celestialPillarTypes.Contains(npc.type) ||
                                              npc.type == NPCID.MoonLordCore ||
                                              npc.type == NPCID.MoonLordHead ||
                                              npc.type == NPCID.MoonLordHand;

                    if (isPillarOrMoonLord)
                    {
                        _genuinelyKilledSlots.Add(i);
                        _transientBossStates.Remove(i);
                        _npcStates.Remove(i);
                        npc.active = false;
                        npc.life = 0;
                        NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, i);
                        cleared++;
                    }
                }
            }

            try
            {
                NetMessage.SendData((int)PacketTypes.WorldInfo, -1, -1, null);
            }
            catch { }

            return cleared;
        }

        private static void SetStaticBoolMember(Type type, string memberName, bool value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            try
            {
                var field = type.GetField(memberName, flags);
                if (field != null && field.FieldType == typeof(bool))
                {
                    field.SetValue(null, value);
                    return;
                }
                var prop = type.GetProperty(memberName, flags);
                if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool))
                {
                    prop.SetValue(null, value);
                }
            }
            catch { }
        }

        private static void SetStaticIntMember(Type type, string memberName, int value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            try
            {
                var field = type.GetField(memberName, flags);
                if (field != null && field.FieldType == typeof(int))
                {
                    field.SetValue(null, value);
                    return;
                }
                var prop = type.GetProperty(memberName, flags);
                if (prop != null && prop.CanWrite && prop.PropertyType == typeof(int))
                {
                    prop.SetValue(null, value);
                }
            }
            catch { }
        }

        #endregion

        #region Boss Identification & Slot Rules

        private static BossCategory ClassifyBossNpc(NPC? npc, out string bossKey)
        {
            bossKey = string.Empty;
            if (npc == null || npc.friendly || npc.townNPC) return BossCategory.None;

            BossCategory category;
            if (_celestialPillarTypes.Contains(npc.type))
            {
                category = BossCategory.CelestialPillar;
            }
            else if (_eventMiniBossTypes.Contains(npc.type))
            {
                category = BossCategory.EventMiniBoss;
            }
            else if (_allBossAndPartTypes.Contains(npc.type) || npc.boss)
            {
                category = BossCategory.MainBoss;
            }
            else
            {
                return BossCategory.None;
            }

            bossKey = Skynomi.Ranks.BossContributionTracker.IdentifyBossKey(npc);
            if (string.IsNullOrWhiteSpace(bossKey))
            {
                bossKey = string.IsNullOrWhiteSpace(npc.FullName) ? $"Boss_{npc.type}" : npc.FullName.Replace(" ", "");
            }

            return category;
        }

        private static double GetConfiguredHpMultiplierForCategory(BossCategory category)
        {
            return category switch
            {
                BossCategory.EventMiniBoss => Config.EventMiniBossHpMultiplier,
                BossCategory.CelestialPillar => Config.CelestialPillarHpMultiplier,
                BossCategory.MainBoss => Config.BossHpMultiplier,
                _ => 1.0
            };
        }

        private static double GetConfiguredHpMultiplierForNpcType(int npcType)
        {
            if (npcType <= 0) return Config.BossHpMultiplier;
            if (_celestialPillarTypes.Contains(npcType)) return Config.CelestialPillarHpMultiplier;
            if (_eventMiniBossTypes.Contains(npcType)) return Config.EventMiniBossHpMultiplier;
            return Config.BossHpMultiplier;
        }

        private static bool IsTrackedBossNpc(NPC? npc, out bool countsTowardArenaSlot, out string bossKey)
        {
            BossCategory category = ClassifyBossNpc(npc, out bossKey);
            if (category == BossCategory.None)
            {
                countsTowardArenaSlot = false;
                return false;
            }

            countsTowardArenaSlot = category == BossCategory.MainBoss ||
                                    (category == BossCategory.EventMiniBoss && Config.CountEventMiniBossesInArenaSlots);
            return true;
        }

        private static string GetBossDisplayName(string bossKey, NPC npc)
        {
            return Skynomi.Ranks.BossContributionTracker.GetBossDisplayName(bossKey, npc);
        }

        private static int CountSlotTrackedBossesOnSide(BossArenaSide side)
        {
            int count = 0;
            foreach (var enc in _encounters.Values)
            {
                if (enc.CountsTowardArenaSlot && enc.Side == side && HasAnyActiveNpc(enc))
                {
                    count++;
                }
            }
            return count;
        }

        private static TrackedBossEncounter? GetFirstSlotEncounterOnSide(BossArenaSide side)
        {
            return _encounters.Values.FirstOrDefault(e => e.CountsTowardArenaSlot && e.Side == side && HasAnyActiveNpc(e));
        }

        private static bool HasAnyActiveNpc(TrackedBossEncounter enc)
        {
            foreach (int idx in enc.NpcIndices)
            {
                if (idx >= 0 && idx < Main.maxNPCs && Main.npc[idx] != null && Main.npc[idx].active)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool CanPlayerSummonBossHere(TSPlayer player, out BossArenaSide playerSide, out string denyMessage)
        {
            playerSide = BossArenaSide.None;
            denyMessage = string.Empty;

            if (player == null || player.TPlayer == null) return false;

            playerSide = ResolveArenaSidePx(player.X, player.Y);
            if (playerSide == BossArenaSide.None)
            {
                denyMessage = $"[ BOSSING / SANCTUARY ] Tidak dapat memanggil boss di dalam Zona Sanctuary Tengah (+/-{Config.CenterSanctuaryRadiusTiles} tile sekitar Spawn)! Keluarlah ke Arena KIRI atau KANAN.";
                return false;
            }

            lock (_syncRoot)
            {
                int currentCount = CountSlotTrackedBossesOnSide(playerSide);
                if (currentCount >= Config.MaxBossesPerSide)
                {
                    var blocking = GetFirstSlotEncounterOnSide(playerSide);
                    string existingName = blocking?.DisplayName ?? "Boss lain";
                    BossArenaSide otherSide = playerSide == BossArenaSide.Left ? BossArenaSide.Right : BossArenaSide.Left;
                    denyMessage = $"[ BOSSING ] Arena {FormatSideLabel(playerSide)} sudah ditempati oleh '{existingName}'! Kalahkan dulu atau pindah ke Arena {FormatSideLabel(otherSide)}.";
                    return false;
                }
            }

            return true;
        }

        private static TrackedBossEncounter? FindMatchingExistingEncounter(NPC npc, string bossKey, BossArenaSide npcSide)
        {
            // 1. Cek relasi shared HP (The Destroyer / Wall of Flesh eyes)
            if (npc.realLife >= 0 && npc.realLife < Main.maxNPCs && npc.realLife != npc.whoAmI &&
                _npcStates.TryGetValue(npc.realLife, out var parentState) &&
                _encounters.TryGetValue(parentState.EncounterId, out var realLifeEnc))
            {
                return realLifeEnc;
            }

            // 2. Cek segmen/lengan dari encounter aktif yang sama
            bool isSegment = _strictSegmentPartTypes.Contains(npc.type);
            if (isSegment)
            {
                TrackedBossEncounter? best = null;
                float bestDistSq = float.MaxValue;
                foreach (var enc in _encounters.Values)
                {
                    if (!enc.BossKey.Equals(bossKey, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!HasAnyActiveNpc(enc)) continue;

                    float dSq = Vector2.DistanceSquared(npc.Center, enc.OriginPosition);
                    if (enc.Side == npcSide || dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        best = enc;
                    }
                }
                if (best != null) return best;
            }

            // 3. Kasus khusus The Twins (Retinazer & Spazmatism spawn bersamaan berjarak < 4 detik)
            if (npc.type == NPCID.Retinazer || npc.type == NPCID.Spazmatism)
            {
                int siblingType = npc.type == NPCID.Retinazer ? NPCID.Spazmatism : NPCID.Retinazer;
                foreach (var enc in _encounters.Values)
                {
                    if (!enc.BossKey.Equals("TheTwins", StringComparison.OrdinalIgnoreCase)) continue;
                    if ((DateTime.UtcNow - enc.CreatedUtc).TotalSeconds > 6.0) continue;

                    bool hasSelfType = enc.NpcIndices.Any(idx =>
                        idx >= 0 && idx < Main.maxNPCs && Main.npc[idx] != null && Main.npc[idx].active && Main.npc[idx].type == npc.type);
                    bool hasSiblingType = enc.NpcIndices.Any(idx =>
                        idx >= 0 && idx < Main.maxNPCs && Main.npc[idx] != null && Main.npc[idx].active && Main.npc[idx].type == siblingType);

                    if (!hasSelfType && hasSiblingType)
                    {
                        return enc;
                    }
                }
            }

            return null;
        }

        #endregion

        #region Hooks

        private static void OnNetGetData(GetDataEventArgs args)
        {
            if (args.Handled || !IsBossingActiveInCurrentWorld()) return;

            var player = TShock.Players[args.Msg.whoAmI];
            if (player == null || !player.Active || !player.IsLoggedIn || !player.RealPlayer) return;

            int msgId = (int)args.MsgID;

            // Packet 61: SpawnBossorInvasion
            if (msgId == 61)
            {
                if (args.Length < 4) return;
                try
                {
                    using var reader = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, args.Length));
                    reader.ReadInt16(); // playerId
                    short spawnType = reader.ReadInt16();

                    if (spawnType > 0)
                    {
                        if (spawnType == NPCID.TownCat || spawnType == NPCID.TownDog || spawnType == NPCID.TownBunny)
                        {
                            return;
                        }

                        if (!CanPlayerSummonBossHere(player, out _, out string denyMsg))
                        {
                            args.Handled = true;
                            NotifyPlayerThrottled(player, denyMsg, "ARENA PENUH / TERSEGEL", spawnBurst: true);
                        }
                    }
                }
                catch { }
                return;
            }

            // Packet 71: ReleaseNPC (Truffle Worm / Prismatic Lacewing)
            if (msgId == 71)
            {
                if (args.Length < 10) return;
                try
                {
                    using var reader = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, args.Length));
                    reader.ReadInt32(); // x
                    reader.ReadInt32(); // y
                    short npcType = reader.ReadInt16();

                    if (npcType == NPCID.TruffleWorm || npcType == NPCID.EmpressButterfly)
                    {
                        if (!CanPlayerSummonBossHere(player, out _, out string denyMsg))
                        {
                            args.Handled = true;
                            NotifyPlayerThrottled(player, denyMsg, "TIDAK BISA SUMMON", spawnBurst: true);
                        }
                    }
                }
                catch { }
                return;
            }

            // Packet 41: PlayerAnimation -> beri peringatan instan saat mencoba pakai item summon di zona terlarang/penuh
            if (msgId == 41)
            {
                Item? held = player.SelectedItem;
                if (held == null || held.type <= 0 || !_bossSummonItemIds.Contains(held.type))
                {
                    return;
                }

                if (!CanPlayerSummonBossHere(player, out _, out string denyMsg))
                {
                    NotifyPlayerThrottled(player, denyMsg, "ARENA TERBATAS", spawnBurst: true);
                }
            }
        }

        private static void OnPlayerCommand(PlayerCommandEventArgs args)
        {
            if (args.Handled || args.Player == null || !IsBossingActiveInCurrentWorld()) return;
            if (!args.Player.RealPlayer || !args.Player.Active) return;

            string cmd = args.CommandName?.ToLowerInvariant() ?? string.Empty;
            if (cmd != "spawnboss" && cmd != "sb") return;

            if (!CanPlayerSummonBossHere(args.Player, out _, out string denyMsg))
            {
                args.Handled = true;
                NotifyPlayerThrottled(args.Player, denyMsg, "ARENA TERBATAS", spawnBurst: true);
            }
        }

        private static void OnServerCommand(CommandEventArgs args)
        {
            // Biarkan command konsol diproses oleh OnNpcSpawn sesuai posisi spawn boss
        }

        private static void OnNpcSpawn(NpcSpawnEventArgs args)
        {
            if (args.Handled || _internalRecovering || !IsBossingActiveInCurrentWorld()) return;
            if (args.NpcId < 0 || args.NpcId >= Main.maxNPCs) return;

            var npc = Main.npc[args.NpcId];
            if (npc == null) return;

            BossCategory category = ClassifyBossNpc(npc, out string bossKey);
            if (category == BossCategory.None)
            {
                return;
            }

            bool countsTowardSlot = category == BossCategory.MainBoss ||
                                    (category == BossCategory.EventMiniBoss && Config.CountEventMiniBossesInArenaSlots);

            lock (_syncRoot)
            {
                _genuinelyKilledSlots.Remove(args.NpcId);
                _transientBossStates.Remove(args.NpcId);

                BossArenaSide npcSide = ResolveArenaSidePx(npc.Center.X, npc.Center.Y);

                // Jalur Event Mini-Boss & 4 Celestial Pillars: bebas batas 2 slot & despawn alami tetap berjalan
                if (!countsTowardSlot)
                {
                    if (npcSide == BossArenaSide.None)
                    {
                        npcSide = npc.Center.X < (Main.spawnTileX * 16f) ? BossArenaSide.Left : BossArenaSide.Right;
                        if (Config.KeepEventBossesOutOfCenterSanctuary)
                        {
                            PushNpcOutOfCenterSanctuary(npc, npcSide, extraOffsetTiles: Config.OriginSafeOffsetFromSanctuaryTiles);
                        }
                    }

                    int tBaseLifeMax = Math.Max(1, npc.lifeMax);
                    int tScaledLifeMax = tBaseLifeMax;
                    bool tHasSharedLife = npc.realLife >= 0 && npc.realLife < Main.maxNPCs && npc.realLife != npc.whoAmI;
                    double mult = GetConfiguredHpMultiplierForCategory(category);

                    if (!tHasSharedLife && mult > 1.0)
                    {
                        long rawScaled = (long)Math.Round(tBaseLifeMax * mult);
                        tScaledLifeMax = (int)Math.Clamp(rawScaled, 1L, 2_000_000_000L);
                        npc.lifeMax = tScaledLifeMax;
                        npc.life = tScaledLifeMax;
                    }

                    _transientBossStates[args.NpcId] = new TransientBossNpcState
                    {
                        NpcIndex = args.NpcId,
                        NpcType = npc.type,
                        Category = category,
                        Side = npcSide,
                        BaseLifeMax = tBaseLifeMax,
                        ScaledLifeMax = tScaledLifeMax,
                        AppliedMultiplier = mult,
                        HasSharedRealLife = tHasSharedLife,
                        SyncCountdownTicks = 2
                    };

                    if (category == BossCategory.CelestialPillar && Config.AnnounceBossEventsInChat)
                    {
                        DateTime now = DateTime.UtcNow;
                        if ((now - _lastPillarSpawnNoticeUtc).TotalSeconds >= 12)
                        {
                            _lastPillarSpawnNoticeUtc = now;
                            TSPlayer.All.SendMessage(
                                $"[ BOSSING ] 4 Celestial Pillars telah muncul! Event & Pillars bebas dari batas 2 slot boss (HP x{mult:0.#}). Kalahkan semua Pillar untuk membangkitkan Moon Lord!",
                                new Color(56, 189, 248));
                        }
                    }
                    return;
                }

                // Jalur Main Boss (Elden Ring Bosses & Moon Lord)
                var existing = FindMatchingExistingEncounter(npc, bossKey, npcSide);

                if (existing == null)
                {
                    TSPlayer? nearestPlayer = FindNearestRealAlivePlayer(npc.Center);
                    bool isMoonLordCore = npc.type == NPCID.MoonLordCore;
                    bool isPostPillarMoonLord = isMoonLordCore &&
                        (DateTime.UtcNow - _lastLunarPillarsClearedUtc).TotalSeconds <= 120.0;

                    // Jika player berada di Arena Kiri/Kanan namun offset random Terraria menaruh NPC di Zona Sanctuary,
                    // geser ke sisi arena player tersebut
                    if (npcSide == BossArenaSide.None && nearestPlayer != null)
                    {
                        BossArenaSide summonerSide = ResolveArenaSidePx(nearestPlayer.X, nearestPlayer.Y);
                        if (summonerSide != BossArenaSide.None)
                        {
                            npcSide = summonerSide;
                            PushNpcOutOfCenterSanctuary(npc, summonerSide, extraOffsetTiles: Config.OriginSafeOffsetFromSanctuaryTiles);
                        }
                    }

                    // Perlindungan khusus Moon Lord pasca 4 Celestial Pillars:
                    // Jika player sempat Recall/Mirror ke Spawn Sanctuary saat countdown 60 detik selesai,
                    // jangan hapus Moon Lord! Arahkan otomatis ke Arena Kiri/Kanan yang kosong.
                    if (npcSide == BossArenaSide.None && isMoonLordCore)
                    {
                        bool leftOpen = CountSlotTrackedBossesOnSide(BossArenaSide.Left) < Config.MaxBossesPerSide;
                        bool rightOpen = CountSlotTrackedBossesOnSide(BossArenaSide.Right) < Config.MaxBossesPerSide;
                        BossArenaSide preferredByPos = npc.Center.X < (Main.spawnTileX * 16f)
                            ? BossArenaSide.Left
                            : BossArenaSide.Right;

                        BossArenaSide? redirectedSide = null;
                        if (leftOpen && rightOpen) redirectedSide = preferredByPos;
                        else if (leftOpen) redirectedSide = BossArenaSide.Left;
                        else if (rightOpen) redirectedSide = BossArenaSide.Right;
                        else if (isPostPillarMoonLord) redirectedSide = preferredByPos;

                        if (redirectedSide.HasValue)
                        {
                            npcSide = redirectedSide.Value;
                            PushNpcOutOfCenterSanctuary(npc, npcSide, extraOffsetTiles: Config.OriginSafeOffsetFromSanctuaryTiles);
                        }
                    }

                    // Blokir jika masih di dalam Zona Sanctuary Tengah
                    if (npcSide == BossArenaSide.None)
                    {
                        args.Handled = true;
                        npc.active = false;
                        npc.life = 0;
                        npc.type = 0;
                        NotifyNearestPlayerBlocked(npc.Center, $"[ BOSSING / SANCTUARY ] {npc.FullName} ditolak oleh Zona Sanctuary Tengah (+/-{Config.CenterSanctuaryRadiusTiles} tile sekitar Spawn)! Tarik pertarungan ke KIRI atau KANAN.");
                        return;
                    }

                    // Cek batas maksimal boss per sisi (1 Kiri + 1 Kanan = max 2 di survival)
                    if (CountSlotTrackedBossesOnSide(npcSide) >= Config.MaxBossesPerSide && !isPostPillarMoonLord)
                    {
                        var blocking = GetFirstSlotEncounterOnSide(npcSide);
                        string existingName = blocking?.DisplayName ?? "Boss lain";
                        args.Handled = true;
                        npc.active = false;
                        npc.life = 0;
                        npc.type = 0;
                        NotifyNearestPlayerBlocked(npc.Center, $"[ BOSSING ] Arena {FormatSideLabel(npcSide)} sedang ditempati oleh '{existingName}'! Kalahkan boss tersebut atau pindah ke sisi lain.");
                        return;
                    }
                }

                // Pastikan jika masih menyentuh tepi Sanctuary saat match encounter part, dorong ke sisi arena
                if (existing == null && npcSide == BossArenaSide.None)
                {
                    npcSide = npc.Center.X < (Main.spawnTileX * 16f) ? BossArenaSide.Left : BossArenaSide.Right;
                    PushNpcOutOfCenterSanctuary(npc, npcSide, extraOffsetTiles: Config.OriginSafeOffsetFromSanctuaryTiles);
                }

                bool isNightBoss = _nightOnlyBossTypes.Contains(npc.type);
                if (isNightBoss && Config.KeepNightTimeForNightBosses && Main.dayTime)
                {
                    Main.dayTime = false;
                    Main.time = 16200.0;
                    _lastNightSyncUtc = DateTime.UtcNow;
                    NetMessage.SendData((int)PacketTypes.TimeSet, -1, -1, null);
                }

                // Terapkan HP pengali (default 5x, Damage TETAP 1x bawaan mode!)
                int baseLifeMax = Math.Max(1, npc.lifeMax);
                int scaledLifeMax = baseLifeMax;
                bool hasSharedLife = npc.realLife >= 0 && npc.realLife < Main.maxNPCs && npc.realLife != npc.whoAmI;

                if (!hasSharedLife && Config.BossHpMultiplier > 1.0)
                {
                    long rawScaled = (long)Math.Round(baseLifeMax * Config.BossHpMultiplier);
                    scaledLifeMax = (int)Math.Clamp(rawScaled, 1L, 2_000_000_000L);
                    npc.lifeMax = scaledLifeMax;
                    npc.life = scaledLifeMax;
                }

                if (Config.PreventBossDespawn)
                {
                    npc.timeLeft = 7500;
                }

                if (existing == null)
                {
                    Vector2 safeOrigin = ComputeSafeOriginPosition(npc.position, npc.width, npcSide, npc.Center.Y);
                    string encId = $"{bossKey}#{npcSide}#{Guid.NewGuid():N}";
                    string display = GetBossDisplayName(bossKey, npc);

                    existing = new TrackedBossEncounter
                    {
                        EncounterId = encId,
                        BossKey = bossKey,
                        DisplayName = display,
                        Side = npcSide,
                        CountsTowardArenaSlot = true,
                        IsUnderworldEncounter = (npc.Center.Y / 16f) >= Main.UnderworldLayer,
                        IsNightOnlyBoss = isNightBoss,
                        OriginPosition = safeOrigin,
                        PrimaryNpcIndex = args.NpcId,
                        CreatedUtc = DateTime.UtcNow
                    };
                    _encounters[encId] = existing;

                    if (Config.AnnounceBossEventsInChat)
                    {
                        TSPlayer.All.SendMessage(
                            $"[ BOSSING ] {display} bangkit di Arena {FormatSideLabel(npcSide)}! (HP {Config.BossHpMultiplier:0.#}x: {scaledLifeMax:N0} | DMG Normal Mode)",
                            new Color(251, 191, 36));
                    }
                }
                else if (isNightBoss)
                {
                    existing.IsNightOnlyBoss = true;
                }

                if (!existing.NpcIndices.Contains(args.NpcId))
                {
                    existing.NpcIndices.Add(args.NpcId);
                }
                if (existing.PrimaryNpcIndex < 0)
                {
                    existing.PrimaryNpcIndex = args.NpcId;
                }

                var npcState = new TrackedBossNpcState
                {
                    NpcIndex = args.NpcId,
                    NpcType = npc.type,
                    BossKey = bossKey,
                    EncounterId = existing.EncounterId,
                    BaseLifeMax = baseLifeMax,
                    ScaledLifeMax = scaledLifeMax,
                    LastValidLife = npc.life,
                    LockedIdleLife = npc.life,
                    LastPosition = npc.position,
                    HasSharedRealLife = hasSharedLife,
                    SyncCountdownTicks = 2
                };
                _npcStates[args.NpcId] = npcState;
            }
        }

        private static void OnNpcTransform(NpcTransformationEventArgs args)
        {
            if (!IsBossingActiveInCurrentWorld()) return;
            if (args.NpcId < 0 || args.NpcId >= Main.maxNPCs) return;

            var npc = Main.npc[args.NpcId];
            if (npc == null || !npc.active) return;

            lock (_syncRoot)
            {
                if (_transientBossStates.TryGetValue(args.NpcId, out var tst))
                {
                    bool tShared = npc.realLife >= 0 && npc.realLife < Main.maxNPCs && npc.realLife != npc.whoAmI;
                    if (!tShared && tst.AppliedMultiplier > 1.0 && npc.lifeMax < tst.ScaledLifeMax)
                    {
                        int newBase = Math.Max(1, npc.lifeMax);
                        long rawScaled = (long)Math.Round(newBase * tst.AppliedMultiplier);
                        int newScaledMax = (int)Math.Clamp(rawScaled, 1L, 2_000_000_000L);
                        double ratio = tst.ScaledLifeMax > 0 ? Math.Clamp((double)npc.life / tst.ScaledLifeMax, 0.01, 1.0) : 1.0;

                        npc.lifeMax = newScaledMax;
                        npc.life = Math.Clamp((int)Math.Round(newScaledMax * ratio), 1, newScaledMax);
                        tst.BaseLifeMax = newBase;
                        tst.ScaledLifeMax = newScaledMax;
                    }
                    tst.NpcType = npc.type;
                    tst.HasSharedRealLife = tShared;
                    tst.SyncCountdownTicks = 1;
                    return;
                }

                if (!_npcStates.TryGetValue(args.NpcId, out var st))
                {
                    return;
                }

                if (!IsTrackedBossNpc(npc, out _, out _)) return;

                bool hasSharedLife = npc.realLife >= 0 && npc.realLife < Main.maxNPCs && npc.realLife != npc.whoAmI;
                if (!hasSharedLife && Config.BossHpMultiplier > 1.0)
                {
                    // Jika NPC baru bertransformasi & lifeMax di-reset ke 1x vanilla, kalikan 10x lagi sambil menjaga sisa HP
                    if (npc.lifeMax < st.ScaledLifeMax)
                    {
                        int newBase = Math.Max(1, npc.lifeMax);
                        long rawScaled = (long)Math.Round(newBase * Config.BossHpMultiplier);
                        int newScaledMax = (int)Math.Clamp(rawScaled, 1L, 2_000_000_000L);

                        npc.lifeMax = newScaledMax;
                        int preservedLife = st.LastValidLife > 0
                            ? Math.Clamp(st.LastValidLife, 1, newScaledMax)
                            : newScaledMax;
                        npc.life = preservedLife;

                        st.BaseLifeMax = newBase;
                        st.ScaledLifeMax = newScaledMax;
                        st.LastValidLife = preservedLife;
                    }
                }

                st.NpcType = npc.type;
                st.HasSharedRealLife = hasSharedLife;
                st.SyncCountdownTicks = 1;
            }
        }

        private static void OnNetSendData(SendDataEventArgs args)
        {
            if (_isSendingMappedNpcPacket || args.Handled || !IsBossingActiveInCurrentWorld()) return;
            if (args.MsgId != PacketTypes.NpcUpdate) return;

            int idx = args.number;
            if (idx < 0 || idx >= Main.maxNPCs) return;

            var npc = Main.npc[idx];
            if (npc == null || !npc.active || npc.life <= 0) return;

            int baseLifeMax = 0;
            int scaledLifeMax = 0;

            lock (_syncRoot)
            {
                if (_npcStates.TryGetValue(idx, out var st) &&
                    !st.HasSharedRealLife &&
                    st.BaseLifeMax > 0 &&
                    st.ScaledLifeMax > st.BaseLifeMax)
                {
                    baseLifeMax = st.BaseLifeMax;
                    scaledLifeMax = st.ScaledLifeMax;
                }
                else if (_transientBossStates.TryGetValue(idx, out var tst) &&
                         !tst.HasSharedRealLife &&
                         tst.BaseLifeMax > 0 &&
                         tst.ScaledLifeMax > tst.BaseLifeMax)
                {
                    baseLifeMax = tst.BaseLifeMax;
                    scaledLifeMax = tst.ScaledLifeMax;
                }
            }

            if (baseLifeMax <= 0 || scaledLifeMax <= baseLifeMax) return;

            int savedLifeMax = npc.lifeMax;
            int savedLife = npc.life;
            double ratio = (double)savedLife / scaledLifeMax;
            int visualLife = Math.Clamp((int)Math.Ceiling(baseLifeMax * ratio), 1, baseLifeMax);
            if (savedLife < scaledLifeMax && baseLifeMax > 1 && visualLife >= baseLifeMax)
            {
                visualLife = baseLifeMax - 1;
            }

            _isSendingMappedNpcPacket = true;
            try
            {
                npc.lifeMax = baseLifeMax;
                npc.life = visualLife;
                args.Handled = true;
                NetMessage.SendData(
                    (int)PacketTypes.NpcUpdate,
                    args.remoteClient,
                    args.ignoreClient,
                    args.text,
                    args.number,
                    args.number2,
                    args.number3,
                    args.number4,
                    args.number5,
                    args.number6,
                    args.number7);
            }
            finally
            {
                npc.lifeMax = savedLifeMax;
                npc.life = savedLife;
                _isSendingMappedNpcPacket = false;
            }
        }

        private static void OnNpcStrike(NpcStrikeEventArgs args)
        {
            if (!IsBossingActiveInCurrentWorld() || args.Npc == null) return;

            int idx = args.Npc.whoAmI;
            lock (_syncRoot)
            {
                if (_npcStates.TryGetValue(idx, out var st) &&
                    _encounters.TryGetValue(st.EncounterId, out var enc))
                {
                    if (enc.IsIdleAtOrigin || enc.IsReturningToOrigin || enc.NoChallengerSinceUtc.HasValue)
                    {
                        int attackerIdx = (args.Player != null && args.Player.active) ? args.Player.whoAmI : 255;
                        WakeUpEncounter(enc, attackerIdx, announce: true);
                    }

                    if (st.ScaledLifeMax > st.BaseLifeMax)
                    {
                        st.NeedsVisualBarSync = true;
                        args.Npc.netUpdate = true;
                    }

                    if (st.HasSharedRealLife && args.Npc.realLife >= 0 && args.Npc.realLife < Main.maxNPCs &&
                        _npcStates.TryGetValue(args.Npc.realLife, out var parentSt) &&
                        parentSt.ScaledLifeMax > parentSt.BaseLifeMax)
                    {
                        parentSt.NeedsVisualBarSync = true;
                        Main.npc[args.Npc.realLife].netUpdate = true;
                    }
                }
                else if (_transientBossStates.TryGetValue(idx, out var tst) && tst.ScaledLifeMax > tst.BaseLifeMax)
                {
                    args.Npc.netUpdate = true;
                }
            }
        }

        private static void OnNpcKilled(NpcKilledEventArgs args)
        {
            if (!IsBossingActiveInCurrentWorld() || args.npc == null) return;

            int idx = args.npc.whoAmI;
            lock (_syncRoot)
            {
                _genuinelyKilledSlots.Add(idx);

                if (_transientBossStates.TryGetValue(idx, out var tst))
                {
                    _transientBossStates.Remove(idx);
                    if (tst.Category == BossCategory.CelestialPillar && !HasAnyOtherActivePillar(idx))
                    {
                        _lastLunarPillarsClearedUtc = DateTime.UtcNow;
                        if (Config.AnnounceBossEventsInChat)
                        {
                            TSPlayer.All.SendMessage(
                                $"[ BOSSING ] Ke-4 Celestial Pillars telah hancur! Keluar dari Sanctuary Tengah ke Arena KIRI/KANAN untuk menyambut Moon Lord (HP {Config.BossHpMultiplier:0.#}x)!",
                                new Color(56, 189, 248));
                        }
                    }
                    return;
                }

                if (!_npcStates.TryGetValue(idx, out var st))
                {
                    return;
                }

                _npcStates.Remove(idx);

                if (_encounters.TryGetValue(st.EncounterId, out var enc))
                {
                    enc.NpcIndices.Remove(idx);

                    if (enc.PrimaryNpcIndex == idx)
                    {
                        enc.PrimaryNpcIndex = enc.NpcIndices.FirstOrDefault(i =>
                            i >= 0 && i < Main.maxNPCs && Main.npc[i] != null && Main.npc[i].active, -1);
                    }

                    // Jika semua bagian/segmen encounter sudah benar-benar mati, bebaskan slot arena!
                    if (!HasAnyActiveNpc(enc))
                    {
                        _encounters.Remove(enc.EncounterId);
                        if (Config.AnnounceBossEventsInChat && enc.CountsTowardArenaSlot)
                        {
                            TSPlayer.All.SendMessage(
                                $"[ BOSSING ] {enc.DisplayName} di Arena {FormatSideLabel(enc.Side)} telah ditumbangkan! Slot Arena {FormatSideLabel(enc.Side)} kembali kosong.",
                                new Color(74, 222, 128));
                        }
                    }
                }
            }
        }

        private static bool HasAnyOtherActivePillar(int excludedIdx)
        {
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                if (i == excludedIdx) continue;
                var n = Main.npc[i];
                if (n != null && n.active && _celestialPillarTypes.Contains(n.type))
                {
                    return true;
                }
            }
            return false;
        }

        private static void OnGameUpdate(EventArgs args)
        {
            if (!IsBossingActiveInCurrentWorld()) return;

            DateTime now = DateTime.UtcNow;

            lock (_syncRoot)
            {
                if (_transientBossStates.Count > 0)
                {
                    ProcessTransientBossesTick(now);
                }

                if (_encounters.Count == 0) return;

                MaintainNightTimeIfNightBossActive(now);

                foreach (var enc in _encounters.Values.ToList())
                {
                    ProcessEncounterTick(enc, now);
                }
            }
        }

        private static void MaintainNightTimeIfNightBossActive(DateTime now)
        {
            if (!Config.KeepNightTimeForNightBosses) return;

            bool hasActiveNightBoss = _encounters.Values.Any(e => e.IsNightOnlyBoss && HasAnyActiveNpc(e));
            if (!hasActiveNightBoss) return;

            if (Main.dayTime || Main.time >= 31000.0)
            {
                bool wasDay = Main.dayTime;
                Main.dayTime = false;
                Main.time = 16200.0; // Kunci di tengah malam agar EoC/Twins/Destroyer/Skeletron tidak kabur ke langit

                if (wasDay || (now - _lastNightSyncUtc).TotalSeconds >= 2.0)
                {
                    _lastNightSyncUtc = now;
                    NetMessage.SendData((int)PacketTypes.TimeSet, -1, -1, null);
                }

                if (wasDay && Config.AnnounceBossEventsInChat && (now - _lastNightNoticeUtc).TotalSeconds >= 30.0)
                {
                    _lastNightNoticeUtc = now;
                    TSPlayer.All.SendMessage(
                        "[ BOSSING ] Waktu malam dipertahankan sementara karena Night Boss masih aktif di arena!",
                        new Color(147, 197, 253));
                }
            }
        }

        private static void ProcessTransientBossesTick(DateTime now)
        {
            _transientTickCounter++;
            bool checkSanctuary = Config.KeepEventBossesOutOfCenterSanctuary && (_transientTickCounter % 6) == 0;

            foreach (int idx in _transientBossStates.Keys.ToList())
            {
                if (idx < 0 || idx >= Main.maxNPCs)
                {
                    _transientBossStates.Remove(idx);
                    continue;
                }

                var npc = Main.npc[idx];
                if (npc == null || !npc.active)
                {
                    // Biarkan Event Mini-Boss / Pillar hilang alami saat event berakhir atau fajar tiba
                    _transientBossStates.Remove(idx);
                    continue;
                }

                var tst = _transientBossStates[idx];

                if (!tst.HasSharedRealLife && tst.AppliedMultiplier > 1.0 && npc.lifeMax < tst.ScaledLifeMax)
                {
                    double ratio = tst.ScaledLifeMax > 0 ? Math.Clamp((double)npc.life / tst.ScaledLifeMax, 0.01, 1.0) : 1.0;
                    npc.lifeMax = tst.ScaledLifeMax;
                    npc.life = Math.Clamp((int)Math.Round(tst.ScaledLifeMax * ratio), 1, tst.ScaledLifeMax);
                    tst.SyncCountdownTicks = 1;
                }

                if (tst.SyncCountdownTicks > 0)
                {
                    tst.SyncCountdownTicks--;
                    if (tst.SyncCountdownTicks == 0)
                    {
                        NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                    }
                }

                if (checkSanctuary && IsInsideCenterSanctuaryPx(npc.Center.X, npc.Center.Y))
                {
                    BossArenaSide pushSide = tst.Side != BossArenaSide.None
                        ? tst.Side
                        : (npc.Center.X < (Main.spawnTileX * 16f) ? BossArenaSide.Left : BossArenaSide.Right);
                    PushNpcOutOfCenterSanctuary(npc, pushSide, extraOffsetTiles: 4);
                    if (pushSide == BossArenaSide.Left && npc.velocity.X > 0) npc.velocity.X = -Math.Max(3f, npc.velocity.X * 0.5f);
                    if (pushSide == BossArenaSide.Right && npc.velocity.X < 0) npc.velocity.X = Math.Max(3f, Math.Abs(npc.velocity.X) * 0.5f);
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                }
            }
        }

        #endregion

        #region Core Encounter Tick (10x Check, Anti-Despawn, Boundary, Return & No-Regen Idle)

        private static void ProcessEncounterTick(TrackedBossEncounter enc, DateTime now)
        {
            // 1. Cek kelangsungan hidup semua NPC dalam encounter & pemulihan Anti-Despawn jika hilang paksa
            for (int i = enc.NpcIndices.Count - 1; i >= 0; i--)
            {
                int idx = enc.NpcIndices[i];
                if (idx < 0 || idx >= Main.maxNPCs)
                {
                    enc.NpcIndices.RemoveAt(i);
                    _npcStates.Remove(idx);
                    continue;
                }

                var npc = Main.npc[idx];
                bool hasState = _npcStates.TryGetValue(idx, out var st);

                if (npc == null || !npc.active)
                {
                    if (_genuinelyKilledSlots.Contains(idx) || !Config.PreventBossDespawn || !hasState || st == null || st.LastValidLife <= 0)
                    {
                        enc.NpcIndices.RemoveAt(i);
                        _npcStates.Remove(idx);
                        continue;
                    }

                    // Layer 2 Anti-Despawn: NPC hilang bukan karena mati -> hidupkan kembali dengan HP persis sama!
                    int recoveredIdx = TryRecoverDespawnedBossNpc(enc, st);
                    if (recoveredIdx >= 0)
                    {
                        enc.NpcIndices[i] = recoveredIdx;
                        if (enc.PrimaryNpcIndex == idx) enc.PrimaryNpcIndex = recoveredIdx;
                        _npcStates.Remove(idx);
                        _npcStates[recoveredIdx] = st;
                        st.NpcIndex = recoveredIdx;
                        st.SyncCountdownTicks = 1;
                    }
                    else
                    {
                        enc.NpcIndices.RemoveAt(i);
                        _npcStates.Remove(idx);
                    }
                    continue;
                }

                if (hasState && st != null)
                {
                    // Jaga 10x HP jika ada mekanik vanilla yang tidak sengaja me-reset lifeMax
                    if (!st.HasSharedRealLife && Config.BossHpMultiplier > 1.0 && npc.lifeMax < st.ScaledLifeMax)
                    {
                        npc.lifeMax = st.ScaledLifeMax;
                        npc.life = Math.Clamp(st.LastValidLife, 1, st.ScaledLifeMax);
                        st.SyncCountdownTicks = 1;
                    }

                    // Jaga agar timeLeft tidak pernah menyentuh 0 (Layer 1 Anti-Despawn)
                    if (Config.PreventBossDespawn && npc.timeLeft < 3600)
                    {
                        npc.timeLeft = 7500;
                    }

                    // Kirim sinkronisasi HP Bar awal atau pembaruan proporsional pasca-hit (anti packet-storm 100ms)
                    if (st.SyncCountdownTicks > 0)
                    {
                        st.SyncCountdownTicks--;
                        if (st.SyncCountdownTicks == 0)
                        {
                            st.NeedsVisualBarSync = false;
                            st.LastBarSyncUtc = now;
                            NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                        }
                    }
                    else if (st.NeedsVisualBarSync && (now - st.LastBarSyncUtc).TotalMilliseconds >= 100)
                    {
                        st.NeedsVisualBarSync = false;
                        st.LastBarSyncUtc = now;
                        NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                    }

                    // Catat HP terendah/terbaru dan posisi terakhir
                    if (!enc.IsIdleAtOrigin && !enc.IsReturningToOrigin)
                    {
                        st.LastValidLife = Math.Clamp(npc.life, 1, Math.Max(1, npc.lifeMax));
                    }
                    st.LastPosition = npc.position;
                }
            }

            if (enc.NpcIndices.Count == 0)
            {
                _encounters.Remove(enc.EncounterId);
                return;
            }

            if (enc.PrimaryNpcIndex < 0 || !enc.NpcIndices.Contains(enc.PrimaryNpcIndex))
            {
                enc.PrimaryNpcIndex = enc.NpcIndices[0];
            }

            // 2. Cek apakah ada petarung aktif di sisi arena boss ini
            TSPlayer? activeChallenger = FindChallengerForEncounter(enc);

            if (activeChallenger != null)
            {
                // Jika boss sedang pulang atau sedang diam menunggu, langsung bangunkan!
                if (enc.IsIdleAtOrigin || enc.IsReturningToOrigin || enc.NoChallengerSinceUtc.HasValue)
                {
                    WakeUpEncounter(enc, activeChallenger.Index, announce: enc.IsIdleAtOrigin || enc.IsReturningToOrigin);
                }
                else if (enc.IsNightOnlyBoss)
                {
                    SanitizeNightBossFleeAi(enc, activeChallenger.Index);
                }

                // Paksa batasan Sanctuary Tengah agar boss tidak bisa menyeberang ke tengah atau sisi seberang
                EnforceCenterSanctuaryBoundary(enc, now);
                return;
            }

            // 3. Tidak ada petarung aktif di arena -> mulai grace period lalu pulang ke Origin & Freeze No-Regen
            if (!Config.ReturnToOriginWhenNoPlayers)
            {
                EnforceNoRegenLock(enc);
                EnforceCenterSanctuaryBoundary(enc, now);
                return;
            }

            if (!enc.NoChallengerSinceUtc.HasValue)
            {
                enc.NoChallengerSinceUtc = now;
                SnapshotLockedIdleState(enc);
            }

            // Kunci HP agar tidak pernah regen selama menunggu atau pulang!
            if (Config.PreventRegenWhileIdle)
            {
                EnforceNoRegenLock(enc);
            }

            if (!enc.IsReturningToOrigin && !enc.IsIdleAtOrigin)
            {
                if (enc.IsNightOnlyBoss)
                {
                    SanitizeNightBossFleeAi(enc, targetPlayerIndex: -1);
                }

                if ((now - enc.NoChallengerSinceUtc.Value).TotalSeconds < Config.NoChallengerGraceSeconds)
                {
                    EnforceCenterSanctuaryBoundary(enc, now);
                    return;
                }

                BeginReturnToOrigin(enc, now);
            }

            if (enc.IsReturningToOrigin)
            {
                StepReturnToOrigin(enc, now);
            }
            else if (enc.IsIdleAtOrigin)
            {
                HoldIdleAtOrigin(enc, now);
            }
        }

        private static TSPlayer? FindChallengerForEncounter(TrackedBossEncounter enc)
        {
            float maxDistTiles = (enc.IsIdleAtOrigin || enc.IsReturningToOrigin)
                ? Config.WakeUpDistanceTiles
                : Config.ActiveCombatMaxDistanceTiles;
            float maxDistSq = (maxDistTiles * 16f) * (maxDistTiles * 16f);

            Vector2 refPos = enc.OriginPosition;
            if (enc.PrimaryNpcIndex >= 0 && enc.PrimaryNpcIndex < Main.maxNPCs &&
                Main.npc[enc.PrimaryNpcIndex] != null && Main.npc[enc.PrimaryNpcIndex].active)
            {
                refPos = Main.npc[enc.PrimaryNpcIndex].Center;
            }

            TSPlayer? best = null;
            float bestDistSq = float.MaxValue;

            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active || !player.IsLoggedIn || !player.RealPlayer) continue;
                if (player.Dead || player.TPlayer == null || player.TPlayer.dead) continue;

                // Player yang berada di dalam Sanctuary Tengah tidak memicu pertarungan (zona aman)
                BossArenaSide pSide = ResolveArenaSidePx(player.X, player.Y);
                if (pSide == BossArenaSide.None) continue;

                // Jika bukan pertarungan Underworld, player harus berada di sisi arena yang sama
                if (!enc.IsUnderworldEncounter && pSide != enc.Side) continue;

                float dSq = Vector2.DistanceSquared(player.TPlayer.Center, refPos);
                if (dSq <= maxDistSq && dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = player;
                }
            }

            return best;
        }

        private static void SnapshotLockedIdleState(TrackedBossEncounter enc)
        {
            Vector2 primaryCenter = Vector2.Zero;
            if (enc.PrimaryNpcIndex >= 0 && enc.PrimaryNpcIndex < Main.maxNPCs &&
                Main.npc[enc.PrimaryNpcIndex] != null && Main.npc[enc.PrimaryNpcIndex].active)
            {
                primaryCenter = Main.npc[enc.PrimaryNpcIndex].position;
            }

            foreach (int idx in enc.NpcIndices)
            {
                if (idx < 0 || idx >= Main.maxNPCs) continue;
                var npc = Main.npc[idx];
                if (npc == null || !npc.active) continue;
                if (!_npcStates.TryGetValue(idx, out var st)) continue;

                int minLife = st.LastValidLife > 0 ? Math.Min(npc.life, st.LastValidLife) : npc.life;
                st.LockedIdleLife = Math.Clamp(minLife, 1, Math.Max(1, npc.lifeMax));
                st.LastValidLife = st.LockedIdleLife;

                if (idx == enc.PrimaryNpcIndex)
                {
                    st.IdleAnchorOffset = Vector2.Zero;
                }
                else
                {
                    Vector2 rawOffset = npc.position - primaryCenter;
                    float len = rawOffset.Length();
                    if (len > 360f && len > 0.001f)
                    {
                        rawOffset = Vector2.Normalize(rawOffset) * 360f;
                    }
                    st.IdleAnchorOffset = rawOffset;
                }

                if (!st.HasSavedAi)
                {
                    if (enc.IsNightOnlyBoss)
                    {
                        SanitizeNightBossSingleNpcAi(npc, targetPlayerIndex: -1);
                    }
                    Array.Copy(npc.ai, st.SavedAi, Math.Min(4, npc.ai.Length));
                    Array.Copy(npc.localAI, st.SavedLocalAi, Math.Min(4, npc.localAI.Length));
                    st.HasSavedAi = true;
                }
            }
        }

        private static void EnforceNoRegenLock(TrackedBossEncounter enc)
        {
            if (!Config.PreventRegenWhileIdle) return;

            foreach (int idx in enc.NpcIndices)
            {
                if (idx < 0 || idx >= Main.maxNPCs) continue;
                var npc = Main.npc[idx];
                if (npc == null || !npc.active) continue;
                if (!_npcStates.TryGetValue(idx, out var st)) continue;

                if (st.LockedIdleLife > 0 && npc.life > st.LockedIdleLife)
                {
                    npc.life = st.LockedIdleLife;
                }
                else if (npc.life > 0 && npc.life < st.LockedIdleLife)
                {
                    st.LockedIdleLife = npc.life;
                    st.LastValidLife = npc.life;
                }
            }
        }

        private static void BeginReturnToOrigin(TrackedBossEncounter enc, DateTime now)
        {
            SnapshotLockedIdleState(enc);
            enc.IsReturningToOrigin = true;
            enc.IsIdleAtOrigin = false;
            enc.IdleSinceUtc = null;
            enc.IsRegenerating = false;
            enc.HasAnnouncedRegen = false;
            enc.ReturnSyncTickCounter = 0;

            if (Config.AnnounceBossEventsInChat && enc.CountsTowardArenaSlot &&
                (now - enc.LastReturnNoticeUtc).TotalSeconds >= 8)
            {
                enc.LastReturnNoticeUtc = now;
                var (curHp, maxHp) = GetEncounterHealthSummary(enc);
                string regenNote = Config.EnableDelayedIdleRegen
                    ? $"regen lambat dimulai setelah diam {Config.IdleRegenDelaySeconds:0}s"
                    : "tanpa regen";
                TSPlayer.All.SendMessage(
                    $"[ BOSSING ] Tidak ada petarung di Arena {FormatSideLabel(enc.Side)}! {enc.DisplayName} kembali ke titik spawn awalnya ({curHp:N0}/{maxHp:N0} HP, {regenNote}).",
                    new Color(148, 163, 184));
            }
        }

        private static void StepReturnToOrigin(TrackedBossEncounter enc, DateTime now)
        {
            float speed = Config.ReturnSpeedPixelsPerTick;
            bool allArrived = true;

            enc.ReturnSyncTickCounter++;
            bool sendSyncThisTick = (enc.ReturnSyncTickCounter % 4) == 0;

            foreach (int idx in enc.NpcIndices)
            {
                if (idx < 0 || idx >= Main.maxNPCs) continue;
                var npc = Main.npc[idx];
                if (npc == null || !npc.active) continue;

                _npcStates.TryGetValue(idx, out var st);
                Vector2 targetPos = enc.OriginPosition + (st?.IdleAnchorOffset ?? Vector2.Zero);

                // Pastikan target tetap di sisi arena yang benar dan di luar Sanctuary Tengah
                targetPos = ClampPositionToArenaSide(targetPos, npc.width, enc.Side, enc.IsUnderworldEncounter);

                Vector2 diff = targetPos - npc.position;
                float dist = diff.Length();

                if (dist > speed)
                {
                    allArrived = false;
                    Vector2 dir = diff / dist;
                    npc.velocity = dir * speed;
                    npc.position += npc.velocity;
                }
                else
                {
                    npc.position = targetPos;
                    npc.velocity = Vector2.Zero;
                }

                npc.target = 255;
                if (st != null && st.HasSavedAi)
                {
                    Array.Copy(st.SavedAi, npc.ai, Math.Min(4, npc.ai.Length));
                    Array.Copy(st.SavedLocalAi, npc.localAI, Math.Min(4, npc.localAI.Length));
                }

                if (sendSyncThisTick)
                {
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                }
            }

            if (allArrived)
            {
                enc.IsReturningToOrigin = false;
                enc.IsIdleAtOrigin = true;
                enc.IdleSinceUtc = now;
                enc.LastRegenTickUtc = now;
                enc.IsRegenerating = false;
                enc.HasAnnouncedRegen = false;
                enc.IdleSyncTickCounter = 0;

                foreach (int idx in enc.NpcIndices)
                {
                    if (idx >= 0 && idx < Main.maxNPCs && Main.npc[idx] != null && Main.npc[idx].active)
                    {
                        NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                    }
                }
            }
        }

        private static void HoldIdleAtOrigin(TrackedBossEncounter enc, DateTime now)
        {
            enc.IdleSyncTickCounter++;
            bool periodicSync = (enc.IdleSyncTickCounter % 120) == 0; // 1x per 2 detik, anti-packet storm

            bool shouldTickRegen = false;
            if (Config.EnableDelayedIdleRegen &&
                enc.IdleSinceUtc.HasValue &&
                (now - enc.IdleSinceUtc.Value).TotalSeconds >= Config.IdleRegenDelaySeconds &&
                (now - enc.LastRegenTickUtc).TotalSeconds >= 1.0)
            {
                enc.LastRegenTickUtc = now;
                shouldTickRegen = true;
            }

            bool anyHealedThisTick = false;

            foreach (int idx in enc.NpcIndices)
            {
                if (idx < 0 || idx >= Main.maxNPCs) continue;
                var npc = Main.npc[idx];
                if (npc == null || !npc.active) continue;

                _npcStates.TryGetValue(idx, out var st);
                Vector2 targetPos = enc.OriginPosition + (st?.IdleAnchorOffset ?? Vector2.Zero);
                targetPos = ClampPositionToArenaSide(targetPos, npc.width, enc.Side, enc.IsUnderworldEncounter);

                npc.position = targetPos;
                npc.velocity = Vector2.Zero;
                npc.target = 255;

                if (st != null && st.HasSavedAi)
                {
                    Array.Copy(st.SavedAi, npc.ai, Math.Min(4, npc.ai.Length));
                    Array.Copy(st.SavedLocalAi, npc.localAI, Math.Min(4, npc.localAI.Length));
                }

                bool healedNpc = false;
                if (shouldTickRegen && st != null && !st.HasSharedRealLife && npc.life > 0 && npc.life < npc.lifeMax)
                {
                    int healAmount = Math.Max(1, (int)Math.Round(npc.lifeMax * (Config.IdleRegenPercentPerSecond / 100.0)));
                    int newLife = Math.Min(npc.lifeMax, npc.life + healAmount);
                    if (newLife > npc.life)
                    {
                        npc.life = newLife;
                        st.LockedIdleLife = newLife;
                        st.LastValidLife = newLife;
                        healedNpc = true;
                        anyHealedThisTick = true;
                    }
                }

                if (periodicSync || healedNpc)
                {
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                }
            }

            if (shouldTickRegen)
            {
                enc.IsRegenerating = anyHealedThisTick;
                if (anyHealedThisTick && !enc.HasAnnouncedRegen && Config.AnnounceBossEventsInChat && enc.CountsTowardArenaSlot)
                {
                    enc.HasAnnouncedRegen = true;
                    TSPlayer.All.SendMessage(
                        $"[ BOSSING ] {enc.DisplayName} di Arena {FormatSideLabel(enc.Side)} mulai memulihkan HP secara perlahan (+{Config.IdleRegenPercentPerSecond:0.##}%/detik)...",
                        new Color(148, 163, 184));
                }
            }
        }

        private static void WakeUpEncounter(TrackedBossEncounter enc, int targetPlayerIndex, bool announce)
        {
            bool wasWaiting = enc.IsIdleAtOrigin || enc.IsReturningToOrigin;
            enc.IsIdleAtOrigin = false;
            enc.IsReturningToOrigin = false;
            enc.NoChallengerSinceUtc = null;
            enc.IdleSinceUtc = null;
            enc.IsRegenerating = false;
            enc.HasAnnouncedRegen = false;

            foreach (int idx in enc.NpcIndices)
            {
                if (idx < 0 || idx >= Main.maxNPCs) continue;
                var npc = Main.npc[idx];
                if (npc == null || !npc.active) continue;

                if (_npcStates.TryGetValue(idx, out var st))
                {
                    if (Config.PreventRegenWhileIdle && st.LockedIdleLife > 0 && npc.life > st.LockedIdleLife)
                    {
                        npc.life = st.LockedIdleLife;
                    }
                    st.LastValidLife = npc.life;
                    st.HasSavedAi = false;
                }

                if (targetPlayerIndex >= 0 && targetPlayerIndex < 255)
                {
                    npc.target = targetPlayerIndex;
                }

                if (enc.IsNightOnlyBoss)
                {
                    SanitizeNightBossSingleNpcAi(npc, targetPlayerIndex);
                }

                npc.timeLeft = 7500;
                npc.netUpdate = true;
                NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
            }

            if (announce && wasWaiting && Config.AnnounceBossEventsInChat && enc.CountsTowardArenaSlot)
            {
                var (curHp, maxHp) = GetEncounterHealthSummary(enc);
                TSPlayer.All.SendMessage(
                    $"[ BOSSING ] {enc.DisplayName} di Arena {FormatSideLabel(enc.Side)} kembali bangkit bertarung! (HP tersisa: {curHp:N0}/{maxHp:N0})",
                    new Color(251, 191, 36));
            }
        }

        private static void SanitizeNightBossFleeAi(TrackedBossEncounter enc, int targetPlayerIndex)
        {
            foreach (int idx in enc.NpcIndices)
            {
                if (idx < 0 || idx >= Main.maxNPCs) continue;
                var npc = Main.npc[idx];
                if (npc == null || !npc.active) continue;

                SanitizeNightBossSingleNpcAi(npc, targetPlayerIndex);
            }
        }

        private static void SanitizeNightBossSingleNpcAi(NPC npc, int targetPlayerIndex)
        {
            if (npc == null || !npc.active) return;

            bool resetSync = false;

            if (npc.type == NPCID.EyeofCthulhu || npc.type == NPCID.Retinazer || npc.type == NPCID.Spazmatism)
            {
                // ai[0] < 0 (-1) adalah state kabur lurus ke langit! Kembalikan ke phase combat aktif.
                if (npc.ai[0] < 0f)
                {
                    bool phase2 = npc.life <= (npc.lifeMax * 0.5);
                    npc.ai[0] = phase2 ? 3f : 0f;
                    npc.ai[1] = 0f;
                    npc.ai[2] = 0f;
                    if (npc.velocity.Y < -4f) npc.velocity.Y = 0f;
                    resetSync = true;
                }
            }
            else if (npc.type == NPCID.SkeletronHead)
            {
                if (npc.ai[0] < 0f || npc.ai[1] == 3f)
                {
                    npc.ai[0] = 0f;
                    npc.ai[1] = 0f;
                    npc.ai[2] = 0f;
                    if (npc.velocity.Y < -4f) npc.velocity.Y = 0f;
                    resetSync = true;
                }
            }
            else if (npc.type == NPCID.SkeletronPrime)
            {
                if (npc.ai[0] < 0f || npc.ai[0] == 3f)
                {
                    npc.ai[0] = 1f;
                    npc.ai[1] = 0f;
                    if (npc.velocity.Y < -4f) npc.velocity.Y = 0f;
                    resetSync = true;
                }
            }

            if (targetPlayerIndex >= 0 && targetPlayerIndex < 255)
            {
                npc.target = targetPlayerIndex;
            }

            if (resetSync)
            {
                npc.netUpdate = true;
            }
        }

        private static void EnforceCenterSanctuaryBoundary(TrackedBossEncounter enc, DateTime now)
        {
            if (enc.IsUnderworldEncounter && !Config.CenterSanctuaryIncludeUnderworld)
            {
                return;
            }

            var (leftLimitPx, rightLimitPx) = GetCenterSanctuaryBoundsPx();
            bool pushedAny = false;
            Vector2? burstPos = null;

            foreach (int idx in enc.NpcIndices)
            {
                if (idx < 0 || idx >= Main.maxNPCs) continue;
                var npc = Main.npc[idx];
                if (npc == null || !npc.active) continue;

                if (!Config.CenterSanctuaryIncludeUnderworld && (npc.Center.Y / 16f) >= Main.UnderworldLayer)
                {
                    continue;
                }

                if (enc.Side == BossArenaSide.Left && npc.position.X + npc.width > leftLimitPx)
                {
                    npc.position.X = leftLimitPx - npc.width - 16f;
                    if (npc.velocity.X > 0) npc.velocity.X = -Math.Max(4f, npc.velocity.X * 0.6f);
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                    pushedAny = true;
                    burstPos ??= new Vector2(leftLimitPx, npc.Center.Y);
                }
                else if (enc.Side == BossArenaSide.Right && npc.position.X < rightLimitPx)
                {
                    npc.position.X = rightLimitPx + 16f;
                    if (npc.velocity.X < 0) npc.velocity.X = Math.Max(4f, Math.Abs(npc.velocity.X) * 0.6f);
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
                    pushedAny = true;
                    burstPos ??= new Vector2(rightLimitPx, npc.Center.Y);
                }
            }

            if (pushedAny && burstPos.HasValue)
            {
                if (Config.ShowCenterSanctuaryVisuals && (now - _lastBarrierVisualUtc).TotalMilliseconds >= 1500)
                {
                    _lastBarrierVisualUtc = now;
                    SpawnWardRing(burstPos.Value, ProjectileID.HallowStar, 6, 42f, 22, 2.0f);
                }

                if ((now - _lastBarrierChatUtc).TotalSeconds >= 6)
                {
                    _lastBarrierChatUtc = now;
                    TSPlayer.All.SendMessage(
                        $"[ SANCTUARY ] {enc.DisplayName} dipukul mundur oleh batas Zona Sanctuary Tengah!",
                        new Color(125, 211, 252));
                }
            }
        }

        private static int TryRecoverDespawnedBossNpc(TrackedBossEncounter enc, TrackedBossNpcState st)
        {
            Vector2 spawnPos = (enc.IsIdleAtOrigin || enc.IsReturningToOrigin)
                ? enc.OriginPosition + st.IdleAnchorOffset
                : st.LastPosition;

            spawnPos = ClampPositionToArenaSide(spawnPos, 64, enc.Side, enc.IsUnderworldEncounter);
            int spawnX = (int)Math.Clamp(spawnPos.X, 640f, (Main.maxTilesX * 16f) - 640f);
            int spawnY = (int)Math.Clamp(spawnPos.Y, 640f, (Main.maxTilesY * 16f) - 640f);

            _internalRecovering = true;
            try
            {
                int newIdx = NPC.NewNPC(Projectile.GetNoneSource(), spawnX, spawnY, st.NpcType);
                if (newIdx < 0 || newIdx >= Main.maxNPCs) return -1;

                var newNpc = Main.npc[newIdx];
                if (newNpc == null || !newNpc.active) return -1;

                if (!st.HasSharedRealLife && st.ScaledLifeMax > 0)
                {
                    newNpc.lifeMax = st.ScaledLifeMax;
                    newNpc.life = Math.Clamp(st.LastValidLife, 1, st.ScaledLifeMax);
                }
                if (st.HasSavedAi)
                {
                    Array.Copy(st.SavedAi, newNpc.ai, Math.Min(4, newNpc.ai.Length));
                    Array.Copy(st.SavedLocalAi, newNpc.localAI, Math.Min(4, newNpc.localAI.Length));
                }
                newNpc.position = spawnPos;
                newNpc.velocity = Vector2.Zero;
                newNpc.timeLeft = 7500;
                NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, newIdx);
                return newIdx;
            }
            catch
            {
                return -1;
            }
            finally
            {
                _internalRecovering = false;
            }
        }

        #endregion

        #region Position & Notification Helpers

        private static Vector2 ComputeSafeOriginPosition(Vector2 rawPos, int npcWidth, BossArenaSide side, float worldY)
        {
            Vector2 clamped = ClampPositionToArenaSide(rawPos, npcWidth, side, (worldY / 16f) >= Main.UnderworldLayer);
            return new Vector2(
                Math.Clamp(clamped.X, 480f, (Main.maxTilesX * 16f) - 480f),
                Math.Clamp(clamped.Y, 480f, (Main.maxTilesY * 16f) - 480f));
        }

        private static Vector2 ClampPositionToArenaSide(Vector2 pos, int npcWidth, BossArenaSide side, bool isUnderworld)
        {
            if (side == BossArenaSide.None || (isUnderworld && !Config.CenterSanctuaryIncludeUnderworld))
            {
                return pos;
            }

            var (leftLimitPx, rightLimitPx) = GetCenterSanctuaryBoundsPx();
            float bufferPx = 32f;

            if (side == BossArenaSide.Left && pos.X + npcWidth > leftLimitPx - bufferPx)
            {
                pos.X = leftLimitPx - npcWidth - bufferPx;
            }
            else if (side == BossArenaSide.Right && pos.X < rightLimitPx + bufferPx)
            {
                pos.X = rightLimitPx + bufferPx;
            }

            pos.X = Math.Clamp(pos.X, 480f, (Main.maxTilesX * 16f) - 480f);
            pos.Y = Math.Clamp(pos.Y, 480f, (Main.maxTilesY * 16f) - 480f);
            return pos;
        }

        private static void PushNpcOutOfCenterSanctuary(NPC npc, BossArenaSide targetSide, int extraOffsetTiles)
        {
            var (leftLimitPx, rightLimitPx) = GetCenterSanctuaryBoundsPx();
            float extraPx = extraOffsetTiles * 16f;

            if (targetSide == BossArenaSide.Left)
            {
                npc.position.X = Math.Max(480f, leftLimitPx - npc.width - extraPx);
            }
            else if (targetSide == BossArenaSide.Right)
            {
                npc.position.X = Math.Min((Main.maxTilesX * 16f) - 480f, rightLimitPx + extraPx);
            }
        }

        private static TSPlayer? FindNearestRealAlivePlayer(Vector2 center)
        {
            TSPlayer? nearest = null;
            float bestDistSq = 3200f * 3200f;

            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active || !p.IsLoggedIn || !p.RealPlayer || p.TPlayer == null) continue;
                float dSq = Vector2.DistanceSquared(p.TPlayer.Center, center);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    nearest = p;
                }
            }
            return nearest;
        }

        private static void NotifyNearestPlayerBlocked(Vector2 npcCenter, string message)
        {
            var nearest = FindNearestRealAlivePlayer(npcCenter);
            if (nearest != null)
            {
                NotifyPlayerThrottled(nearest, message, "ARENA TERBATAS", spawnBurst: true);
            }
        }

        private static void NotifyPlayerThrottled(TSPlayer player, string chatMessage, string combatTag, bool spawnBurst)
        {
            if (player == null || !player.Active || !player.RealPlayer) return;

            DateTime now = DateTime.UtcNow;
            if (_lastPlayerNoticeUtc.TryGetValue(player.Index, out DateTime last) &&
                (now - last).TotalMilliseconds < 1500)
            {
                return;
            }
            _lastPlayerNoticeUtc[player.Index] = now;

            player.SendMessage(chatMessage, new Color(251, 191, 36));
            player.SendData(
                PacketTypes.CreateCombatTextExtended,
                $"[ {combatTag} ]",
                (int)Color.OrangeRed.PackedValue,
                player.X,
                player.Y - 32f);

            if (spawnBurst && Config.ShowCenterSanctuaryVisuals && player.TPlayer != null)
            {
                SpawnWardRing(player.TPlayer.Center, ProjectileID.HallowStar, 6, 44f, 22, 2.0f);
            }
        }

        private static void SpawnWardRing(Vector2 center, int projId, int count, float radius, int timeLeft, float outwardSpeed)
        {
            for (int i = 0; i < count; i++)
            {
                double angle = (Math.PI * 2.0 / count) * i;
                Vector2 offset = new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                Vector2 vel = new Vector2((float)Math.Cos(angle) * outwardSpeed, (float)Math.Sin(angle) * outwardSpeed);
                int idx = Projectile.NewProjectile(Projectile.GetNoneSource(), center + offset, vel, projId, 0, 0f, Main.myPlayer);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    var proj = Main.projectile[idx];
                    proj.friendly = true;
                    proj.hostile = false;
                    proj.damage = 0;
                    proj.knockBack = 0f;
                    proj.timeLeft = timeLeft;
                    NetMessage.SendData((int)PacketTypes.ProjectileNew, -1, -1, null, idx);
                }
            }
        }

        #endregion
    }
}
