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

        // [DOMAIN EXPANSION - JOGO VOLCANIC MAGMA VIBE]: Ryoiki Tenkai: CRIMSON HELLFIRE
        public bool SupportsDomainExpansion => true;
        public string DomainName => "CRIMSON HELLFIRE";

        public static float GetDomainRadius(int tier) => 190f + (Math.Clamp(tier, 1, 7) * 40f);

        public void OnDomainActivate(TSPlayer owner, Vector2 center, int tier)
        {
            if (owner == null || !owner.Active || owner.TPlayer == null) return;

            int ownerIdx = owner.Index;
            float radius = GetDomainRadius(tier);

            // 1. Dinding Magma Keliling Gila-gilaan (Multi-layer boundary ring)
            ElementVisualHelper.SpawnRingProjectiles(center, ProjectileID.SolarFlareRay, 12, radius * 0.9f, 40, 4f, ownerIdx, scale: 1.3f);
            ElementVisualHelper.SpawnRingProjectiles(center, ProjectileID.DD2BetsyFireball, 8, radius * 0.75f, 38, 3.5f, ownerIdx, scale: 1.2f);

            // 2. Pusat Kawah Meledak (Volcano & Solar Explosion)
            ElementVisualHelper.SpawnVisualProjectile(center, Vector2.Zero, ElementVisualHelper.ProjVolcano144, 36, ownerIdx, scale: 1.5f);
            ElementVisualHelper.SpawnVisualProjectile(center, Vector2.Zero, ProjectileID.SolarWhipSwordExplosion, 34, ownerIdx, scale: 1.4f);
            ElementVisualHelper.SpawnVisualProjectile(center, Vector2.Zero, ProjectileID.DD2ExplosiveTrapT3Explosion, 32, ownerIdx);

            // 3. Magic Ritual Circle Magma Kosmik
            ElementVisualHelper.SpawnMagicRitualRing(
                center,
                ownerIdx,
                ProjectileID.SolarFlareRay,
                ProjectileID.FairyQueenSunDance,
                ProjectileID.StardustTowerMark,
                radius: Math.Min(110f, radius * 0.4f),
                outerCount: 8,
                chordCount: 4,
                spinSpeed: 6.0f,
                timeLeft: 36);

            // 4. Hujan Meteor Magma Awal (Volcanic Rain Burst)
            ElementVisualHelper.SpawnRainProjectiles(center, ProjectileID.DD2BetsyFireball, 6, radius * 1.4f, 180f, 14f, 32, ownerIdx);
            ElementVisualHelper.SpawnRainProjectiles(center, ProjectileID.Daybreak, 4, radius * 1.2f, 160f, 15f, 30, ownerIdx);

            owner.SetBuff(BuffID.Inferno, (8 + tier) * 60);
            owner.SetBuff(BuffID.Wrath, (8 + tier) * 60);
            owner.SetBuff(BuffID.Lifeforce, (8 + tier) * 60);
        }

        public void OnDomainPulse(TSPlayer owner, Vector2 center, int tier, int pulseIndex)
        {
            if (owner == null || !owner.Active || owner.TPlayer == null) return;

            int ownerIdx = owner.Index;
            float radius = GetDomainRadius(tier);
            float spinAngle = pulseIndex * 0.55f;

            // 1. Dinding Magma Berputar Cepat di Batas Domain
            int edgeCount = Math.Min(12, 6 + tier);
            int edgeProj = (pulseIndex % 2 == 0) ? ProjectileID.SolarFlareRay : ProjectileID.DD2BetsyFireball;
            for (int i = 0; i < edgeCount; i++)
            {
                float angle = spinAngle + MathHelper.TwoPi * i / edgeCount;
                Vector2 edgePos = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                Vector2 tangentVel = new Vector2(-(float)Math.Sin(angle), (float)Math.Cos(angle)) * 5.5f;
                ElementVisualHelper.SpawnVisualProjectile(edgePos, tangentVel, edgeProj, 26, ownerIdx);
            }

            // 2. Lantai Kawah Meletus (Eruption from Ground)
            for (int e = 0; e < 2; e++)
            {
                float randOffset = (Rnd.NextSingle() - 0.5f) * (radius * 1.3f);
                Vector2 floorPos = new Vector2(center.X + randOffset, center.Y + 20f);
                int floorProj = (e == 0) ? ProjectileID.DD2ExplosiveTrapT2Explosion : ElementVisualHelper.ProjVolcano144;
                ElementVisualHelper.SpawnVisualProjectile(floorPos, new Vector2(0f, -8f), floorProj, 26, ownerIdx);
            }

            // 3. Hujan Magma Jogo (Volcanic Rain setiap pulse)
            ElementVisualHelper.SpawnRainProjectiles(center, ProjectileID.FlamingScythe, 2, radius * 1.1f, 150f, 13f, 26, ownerIdx);
            if (pulseIndex % 2 == 0)
            {
                ElementVisualHelper.SpawnRainProjectiles(center, ProjectileID.Daybreak, 2, radius * 1.2f, 170f, 14f, 26, ownerIdx);
            }

            // 4. Letusan Sigil Inti Berkala
            if (pulseIndex % 3 == 0)
            {
                ElementVisualHelper.SpawnMagicRitualRing(
                    center,
                    ownerIdx,
                    ProjectileID.SolarFlareRay,
                    ProjectileID.EyeBeam,
                    ProjectileID.SolarWhipSwordExplosion,
                    radius: Math.Min(96f, radius * 0.35f),
                    outerCount: 8,
                    chordCount: 4,
                    spinSpeed: 5.5f,
                    timeLeft: 30,
                    tryNativeCultistRitual: false,
                    angleOffset: spinAngle);
            }

            // 5. Caster Buff Maintenance
            if ((owner.TPlayer.Center - center).Length() <= radius)
            {
                owner.SetBuff(BuffID.Inferno, 180);
                owner.SetBuff(BuffID.Campfire, 180);
            }

            // 6. Siksa Musuh di dalam Domain (Magma Burn & Volcanic Strike)
            int hitCount = 0;
            int pulseDamage = 35 + (tier * 35);
            int debuffId = GetMobDebuffId(tier);

            foreach (var npc in Main.npc)
            {
                if (npc == null || !npc.active || npc.friendly || npc.lifeMax <= 5 || npc.dontTakeDamage) continue;
                if ((npc.Center - center).Length() > radius) continue;

                npc.AddBuff(debuffId, 280);
                npc.AddBuff(BuffID.OnFire3, 280);
                NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, npc.whoAmI);

                int hitDir = npc.Center.X >= center.X ? 1 : -1;
                npc.StrikeNPC(pulseDamage, 2f, hitDir);
                NetMessage.SendData((int)PacketTypes.NpcStrike, -1, -1, null, npc.whoAmI, pulseDamage, 2f, hitDir);

                // Volcanic Bombardment tepat ke badan musuh
                Vector2 strikeDir = ElementVisualHelper.GetAimDirection(center, npc.Center);
                int strikeProj = tier switch
                {
                    <= 3 => ProjectileID.DD2BetsyFireball,
                    <= 5 => ProjectileID.DD2BetsyFlameBreath,
                    6 => ProjectileID.EyeBeam,
                    _ => ProjectileID.Daybreak
                };
                ElementVisualHelper.SpawnVisualProjectile(npc.Center - strikeDir * 40f, strikeDir * 12f, strikeProj, 26, ownerIdx);

                hitCount++;
                if (hitCount >= 5) break;
            }
        }
    }
}
