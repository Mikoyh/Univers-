using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace Univers.Aetherial.Elements
{
    public class VoltElement : IElementProfile
    {
        public string ElementId => "VOLT";
        public string HexColor => "a855f7";               // Ungu Petir Kosmik
        public int LargeGemItemId => ItemID.LargeAmethyst;

        // Evolusi Debuff: Tier 1-3 Confused (31) | Tier 4-6 Electrified (144) | Tier 7 ShadowFlame (153 + Electrified)
        public int GetMobDebuffId(int tier)
        {
            if (tier >= 7) return BuffID.ShadowFlame; // Petir Void Ungu (15 DPS + efek ungu)
            if (tier >= 4) return BuffID.Electrified; // Tersengat Listrik
            return BuffID.Confused;                   // Kejutan Statis
        }

        public bool IsWeaponSynergy(Item held)
        {
            if (held == null || string.IsNullOrEmpty(held.Name)) return false;
            string n = held.Name;
            return n.Contains("Thunder") || n.Contains("Lightning") || n.Contains("Spark") ||
                   n.Contains("Storm") || n.Contains("Volt") || n.Contains("Magnet") ||
                   n.Contains("Electro") || n.Contains("Tesla") || n.Contains("Shock") ||
                   n.Contains("Zap") || n.Contains("Nebula") || n.Contains("Phase");
        }

        // [SKILL 10 DETIK - TIER 2+]: Menembakkan Amethyst Bolt & menyetrum target!
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
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 12f, ProjectileID.AmethystBolt, 35);
            }

            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            if (tier >= 7)
            {
                target.AddBuff(BuffID.Electrified, (4 + tier) * 60);
            }
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            // Tier 4+: Gerakan secepat kilat (Swiftness)
            if (tier >= 4)
            {
                attacker.SetBuff(BuffID.Swiftness, 360);
            }
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier == 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 8f, ProjectileID.ThunderStaffShot, 22);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 8f, ProjectileID.ThunderSpearShot, 22);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 6.5f, ProjectileID.MagnetSphereBolt, 22);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DD2LightningAuraT1, 24);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7f, ProjectileID.MagnetSphereBolt, 24);
                attacker.SetBuff(BuffID.Swiftness, 300);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DD2LightningAuraT2, 24);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.Electrosphere, 24);
                attacker.SetBuff(BuffID.Swiftness, 360);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DD2LightningAuraT3, 26);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 7.5f, ProjectileID.NebulaBlaze1, 26);
                attacker.SetBuff(BuffID.Swiftness, 360);
            }
            else
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 4.5f, ProjectileID.NebulaArcanum, 28);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 8.5f, ProjectileID.NebulaBlaze2, 26);
                attacker.SetBuff(BuffID.Swiftness, 420);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.AmethystBolt, 2, 20f, 24, 3.5f);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, dir * 3f, ProjectileID.MagnetSphereBall, 26);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.Electrosphere, 26);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.MagnetSphereBolt, 2, 26f, 26, 3.5f);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DD2LightningAuraT3, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.NebulaBlaze1, 2, 32f, 28, 4f);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.NebulaArcanum, 30);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.MagnetSphereBolt, 3, 38f, 28, 4f);
            }
            else
            {
                // Tier 7: Badai Petir Kosmik Nebula
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.NebulaArcanum, 32);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.Electrosphere, 30);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.NebulaBlaze2, 4, 44f, 32, 4.5f);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            attackerNpc.AddBuff(GetMobDebuffId(tier), (3 + tier) * 60);
            if (tier >= 4) attackerNpc.AddBuff(BuffID.Electrified, 180);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 8.5f, ProjectileID.MagnetSphereBolt, 24);
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(GetMobDebuffId(tier), 180 + (tier * 30));

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 8.5f, ProjectileID.MagnetSphereBolt, 24);
            }
        }
    }
}
