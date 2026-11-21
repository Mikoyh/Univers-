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

        // Evolusi Debuff (100% Hijau): Tier 1-3 Poisoned (20) | Tier 4-6 Cursed Inferno (39 - Api Hijau!) | Tier 7 Acid Venom (70)
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

        // [SKILL 10 DETIK - TIER 2+]: Menembakkan Emerald Bolt hijau & memicu Racun/Cursed Burn + Cincin Daun Dryad!
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
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 11f, ProjectileID.EmeraldBolt, 35);
            }

            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            if (tier >= 4)
            {
                target.AddBuff(BuffID.DryadsWardDebuff, (4 + tier) * 60);
            }
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            // Tier 4+: Aktifkan cincin daun hijau Dryad's Blessing (Buff 165) & Thorns (Buff 14) di tubuh pemain!
            if (tier >= 4)
            {
                attacker.SetBuff(BuffID.DryadsWard, 360);
                attacker.SetBuff(BuffID.Thorns, 360);
            }
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier == 2)
            {
                // Tier 2: Tebasan Daun Blade of Grass (976)
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 6f, ProjectileID.BladeOfGrass, 24);
            }
            else if (tier == 3)
            {
                // Tier 3: Daun Leaf Blower + Biji Seedler
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 6.5f, ProjectileID.Leaf, 24);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 5f, ProjectileID.SeedlerNut, 24);
            }
            else if (tier == 4)
            {
                // Tier 4: Sulur Duri Nettle Burst + Cincin Dryad Hijau
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 6f, ProjectileID.NettleBurstRight, 24);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DryadsWardCircle, 24);
                attacker.SetBuff(BuffID.DryadsWard, 300);
            }
            else if (tier == 5)
            {
                // Tier 5: Awan Spora Hijau + Jarum Pinus Razorpine
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.SporeCloud, 26);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 7.5f, ProjectileID.PineNeedleFriendly, 24);
                attacker.SetBuff(BuffID.DryadsWard, 360);
                attacker.SetBuff(BuffID.Thorns, 360);
            }
            else if (tier == 6)
            {
                // Tier 6: Api Hijau Cursed Flame + Tebasan Hijau Terra Beam
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 7f, ProjectileID.CursedFlameFriendly, 26);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7.5f, ProjectileID.TerraBeam, 26);
                attacker.SetBuff(BuffID.DryadsWard, 360);
                attacker.SetBuff(BuffID.Thorns, 360);
            }
            else
            {
                // Tier 7: Terra Beam + Kristal Daun Hijau Chlorophyte
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 8f, ProjectileID.TerraBeam, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.CrystalLeafShot, 3, 28f, 26, 3f);
                attacker.SetBuff(BuffID.DryadsWard, 420);
                attacker.SetBuff(BuffID.Thorns, 420);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.Leaf, 2, 20f, 24, 3f);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.SporeCloud, 26);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 16f, dir * 5f, ProjectileID.SeedlerNut, 26);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 6f, ProjectileID.NettleBurstRight, 26);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.SeedlerThorn, 3, 26f, 26, 3f);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 7f, ProjectileID.TerraBeam, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.SporeCloud, 2, 28f, 28, 1.5f);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 7.5f, ProjectileID.TerraBeam, 28);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.ChlorophyteOrb, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.CursedFlameFriendly, 3, 34f, 28, 3.5f);
            }
            else
            {
                // Tier 7: Murka Rimba Abadi (Terra Beam + Spora + 4x Terrarian Beam Hijau Puncak!)
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.SporeCloud, 30);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 7.5f, ProjectileID.TerraBeam, 30);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.TerrarianBeam, 4, 42f, 32, 3.5f);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            attackerNpc.AddBuff(GetMobDebuffId(tier), (3 + tier) * 60);
            if (tier >= 4) attackerNpc.AddBuff(BuffID.DryadsWardDebuff, 180);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 7.5f, ProjectileID.CrystalLeafShot, 25);
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(GetMobDebuffId(tier), 180 + (tier * 30));

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 7.5f, ProjectileID.CrystalLeafShot, 25);
            }
        }
    }
}
