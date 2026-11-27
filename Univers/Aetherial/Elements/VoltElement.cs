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

        // Evolusi Debuff: Tier 1-3 Confused | Tier 4-6 Electrified | Tier 7 ShadowFlame
        public int GetMobDebuffId(int tier)
        {
            if (tier >= 7) return BuffID.ShadowFlame;
            if (tier >= 4) return BuffID.Electrified;
            return BuffID.Confused;
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

        // [SKILL 10 DETIK - TIER 2+]: Menembakkan Amethyst Bolt ungu listrik ke arah musuh!
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
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 13f, ProjectileID.AmethystBolt, 34, attacker.Index);
            }

            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            if (tier >= 7) target.AddBuff(BuffID.Electrified, (4 + tier) * 60);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            if (tier >= 4)
            {
                attacker.SetBuff(BuffID.Swiftness, 360 + (tier * 30));
            }
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2 || attacker == null || target == null || !target.active) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);
            int ownerIdx = attacker.Index;

            if (tier == 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 9f, ProjectileID.ThunderStaffShot, 22, ownerIdx);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 9f, ProjectileID.ThunderStaffShot, 24, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 7f, ProjectileID.MagnetSphereBolt, 24, ownerIdx);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.Electrosphere, 26, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7f, ProjectileID.MagnetSphereBolt, 25, ownerIdx);
                attacker.SetBuff(BuffID.Swiftness, 300);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.Electrosphere, 28, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 8.5f, ProjectileID.NebulaBlaze1, 26, ownerIdx);
                attacker.SetBuff(BuffID.Swiftness, 360);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 9f, ProjectileID.NebulaBlaze2, 28, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, dir * 3f, ProjectileID.MagnetSphereBall, 28, ownerIdx);
                attacker.SetBuff(BuffID.Swiftness, 360);
            }
            else
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 9.5f, ProjectileID.NebulaBlaze2, 30, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 5f, ProjectileID.NebulaArcanum, 28, ownerIdx);
                attacker.SetBuff(BuffID.Swiftness, 420);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (attacker == null || target == null || !target.active) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);
            int ownerIdx = attacker.Index;

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.ThunderStaffShot, 3, 22f, 24, 4f, ownerIdx);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, dir * 3f, ProjectileID.MagnetSphereBall, 26, ownerIdx);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.Electrosphere, 26, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.MagnetSphereBolt, 4, 28f, 26, 4.5f, ownerIdx);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.Electrosphere, 28, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.NebulaBlaze1, 3, 32f, 28, 4.5f, ownerIdx);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.NebulaArcanum, 30, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.NebulaBlaze2, 3, 34f, 28, 5f, ownerIdx);
            }
            else
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.NebulaArcanum, 32, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.Electrosphere, 30, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.NebulaBlaze2, 4, 40f, 30, 5f, ownerIdx);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || owner == null || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            attackerNpc.AddBuff(GetMobDebuffId(tier), (3 + tier) * 60);
            if (tier >= 4) attackerNpc.AddBuff(BuffID.Electrified, 180);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 9f, ProjectileID.MagnetSphereBolt, 25, owner.Index);
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || owner == null || attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(GetMobDebuffId(tier), 180 + (tier * 30));

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 9f, ProjectileID.MagnetSphereBolt, 25, owner.Index);
            }
        }
    }
}
