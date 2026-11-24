/*
 * =====================================================================
 *  SanctuaryModule.cs - Engine Segel Sihir Pelindung Sang Legenda
 *  Namespace: Univers.Sanctuary (.NET 9.0 / TShock 6.x)
 *  Melindungi world Lobby dari boss, monster, event, & summon item/cmd
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
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Univers.MultiWorld;

namespace Univers.Sanctuary
{
    public static class SanctuaryModule
    {
        public static SanctuaryConfig Config { get; private set; } = new();
        public static long TotalBlockedAttempts { get; private set; }

        private static string _configPath = string.Empty;
        private static bool _initialized;
        private static bool _initialSweepDone;

        private static DateTime _lastSweepUtc = DateTime.MinValue;
        private static DateTime _lastAmbientPulseUtc = DateTime.MinValue;
        private static DateTime _lastBanishVisualUtc = DateTime.MinValue;
        private static DateTime _lastBossSpawnNoticeUtc = DateTime.MinValue;

        private static readonly ConcurrentDictionary<int, DateTime> _lastPlayerNoticeUtc = new();
        private static readonly ConcurrentDictionary<int, DateTime> _lastManualPulseUtc = new();

        // Daftar resmi ItemID pemanggil Boss & Event yang disegel di Lobby
        private static readonly HashSet<int> _blockedSummonItems = new()
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
            ItemID.CelestialSigil,
            ItemID.GoblinBattleStandard,
            ItemID.PirateMap,
            ItemID.SnowGlobe,
            ItemID.PumpkinMoonMedallion,
            ItemID.NaughtyPresent,
            ItemID.SolarTablet,
            ItemID.BloodMoonStarter,
            ItemID.DD2ElderCrystal,
            ItemID.DD2ElderCrystalStand
        };

        // NPC segmen/minion boss yang wajib ikut ditolak walau flag npc.boss == false
        private static readonly HashSet<int> _explicitBossTypes = new()
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
            NPCID.IceQueen,
            NPCID.MartianSaucer,
            NPCID.MartianSaucerCore,
            NPCID.MartianSaucerTurret,
            NPCID.MartianSaucerCannon,
            NPCID.DD2Betsy,
            NPCID.LunarTowerSolar,
            NPCID.LunarTowerVortex,
            NPCID.LunarTowerNebula,
            NPCID.LunarTowerStardust,
            NPCID.HallowBoss,
            NPCID.CultistBoss,
            NPCID.MoonLordCore,
            NPCID.MoonLordHead,
            NPCID.MoonLordHand,
            NPCID.DungeonGuardian
        };

        public static bool IsSanctuaryActiveInCurrentWorld()
        {
            return _initialized && Config.IsWorldProtected(MultiWorldModule.CurrentWorldId);
        }

        public static void Initialize(string savePath)
        {
            if (_initialized) return;

            string universDir = Path.Combine(savePath, "Univers");
            Directory.CreateDirectory(universDir);

            _configPath = Path.Combine(universDir, "SanctuaryConfig.json");
            Config = SanctuaryConfig.Load(_configPath);

            ServerApi.Hooks.NpcSpawn.Register(UniversPlugin.Instance, OnNpcSpawn);
            ServerApi.Hooks.NpcTransform.Register(UniversPlugin.Instance, OnNpcTransform);
            ServerApi.Hooks.NetGetData.Register(UniversPlugin.Instance, OnNetGetData);
            ServerApi.Hooks.GameUpdate.Register(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.ServerLeave.Register(UniversPlugin.Instance, OnServerLeave);
            ServerApi.Hooks.ServerCommand.Register(UniversPlugin.Instance, OnServerCommand);
            PlayerHooks.PlayerCommand += OnPlayerCommand;

            SanctuaryCommands.Register();

            _initialSweepDone = false;
            _lastSweepUtc = DateTime.UtcNow;
            _lastAmbientPulseUtc = DateTime.UtcNow;
            _initialized = true;

            string status = IsSanctuaryActiveInCurrentWorld()
                ? $"AKTIF MELINDUNGI world '{MultiWorldModule.CurrentWorldId}' ({Config.BarrierName})"
                : $"STANDBY di world '{MultiWorldModule.CurrentWorldId}' (Aktif di: {string.Join(", ", Config.ProtectedWorldIds)})";
            TShock.Log.ConsoleInfo($"[Univers.Sanctuary] {status}.");
        }

        public static void Reload()
        {
            if (!string.IsNullOrEmpty(_configPath))
            {
                Config = SanctuaryConfig.Load(_configPath);
            }
            _initialSweepDone = false;
            TShock.Log.ConsoleInfo("[Univers.Sanctuary] Konfigurasi SanctuaryConfig.json dimuat ulang.");
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
            try { ServerApi.Hooks.NetGetData.Deregister(UniversPlugin.Instance, OnNetGetData); } catch { }
            try { ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate); } catch { }
            try { ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnPlayerGreet); } catch { }
            try { ServerApi.Hooks.ServerLeave.Deregister(UniversPlugin.Instance, OnServerLeave); } catch { }
            try { ServerApi.Hooks.ServerCommand.Deregister(UniversPlugin.Instance, OnServerCommand); } catch { }
            try { PlayerHooks.PlayerCommand -= OnPlayerCommand; } catch { }

            _lastPlayerNoticeUtc.Clear();
            _lastManualPulseUtc.Clear();
        }

        #region Hook Handlers

        private static void OnPlayerGreet(GreetPlayerEventArgs args)
        {
            if (!IsSanctuaryActiveInCurrentWorld()) return;

            var player = TShock.Players[args.Who];
            if (player == null || !player.Active || !player.RealPlayer) return;

            if (Config.ShowLoreOnJoin)
            {
                player.SendMessage($"[ SANCTUARY ] Kamu memasuki '{MultiWorldModule.CurrentWorldId}' yang dilindungi oleh {Config.BarrierName} milik {Config.GuardianName}.", new Color(186, 230, 253));
                player.SendMessage("[ SANCTUARY ] Semua boss, monster, dan event tersegel abadi dari dunia ini. Ketik /ward untuk membaca kisah segel.", new Color(167, 243, 208));
            }

            if (Config.ShowWardVisuals && player.TPlayer != null)
            {
                SpawnWardRing(player.TPlayer.Center, ProjectileID.HallowStar, 4, 46f, 22, 1.4f);
            }
        }

        private static void OnServerLeave(LeaveEventArgs args)
        {
            _lastPlayerNoticeUtc.TryRemove(args.Who, out _);
            _lastManualPulseUtc.TryRemove(args.Who, out _);
        }

        private static void OnNpcSpawn(NpcSpawnEventArgs args)
        {
            if (args.Handled || !IsSanctuaryActiveInCurrentWorld()) return;
            if (args.NpcId < 0 || args.NpcId >= Main.maxNPCs) return;

            var npc = Main.npc[args.NpcId];
            if (npc == null) return;

            if (!IsForbiddenNpc(npc, out bool isBoss)) return;

            args.Handled = true;
            npc.active = false;
            npc.life = 0;
            npc.type = 0;

            TotalBlockedAttempts++;
            TriggerBanishVisualThrottled(npc.Center);

            if (isBoss)
            {
                NotifyNearestPlayerOfBlockedBoss(npc.Center, npc.FullName);
            }
        }

        private static void OnNpcTransform(NpcTransformationEventArgs args)
        {
            if (args.Handled || !IsSanctuaryActiveInCurrentWorld()) return;
            if (args.NpcId < 0 || args.NpcId >= Main.maxNPCs) return;

            var npc = Main.npc[args.NpcId];
            if (npc == null) return;

            if (!IsForbiddenNpc(npc, out bool isBoss)) return;

            args.Handled = true;
            npc.active = false;
            npc.life = 0;
            NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, args.NpcId);

            TotalBlockedAttempts++;
            TriggerBanishVisualThrottled(npc.Center);

            if (isBoss)
            {
                NotifyNearestPlayerOfBlockedBoss(npc.Center, npc.FullName);
            }
        }

        private static void OnNetGetData(GetDataEventArgs args)
        {
            if (args.Handled || !IsSanctuaryActiveInCurrentWorld()) return;

            var player = TShock.Players[args.Msg.whoAmI];
            if (player == null || !player.Active || !player.IsLoggedIn || !player.RealPlayer) return;

            int msgId = (int)args.MsgID;

            // Packet 61: SpawnBossorInvasion (Boss summons & Event/Invasion summons)
            if (msgId == 61)
            {
                if (args.Length < 4) return;
                try
                {
                    using var reader = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, args.Length));
                    short _playerId = reader.ReadInt16();
                    short spawnType = reader.ReadInt16();

                    if (spawnType < 0)
                    {
                        // Abaikan unlock damai seperti Combat Book (-11) atau Peddler's Satchel (-12)
                        if (spawnType == -11 || spawnType == -12) return;

                        if (Config.BlockEventsAndInvasions || Config.BlockSummonItems)
                        {
                            args.Handled = true;
                            TotalBlockedAttempts++;
                            NotifyBlockedAttempt(
                                player,
                                $"[ SANCTUARY ] Gelombang event & invasi gagal menembus {Config.BarrierName}! Dunia '{MultiWorldModule.CurrentWorldId}' tetap damai di bawah penjagaan {Config.GuardianName}.",
                                "TERSEGEL SIHIR!",
                                triggerBurst: true);
                        }
                        return;
                    }

                    if (spawnType > 0)
                    {
                        // Izinkan Town Pet License jika ada
                        if (spawnType == NPCID.TownCat || spawnType == NPCID.TownDog || spawnType == NPCID.TownBunny)
                        {
                            return;
                        }

                        if (Config.BlockBosses || Config.BlockSummonItems)
                        {
                            args.Handled = true;
                            TotalBlockedAttempts++;
                            NotifyBlockedAttempt(
                                player,
                                $"[ SANCTUARY ] Pemanggilan boss ditolak oleh {Config.BarrierName} milik {Config.GuardianName}! Tidak ada boss yang dapat memasuki '{MultiWorldModule.CurrentWorldId}'.",
                                "SIHIR PELINDUNG!",
                                triggerBurst: true);
                        }
                    }
                }
                catch { }
                return;
            }

            // Packet 113: CrystalInvasionStart (Old One's Army / Eternia Crystal Stand)
            if (msgId == 113)
            {
                if (Config.BlockEventsAndInvasions || Config.BlockSummonItems)
                {
                    args.Handled = true;
                    TotalBlockedAttempts++;
                    NotifyBlockedAttempt(
                        player,
                        $"[ SANCTUARY ] Ritual Eternia Crystal ditolak oleh {Config.BarrierName}! Event invasi terlarang di '{MultiWorldModule.CurrentWorldId}'.",
                        "TERSEGEL SIHIR!",
                        triggerBurst: true);
                }
                return;
            }

            // Packet 71: ReleaseNPC (Blokir pelepasan Truffle Worm / Prismatic Lacewing)
            if (msgId == 71)
            {
                if ((!Config.BlockBosses && !Config.BlockSummonItems) || args.Length < 10) return;
                try
                {
                    using var reader = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, args.Length));
                    reader.ReadInt32(); // x
                    reader.ReadInt32(); // y
                    short npcType = reader.ReadInt16();

                    if (npcType == NPCID.TruffleWorm || npcType == NPCID.EmpressButterfly)
                    {
                        args.Handled = true;
                        TotalBlockedAttempts++;
                        NotifyBlockedAttempt(
                            player,
                            $"[ SANCTUARY ] Makhluk pemicu boss dilarang dilepaskan di dalam {Config.BarrierName}!",
                            "TERSEGEL SIHIR!",
                            triggerBurst: true);
                    }
                }
                catch { }
                return;
            }

            // Packet 41: PlayerAnimation -> beri visual & notif seketika saat player mencoba memakai item pemanggil boss/event
            if (msgId == 41 && Config.BlockSummonItems)
            {
                Item? heldItem = player.SelectedItem;
                if (heldItem == null || heldItem.type <= 0 || !_blockedSummonItems.Contains(heldItem.type))
                {
                    var cursorItem = player.TPlayer?.inventory != null && player.TPlayer.inventory.Length > 58
                        ? player.TPlayer.inventory[58]
                        : null;
                    if (cursorItem != null && cursorItem.type > 0 && _blockedSummonItems.Contains(cursorItem.type))
                    {
                        heldItem = cursorItem;
                    }
                    else
                    {
                        heldItem = null;
                    }
                }

                if (heldItem != null)
                {
                    TotalBlockedAttempts++;
                    NotifyBlockedAttempt(
                        player,
                        $"[ SANCTUARY ] Item '{heldItem.Name}' tidak bereaksi! Dunia '{MultiWorldModule.CurrentWorldId}' dilindungi oleh {Config.BarrierName} milik {Config.GuardianName}.",
                        "TERPROTEKSI!",
                        triggerBurst: true);
                }
            }
        }

        private static void OnPlayerCommand(PlayerCommandEventArgs args)
        {
            if (args.Handled || args.Player == null) return;
            if (!IsSanctuaryActiveInCurrentWorld()) return;

            string cmd = args.CommandName?.ToLowerInvariant() ?? string.Empty;
            if (!Config.IsCommandBlocked(cmd)) return;

            if (Config.AllowAdminCommandBypass && args.Player.HasPermission("univers.sanctuary.bypass"))
            {
                return;
            }

            args.Handled = true;
            TotalBlockedAttempts++;

            if (args.Player.RealPlayer && args.Player.Active)
            {
                NotifyBlockedAttempt(
                    args.Player,
                    $"[ SANCTUARY ] Perintah '/{cmd}' ditolak oleh {Config.BarrierName}! Boss, monster, dan event tidak dapat masuk ke dunia '{MultiWorldModule.CurrentWorldId}' yang dijaga {Config.GuardianName}.",
                    "DITOLAK SEGEL!",
                    triggerBurst: true);
            }
            else
            {
                args.Player.SendErrorMessage($"[ SANCTUARY ] Perintah '/{cmd}' ditolak oleh {Config.BarrierName} di world '{MultiWorldModule.CurrentWorldId}'.");
            }
        }

        private static void OnServerCommand(CommandEventArgs args)
        {
            if (args.Handled || !IsSanctuaryActiveInCurrentWorld()) return;
            if (string.IsNullOrWhiteSpace(args.Command)) return;

            string trimmed = args.Command.Trim().TrimStart('/', '.');
            if (string.IsNullOrWhiteSpace(trimmed)) return;

            int spaceIdx = trimmed.IndexOf(' ');
            string cmdName = (spaceIdx > 0 ? trimmed.Substring(0, spaceIdx) : trimmed).ToLowerInvariant();

            if (!Config.IsCommandBlocked(cmdName)) return;

            args.Handled = true;
            TotalBlockedAttempts++;
            TShock.Log.ConsoleWarn($"[Univers.Sanctuary] Perintah konsol '/{cmdName}' diblokir karena world '{MultiWorldModule.CurrentWorldId}' dilindungi oleh {Config.BarrierName}.");
        }

        private static void OnGameUpdate(EventArgs args)
        {
            if (!IsSanctuaryActiveInCurrentWorld()) return;

            DateTime now = DateTime.UtcNow;

            if (!_initialSweepDone || (now - _lastSweepUtc).TotalMilliseconds >= 1000)
            {
                _initialSweepDone = true;
                _lastSweepUtc = now;
                PerformSweepInternal();
            }

            if (Config.ShowWardVisuals && Config.AmbientBarrierPulseSeconds > 0 &&
                (now - _lastAmbientPulseUtc).TotalSeconds >= Math.Max(3, Config.AmbientBarrierPulseSeconds))
            {
                _lastAmbientPulseUtc = now;
                TryEmitAmbientBarrierPulse();
            }
        }

        #endregion

        #region Core Protection & Sweep

        public static int ForceSanctuarySweep(out bool clearedEvent)
        {
            clearedEvent = ClearActiveWorldEvents();
            return BanishForbiddenNpcs(emitVisual: true);
        }

        private static void PerformSweepInternal()
        {
            if (Config.BlockEventsAndInvasions)
            {
                ClearActiveWorldEvents();
            }

            if (Config.BlockBosses || Config.BlockHostileMobs)
            {
                BanishForbiddenNpcs(emitVisual: true);
            }
        }

        private static bool ClearActiveWorldEvents()
        {
            if (!Config.BlockEventsAndInvasions) return false;

            bool changed = false;

            if (Main.bloodMoon)
            {
                Main.bloodMoon = false;
                changed = true;
            }
            if (Main.eclipse)
            {
                Main.eclipse = false;
                changed = true;
            }
            if (Main.slimeRain)
            {
                Main.slimeRain = false;
                changed = true;
            }
            if (Main.pumpkinMoon)
            {
                Main.pumpkinMoon = false;
                changed = true;
            }
            if (Main.snowMoon)
            {
                Main.snowMoon = false;
                changed = true;
            }
            if (Main.invasionType != 0 || Main.invasionSize > 0)
            {
                Main.invasionType = 0;
                Main.invasionSize = 0;
                Main.invasionDelay = 0;
                changed = true;
            }

            if (changed)
            {
                TotalBlockedAttempts++;
                NetMessage.SendData((int)PacketTypes.WorldInfo);
                TShock.Log.ConsoleInfo($"[Univers.Sanctuary] Event/invasi aktif di '{MultiWorldModule.CurrentWorldId}' dibubarkan oleh {Config.BarrierName}.");
            }

            return changed;
        }

        private static int BanishForbiddenNpcs(bool emitVisual)
        {
            int count = 0;
            Vector2? firstBanishedPos = null;

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var npc = Main.npc[i];
                if (npc == null || !npc.active) continue;

                if (!IsForbiddenNpc(npc, out _)) continue;

                firstBanishedPos ??= npc.Center;
                npc.active = false;
                npc.life = 0;
                NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, i);
                count++;
            }

            if (count > 0)
            {
                TotalBlockedAttempts += count;
                if (emitVisual && firstBanishedPos.HasValue)
                {
                    TriggerBanishVisualThrottled(firstBanishedPos.Value);
                }
            }

            return count;
        }

        private static bool IsForbiddenNpc(NPC npc, out bool isBoss)
        {
            isBoss = npc.boss || _explicitBossTypes.Contains(npc.type);
            if (isBoss)
            {
                return Config.BlockBosses;
            }

            if (!Config.BlockHostileMobs)
            {
                return false;
            }

            // Pertahankan Town NPC, Bound NPC, Town Pet, dan makhluk ramah tanpa damage
            if (npc.friendly || npc.townNPC || npc.isLikeATownNPC)
            {
                return false;
            }

            // Izinkan critter pasif biasa, kecuali pemicu boss (Truffle Worm / Prismatic Lacewing)
            if (npc.type == NPCID.TruffleWorm || npc.type == NPCID.EmpressButterfly)
            {
                isBoss = true;
                return Config.BlockBosses || Config.BlockSummonItems;
            }

            return npc.damage > 0;
        }

        #endregion

        #region Visual Effects & Notifications

        public static bool TryTriggerManualPulse(TSPlayer player, out int waitSeconds)
        {
            waitSeconds = 0;
            if (player == null || !player.Active || !player.IsLoggedIn || player.TPlayer == null) return false;

            DateTime now = DateTime.UtcNow;
            if (_lastManualPulseUtc.TryGetValue(player.Index, out DateTime last) &&
                (now - last).TotalSeconds < 8)
            {
                waitSeconds = Math.Max(1, (int)Math.Ceiling(8.0 - (now - last).TotalSeconds));
                return false;
            }

            _lastManualPulseUtc[player.Index] = now;
            SpawnReactiveShieldBurst(player.TPlayer.Center);
            player.SendData(
                PacketTypes.CreateCombatTextExtended,
                "[ SEGEL PELINDUNG ]",
                (int)new Color(125, 211, 252).PackedValue,
                player.X,
                player.Y - 34f);
            return true;
        }

        private static void NotifyBlockedAttempt(TSPlayer player, string chatMessage, string combatTag, bool triggerBurst)
        {
            if (player == null || !player.Active || !player.RealPlayer) return;

            DateTime now = DateTime.UtcNow;
            if (_lastPlayerNoticeUtc.TryGetValue(player.Index, out DateTime lastNotice) &&
                (now - lastNotice).TotalMilliseconds < 1500)
            {
                return;
            }
            _lastPlayerNoticeUtc[player.Index] = now;

            player.SendMessage(chatMessage, new Color(125, 211, 252));

            if (Config.ShowWardVisuals && player.TPlayer != null)
            {
                player.SendData(
                    PacketTypes.CreateCombatTextExtended,
                    $"[ {combatTag} ]",
                    (int)Color.Gold.PackedValue,
                    player.X,
                    player.Y - 32f);

                if (triggerBurst)
                {
                    SpawnReactiveShieldBurst(player.TPlayer.Center);
                }
            }
        }

        private static void NotifyNearestPlayerOfBlockedBoss(Vector2 npcCenter, string? bossName)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastBossSpawnNoticeUtc).TotalMilliseconds < 1500) return;

            TSPlayer? nearest = null;
            float bestDistSq = 2800f * 2800f;

            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active || !p.IsLoggedIn || !p.RealPlayer || p.TPlayer == null) continue;
                float dSq = Vector2.DistanceSquared(p.TPlayer.Center, npcCenter);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    nearest = p;
                }
            }

            if (nearest != null)
            {
                _lastBossSpawnNoticeUtc = now;
                string label = string.IsNullOrWhiteSpace(bossName) ? "Boss" : bossName;
                NotifyBlockedAttempt(
                    nearest,
                    $"[ SANCTUARY ] {label} mencoba menembus perbatasan, namun langsung dipukul mundur oleh {Config.BarrierName} milik {Config.GuardianName}!",
                    "BOSS DITOLAK!",
                    triggerBurst: false);
            }
        }

        private static void TryEmitAmbientBarrierPulse()
        {
            Vector2 shrineCenter = new Vector2(Main.spawnTileX * 16f, (Main.spawnTileY - 2) * 16f);
            float maxDistSq = Config.AmbientTriggerDistancePixels * Config.AmbientTriggerDistancePixels;

            bool anyNearbyPlayer = false;
            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active || !p.IsLoggedIn || !p.RealPlayer || p.TPlayer == null) continue;
                if (Vector2.DistanceSquared(p.TPlayer.Center, shrineCenter) <= maxDistSq)
                {
                    anyNearbyPlayer = true;
                    break;
                }
            }

            if (!anyNearbyPlayer) return;

            float radius = Math.Clamp(Config.AmbientBarrierRadiusPixels, 48f, 240f);
            SpawnWardRing(shrineCenter, ProjectileID.HallowStar, 6, radius, 40, 0.55f);
        }

        private static void TriggerBanishVisualThrottled(Vector2 center)
        {
            if (!Config.ShowWardVisuals) return;

            DateTime now = DateTime.UtcNow;
            if ((now - _lastBanishVisualUtc).TotalMilliseconds < 1200) return;

            bool anyNearby = TShock.Players.Any(p =>
                p != null && p.Active && p.IsLoggedIn && p.RealPlayer && p.TPlayer != null &&
                Vector2.DistanceSquared(p.TPlayer.Center, center) <= 1800f * 1800f);

            if (!anyNearby) return;

            _lastBanishVisualUtc = now;
            SpawnReactiveShieldBurst(center);
        }

        private static void SpawnReactiveShieldBurst(Vector2 center)
        {
            if (!Config.ShowWardVisuals) return;

            SpawnWardRing(center, ProjectileID.HallowStar, 6, 48f, 26, 2.2f);
            SpawnWardRing(center, ProjectileID.DiamondBolt, 4, 28f, 22, 3.0f);
        }

        private static void SpawnWardRing(Vector2 center, int projId, int count, float radius, int timeLeft, float outwardSpeed)
        {
            for (int i = 0; i < count; i++)
            {
                double angle = (Math.PI * 2.0 / count) * i;
                Vector2 offset = new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                Vector2 vel = new Vector2((float)Math.Cos(angle) * outwardSpeed, (float)Math.Sin(angle) * outwardSpeed);
                SpawnSafeVisualProjectile(center + offset, vel, projId, timeLeft);
            }
        }

        private static void SpawnSafeVisualProjectile(Vector2 pos, Vector2 vel, int projId, int timeLeft)
        {
            int idx = Projectile.NewProjectile(Projectile.GetNoneSource(), pos, vel, projId, 0, 0f, Main.myPlayer);
            if (idx >= 0 && idx < Main.maxProjectiles)
            {
                var proj = Main.projectile[idx];
                proj.friendly = true;
                proj.hostile = false;
                proj.damage = 0;
                proj.knockBack = 0f;
                proj.direction = vel.X >= 0 ? 1 : -1;
                proj.spriteDirection = proj.direction;
                proj.timeLeft = timeLeft;
                NetMessage.SendData((int)PacketTypes.ProjectileNew, -1, -1, null, idx);
            }
        }

        #endregion
    }
}
