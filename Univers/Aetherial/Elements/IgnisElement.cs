using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace Univers.Aetherial.Elements
{
    public class IgnisElement : IElementProfile
    {
        public string ElementId => "IGNIS";
        public string HexColor => "f97316";      // Oranye Api
        public int LargeGemItemId => ItemID.LargeAmber;

        // Evolusi Debuff: Tier 1-3 On Fire | Tier 4-6 Hellfire (OnFire3) | Tier 7 Daybroken (100 DPS!)
        public int GetMobDebuffId(int tier)
        {
            if (tier >= 7) return BuffID.Daybreak;
            if (tier >= 4) return BuffID.OnFire3;
            return BuffID.OnFire;
        }

        public bool IsWeaponSynergy(Item held)
        {
            if (held == null || string.IsNullOrEmpty(held.Name)) return false;
            string n = held.Name;
            return n.Contains("Flame") || n.Contains("Fire") || n.Contains("Molten") ||
                   n.Contains("Hell") || n.Contains("Solar") || n.Contains("Volcano") ||
                   n.Contains("Sunfury") || n.Contains("Flamarang");
        }

        // [SKILL 10 DETIK - TIER 2+]: Menembakkan Amber Bolt oranye api yang bertumbuh setiap tier!
        public void OnTriggerElementSkill(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2 || attacker == null || target == null || !target.active) return;

            int boltCount = tier switch
            {
                2 => 2,
                3 => 3,
                <= 5 => 4,
                6 => 5,
                _ => 6
            };

            Vector2 baseDir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            for (int i = 0; i < boltCount; i++)
            {
                float spread = (i - (boltCount - 1) / 2f) * 0.13f;
                Vector2 shotDir = baseDir.RotatedBy(spread);
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 12f, ProjectileID.AmberBolt, 34, attacker.Index);
            }

            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            // Tier 4+: Aktifkan cincin api Inferno mengelilingi badan player!
            if (tier >= 4)
            {
                attacker.SetBuff(BuffID.Inferno, 360 + (tier * 30));
            }
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2 || attacker == null || target == null || !target.active) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);
            int ownerIdx = attacker.Index;

            if (tier == 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 7f, ElementVisualHelper.ProjFoxparksFireball, 24, ownerIdx);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 7.5f, ElementVisualHelper.ProjFoxparksFireball, 25, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 6f, ProjectileID.FlamingScythe, 25, ownerIdx);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 8f, ProjectileID.CultistBossFireBall, 26, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DD2ExplosiveTrapT1Explosion, 22, ownerIdx);
                attacker.SetBuff(BuffID.Inferno, 300);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7.5f, ElementVisualHelper.ProjHorsemansBlade144, 26, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.SolarWhipSwordExplosion, 26, ownerIdx);
                attacker.SetBuff(BuffID.Inferno, 360);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 8f, ElementVisualHelper.ProjHorsemansBlade144, 28, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ElementVisualHelper.ProjVolcano144, 26, ownerIdx);
                attacker.SetBuff(BuffID.Inferno, 360);
            }
            else
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ElementVisualHelper.ProjVolcano144, 28, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DD2ExplosiveTrapT3Explosion, 26, ownerIdx);
                attacker.SetBuff(BuffID.Inferno, 420);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (attacker == null || target == null || !target.active) return;
            int ownerIdx = attacker.Index;

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DD2ExplosiveTrapT1Explosion, 24, ownerIdx);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DD2ExplosiveTrapT2Explosion, 26, ownerIdx);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ElementVisualHelper.ProjVolcano144, 26, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DD2ExplosiveTrapT3Explosion, 26, ownerIdx);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.SolarWhipSwordExplosion, 28, ownerIdx);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.SolarWhipSwordExplosion, 30, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.InfernoFriendlyBlast, 3, 40f, 30, 3f, ownerIdx);
            }
            else
            {
                // Tier 7: Kiamat Api Neraka (Volcano + Solar + Inferno + Daybreak!)
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ElementVisualHelper.ProjVolcano144, 30, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.SolarWhipSwordExplosion, 30, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.Daybreak, 4, 48f, 32, 3.5f, ownerIdx);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || owner == null || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            int debuffId = GetMobDebuffId(tier);
            attackerNpc.AddBuff(debuffId, (3 + tier) * 60);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 8.5f, ElementVisualHelper.ProjFoxparksFireball, 25, owner.Index);
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || owner == null || attackerPlayer == null || !attackerPlayer.Active) return;

            int debuffId = GetMobDebuffId(tier);
            attackerPlayer.SetBuff(debuffId, 180 + (tier * 30));

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 8.5f, ElementVisualHelper.ProjFoxparksFireball, 25, owner.Index);
            }
        }
    }
}
