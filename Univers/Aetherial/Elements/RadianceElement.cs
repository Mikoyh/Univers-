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

        // Evolusi Debuff: Tier 1-3 Midas | Tier 4-6 Ichor (-15 DEF) | Tier 7 Daybroken (100 DPS + Ichor)
        public int GetMobDebuffId(int tier)
        {
            if (tier >= 7) return BuffID.Daybreak;
            if (tier >= 4) return BuffID.Ichor;
            return BuffID.Midas;
        }

        public bool IsWeaponSynergy(Item held)
        {
            if (held == null || string.IsNullOrEmpty(held.Name)) return false;
            string n = held.Name;
            return n.Contains("Sun") || n.Contains("Star") || n.Contains("Light") ||
                   n.Contains("Hallow") || n.Contains("Excalibur") || n.Contains("Golden") ||
                   n.Contains("Prism") || n.Contains("Rainbow") || n.Contains("Fairy") ||
                   n.Contains("Terraprisma") || n.Contains("Daybreak") || n.Contains("Holy") ||
                   n.Contains("Ichor") || n.Contains("Resonance");
        }

        // Aura Halo Bintang & Pelangi berputar mengelilingi tubuh pemilik Radiance
        private static void SpawnRadianceHaloAura(TSPlayer owner, int count, float radius)
        {
            if (owner == null || !owner.Active || owner.TPlayer == null) return;
            ElementVisualHelper.SpawnOrbitAura(
                owner.TPlayer.Center,
                ProjectileID.FallingStar,
                ProjectileID.RainbowRodBullet,
                count,
                radius,
                4.8f,
                28,
                owner.Index);
        }

        // [SKILL 10 DETIK - TIER 2+]: Hujan Bintang Emas, Sinar Surya, Tebasan Excalibur & Ring Sihir Cahaya!
        public void OnTriggerElementSkill(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2 || attacker == null || target == null || !target.active) return;

            Vector2 baseDir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);
            int ownerIdx = attacker.Index;

            int boltCount = tier switch
            {
                2 => 2,
                3 => 3,
                4 => 4,
                5 => 5,
                6 => 6,
                _ => 7
            };

            for (int i = 0; i < boltCount; i++)
            {
                float spread = (i - (boltCount - 1) / 2f) * 0.13f;
                Vector2 shotDir = baseDir.RotatedBy(spread);
                int projId = (i % 2 == 0) ? ProjectileID.FallingStar : ProjectileID.TopazBolt;
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 12.5f, projId, 34, ownerIdx);
            }

            // Tier 2+: Golden Shower + Starfury rain
            ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, baseDir * 11f, ProjectileID.GoldenShowerFriendly, 28, ownerIdx);
            ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.Starfury, Math.Min(4, tier), 56f, 120f, 12f, 24, ownerIdx);

            // Tier 3+: Ichor Burst & Golden Laser
            if (tier >= 3)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.IchorSplash, 3, 22f, 24, 3.2f, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 24f, baseDir * 10f, ProjectileID.GoldenShowerHostile, 24, ownerIdx);
            }

            // Tier 4+: Solar EyeBeam + UFO/Scutlix Laser + Crosshair Lock + Excalibur Golden Slash
            if (tier >= 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, baseDir * 13f, ProjectileID.EyeBeam, 28, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 26f, baseDir * 12f, ProjectileID.UFOLaser, 26, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 26f, baseDir * 12f, ProjectileID.ScutlixLaserFriendly, 26, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.ScutlixLaserCrosshair, 24, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 16f, baseDir * 4f, ProjectileID.Excalibur, 24, ownerIdx);
            }

            // Tier 5+: Cincin Sihir Cahaya + Phoenix Shot
            if (tier >= 5)
            {
                ElementVisualHelper.SpawnMagicRitualRing(
                    target.Center,
                    ownerIdx,
                    ProjectileID.FallingStar,
                    ProjectileID.ScutlixLaserFriendly,
                    ProjectileID.HallowBossSplitShotCore,
                    radius: 56f,
                    outerCount: 6,
                    chordCount: 3,
                    spinSpeed: 4.5f,
                    timeLeft: 28);
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, baseDir * 12f, ProjectileID.DD2PhoenixBowShot, 28, ownerIdx);
            }

            // Tier 6+: Terraprisma + Royal Resonance + Golden Portal Gate + Drill Lock Crosshair
            if (tier >= 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 22f, baseDir * 9f, ProjectileID.EmpressBlade, 30, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 20f, baseDir * 9f, ProjectileID.PrincessWeapon, 28, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DrillMountCrosshair, 24, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.PortalGunGate, 28, ownerIdx, ai0: baseDir.ToRotation(), ai1: 1f);
            }

            // Tier 7: Cincin Sihir Agung Surya (StardustTowerMark + RainbowRod + SunDance + Rainbow Crystal Explosion)
            if (tier >= 7)
            {
                ElementVisualHelper.SpawnMagicRitualRing(
                    target.Center,
                    ownerIdx,
                    ProjectileID.RainbowRodBullet,
                    ProjectileID.FairyQueenSunDance,
                    ProjectileID.StardustTowerMark,
                    radius: 72f,
                    outerCount: 8,
                    chordCount: 4,
                    spinSpeed: 5.2f,
                    timeLeft: 32);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.RainbowCrystalExplosion, 30, ownerIdx);
            }

            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            if (tier >= 7)
            {
                target.AddBuff(BuffID.Ichor, (4 + tier) * 60);
            }
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            if (tier >= 3)
            {
                SpawnRadianceHaloAura(attacker, Math.Min(8, tier + 1), 46f + (tier * 3f));
                attacker.SetBuff(BuffID.Shine, 360 + (tier * 30));
            }
            if (tier >= 4)
            {
                attacker.SetBuff(BuffID.Heartreach, 360);
            }
            if (tier >= 6)
            {
                attacker.SetBuff(BuffID.Wrath, 360);
            }
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2 || attacker == null || target == null || !target.active) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);
            int ownerIdx = attacker.Index;

            if (tier == 2)
            {
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.Starfury, 2, 32f, 115f, 11.5f, 22, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 10f, ProjectileID.FallingStar, 24, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 9f, ProjectileID.GoldenShowerFriendly, 22, ownerIdx);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.FallingStar, 3, 56f, 120f, 11f, 24, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 10f, ProjectileID.GoldenShowerHostile, 24, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 7.5f, ProjectileID.IchorSplash, 24, ownerIdx);
                SpawnRadianceHaloAura(attacker, 3, 46f);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 26f, dir * 12f, ProjectileID.EyeBeam, 26, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 12f, ProjectileID.UFOLaser, 26, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 12f, ProjectileID.ScutlixLaserFriendly, 26, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.ScutlixLaserCrosshair, 22, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 16f, dir * 4f, ProjectileID.Excalibur, 24, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 28f, dir * 14f, ProjectileID.SniperBullet, 22, ownerIdx);
                SpawnRadianceHaloAura(attacker, 4, 50f);
                attacker.SetBuff(BuffID.Shine, 300);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnMagicRitualRing(
                    target.Center,
                    ownerIdx,
                    ProjectileID.FallingStar,
                    ProjectileID.CultistBossLightningOrbArc,
                    ProjectileID.HallowBossSplitShotCore,
                    radius: 56f,
                    outerCount: 6,
                    chordCount: 3,
                    spinSpeed: 4.5f,
                    timeLeft: 28);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 8.5f, ProjectileID.PrincessWeapon, 26, ownerIdx);
                SpawnRadianceHaloAura(attacker, 5, 54f);
                attacker.SetBuff(BuffID.Shine, 360);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 9f, ProjectileID.EmpressBlade, 28, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 11f, ProjectileID.DD2PhoenixBowShot, 26, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.DrillMountCrosshair, 24, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.RainbowCrystalExplosion, 28, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FairyQueenSunDance, 3, 28f, 26, 3.5f, ownerIdx);
                SpawnRadianceHaloAura(attacker, 6, 58f);
                attacker.SetBuff(BuffID.Shine, 360);
                attacker.SetBuff(BuffID.Wrath, 300);
            }
            else
            {
                // Tier 7: Ring Sihir Cahaya Suci + Empress Terraprisma + Excalibur + Rainbow Crystal Burst
                ElementVisualHelper.SpawnMagicRitualRing(
                    target.Center,
                    ownerIdx,
                    ProjectileID.RainbowRodBullet,
                    ProjectileID.FairyQueenSunDance,
                    ProjectileID.StardustTowerMark,
                    radius: 68f,
                    outerCount: 8,
                    chordCount: 4,
                    spinSpeed: 5f,
                    timeLeft: 32);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.RainbowCrystalExplosion, 30, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 16f, dir * 4f, ProjectileID.Excalibur, 26, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.EmpressBlade, 2, 30f, 28, 4f, ownerIdx);
                SpawnRadianceHaloAura(attacker, 8, 64f);
                attacker.SetBuff(BuffID.Shine, 420);
                attacker.SetBuff(BuffID.Wrath, 360);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (attacker == null || target == null || !target.active) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);
            int ownerIdx = attacker.Index;

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.Starfury, 3, 46f, 120f, 12f, 24, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FallingStar, 4, 24f, 24, 4.5f, ownerIdx);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.IchorSplash, 4, 26f, 26, 3.8f, ownerIdx);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.FallingStar, 4, 70f, 125f, 12f, 26, ownerIdx);
                SpawnRadianceHaloAura(attacker, 4, 48f);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 4f, ProjectileID.Excalibur, 26, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.EyeBeam, 3, 28f, 26, 6.5f, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.ScutlixLaserFriendly, 3, 30f, 26, 7f, ownerIdx);
                SpawnRadianceHaloAura(attacker, 5, 52f);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnMagicRitualRing(
                    target.Center,
                    ownerIdx,
                    ProjectileID.RainbowRodBullet,
                    ProjectileID.ScutlixLaserFriendly,
                    ProjectileID.HallowBossSplitShotCore,
                    radius: 62f,
                    outerCount: 6,
                    chordCount: 4,
                    spinSpeed: 4.8f,
                    timeLeft: 30);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 4f, ProjectileID.Excalibur, 28, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 9f, ProjectileID.PrincessWeapon, 28, ownerIdx);
                SpawnRadianceHaloAura(attacker, 6, 56f);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnMagicRitualRing(
                    target.Center,
                    ownerIdx,
                    ProjectileID.FallingStar,
                    ProjectileID.CultistBossLightningOrbArc,
                    ProjectileID.RainbowCrystalExplosion,
                    radius: 68f,
                    outerCount: 8,
                    chordCount: 4,
                    spinSpeed: 5.2f,
                    timeLeft: 32);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.EmpressBlade, 3, 36f, 30, 4.5f, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.PortalGunGate, 28, ownerIdx, ai0: dir.ToRotation(), ai1: 1f);
                SpawnRadianceHaloAura(attacker, 7, 60f);
            }
            else
            {
                // Tier 7: Penghakiman Surya Absolut (Magic Ritual Circle + RainbowCrystalExplosion + SunDance + Terraprisma + Excalibur + Starfall)
                ElementVisualHelper.SpawnMagicRitualRing(
                    target.Center,
                    ownerIdx,
                    ProjectileID.RainbowRodBullet,
                    ProjectileID.FairyQueenSunDance,
                    ProjectileID.StardustTowerMark,
                    radius: 76f,
                    outerCount: 8,
                    chordCount: 4,
                    spinSpeed: 5.5f,
                    timeLeft: 34);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.RainbowCrystalExplosion, 34, ownerIdx);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 16f, dir * 4f, ProjectileID.Excalibur, 30, ownerIdx);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.EmpressBlade, 4, 44f, 32, 4.5f, ownerIdx);
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.FallingStar, 5, 100f, 140f, 13f, 28, ownerIdx);
                SpawnRadianceHaloAura(attacker, 8, 66f);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || owner == null || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            attackerNpc.AddBuff(GetMobDebuffId(tier), (3 + tier) * 60);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            if (tier >= 3)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
                int counterProj = tier >= 5 ? ProjectileID.ScutlixLaserFriendly : ProjectileID.FallingStar;
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 10.5f, counterProj, 26, owner.Index);
                if (tier >= 4)
                {
                    ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 11f, ProjectileID.EyeBeam, 24, owner.Index);
                }
                SpawnRadianceHaloAura(owner, Math.Min(8, tier + 1), 52f);
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || owner == null || attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(GetMobDebuffId(tier), 180 + (tier * 30));

            if (tier >= 3)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                int counterProj = tier >= 5 ? ProjectileID.ScutlixLaserFriendly : ProjectileID.FallingStar;
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 10.5f, counterProj, 26, owner.Index);
                SpawnRadianceHaloAura(owner, Math.Min(8, tier + 1), 52f);
            }
        }
    }
}
