/*
 * =====================================================================
 *  AETHERIAL RESONANCE SYSTEM (MODULAR COMPONENT)
 *  Platform : TShock 6.x / Terraria 1.4.5 / .NET 9.0 (C# 13)
 *  Features : 
 *    1. 6 Core Elements: IGNIS, GLACIAL, TORRENT, VOLT, BRAMBLE, RADIANCE
 *    2. Secret Easter Egg Element: OMNIA (0.000001% Chance, Multiversal Shatter)
 *    3. Server-Side Packet Interception (100% Vanilla Client Compatible)
 *    4. 8-Second Lockout Rule (Proc, Tag Duration + Personal Cooldown)
 *    5. 15 Elemental Reactions Matrix + Rank Mastery Tracker
 *    6. Solo Player Balance (Weapon Synergy + Marked Target Bonus)
 *    7. Tutorial Warp Awakening Randomizer & Univers EXP Integration
 * =====================================================================
 */

using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using Univers;
using Univers.Exp;
using Skynomi.Ranks;

namespace Univers.Aetherial
{
    public class ElementTagData
    {
        public string ActiveTag { get; set; } = string.Empty; // "IGNIS", "GLACIAL", "TORRENT", "VOLT", "BRAMBLE", "RADIANCE", "OMNIA"
        public int AppliedByPlayerId { get; set; }
        public DateTime TagExpiration { get; set; }
        public DateTime CooldownExpiration { get; set; }
    }

    public static class AetherialModule
    {
        public static AetherialConfig Config { get; private set; } = new();
        public static ElementDatabase Database { get; private set; } = null!;

        public static ConcurrentDictionary<int, ElementTagData> NPCElementStore { get; } = new();
        private static readonly ConcurrentDictionary<int, Item> SavedTrashItems = new();
        private static readonly ConcurrentDictionary<int, DateTime> OmniaGemExpirations = new();
        private static readonly ConcurrentDictionary<int, DateTime> ElementSkillCooldowns = new();
        private static readonly Random Rnd = new();

        public static readonly Dictionary<string, IElementProfile> ElementRegistry = new(StringComparer.OrdinalIgnoreCase)
        {
            ["IGNIS"] = new Elements.IgnisElement(),
            ["GLACIAL"] = new Elements.GlacialElement(),
            ["TORRENT"] = new Elements.TorrentElement(),
            ["VOLT"] = new Elements.VoltElement(),
            ["BRAMBLE"] = new Elements.BrambleElement(),
            ["RADIANCE"] = new Elements.RadianceElement(),
            ["OMNIA"] = new Elements.OmniaElement()
        };

        public static IElementProfile? GetElementProfile(string elementId)
        {
            if (string.IsNullOrWhiteSpace(elementId)) return null;
            return ElementRegistry.TryGetValue(elementId.Trim(), out var prof) ? prof : null;
        }

        public static void Initialize(string savePath)
        {
            string configDir = Path.Combine(savePath, "Univers");
            Directory.CreateDirectory(configDir);

            Config = AetherialConfig.Load(Path.Combine(configDir, "AetherialConfig.json"));
            Database = new ElementDatabase(Path.Combine(configDir, "univers_aetherial.db"));

            ServerApi.Hooks.NpcStrike.Register(UniversPlugin.Instance, OnNpcStrike);
            ServerApi.Hooks.GameUpdate.Register(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.ServerLeave.Register(UniversPlugin.Instance, OnServerLeave);
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnPlayerGreet);
            GetDataHandlers.PlayerInfo += OnPlayerInfo;
            GetDataHandlers.PlayerDamage += OnPlayerDamage;

            // [JEMBATAN LEVEL-TO-ELEMENT]: Sinkron otomatis saat naik level normal maupun /expadmin set
            ExpApi.OnLevelChanged += OnExpLevelChanged;

            AetherialCommands.Register();
            TShock.Log.ConsoleInfo("[Univers.Aetherial] Aetherial Resonance Combat Engine v2.0 loaded on .NET 9.0!");
        }

        public static void Dispose()
        {
            ServerApi.Hooks.NpcStrike.Deregister(UniversPlugin.Instance, OnNpcStrike);
            ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.ServerLeave.Deregister(UniversPlugin.Instance, OnServerLeave);
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnPlayerGreet);
            GetDataHandlers.PlayerInfo -= OnPlayerInfo;
            GetDataHandlers.PlayerDamage -= OnPlayerDamage;

            ExpApi.OnLevelChanged -= OnExpLevelChanged;

            NPCElementStore.Clear();
            SavedTrashItems.Clear();
            OmniaGemExpirations.Clear();
            ElementSkillCooldowns.Clear();
            Database?.Dispose();
        }

        private static void OnExpLevelChanged(string accountName, int newLevel, bool isAdminSet)
        {
            if (string.IsNullOrWhiteSpace(accountName) || Database == null) return;

            var profile = Database.GetOrCreateProfile(accountName);
            if (profile == null) return;

            int clamped = Math.Clamp(newLevel, 1, 1000);
            if (profile.ElementLevel != clamped)
            {
                profile.ElementLevel = clamped;
                Database.SaveProfile(profile);
            }

            var player = TShock.Players.FirstOrDefault(p =>
                p != null && p.Active && p.IsLoggedIn &&
                string.Equals(p.Account?.Name, accountName, StringComparison.OrdinalIgnoreCase));

            if (player != null && !isAdminSet)
            {
                player.SendInfoMessage($"[ INFO ] Resonansi Elemen jiwamu meningkat selaras dengan Level Karakter (Lv. {clamped})!");
            }
        }

