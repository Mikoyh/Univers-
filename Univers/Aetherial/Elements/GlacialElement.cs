using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace Univers.Aetherial.Elements
{
    public class GlacialElement : IElementProfile
    {
        public string ElementId => "GLACIAL";
        public string HexColor => "38bdf8";              // Biru Kristal Es
        public int LargeGemItemId => ItemID.LargeDiamond;

        public int GetMobDebuffId(int tier)
        {
            if (tier >= 7) return BuffID.Frostburn2;
            if (tier >= 4) return BuffID.Frostburn;
            return BuffID.Chilled;
        }

        public bool IsWeaponSynergy(Item held)
        {
            if (held == null || string.IsNullOrEmpty(held.Name)) return false;
            string n = held.Name;
            return n.Contains("Frost") || n.Contains("Ice") || n.Contains("Blizzard") ||
                   n.Contains("Snow") || n.Contains("Frozen") || n.Contains("Cool Whip") ||
                   n.Contains("North Pole") || n.Contains("Amarok") || n.Contains("Boreal") ||
                   n.Contains("Cryo") || n.Contains("Glacial") || n.Contains("Stardust");
        }

        // Cincin Kristal Es (Twinkle + NorthPoleSnowflake) berputar mengelilingi tubuh player
        private static void SpawnGlacialCrystalAura(TSPlayer owner, int count, float radius)
        {
            if (owner == null || !owner.Active || owner.TPlayer == null) return;
            ElementVisualHelper.SpawnOrbitAura(
                owner.TPlayer.Center,
                ProjectileID.Twinkle,
                ProjectileID.NorthPoleSnowflake,
                count,
                radius,
                4.5f,
                30,
                owner.Index);
        }

        // [SKILL 10 DETIK - TIER 2+]: Diamond Bolt + Badai Es + Cincin Kristal Pelindung!
        public void OnTriggerElementSkill(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2 || attacker == null || target == null || !target.active) return;

            int boltCount = tier switch
            {
                2 => 2,
                3 => 3,
                4 => 4,
                5 => 5,
                6 => 6,
                _ => 8
            };

            Vector2 baseDir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);
            int ownerIdx = attacker.Index;

            for (int i = 0; i < boltCount; i++)
            {
                float spread = (i - (boltCount - 1) / 2f) * 0.12f;
                Vector2 shotDir = baseDir.RotatedBy(spread);
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 12f, ProjectileID.DiamondBolt, 35, ownerIdx);
            }

            if (tier >= 3)
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FrostShard, tier, 30f, 26, 3f, ownerIdx);

            if (tier >= 5)
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 24f, baseDir * 9f, ProjectileID.FrostWave, 30, ownerIdx);

            if (tier >= 6)
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.CultistBossIceMist, 32, ownerIdx);

            if (tier >= 7)
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.ChargedBlasterOrb, 4, 46f, 34, 5f, ownerIdx);

            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            if (tier >= 4) target.AddBuff(BuffID.Chilled, (3 + tier) * 60);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            if (tier >= 3)
            {
                SpawnGlacialCrystalAura(attacker, Math.Min(8, tier + 1), 44f + (tier * 4f));
            }
            if (tier >= 4) attacker.SetBuff(BuffID.IceBarrier, 360 + (tier * 30));
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2 || attacker == null || target == null || !target.active) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);
            int ownerIdx = attacker.Index;

            if (tier == 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 6.5f, ProjectileID.BallofFrost, 24, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 6f, ProjectileID.FrostShard, 24, ownerIdx);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 7f, ProjectileID.IceSickle, 25, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FrostShard, 3, 22f, 24, 2.5f, ownerIdx);
                SpawnGlacialCrystalAura(attacker, 3, 46f);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 8f, ProjectileID.FrostBlastFriendly, 26, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, new Vector2(0f, -1f), ProjectileID.DeerclopsIceSpike, 26, ownerIdx);
                SpawnGlacialCrystalAura(attacker, 4, 50f);
                attacker.SetBuff(BuffID.IceBarrier, 300);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7.5f, ProjectileID.FrostWave, 28, ownerIdx);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.Blizzard, 3, 56f, 115f, 10f, 26, ownerIdx);
                SpawnGlacialCrystalAura(attacker, 5, 54f);
                attacker.SetBuff(BuffID.IceBarrier, 360);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 8.5f, ProjectileID.MonkStaffT3_AltShot, 28, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.CultistBossIceMist, 28, ownerIdx);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.NorthPoleSnowflake, 4, 68f, 125f, 8f, 28, ownerIdx);
                SpawnGlacialCrystalAura(attacker, 6, 58f);
                attacker.SetBuff(BuffID.IceBarrier, 420);
            }
            else
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.CultistBossIceMist, 30, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 9.5f, ProjectileID.ChargedBlasterOrb, 30, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, new Vector2(0f, -1f), ProjectileID.DeerclopsIceSpike, 30, ownerIdx);
                SpawnGlacialCrystalAura(attacker, 8, 64f);
                attacker.SetBuff(BuffID.IceBarrier, 480);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (attacker == null || target == null || !target.active) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);
            int ownerIdx = attacker.Index;

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FrostShard, 3, 22f, 24, 3f, ownerIdx);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.IceSickle, 3, 26f, 26, 2.5f, ownerIdx);
                SpawnGlacialCrystalAura(attacker, 4, 48f);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, new Vector2(0f, -1f), ProjectileID.DeerclopsIceSpike, 28, ownerIdx);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.Blizzard, 4, 70f, 120f, 10f, 26, ownerIdx);
                SpawnGlacialCrystalAura(attacker, 5, 52f);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 7.5f, ProjectileID.FrostWave, 28, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.IceSickle, 4, 32f, 28, 3f, ownerIdx);
                SpawnGlacialCrystalAura(attacker, 6, 56f);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.CultistBossIceMist, 30, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.ChargedBlasterOrb, 3, 36f, 30, 3.5f, ownerIdx);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.NorthPoleSnowflake, 4, 80f, 130f, 8.5f, 30, ownerIdx);
                SpawnGlacialCrystalAura(attacker, 7, 60f);
            }
            else
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.CultistBossIceMist, 32, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, new Vector2(0f, -1f), ProjectileID.DeerclopsIceSpike, 32, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.ChargedBlasterOrb, 4, 44f, 30, 4.5f, ownerIdx);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.NorthPoleSnowflake, 5, 95f, 140f, 9f, 32, ownerIdx);
                SpawnGlacialCrystalAura(attacker, 8, 66f);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || owner == null || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            attackerNpc.AddBuff(GetMobDebuffId(tier), (3 + tier) * 60);
            attackerNpc.AddBuff(BuffID.Chilled, 180);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            if (tier >= 3)
            {
                int counterShards = Math.Min(6, tier);
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
                for (int i = 0; i < counterShards; i++)
                {
                    float spread = (i - (counterShards - 1) / 2f) * 0.18f;
                    ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir.RotatedBy(spread) * 8.5f, ProjectileID.FrostShard, 25, owner.Index);
                }
                SpawnGlacialCrystalAura(owner, Math.Min(8, tier + 1), 54f);
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || owner == null || attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(GetMobDebuffId(tier), 180 + (tier * 30));
            attackerPlayer.SetBuff(BuffID.Chilled, 150);

            if (tier >= 3)
            {
                int counterShards = Math.Min(5, tier);
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                for (int i = 0; i < counterShards; i++)
                {
                    float spread = (i - (counterShards - 1) / 2f) * 0.18f;
                    ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir.RotatedBy(spread) * 8.5f, ProjectileID.FrostShard, 25, owner.Index);
                }
                SpawnGlacialCrystalAura(owner, Math.Min(8, tier + 1), 54f);
            }
        }
    }
}
