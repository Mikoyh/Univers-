using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace Univers.Aetherial.Elements
{
    public class RadianceElement : IElementProfile
    {
        public string ElementId => "RADIANCE";
        public string HexColor => "facc15";             // Emas Cahaya Suci
        public int LargeGemItemId => ItemID.LargeTopaz;

        // Evolusi Debuff: Tier 1-3 Midas (72 - Kilau Emas!) | Tier 4-6 Ichor (69 - -15 DEF) | Tier 7 Daybroken (189)
        public int GetMobDebuffId(int tier)
        {
            if (tier >= 7) return BuffID.Daybreak; // Cahaya Surya Membakar (100 DPS!)
            if (tier >= 4) return BuffID.Ichor;    // Pengurang Defense Emas
            return BuffID.Midas;                   // Kilauan Partikel Emas
        }

        public bool IsWeaponSynergy(Item held)
        {
            if (held == null || string.IsNullOrEmpty(held.Name)) return false;
            string n = held.Name;
            return n.Contains("Sun") || n.Contains("Star") || n.Contains("Light") ||
                   n.Contains("Hallow") || n.Contains("Excalibur") || n.Contains("Golden") ||
                   n.Contains("Prism") || n.Contains("Rainbow") || n.Contains("Fairy") ||
                   n.Contains("Terraprisma") || n.Contains("Daybreak") || n.Contains("Holy");
        }

        // [SKILL 10 DETIK - TIER 2+]: Menembakkan Topaz Bolt emas cahaya & memicu debuff suci!
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
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 11.5f, ProjectileID.TopazBolt, 35);
            }

            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            if (tier >= 7)
            {
                target.AddBuff(BuffID.Ichor, (4 + tier) * 60);
            }
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            // Tier 4+: Aura cahaya terang Shine + Wrath (+10% DMG di Tier 6+)
            if (tier >= 4)
            {
                attacker.SetBuff(BuffID.Shine, 360);
                attacker.SetBuff(BuffID.Heartreach, 360);
            }
            if (tier >= 6)
            {
                attacker.SetBuff(BuffID.Wrath, 300);
            }
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier == 2)
            {
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.Starfury, 1, 0f, 110f, 11f, 22);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.HallowStar, 2, 36f, 110f, 10f, 22);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 7.5f, ProjectileID.SkyFracture, 24);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 8f, ProjectileID.LightBeam, 25);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7f, ProjectileID.SkyFracture, 24);
                attacker.SetBuff(BuffID.Shine, 300);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7f, ProjectileID.TrueExcalibur, 25);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 7f, ProjectileID.FairyQueenMagicItemShot, 26);
                attacker.SetBuff(BuffID.Shine, 360);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 8.5f, ProjectileID.FairyQueenLance, 25);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 6f, ProjectileID.EmpressBlade, 26);
                attacker.SetBuff(BuffID.Shine, 360);
                attacker.SetBuff(BuffID.Wrath, 300);
            }
            else
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.FairyQueenSunDance, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.EmpressBlade, 2, 28f, 28, 3.5f);
                attacker.SetBuff(BuffID.Shine, 420);
                attacker.SetBuff(BuffID.Wrath, 360);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.HallowStar, 2, 30f, 110f, 10f, 24);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.SkyFracture, 2, 24f, 24, 4f);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7.5f, ProjectileID.LightBeam, 26);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.HallowStar, 3, 60f, 120f, 10.5f, 24);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7f, ProjectileID.TrueExcalibur, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.SkyFracture, 3, 30f, 26, 4.5f);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7f, ProjectileID.EmpressBlade, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FairyQueenLance, 3, 36f, 28, 4.5f);
            }
            else
            {
                // Tier 7: Penghakiman Cahaya Suci (SunDance + 4x Terraprisma)
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.FairyQueenSunDance, 30);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.EmpressBlade, 4, 44f, 32, 4f);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            attackerNpc.AddBuff(GetMobDebuffId(tier), (3 + tier) * 60);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 8f, ProjectileID.SkyFracture, 24);
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(GetMobDebuffId(tier), 180 + (tier * 30));

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 8f, ProjectileID.SkyFracture, 24);
            }
        }
    }
}