        private static void OnServerLeave(LeaveEventArgs args)
        {
            ClearPlayerVisualState(args.Who);
        }

        internal static void ClearPlayerVisualState(int playerIndex)
        {
            ClearPlayerFloatingGem(playerIndex);
            OmniaGemExpirations.TryRemove(playerIndex, out _);
            ElementSkillCooldowns.TryRemove(playerIndex, out _);
        }

        private static int _tickCounter = 0;
        private static void OnGameUpdate(EventArgs args)
        {
            if (++_tickCounter < 30) return;
            _tickCounter = 0;

            DateTime now = DateTime.UtcNow;

            foreach (var kvp in NPCElementStore.ToArray())
            {
                int npcIdx = kvp.Key;
                var tagData = kvp.Value;
                bool npcDead = npcIdx < 0 || npcIdx >= Main.maxNPCs || !Main.npc[npcIdx].active;

                if (now >= tagData.TagExpiration || npcDead)
                {
                    if (NPCElementStore.TryRemove(npcIdx, out _))
                    {
                        if (!npcDead) RemoveMobVisualDebuff(Main.npc[npcIdx], tagData.ActiveTag);

                        bool stillHasTag = NPCElementStore.Values.Any(t => t.AppliedByPlayerId == tagData.AppliedByPlayerId && now < t.TagExpiration);
                        if (!stillHasTag)
                        {
                            ClearPlayerFloatingGem(tagData.AppliedByPlayerId);
                        }
                    }
                }
            }

            foreach (var kvp in OmniaGemExpirations.ToArray())
            {
                if (now >= kvp.Value)
                {
                    OmniaGemExpirations.TryRemove(kvp.Key, out _);
                    ClearPlayerFloatingGem(kvp.Key);
                }
            }

            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active || !player.IsLoggedIn || player.TPlayer == null) continue;

                bool isValidAetherialGemActive =
                    NPCElementStore.Values.Any(t => t.AppliedByPlayerId == player.Index && now < t.TagExpiration) ||
                    (OmniaGemExpirations.TryGetValue(player.Index, out DateTime omniaExp) && now < omniaExp);

