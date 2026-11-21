/*
 * =====================================================================
 *  ElementVisualHelper.cs - Shared Projectile & Vector Visual Utilities
 *  Namespace: Univers.Aetherial.Elements (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace Univers.Aetherial.Elements
{
    internal static class ElementVisualHelper
    {
        public static Vector2 GetAimDirection(Vector2 from, Vector2 to)
        {
            Vector2 diff = to - from;
            return diff.LengthSquared() > 1f ? Vector2.Normalize(diff) : Vector2.UnitX;
        }

        public static void SpawnRingProjectiles(Vector2 center, int projId, int count, float radius, int timeLeft, float outwardSpeed = 2.5f)
        {
            for (int i = 0; i < count; i++)
            {
                double angle = (Math.PI * 2.0 / count) * i;
                Vector2 offset = new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                Vector2 vel = new Vector2((float)Math.Cos(angle) * outwardSpeed, (float)Math.Sin(angle) * outwardSpeed);
                SpawnVisualProjectile(center + offset, vel, projId, timeLeft);
            }
        }

        public static void SpawnRainProjectiles(Vector2 targetCenter, int projId, int count, float spreadWidth, float heightOffset, float fallSpeed, int timeLeft)
        {
            for (int i = 0; i < count; i++)
            {
                float offsetX = count == 1 ? 0f : (i - (count - 1) / 2f) * (spreadWidth / Math.Max(1, count - 1));
                Vector2 spawnPos = new Vector2(targetCenter.X + offsetX, targetCenter.Y - heightOffset);
                Vector2 vel = new Vector2(-offsetX * 0.02f, fallSpeed);
                SpawnVisualProjectile(spawnPos, vel, projId, timeLeft);
            }
        }

        public static void SpawnVisualProjectile(Vector2 pos, Vector2 vel, int projId, int timeLeft)
        {
            int idx = Projectile.NewProjectile(Projectile.GetNoneSource(), pos, vel, projId, 0, 0f, Main.myPlayer);
            if (idx >= 0 && idx < Main.maxProjectiles)
            {
                Main.projectile[idx].friendly = true;
                Main.projectile[idx].hostile = false;
                Main.projectile[idx].direction = vel.X >= 0 ? 1 : -1;
                Main.projectile[idx].spriteDirection = Main.projectile[idx].direction;
                Main.projectile[idx].timeLeft = timeLeft;
                NetMessage.SendData((int)PacketTypes.ProjectileNew, -1, -1, null, idx);
            }
        }
    }
}
