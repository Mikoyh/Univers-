using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace Univers.Aetherial.Elements
{
    public class BrambleElement : IElementProfile
    {
        public string ElementId => "BRAMBLE";
        public string HexColor => "10b981";               // Hijau Alam / Rimba Zamrud
        public int LargeGemItemId => ItemID.LargeEmerald;

        public int GetMobDebuffId(int tier)
        {
            if (tier >= 7) return BuffID.Venom;         // Acid Venom (30 DPS!)
            if (tier >= 4) return BuffID.CursedInferno; // Api Hijau Zamrud (24 DPS!)
            return BuffID.Poisoned;                     // Partikel Racun Hijau
        }

        public bool IsWeaponSynergy(Item held)
        {
            if (held == null || string.IsNullOrEmpty(held.Name)) return false;
            string n = held.Name;
            return n.Contains("Grass") || n.Contains("Leaf") || n.Contains("Vine") ||
                   n.Contains("Thorn") || n.Contains("Seed") || n.Contains("Jungle") ||
                   n.Contains("Poison") || n.Contains("Venom") || n.Contains("Spore") ||
                   n.Contains("Chlorophyte") || n.Contains("Terra") || n.Contains("Flower") ||
                   n.Contains("Nettle") || n.Contains("Pine") || n.Contains("Dryad");
        }

        // Helper khusus: Memunculkan cincin daun Dryad (586) berputar mengelilingi tubuh player
        private static void SpawnDryadLeafAura(TSPlayer owner, int leafCount, float radius)
        {
            if (owner == null || !owner.Active || owner.TPlayer == null) return;
            Vector2 center = owner.TPlayer.Center;

            for (int i = 0; i < leafCount; i++)
            {
                float angle = MathHelper.TwoPi * i / leafCount;
                Vector2 offset = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                Vector2 tangentVel = new Vector2(-(float)Math.Sin(angle), (float)Math.Cos(angle)) * 4.5f;

                // 586 = DryadsWardCircle (Daun Dryad berputar)
                ElementVisualHelper.SpawnVisualProjectile(center + offset, tangentVel, 586, 30);
            }
        }

        // [SKILL 10 DETIK - TIER 2+]: Emerald Bolt + Badai Daun & Aura Daun Dryad Berputar!
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
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 12f, ProjectileID.EmeraldBolt, 35);
            }

            if (tier >= 3)
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 523, tier, 32f, 28, 2.5f); // ToxicBubble

            if (tier >= 5)
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 28f, baseDir * 9f, 1126, 32); // ChlorophyteClaymoreBlade

            if (tier >= 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 24f, baseDir * 10f, 984, 34); // TerraBlade2
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 20f, baseDir * 11f, 985, 34); // TerraBlade2Shot
            }

            if (tier >= 7)
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 578, 4, 48f, 36, 4f); // VortexVortexLightning

            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            if (tier >= 4) target.AddBuff(BuffID.DryadsWardDebuff, (4 + tier) * 60);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            if (tier >= 3)
            {
                int leafCount = Math.Min(8, tier + 1);
                float radius = 44f + (tier * 4f);
                SpawnDryadLeafAura(attacker, leafCount, radius);
                attacker.SetBuff(BuffID.DryadsWard, 360 + (tier * 30));
            }
            if (tier >= 4) attacker.SetBuff(BuffID.Thorns, 360 + (tier * 30));
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier == 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 6.5f, 976, 24);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 16f, dir * 7f, 227, 24);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 7f, 976, 24);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 523, 2, 22f, 24, 2f);
                SpawnDryadLeafAura(attacker, 3, 46f);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 7.5f, 1127, 26);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, 569, 26);
                SpawnDryadLeafAura(attacker, 4, 50f);
                attacker.SetBuff(BuffID.DryadsWard, 300);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 8f, 1126, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 228, 3, 28f, 26, 1.8f);
                SpawnDryadLeafAura(attacker, 5, 54f);
                attacker.SetBuff(BuffID.DryadsWard, 360);
                attacker.SetBuff(BuffID.Thorns, 360);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 8.5f, 984, 30);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 9f, 985, 30);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 570, 3, 32f, 28, 2.5f);
                SpawnDryadLeafAura(attacker, 6, 58f);
                attacker.SetBuff(BuffID.DryadsWard, 420);
                attacker.SetBuff(BuffID.Thorns, 420);
            }
            else
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 9f, 984, 32);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 9.5f, 1129, 32);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 227, 4, 34f, 30, 4f);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 578, 2, 38f, 30, 3f);
                SpawnDryadLeafAura(attacker, 8, 64f);
                attacker.SetBuff(BuffID.DryadsWard, 480);
                attacker.SetBuff(BuffID.Thorns, 480);
            }
        }
        
        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 227, 3, 22f, 24, 3.5f);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, 228, 26);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 523, 3, 26f, 26, 2.8f);
                SpawnDryadLeafAura(attacker, 4, 48f);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7.5f, 1126, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 569, 4, 30f, 28, 2.5f);
                SpawnDryadLeafAura(attacker, 5, 52f);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 8.5f, 984, 30);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, 229, 30);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 570, 4, 34f, 28, 3f);
                SpawnDryadLeafAura(attacker, 6, 56f);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 9f, 984, 32);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 9f, 1129, 32);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 229, 3, 36f, 30, 3.5f);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 571, 4, 38f, 30, 3f);
                SpawnDryadLeafAura(attacker, 7, 60f);
            }
            else
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, 228, 34);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 9.5f, 984, 34);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 9.5f, 1126, 34);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.TerrarianBeam, 6, 46f, 36, 4f);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 578, 3, 40f, 34, 3.5f);
                SpawnDryadLeafAura(attacker, 8, 66f);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            attackerNpc.AddBuff(GetMobDebuffId(tier), (3 + tier) * 60);
            if (tier >= 4) attackerNpc.AddBuff(BuffID.DryadsWardDebuff, 180);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            if (tier >= 3)
            {
                int counterLeaves = Math.Min(6, tier);
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
                for (int i = 0; i < counterLeaves; i++)
                {
                    float spread = (i - (counterLeaves - 1) / 2f) * 0.18f;
                    ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir.RotatedBy(spread) * 8.5f, 227, 26);
                }
                SpawnDryadLeafAura(owner, Math.Min(8, tier + 1), 54f);
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(GetMobDebuffId(tier), 180 + (tier * 30));

            if (tier >= 3)
            {
                int counterLeaves = Math.Min(5, tier);
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                for (int i = 0; i < counterLeaves; i++)
                {
                    float spread = (i - (counterLeaves - 1) / 2f) * 0.18f;
                    ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir.RotatedBy(spread) * 8.5f, 227, 26);
                }
                SpawnDryadLeafAura(owner, Math.Min(8, tier + 1), 54f);
            }
        }
    }
}