                for (int slot = 0; slot < player.TPlayer.inventory.Length; slot++)
                {
                    Item invItem = player.TPlayer.inventory[slot];
                    if (invItem != null && IsAnyLargeGemId(invItem.type))
                    {
                        if (slot == 49 && isValidAetherialGemActive)
                            continue;

                        invItem.SetDefaults(0);
                        player.SendData(PacketTypes.PlayerSlot, "", player.Index, slot);
                        NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, slot);
                    }
                }
            }
        }

        #region [1] COMBAT ENGINE HOOK (NpcStrike & Reaction Processing)
        private static void OnNpcStrike(NpcStrikeEventArgs args)
        {
            if (args.Handled || args.Player == null || !args.Player.active) return;

            TSPlayer attacker = TShock.Players[args.Player.whoAmI];
            if (attacker == null || !attacker.Active || !attacker.IsLoggedIn) return;

            NPC targetNpc = args.Npc;
            if (targetNpc == null || !targetNpc.active || targetNpc.friendly || targetNpc.lifeMax <= 5) return;

            var profile = Database.GetOrCreateProfile(attacker.Account.Name);
            if (profile == null || string.IsNullOrEmpty(profile.AssignedElement)) return;

            string playerElement = profile.AssignedElement.ToUpperInvariant();
            int npcId = targetNpc.whoAmI;
            DateTime now = DateTime.UtcNow;

            int elementLevel = Math.Max(1, profile.ElementLevel);
            var tierStats = GetTierStatsForLevel(elementLevel);
            var elemMod = GetElementProfile(playerElement);
            if (elemMod != null && tierStats.TierNumber >= 2)
            {
                if (!ElementSkillCooldowns.TryGetValue(attacker.Index, out DateTime nextSkill) || now >= nextSkill)
                {
                    ElementSkillCooldowns[attacker.Index] = now.AddSeconds(10);
                    elemMod.OnTriggerElementSkill(attacker, targetNpc, tierStats.TierNumber);
                }
            }

            double soloBonus = 1.0;
            if (IsWeaponSynergy(playerElement, args.Player.HeldItem))
            {
                soloBonus += (tierStats.WeaponSynergyBonusPercent / 100.0);
            }

            if (NPCElementStore.TryGetValue(npcId, out var existingTagData))
            {
                if (now >= existingTagData.TagExpiration)
                {
                    ClearPlayerFloatingGem(existingTagData.AppliedByPlayerId);
                    RemoveMobVisualDebuff(targetNpc, existingTagData.ActiveTag);
                    NPCElementStore.TryRemove(npcId, out _);
                    existingTagData = null;
                }
                else
                {
                    if (existingTagData.ActiveTag == playerElement)
                    {
                        soloBonus += (tierStats.SoloMarkedBonusPercent / 100.0);
                    }
                }
            }

            double maxCapMultiplier = 1.0 + (tierStats.MaxSoloDamagePercent / 100.0);
            args.Damage = (int)(args.Damage * Math.Min(maxCapMultiplier, soloBonus));

            if (existingTagData != null)
            {
                if (existingTagData.AppliedByPlayerId == attacker.Index && now < existingTagData.CooldownExpiration)
                {
                    return;
                }

                if (existingTagData.ActiveTag != playerElement || playerElement == "OMNIA" || existingTagData.ActiveTag == "OMNIA")
                {
                    ClearPlayerFloatingGem(existingTagData.AppliedByPlayerId);
                    RemoveMobVisualDebuff(targetNpc, existingTagData.ActiveTag);

                    ExecuteElementalReaction(attacker, targetNpc, existingTagData.ActiveTag, playerElement, ref args);
                    NPCElementStore.TryRemove(npcId, out _);
                    return;
                }
            }
            else
            {
                if (playerElement == "OMNIA")
                {
                    OmniaGemExpirations[attacker.Index] = now.AddSeconds(4);
                    SetPlayerFloatingGem(attacker, playerElement);
                    ApplyMobVisualDebuff(targetNpc, playerElement, 8, 7);
                    ShowTagAppliedVisual(attacker, targetNpc, playerElement);
                    var omniaMod = GetElementProfile(playerElement);
                    if (omniaMod != null)
                        omniaMod.OnTagVisual(attacker, targetNpc, 7);
                    else
                        TriggerElementalTagVisualProjectile(targetNpc, playerElement, 7, attacker.TPlayer.Center);

                    ExecuteElementalReaction(attacker, targetNpc, "NONE", playerElement, ref args);
                    return;
                }

                if (tierStats.TagProcChance > 0 && Rnd.Next(100) < tierStats.TagProcChance)
                {
                    NPCElementStore[npcId] = new ElementTagData
                    {
                        ActiveTag = playerElement,
                        AppliedByPlayerId = attacker.Index,
                        TagExpiration = now.AddSeconds(tierStats.TagDurationSeconds),
                        CooldownExpiration = now.AddSeconds(tierStats.TagDurationSeconds + tierStats.LockoutSeconds)
                    };

                    if (tierStats.HasLargeGemDisplay)
                    {
                        SetPlayerFloatingGem(attacker, playerElement);
                    }

                    ApplyMobVisualDebuff(targetNpc, playerElement, tierStats.TagDurationSeconds, tierStats.TierNumber);
                    ShowTagAppliedVisual(attacker, targetNpc, playerElement);

                    try
                    {
                        if (RankModule.Database != null)
                        {
                            var rData = RankModule.Database.GetOrCreate(attacker.Account.Name);
                            rData.ElementTagsCount++;
                            RankModule.Database.Update(rData);
                        }
                    }
                    catch { }

                    var mod = GetElementProfile(playerElement);
                    if (mod != null)
                    {
                        mod.OnTagVisual(attacker, targetNpc, tierStats.TierNumber);
                    }
                    else if (tierStats.VisualStage >= 2)
                    {
                        TriggerElementalTagVisualProjectile(targetNpc, playerElement, 1, attacker.TPlayer.Center);
                    }
                }
            }
        }
        #endregion

        #region [2] ELEMENTAL REACTION ENGINE
        private static void ExecuteElementalReaction(TSPlayer attacker, NPC target, string tagA, string tagB, ref NpcStrikeEventArgs args)
        {
            string reactionName;
            double multiplier = 1.0;
            string combatColor;
            string extraEffect = "";

            string[] pair = { tagA, tagB };
            Array.Sort(pair);
            string comboKey = $"{pair[0]}+{pair[1]}";

            if (tagA == "OMNIA" || tagB == "OMNIA")
            {
                reactionName = "SOVEREIGN APEX: MULTIVERSAL SHATTER";
                args.Damage = target.life + (target.defense * 2) + 999999;
                combatColor = "e879f9";
                extraEffect = " [c/ff0055:[INSTAKILL]]";

                attacker.SendData(PacketTypes.CreateCombatTextExtended, "[c/e879f9:[ MULTIVERSAL SHATTER ]]", (int)target.position.X, (int)target.position.Y - 50);
                for (int i = 0; i < 5; i++)
                {
                    attacker.SendData(PacketTypes.CreateCombatTextExtended, "[c/f43f5e:[INSTAKILL] 999,999!]", (int)target.position.X + Rnd.Next(-45, 46), (int)target.position.Y - Rnd.Next(10, 80));
                }
            }
            else
            {
                switch (comboKey)
                {
                    case "GLACIAL+IGNIS":
                        reactionName = "THERMAL SHOCK";
                        multiplier = 2.00;
                        combatColor = "f97316";
                        break;

                    case "IGNIS+TORRENT":
                        reactionName = "SCALDING";
                        multiplier = 1.85;
                        combatColor = "fbbf24";
                        break;

                    case "IGNIS+VOLT":
                        reactionName = "PLASMA BLAST";
                        multiplier = 1.55;
                        combatColor = "f43f5e";
                        extraEffect = " [PLASMA KNOCKBACK]";
                        TriggerAreaBlast(target, (int)(args.Damage * 0.45), 250f);
                        break;

                    case "BRAMBLE+IGNIS":
                        reactionName = "WILDFIRE";
                        multiplier = 1.50;
                        combatColor = "ea580c";
                        extraEffect = " [CURSED BURN]";
                        target.AddBuff(BuffID.CursedInferno, 300);
                        NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);
                        break;

                    case "IGNIS+RADIANCE":
                        reactionName = "SUPERNOVA";
                        multiplier = 1.95;
                        combatColor = "facc15";
                        extraEffect = " [SOLAR BURST]";
                        TriggerAreaBlast(target, (int)(args.Damage * 0.50), 300f);
                        break;

                    case "GLACIAL+TORRENT":
                        reactionName = "PERMAFROST";
                        multiplier = 1.40;
                        combatColor = "38bdf8";
                        extraEffect = " [FROZEN STUN]";
                        target.velocity *= 0.05f;
                        break;

                    case "GLACIAL+VOLT":
                        reactionName = "STATIC DECAY";
                        multiplier = 1.45;
                        combatColor = "a855f7";
                        extraEffect = " [ARMOR -30%]";
                        target.defense = (int)(target.defense * 0.70);
                        break;

                    case "BRAMBLE+GLACIAL":
                        reactionName = "FROSTBLIGHT";
                        multiplier = 1.40;
                        combatColor = "2dd4bf";
                        extraEffect = " [CHILLED SLOW]";
                        target.velocity *= 0.4f;
                        target.AddBuff(BuffID.Frostburn, 300);
                        NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);
                        break;

                    case "GLACIAL+RADIANCE":
                        reactionName = "PRISM REFRACTION";
                        multiplier = 1.65;
                        combatColor = "fef08a";
                        extraEffect = " [LIGHT PRISM]";
                        TriggerChainLightning(attacker, target, (int)(args.Damage * 0.45));
                        break;

                    case "TORRENT+VOLT":
                        reactionName = "IONIZED STREAM";
                        multiplier = 1.45;
                        combatColor = "06b6d4";
                        extraEffect = " [CHAIN LIGHTNING]";
                        TriggerChainLightning(attacker, target, (int)(args.Damage * 0.45));
                        break;

                    case "BRAMBLE+TORRENT":
                        reactionName = "OVERGROWTH";
                        multiplier = 1.40;
                        combatColor = "10b981";
                        extraEffect = " [VENOM SPORES]";
                        target.AddBuff(BuffID.Venom, 300);
                        NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);
                        break;

                    case "RADIANCE+TORRENT":
                        reactionName = "PURIFICATION";
                        multiplier = 1.60;
                        combatColor = "fde047";
                        extraEffect = " [HEAL +20 HP]";
                        attacker.Heal(20);
                        break;

                    case "BRAMBLE+VOLT":
                        reactionName = "CATALYZE";
                        multiplier = 1.60;
                        combatColor = "a3e635";
                        extraEffect = " [OVERCHARGE]";
                        break;

                    case "RADIANCE+VOLT":
                        reactionName = "RADIANT ARC";
                        multiplier = 1.70;
                        combatColor = "eab308";
                        extraEffect = " [HOLY CRIT]";
                        args.Critical = true;
                        break;

                    case "BRAMBLE+RADIANCE":
                        reactionName = "PHOTOSYNTHESIS";
                        multiplier = 1.55;
                        combatColor = "84cc16";
                        extraEffect = " [+15 HP & +30 MANA]";
                        attacker.Heal(15);
                        attacker.TPlayer.statMana = Math.Min(attacker.TPlayer.statManaMax2, attacker.TPlayer.statMana + 30);
                        NetMessage.SendData((int)PacketTypes.PlayerMana, -1, -1, null, attacker.Index);
                        break;

                    default:
                        reactionName = "ELEMENTAL DISCHARGE";
                        multiplier = 1.25;
                        combatColor = "e2e8f0";
                        break;
                }
            }

            args.Damage = (int)(args.Damage * multiplier);

            try { ExpApi.AddExp(attacker, 35); } catch { }

            try
            {
                var reactionProfile = Database.GetOrCreateProfile(attacker.Account.Name);
                if (reactionProfile != null)
                {
                    reactionProfile.TotalReactionsTriggered++;
                    Database.SaveProfile(reactionProfile);
                }

                if (RankModule.Database != null)
                {
                    var rData = RankModule.Database.GetOrCreate(attacker.Account.Name);
                    rData.ElementCombosCount++;
                    RankModule.Database.Update(rData);
                }
            }
            catch { }

            attacker.SendData(
                PacketTypes.CreateCombatTextExtended,
                $"[c/{combatColor}:{reactionName}! +{args.Damage:N0}{extraEffect}]",
                (int)target.position.X,
                (int)target.position.Y - 30
            );

            var attackerProfile = Database.GetOrCreateProfile(attacker.Account.Name);
            int attackerTier = GetTierStatsForLevel(attackerProfile?.ElementLevel ?? 1).TierNumber;
            if (tagA == "OMNIA" || tagB == "OMNIA") attackerTier = 7;

            if (tagA != "NONE")
            {
                var modA = GetElementProfile(tagA);
                if (modA != null) modA.OnComboVisual(attacker, target, attackerTier);
                else TriggerElementalTagVisualProjectile(target, tagA, attackerTier, attacker.TPlayer.Center);
            }
            if (tagB != "NONE" && tagB != tagA)
            {
                var modB = GetElementProfile(tagB);
                if (modB != null) modB.OnComboVisual(attacker, target, attackerTier);
                else TriggerElementalTagVisualProjectile(target, tagB, attackerTier, attacker.TPlayer.Center);
            }

            AetherialApi.InvokeReaction(attacker, target, reactionName, args.Damage);
        }

        private static void TriggerChainLightning(TSPlayer attacker, NPC primaryTarget, int chainDamage)
        {
            int hitCount = 0;
            foreach (var npc in Main.npc)
            {
                if (npc != null && npc.active && !npc.friendly && npc.whoAmI != primaryTarget.whoAmI)
                {
                    float dist = (npc.Center - primaryTarget.Center).Length();
                    if (dist < 400f)
                    {
                        npc.StrikeNPC(chainDamage, 2f, 1);
                        NetMessage.SendData((int)PacketTypes.NpcStrike, -1, -1, null, npc.whoAmI, chainDamage, 2f, 1);
                        hitCount++;
                        if (hitCount >= 3) break;
                    }
                }
            }
        }

        private static void TriggerAreaBlast(NPC primaryTarget, int blastDamage, float radius)
        {
            foreach (var npc in Main.npc)
            {
                if (npc != null && npc.active && !npc.friendly && npc.whoAmI != primaryTarget.whoAmI)
                {
                    if ((npc.Center - primaryTarget.Center).Length() <= radius)
                    {
                        npc.StrikeNPC(blastDamage, 6f, npc.Center.X > primaryTarget.Center.X ? 1 : -1);
                        NetMessage.SendData((int)PacketTypes.NpcStrike, -1, -1, null, npc.whoAmI, blastDamage, 6f, npc.Center.X > primaryTarget.Center.X ? 1 : -1);
                    }
                }
            }
        }

        private static void ShowTagAppliedVisual(TSPlayer player, NPC target, string element)
        {
            string cleanElem = element.Trim().ToUpperInvariant();
            var mod = GetElementProfile(cleanElem);
            string color = mod != null ? mod.HexColor : cleanElem switch
            {
                "IGNIS" => "f97316",
                "GLACIAL" => "38bdf8",
                "TORRENT" => "06b6d4",
                "VOLT" => "a855f7",
                "BRAMBLE" => "10b981",
                "RADIANCE" => "facc15",
                "OMNIA" => "e879f9",
                _ => "ffffff"
            };

            player.SendData(
                PacketTypes.CreateCombatTextExtended,
                $"[c/{color}:[ {cleanElem} TAG ]]",
                (int)target.position.X,
                (int)target.position.Y - 20
            );
        }

        private static int[] GetTieredProjectilesForElement(string element)
        {
            return element.Trim().ToUpperInvariant() switch
            {
                "IGNIS" => new[] { 15, 612, 296, 953 },
                "GLACIAL" => new[] { 118, 348, 337, 961 },
                "TORRENT" => new[] { 22, 405, 409, 1135 },
                "VOLT" => new[] { 121, 496, 617, 634 },
                "BRAMBLE" => new[] { 206, 513, 228, 1038 },
                "RADIANCE" => new[] { 92, 122, 923, 873 },
                "OMNIA" => new[] { 573, 645, 619, 933 },
                _ => Array.Empty<int>()
            };
        }

        private static void TriggerElementalTagVisualProjectile(NPC target, string element, int tierNumber, Vector2? attackerPos = null)
        {
            if (target == null || !target.active) return;

            int[] projPool = GetTieredProjectilesForElement(element);
            if (projPool.Length < 4) return;

            Vector2 aimDir = Vector2.UnitX;
            if (attackerPos.HasValue)
            {
                Vector2 diff = target.Center - attackerPos.Value;
                if (diff.LengthSquared() > 1f)
                {
                    aimDir = Vector2.Normalize(diff);
                }
            }

            int maxLayer = tierNumber switch
            {
                <= 2 => 0,
                <= 4 => 1,
                <= 6 => 2,
                _ => 3
            };

            for (int layer = 0; layer <= maxLayer; layer++)
            {
                int projectileId = projPool[layer];
                float spreadAngle = (layer - (maxLayer / 2f)) * 0.35f;
                Vector2 finalDir = aimDir.RotatedBy(spreadAngle);
                Vector2 velocity = finalDir * (4.5f + layer * 1.2f);
                Vector2 spawnPos = target.Center - (finalDir * 24f);

                int projIndex = Projectile.NewProjectile(
                    Projectile.GetNoneSource(),
                    spawnPos,
                    velocity,
                    projectileId,
                    0,
                    0f,
                    Main.myPlayer
                );

                if (projIndex >= 0 && projIndex < Main.maxProjectiles)
                {
                    Main.projectile[projIndex].direction = finalDir.X >= 0 ? 1 : -1;
                    Main.projectile[projIndex].spriteDirection = Main.projectile[projIndex].direction;
                    Main.projectile[projIndex].timeLeft = 28;
                    NetMessage.SendData((int)PacketTypes.ProjectileNew, -1, -1, null, projIndex);
                }
            }
        }

        #region [VISUAL PACKET HELPERS - LARGE GEMS & MOB DEBUFFS]
        private static int GetGemItemIdForElement(string element)
        {
            var mod = GetElementProfile(element);
            if (mod != null) return mod.LargeGemItemId;
            return element.Trim().ToUpperInvariant() switch
            {
                "VOLT" => ItemID.LargeAmethyst,
                "RADIANCE" => ItemID.LargeTopaz,
                "TORRENT" => ItemID.LargeSapphire,
                "BRAMBLE" => ItemID.LargeEmerald,
                "OMNIA" => ItemID.LargeRuby,
                "GLACIAL" => ItemID.LargeDiamond,
                "IGNIS" => ItemID.LargeAmber,
                _ => 0
            };
        }

        private static bool IsAnyLargeGemId(int itemId)
        {
            return itemId == ItemID.LargeAmethyst || itemId == ItemID.LargeTopaz ||
                   itemId == ItemID.LargeSapphire || itemId == ItemID.LargeEmerald ||
                   itemId == ItemID.LargeRuby || itemId == ItemID.LargeDiamond ||
                   itemId == ItemID.LargeAmber;
        }

        private static void SetPlayerFloatingGem(TSPlayer player, string element)
        {
            if (player == null || !player.Active || player.TPlayer == null) return;

            int gemItemId = GetGemItemIdForElement(element);
            if (gemItemId <= 0) return;

            const int targetSlot = 49;
            Item currentSlot = player.TPlayer.inventory[targetSlot];

            if (currentSlot != null && currentSlot.type > 0 && !IsAnyLargeGemId(currentSlot.type))
            {
                SavedTrashItems[player.Index] = currentSlot.Clone();
            }

            player.TPlayer.inventory[targetSlot].SetDefaults(gemItemId);
            player.TPlayer.inventory[targetSlot].stack = 1;

            player.SendData(PacketTypes.PlayerSlot, "", player.Index, targetSlot);
            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, targetSlot);
        }

        private static void ClearPlayerFloatingGem(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= TShock.Players.Length) return;
            var player = TShock.Players[playerIndex];
            if (player == null || !player.Active || player.TPlayer == null) return;

            const int targetSlot = 49;
            Item currentSlot = player.TPlayer.inventory[targetSlot];

            if (currentSlot != null && IsAnyLargeGemId(currentSlot.type))
            {
                if (SavedTrashItems.TryRemove(playerIndex, out var restoredItem))
                {
                    player.TPlayer.inventory[targetSlot] = restoredItem;
                }
                else
                {
                    player.TPlayer.inventory[targetSlot].SetDefaults(0);
                }

                player.SendData(PacketTypes.PlayerSlot, "", player.Index, targetSlot);
                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, targetSlot);
            }
        }

        private static void ApplyMobVisualDebuff(NPC target, string element, int durationSeconds, int tierNumber = 1)
        {
            if (target == null || !target.active) return;

            var mod = GetElementProfile(element);
            int buffId = mod != null
                ? mod.GetMobDebuffId(tierNumber)
                : element.Trim().ToUpperInvariant() switch
                {
                    "IGNIS" => BuffID.OnFire,
                    "GLACIAL" => BuffID.Frostburn,
                    "TORRENT" => BuffID.Wet,
                    "VOLT" => BuffID.Electrified,
                    "BRAMBLE" => BuffID.Poisoned,
                    "RADIANCE" => BuffID.Ichor,
                    "OMNIA" => BuffID.ShadowFlame,
                    _ => 0
                };

            if (buffId > 0)
            {
                target.AddBuff(buffId, durationSeconds * 60);
                NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);
            }
        }

        private static void RemoveMobVisualDebuff(NPC target, string element)
        {
            if (target == null || !target.active) return;

            var mod = GetElementProfile(element);
            var candidates = new HashSet<int>();
            if (mod != null)
            {
                candidates.Add(mod.GetMobDebuffId(1));
                candidates.Add(mod.GetMobDebuffId(4));
                candidates.Add(mod.GetMobDebuffId(7));
            }
            else
            {
                int fallback = element.Trim().ToUpperInvariant() switch
                {
                    "IGNIS" => BuffID.OnFire,
                    "GLACIAL" => BuffID.Frostburn,
                    "TORRENT" => BuffID.Wet,
                    "VOLT" => BuffID.Electrified,
                    "BRAMBLE" => BuffID.Poisoned,
                    "RADIANCE" => BuffID.Ichor,
                    "OMNIA" => BuffID.ShadowFlame,
                    _ => 0
                };
                if (fallback > 0) candidates.Add(fallback);
            }

            bool removedAny = false;
            foreach (int buffId in candidates)
            {
                if (buffId <= 0) continue;
                int index = target.FindBuffIndex(buffId);
                if (index != -1)
                {
                    target.DelBuff(index);
                    removedAny = true;
                }
            }

            if (removedAny)
            {
                NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);
            }
        }

        private static void OnPlayerInfo(object? sender, GetDataHandlers.PlayerInfoEventArgs args)
        {
            var player = TShock.Players[args.PlayerId];
            if (player == null || !player.Active || !player.IsLoggedIn || player.TPlayer == null) return;

            DateTime now = DateTime.UtcNow;

            if (OmniaGemExpirations.TryGetValue(player.Index, out DateTime omniaExp))
            {
                if (now < omniaExp) return;
                OmniaGemExpirations.TryRemove(player.Index, out _);
            }

            bool hasActiveTag = NPCElementStore.Values.Any(t => t.AppliedByPlayerId == player.Index && now < t.TagExpiration);
            if (!hasActiveTag)
            {
                ClearPlayerFloatingGem(player.Index);
            }
        }

        private static void OnPlayerDamage(object? sender, GetDataHandlers.PlayerDamageEventArgs args)
        {
            if (args.Handled || args.Player == null || !args.Player.Active || !args.Player.IsLoggedIn) return;

            var profile = Database.GetOrCreateProfile(args.Player.Account.Name);
            var elemProfile = GetElementProfile(profile?.AssignedElement ?? "");
            if (elemProfile == null) return;

            int tier = GetTierStatsForLevel(profile!.ElementLevel).TierNumber;

            if (args.PlayerDeathReason._sourceNPCIndex >= 0 && args.PlayerDeathReason._sourceNPCIndex < Main.maxNPCs)
            {
                NPC attackerNpc = Main.npc[args.PlayerDeathReason._sourceNPCIndex];
                if (attackerNpc != null && attackerNpc.active)
                {
                    elemProfile.OnDamagedByNpc(args.Player, attackerNpc, args.Damage, tier);
                }
            }
            else if (args.PlayerDeathReason._sourcePlayerIndex >= 0 && args.PlayerDeathReason._sourcePlayerIndex < TShock.Players.Length)
            {
                TSPlayer attackerPlayer = TShock.Players[args.PlayerDeathReason._sourcePlayerIndex];
                if (attackerPlayer != null && attackerPlayer.Active && attackerPlayer.Index != args.Player.Index)
                {
                    elemProfile.OnDamagedByPlayer(args.Player, attackerPlayer, args.Damage, tier);
                }
            }
        }
        #endregion

        public class ElementTierStats
        {
            public int TierNumber { get; set; }
            public string TierColor { get; set; } = "ffffff";
            public int TagProcChance { get; set; }
            public int TagDurationSeconds { get; set; }
            public int LockoutSeconds { get; set; }
            public double WeaponSynergyBonusPercent { get; set; }
            public double SoloMarkedBonusPercent { get; set; }
            public double MaxSoloDamagePercent { get; set; }
            public bool HasLargeGemDisplay { get; set; }
            public int VisualStage { get; set; }
        }

        public static ElementTierStats GetTierStatsForLevel(int level)
        {
            int lvl = Math.Clamp(level, 1, 1000);

            if (lvl < 25)
                return new ElementTierStats { TierNumber = 1, TierColor = "94a3b8", TagProcChance = 0, TagDurationSeconds = 0, LockoutSeconds = 0, WeaponSynergyBonusPercent = 1.5, SoloMarkedBonusPercent = 0.0, MaxSoloDamagePercent = 1.5, HasLargeGemDisplay = false, VisualStage = 0 };
            if (lvl < 80)
                return new ElementTierStats { TierNumber = 2, TierColor = "38bdf8", TagProcChance = 8, TagDurationSeconds = 3, LockoutSeconds = 8, WeaponSynergyBonusPercent = 3.0, SoloMarkedBonusPercent = 2.0, MaxSoloDamagePercent = 5.0, HasLargeGemDisplay = false, VisualStage = 1 };
            if (lvl < 180)
                return new ElementTierStats { TierNumber = 3, TierColor = "4ade80", TagProcChance = 11, TagDurationSeconds = 4, LockoutSeconds = 8, WeaponSynergyBonusPercent = 4.5, SoloMarkedBonusPercent = 4.5, MaxSoloDamagePercent = 9.0, HasLargeGemDisplay = false, VisualStage = 2 };
            if (lvl < 350)
                return new ElementTierStats { TierNumber = 4, TierColor = "fbbf24", TagProcChance = 14, TagDurationSeconds = 4, LockoutSeconds = 7, WeaponSynergyBonusPercent = 6.0, SoloMarkedBonusPercent = 7.0, MaxSoloDamagePercent = 13.0, HasLargeGemDisplay = true, VisualStage = 3 };
            if (lvl < 580)
                return new ElementTierStats { TierNumber = 5, TierColor = "f97316", TagProcChance = 16, TagDurationSeconds = 5, LockoutSeconds = 7, WeaponSynergyBonusPercent = 7.5, SoloMarkedBonusPercent = 10.0, MaxSoloDamagePercent = 17.5, HasLargeGemDisplay = true, VisualStage = 4 };
            if (lvl < 850)
                return new ElementTierStats { TierNumber = 6, TierColor = "e879f9", TagProcChance = 18, TagDurationSeconds = 5, LockoutSeconds = 6, WeaponSynergyBonusPercent = 9.0, SoloMarkedBonusPercent = 12.5, MaxSoloDamagePercent = 21.5, HasLargeGemDisplay = true, VisualStage = 4 };

            return new ElementTierStats { TierNumber = 7, TierColor = "f43f5e", TagProcChance = 20, TagDurationSeconds = 6, LockoutSeconds = 5, WeaponSynergyBonusPercent = 10.0, SoloMarkedBonusPercent = 15.0, MaxSoloDamagePercent = 25.0, HasLargeGemDisplay = true, VisualStage = 5 };
        }

        private static bool IsWeaponSynergy(string element, Item held)
        {
            if (held == null) return false;

            var mod = GetElementProfile(element);
            if (mod != null) return mod.IsWeaponSynergy(held);

            if (element == "IGNIS" && (held.Name.Contains("Flame") || held.Name.Contains("Fire") || held.Name.Contains("Molten") || held.Name.Contains("Hell")))
                return true;
            if (element == "GLACIAL" && (held.Name.Contains("Frost") || held.Name.Contains("Ice") || held.Name.Contains("Blizzard") || held.Name.Contains("Snow")))
                return true;
            if (element == "TORRENT" && (held.Name.Contains("Water") || held.Name.Contains("Tide") || held.Name.Contains("Ocean") || held.Name.Contains("Aqua")))
                return true;
            if (element == "VOLT" && (held.Name.Contains("Thunder") || held.Name.Contains("Lightning") || held.Name.Contains("Spark") || held.Name.Contains("Storm")))
                return true;
            if (element == "BRAMBLE" && (held.Name.Contains("Poison") || held.Name.Contains("Jungle") || held.Name.Contains("Leaf") || held.Name.Contains("Seed")))
                return true;
            if (element == "RADIANCE" && (held.Name.Contains("Sun") || held.Name.Contains("Star") || held.Name.Contains("Light") || held.Name.Contains("Hallow") || held.Name.Contains("Excalibur")))
                return true;

            return false;
        }
        #endregion

        #region [3] TUTORIAL WARP AWAKENING (Randomizer Engine)
        private static void OnPlayerGreet(GreetPlayerEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            // Sinkronkan ElementLevel dengan Level Utama segera saat player login
            var profile = Database.GetOrCreateProfile(player.Account.Name);
            if (profile != null)
            {
                int currentMainLevel = ExpApi.GetLevel(player.Account.Name);
                if (profile.ElementLevel != currentMainLevel)
                {
                    profile.ElementLevel = currentMainLevel;
                    Database.SaveProfile(profile);
                }
            }

            System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ =>
            {
                if (player == null || !player.Active || !player.IsLoggedIn) return;

                var greetProfile = Database.GetOrCreateProfile(player.Account.Name);
                if (greetProfile == null) return;

                if (string.IsNullOrEmpty(greetProfile.AssignedElement))
                {
                    player.SendInfoMessage("[i:73] [c/38bdf8:=== AETHERIAL RESONANCE ===]");
                    player.SendInfoMessage("[c/f59e0b:Selesaikan Warp Tutorial atau ketik ][c/4ade80:/element awaken][c/f59e0b: untuk membangkitkan elemen jiwamu!]");
                }
                else
                {
                    player.SendInfoMessage($"[i:73] [c/38bdf8:Elemen Jiwamu:] [c/f59e0b:{greetProfile.AssignedElement}] (Lv.{greetProfile.ElementLevel} | Ketik /element untuk info reaksi)");
                }
            });
        }

        public static string PerformAwakening(TSPlayer player)
        {
            if (player == null || !player.Active || !player.IsLoggedIn) return "IGNIS";

            var profile = Database.GetOrCreateProfile(player.Account.Name);
            if (profile == null) return "IGNIS";

            double roll = Rnd.NextDouble() * 100.0;
            string picked;
            string rarityLabel;
            string rarityColor;

            if (roll < 0.000001)
            {
                picked = "OMNIA";
                rarityLabel = "SECRET COSMIC";
                rarityColor = "e879f9";

                TShock.Utils.Broadcast("[i:273] [c/e879f9:[ SINGULARITAS KOSMIS ]]", 232, 121, 249);
                TShock.Utils.Broadcast($"[c/e879f9:Pemain ][c/ffffff:{player.Name}][c/e879f9: telah membangkitkan ELEMEN RAHASIA: OMNIA (0.000001% CHANCE)!]", 232, 121, 249);
                TShock.Utils.Broadcast("[c/fdf4ff:Multiversal Instakill dan Universal Resonance diaktifkan!]", 253, 244, 255);
            }
            else if (roll < 5.0)
            {
                picked = "RADIANCE";
                rarityLabel = "EPIC";
                rarityColor = "facc15";

                TShock.Utils.Broadcast($"[i:273] [c/facc15:[ CAHAYA SUCI TERBANGKIT ] Pemain [c/ffffff:{player.Name}] mendapatkan Elemen EPIC: RADIANCE!]", 250, 204, 21);
            }
            else if (roll < 30.0)
            {
                string[] rarePool = { "GLACIAL", "VOLT" };
                picked = rarePool[Rnd.Next(rarePool.Length)];
                rarityLabel = "RARE";
                rarityColor = "38bdf8";
            }
            else
            {
                string[] commonPool = { "IGNIS", "TORRENT", "BRAMBLE" };
                picked = commonPool[Rnd.Next(commonPool.Length)];
                rarityLabel = "COMMON";
                rarityColor = "4ade80";
            }

            profile.AssignedElement = picked;
            profile.ElementLevel = ExpApi.GetLevel(player.Account.Name);
            profile.AwakenedAt = DateTime.UtcNow;
            Database.SaveProfile(profile);

            player.SendSuccessMessage("[i:73] [c/4ade80:=== RITUAL AWAKENING SELESAI! ===]");
            player.SendSuccessMessage($"Jiwa pertempuranmu menyatu dengan Elemen: [c/f59e0b:{picked}] ([c/{rarityColor}:{rarityLabel}])!");
            player.SendInfoMessage("[c/94a3b8:Serang musuh bersama pemain lain untuk memicu Reaksi Kombo Elemen! Ketik /element untuk detail.]");

            return picked;
        }
        #endregion
    }
}
