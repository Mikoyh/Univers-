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

        // Evolusi Debuff: Tier 1-3 Chilled (46) | Tier 4-6 Frostburn (44) | Tier 7 Frostbite / Frostburn2 (324)
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

        // [SKILL 10 DETIK - TIER 2+]: Menembakkan Diamond Bolt es kristal & memicu efek beku!
        public void OnTriggerElementSkill(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2 || attacker == null || target == null || !target.active) return;

            int boltCount = tier switch
            {
                2 => 1,
                3 => 2,
                <= 5 => 3,
                6 => 4,
                _ => 5
            };

            Vector2 baseDir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            for (int i = 0; i < boltCount; i++)
            {
                float spread = (i - (boltCount - 1) / 2f) * 0.14f;
                Vector2 shotDir = baseDir.RotatedBy(spread);
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 11f, ProjectileID.DiamondBolt, 35);
            }

            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            if (tier >= 4)
            {
                target.AddBuff(BuffID.Chilled, (3 + tier) * 60);
            }
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            // Tier 4+: Aktifkan aura Ice Barrier (pengurang damage -25% + efek biru es) di tubuh pemain!
            if (tier >= 4)
            {
                attacker.SetBuff(BuffID.IceBarrier, 360);
            }
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier == 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 6f, ProjectileID.IceBolt, 24);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 6.5f, ProjectileID.WandOfFrostingFrost, 24);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 5.5f, ProjectileID.SnowBallFriendly, 22);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 7.5f, ProjectileID.FrostBoltSword, 26);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.IceSickle, 24);
                attacker.SetBuff(BuffID.IceBarrier, 300);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 6f, ProjectileID.IceSickle, 26);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.Blizzard, 2, 48f, 110f, 9f, 24);
                attacker.SetBuff(BuffID.IceBarrier, 360);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7f, ProjectileID.FrostWave, 26);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.Blizzard, 3, 64f, 120f, 10f, 26);
                attacker.SetBuff(BuffID.IceBarrier, 360);
            }
            else
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7.5f, ProjectileID.FrostWave, 28);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.NorthPoleSnowflake, 3, 72f, 130f, 7f, 28);
                attacker.SetBuff(BuffID.IceBarrier, 420);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 16f, dir * 5f, ProjectileID.IceBolt, 24);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, dir * 4f, ProjectileID.FrostArrow, 24);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.IceSickle, 2, 24f, 26, 2f);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7f, ProjectileID.FrostBoltSword, 26);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.Blizzard, 3, 64f, 120f, 9.5f, 26);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 16f, dir * 6f, ProjectileID.FrostWave, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.IceSickle, 3, 30f, 28, 2.5f);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 16f, dir * 6.5f, ProjectileID.FrostWave, 28);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.NorthPoleSnowflake, 3, 76f, 130f, 8f, 30);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FrostBoltSword, 3, 36f, 28, 4f);
            }
            else
            {
                // Tier 7: Badai Kristal Absolut
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.FrostWave, 30);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.NorthPoleSnowflake, 4, 90f, 140f, 8.5f, 32);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.IceSickle, 4, 42f, 32, 3f);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            attackerNpc.AddBuff(GetMobDebuffId(tier), (3 + tier) * 60);
            attackerNpc.AddBuff(BuffID.Chilled, 180);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 7.5f, ProjectileID.IceBolt, 25);
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(GetMobDebuffId(tier), 180 + (tier * 30));
            attackerPlayer.SetBuff(BuffID.Chilled, 150);

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 7.5f, ProjectileID.IceBolt, 25);
            }
        }
    }
}
