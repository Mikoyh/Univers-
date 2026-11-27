/*
 * =====================================================================
 *  ElementVisualHelper.cs - Shared Projectile & Magic Ring Visual Utilities
 *  Namespace: Univers.Aetherial.Elements (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Univers.Aetherial.Elements
{
    internal static class ElementVisualHelper
    {
        private static readonly Random Rnd = new();

        // Konstanta Projectile ID Terraria 1.4.4 / 1.4.5 yang mungkin belum memiliki alias tetap di semua build OTAPI
        public const int ProjTrueExcalibur144 = 983;
        public const int ProjTerraBlade144 = 984;
        public const int ProjTerraBladeShot144 = 985;
        public const int ProjVolcano144 = 978;
        public const int ProjHorsemansBlade144 = 997;
        public const int ProjDeadCellsFlintSlash = 1043;
        public const int ProjFoxparksFireball = 1097;
        public const int ProjChlorophyteClaymoreBlade = 1126;
        public const int ProjChlorophyteSaber = 1127;
        public const int ProjChlorophyteOrbShot = 1129;
        public const int ProjEyeOfCthulhuDash = 1130;
        public const int ProjPyroclasticStoneEffect = 1131;
        public const int ProjArmletOfRuinEffect = 1132;

        public static Vector2 GetAimDirection(Vector2 from, Vector2 to)
        {
            Vector2 diff = to - from;
            return diff.LengthSquared() > 1f ? Vector2.Normalize(diff) : Vector2.UnitX;
        }

        public static void SpawnRingProjectiles(
            Vector2 center,
            int projId,
            int count,
            float radius,
            int timeLeft,
            float outwardSpeed = 2.5f,
            int ownerIndex = -1,
            float scale = 1f)
        {
            if (count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                double angle = (Math.PI * 2.0 / count) * i;
                Vector2 offset = new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                Vector2 vel = new Vector2((float)Math.Cos(angle) * outwardSpeed, (float)Math.Sin(angle) * outwardSpeed);
                SpawnVisualProjectile(center + offset, vel, projId, timeLeft, ownerIndex, scale: scale);
            }
        }

        public static void SpawnOrbitAura(
            Vector2 center,
            int projIdA,
            int projIdB,
            int count,
            float radius,
            float tangentSpeed,
            int timeLeft,
            int ownerIndex = -1)
        {
            if (count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                float angle = MathHelper.TwoPi * i / count;
                Vector2 offset = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                Vector2 tangentVel = new Vector2(-(float)Math.Sin(angle), (float)Math.Cos(angle)) * tangentSpeed;
                int projId = (i % 2 == 0) ? projIdA : projIdB;
                SpawnVisualProjectile(center + offset, tangentVel, projId, timeLeft, ownerIndex);
            }
        }

        public static void SpawnRainProjectiles(
            Vector2 targetCenter,
            int projId,
            int count,
            float spreadWidth,
            float heightOffset,
            float fallSpeed,
            int timeLeft,
            int ownerIndex = -1)
        {
            if (count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                float offsetX = count == 1 ? 0f : (i - (count - 1) / 2f) * (spreadWidth / Math.Max(1, count - 1));
                Vector2 spawnPos = new Vector2(targetCenter.X + offsetX, targetCenter.Y - heightOffset);
                Vector2 vel = new Vector2(-offsetX * 0.02f, fallSpeed);
                SpawnVisualProjectile(spawnPos, vel, projId, timeLeft, ownerIndex);
            }
        }

        /// <summary>
        /// Membentuk Cincin Sihir Ritual bergaya Lunatic Cultist (Core Sigil + Cincin Berputar + Garis Bintang Arcane)
        /// yang 100% tampil di client vanilla tanpa perlu spawn NPC boss palsu.
        /// </summary>
        public static void SpawnMagicRitualRing(
            Vector2 center,
            int ownerIndex,
            int ringOuterProjId,
            int arcProjId,
            int coreProjId,
            float radius = 58f,
            int outerCount = 8,
            int chordCount = 4,
            float spinSpeed = 4.2f,
            int timeLeft = 30,
            bool tryNativeCultistRitual = true)
        {
            if (tryNativeCultistRitual)
            {
                for (int n = 0; n < Main.maxNPCs; n++)
                {
                    var npc = Main.npc[n];
                    if (npc != null && npc.active && npc.type == NPCID.CultistBoss)
                    {
                        SpawnVisualProjectile(center, Vector2.Zero, ProjectileID.CultistRitual, 45, ownerIndex, ai0: 10f, ai1: n);
                        break;
                    }
                }
            }

            if (coreProjId > 0)
            {
                SpawnVisualProjectile(center, Vector2.Zero, coreProjId, timeLeft + 4, ownerIndex, ai0: 0f, ai1: 0f, scale: 1.25f);
            }

            if (outerCount > 0 && ringOuterProjId > 0)
            {
                for (int i = 0; i < outerCount; i++)
                {
                    float angle = MathHelper.TwoPi * i / outerCount;
                    Vector2 offset = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                    Vector2 tangentVel = new Vector2(-(float)Math.Sin(angle), (float)Math.Cos(angle)) * spinSpeed;
                    SpawnVisualProjectile(center + offset, tangentVel, ringOuterProjId, timeLeft, ownerIndex);
                }
            }

            if (chordCount > 0 && arcProjId > 0)
            {
                for (int k = 0; k < chordCount; k++)
                {
                    float a1 = MathHelper.TwoPi * k / chordCount;
                    float a2 = a1 + (float)(Math.PI * 0.75);
                    Vector2 p1 = center + new Vector2((float)Math.Cos(a1), (float)Math.Sin(a1)) * (radius * 0.9f);
                    Vector2 p2 = center + new Vector2((float)Math.Cos(a2), (float)Math.Sin(a2)) * (radius * 0.9f);
                    Vector2 chordDir = GetAimDirection(p1, p2);
                    float chordSpeed = (arcProjId == ProjectileID.FairyQueenSunDance) ? 2.5f : 11f;
                    SpawnVisualProjectile(
                        p1,
                        chordDir * chordSpeed,
                        arcProjId,
                        Math.Min(timeLeft, 24),
                        ownerIndex,
                        ai0: chordDir.ToRotation(),
                        ai1: arcProjId == ProjectileID.FairyQueenSunDance ? 0.04f : Rnd.Next(100));
                }
            }
        }

        public static void SpawnVisualProjectile(
            Vector2 pos,
            Vector2 vel,
            int projId,
            int timeLeft,
            int ownerIndex = -1,
            float ai0 = 0f,
            float ai1 = 0f,
            float ai2 = 0f,
            float rotation = 0f,
            float scale = 1f)
        {
            if (projId <= 0) return;

            int effectiveOwner = (ownerIndex >= 0 && ownerIndex < 255) ? ownerIndex : Main.myPlayer;
            Vector2 finalVel = vel;

            // Lightning arc (aiStyle 88: 466 CultistBossLightningOrbArc, 580 VortexLightning) butuh arah & seed non-nol
            if (projId == ProjectileID.CultistBossLightningOrbArc || projId == ProjectileID.VortexLightning)
            {
                if (finalVel.LengthSquared() < 0.04f)
                {
                    double a = Rnd.NextDouble() * Math.PI * 2.0;
                    finalVel = new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * 10.5f;
                }
                if (ai0 == 0f) ai0 = finalVel.ToRotation();
                if (ai1 == 0f) ai1 = Rnd.Next(1, 100);
            }
            // Tebasan pedang energi 1.4.4/1.4.5 butuh ai[0] arah swing, ai[1] durasi, ai[2] scale agar tidak 0/invisible
            else if (projId == ProjectileID.Excalibur ||
                     projId == ProjTrueExcalibur144 ||
                     projId == ProjTerraBlade144 ||
                     projId == ProjDeadCellsFlintSlash)
            {
                if (ai0 == 0f) ai0 = finalVel.X >= 0f ? 1f : -1f;
                if (ai1 == 0f) ai1 = Math.Max(20f, timeLeft);
                if (ai2 == 0f) ai2 = scale > 1f ? scale : 1.35f;
            }
            // FairyQueenSunDance (923): ai[0] sudut sinar, ai[1] kecepatan rotasi
            else if (projId == ProjectileID.FairyQueenSunDance)
            {
                if (ai0 == 0f && finalVel.LengthSquared() > 0.01f) ai0 = finalVel.ToRotation();
                if (ai1 == 0f) ai1 = 0.045f;
            }
            // PortalGunGate (602): ai[0] rotasi gerbang
            else if (projId == ProjectileID.PortalGunGate)
            {
                if (ai0 == 0f && finalVel.LengthSquared() > 0.01f) ai0 = finalVel.ToRotation();
            }

            int idx = Projectile.NewProjectile(
                Projectile.GetNoneSource(),
                pos,
                finalVel,
                projId,
                0,
                0f,
                effectiveOwner,
                ai0,
                ai1,
                ai2);

            if (idx >= 0 && idx < Main.maxProjectiles)
            {
                var p = Main.projectile[idx];
                p.friendly = true;
                p.hostile = false;
                p.damage = 0;
                p.knockBack = 0f;
                p.direction = finalVel.X >= 0f ? 1 : -1;
                p.spriteDirection = p.direction;
                p.timeLeft = Math.Max(2, timeLeft);
                if (rotation != 0f) p.rotation = rotation;
                else if (finalVel.LengthSquared() > 0.01f &&
                         (projId == ProjectileID.CultistBossLightningOrbArc ||
                          projId == ProjectileID.VortexLightning ||
                          projId == ProjectileID.FairyQueenSunDance))
                {
                    p.rotation = finalVel.ToRotation();
                }
                if (scale != 1f) p.scale = scale;

                NetMessage.SendData((int)PacketTypes.ProjectileNew, -1, -1, null, idx);
            }
        }

    }
}
