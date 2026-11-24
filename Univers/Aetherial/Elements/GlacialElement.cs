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
            if (tier >= 7) return BuffID.Frostburn2; // Frostbite (25 DPS!)
            if (tier >= 4) return BuffID.Frostburn;  // Frostburn (8 DPS)
            return BuffID.Chilled;                   // Chilled (-25% Speed)
        }

        public bool IsWeaponSynergy(Item held)
        {
            if (held == null || string.IsNullOrEmpty(held.Name)) return false;
            string n = held.Name;
            return n.Contains("Frost") || n.Contains("Ice") || n.Contains("Blizzard") ||
                   n.Contains("Snow") || n.Contains("Frozen") || n.Contains("Cool Whip") ||
                   n.Contains("North Pole") || n.Contains("Amarok") || n.Contains("Boreal") ||
                   n.Contains("Cryo") || n.Contains("Glacial");
        }

        // Helper khusus: Cincin Kristal Es (538 Twinkle + 344 Snowflake) berputar mengelilingi tubuh player
        private static void SpawnGlacialCrystalAura(TSPlayer owner, int count, float radius)
        {
            if (owner == null || !owner.Active || owner.TPlayer == null) return;
            Vector2 center = owner.TPlayer.Center;

            for (int i = 0; i < count; i++)
            {
                float angle = MathHelper.TwoPi * i / count;
                Vector2 offset = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                Vector2 tangentVel = new Vector2(-(float)Math.Sin(angle), (float)Math.Cos(angle)) * 4.5f;

                int projId = (i % 2 == 0) ? 538 : 344; // 538 = Twinkle, 344 = NorthPoleSnowflake
                ElementVisualHelper.SpawnVisualProjectile(center + offset, tangentVel, projId, 30);
            }
        }

        // [SKILL 10 DETIK - TIER 2+]: Diamond Bolt + Badai Es & Aura Perisai Kristal Berputar!
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

            for (int i = 0; i < boltCount; i++)
            {
                float spread = (i - (boltCount - 1) / 2f) * 0.12f;
                Vector2 shotDir = baseDir.RotatedBy(spread);
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 12f, ProjectileID.DiamondBolt, 35);
            }

            if (tier >= 3)
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 349, tier, 30f, 26, 3f); // 349 = FrostShard

            if (tier >= 5)
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 24f, baseDir * 9f, 348, 30); // 348 = FrostWave

            if (tier >= 6)
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, 464, 32); // 464 = CultistBossIceMist

            if (tier >= 7)
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 461, 4, 46f, 34, 5f); // 461 = ChargedBlasterLaser

            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            if (tier >= 4) target.AddBuff(BuffID.Chilled, (3 + tier) * 60);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            if (tier >= 3)
            {
                int crystalCount = Math.Min(8, tier + 1);
                float radius = 44f + (tier * 4f);
                SpawnGlacialCrystalAura(attacker, crystalCount, radius);
            }
            if (tier >= 4) attacker.SetBuff(BuffID.IceBarrier, 360 + (tier * 30));
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier == 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 6.5f, 253, 24); // 253 = BallofFrost
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 6f, 349, 24);   // 349 = FrostShard
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 7f, 263, 25);   // 263 = IceSickle
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 349, 2, 22f, 24, 2.5f);
                SpawnGlacialCrystalAura(attacker, 3, 46f);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 8f, 309, 26);   // 309 = FrostBlastFriendly
                ElementVisualHelper.SpawnVisualProjectile(target.Center, new Vector2(0f, -1f), 961, 26);  // 961 = DeerclopsIceSpike
                SpawnGlacialCrystalAura(attacker, 4, 50f);
                attacker.SetBuff(BuffID.IceBarrier, 300);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7.5f, 348, 28); // 348 = FrostWave
                ElementVisualHelper.SpawnRainProjectiles(target.Center, 337, 3, 56f, 115f, 10f, 26);      // 337 = Blizzard
                SpawnGlacialCrystalAura(attacker, 5, 54f);
                attacker.SetBuff(BuffID.IceBarrier, 360);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 8.5f, 709, 28); // 709 = MonkStaffT3_AltShot
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, 464, 28);           // 464 = Ice Mist
                ElementVisualHelper.SpawnRainProjectiles(target.Center, 344, 3, 68f, 125f, 8f, 28);        // 344 = NorthPoleSnowflake
                SpawnGlacialCrystalAura(attacker, 6, 58f);
                attacker.SetBuff(BuffID.IceBarrier, 420);
            }
            else
            {
                // Tier 7 (Lategame): Ice Mist + Laser Es + Duri Es Deerclops + 8 Kristal Aura!
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, 464, 30);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 9.5f, 537, 30); // 537 = StardustLaser
                ElementVisualHelper.SpawnVisualProjectile(target.Center, new Vector2(0f, -1f), 961, 30);  // 961 = Ice Spike
                ElementVisualHelper.SpawnRainProjectiles(target.Center, 344, 4, 80f, 130f, 8.5f, 30);
                SpawnGlacialCrystalAura(attacker, 8, 64f);
                attacker.SetBuff(BuffID.IceBarrier, 480);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 349, 3, 22f, 24, 3f);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 263, 3, 26f, 26, 2.5f); // 263 = IceSickle
                SpawnGlacialCrystalAura(attacker, 4, 48f);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, new Vector2(0f, -1f), 961, 28); // 961 = Ice Spike
                ElementVisualHelper.SpawnRainProjectiles(target.Center, 337, 4, 70f, 120f, 10f, 26);
                SpawnGlacialCrystalAura(attacker, 5, 52f);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 7.5f, 348, 28); // 348 = FrostWave
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 263, 4, 32f, 28, 3f);
                SpawnGlacialCrystalAura(attacker, 6, 56f);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, 464, 30);           // 464 = Ice Mist
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 459, 3, 36f, 30, 3.5f);          // 459 = ChargedBlasterOrb
                ElementVisualHelper.SpawnRainProjectiles(target.Center, 344, 4, 80f, 130f, 8.5f, 30);
                SpawnGlacialCrystalAura(attacker, 7, 60f);
            }
            else
            {
                // Tier 7: Kiamat Badai Kristal Absolut (Ice Mist + 4x Ice Sickle + 4x Charged Blaster + Hujan Salju + 8 Kristal Aura!)
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, 464, 34);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, new Vector2(0f, -1f), 961, 32);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 263, 4, 42f, 32, 3.5f);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 461, 4, 46f, 34, 4.5f);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, 344, 5, 95f, 140f, 9f, 32);
                SpawnGlacialCrystalAura(attacker, 8, 66f);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

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
                    ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir.RotatedBy(spread) * 8.5f, 349, 25); // 349 = FrostShard
                }
                SpawnGlacialCrystalAura(owner, Math.Min(8, tier + 1), 54f);
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(GetMobDebuffId(tier), 180 + (tier * 30));
            attackerPlayer.SetBuff(BuffID.Chilled, 150);

            if (tier >= 3)
            {
                int counterShards = Math.Min(5, tier);
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                for (int i = 0; i < counterShards; i++)
                {
                    float spread = (i - (counterShards - 1) / 2f) * 0.18f;
                    ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir.RotatedBy(spread) * 8.5f, 349, 25);
                }
                SpawnGlacialCrystalAura(owner, Math.Min(8, tier + 1), 54f);
            }
        }
    }
}
