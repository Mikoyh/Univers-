using System;
using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI;

namespace Univers.Aetherial.Elements
{
    public class IgnisElement : IElementProfile
    {
        public string ElementId => "IGNIS";
        public string HexColor => "f97316";      // Oranye Api
        public int LargeGemItemId => 3643;       // Large Amber

        // Evolusi Debuff: Tier 1-3 On Fire (24) | Tier 4-6 Hellfire (323) | Tier 7 Daybroken (189)
        public int GetMobDebuffId(int tier)
        {
            if (tier >= 7) return 189; // Daybroken (100 DPS!)
            if (tier >= 4) return 323; // Hellfire (15 DPS)
            return 24;                 // On Fire! (4 DPS)
        }

        public bool IsWeaponSynergy(Item held)
        {
            if (held == null || string.IsNullOrEmpty(held.Name)) return false;
            string n = held.Name;
            return n.Contains("Flame") || n.Contains("Fire") || n.Contains("Molten") ||
                   n.Contains("Hell") || n.Contains("Solar") || n.Contains("Volcano") ||
                   n.Contains("Sunfury") || n.Contains("Flamarang") || n.Contains("Phoenix");
        }

        // [SKILL 10 DETIK - TIER 2+]: Menembakkan Amber Bolt (597) & Memicu Debuff Sesuai Tier!
        public void OnTriggerElementSkill(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2 || attacker == null || target == null || !target.active) return;

            // Tentukan jumlah tembakan Amber Bolt (597) berdasarkan Tier
            int boltCount = tier switch
            {
                2 => 1,
                3 => 2,
                <= 5 => 3,
                6 => 4,
                _ => 5
            };

            Vector2 baseDir = GetAimDirection(attacker.TPlayer.Center, target.Center);

            for (int i = 0; i < boltCount; i++)
            {
                float spread = (i - (boltCount - 1) / 2f) * 0.14f;
                Vector2 shotDir = baseDir.RotatedBy(spread);
                SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 11f, 597, 35); // 597 = Amber Bolt
            }

            // Musuh langsung terkena debuff api sesuai tingkatan Tier pemain!
            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            // Tier 4+: Aktifkan visual cincin api Inferno (Buff ID 116) di tubuh pemain selama 6 detik!
            if (tier >= 4)
            {
                attacker.SetBuff(116, 360);
            }
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2) return;
            Vector2 dir = GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier == 2)
            {
                // Tier 2: Foxparks Fireball (1097)
                SpawnVisualProjectile(target.Center - dir * 24f, dir * 6f, 1097, 24);
            }
            else if (tier == 3)
            {
                // Tier 3: Foxparks Fireball (1097) + Flaming Scythe (329)
                SpawnVisualProjectile(target.Center - dir * 24f, dir * 6f, 1097, 25);
                SpawnVisualProjectile(target.Center - dir * 20f, dir * 5f, 329, 25);
            }
            else if (tier == 4)
            {
                // Tier 4: Cultist Fireball (467) + Explosive Trap T1 (694) + Inferno Ring
                SpawnVisualProjectile(target.Center - dir * 24f, dir * 7f, 467, 26);
                SpawnVisualProjectile(target.Center, Vector2.Zero, 694, 22);
                attacker.SetBuff(116, 300);
            }
            else if (tier == 5)
            {
                // Tier 5: Horseman's Blade (997) + Explosive Trap T2 (695)
                SpawnVisualProjectile(target.Center - dir * 20f, dir * 7f, 997, 26);
                SpawnVisualProjectile(target.Center, Vector2.Zero, 695, 24);
                attacker.SetBuff(116, 360);
            }
            else if (tier == 6)
            {
                // Tier 6: Horseman's Blade (997) + Volcano (978)
                SpawnVisualProjectile(target.Center - dir * 20f, dir * 7f, 997, 28);
                SpawnVisualProjectile(target.Center, Vector2.Zero, 978, 25);
                attacker.SetBuff(116, 360);
            }
            else
            {
                // Tier 7: Volcano (978) + Explosive Trap T3 (696)
                SpawnVisualProjectile(target.Center, Vector2.Zero, 978, 28);
                SpawnVisualProjectile(target.Center, Vector2.Zero, 696, 26);
                attacker.SetBuff(116, 420);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            Vector2 dir = GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier <= 2)
            {
                SpawnVisualProjectile(target.Center, Vector2.Zero, 694, 24); // Trap Explosion T1
            }
            else if (tier == 3)
            {
                SpawnVisualProjectile(target.Center, Vector2.Zero, 695, 26); // Trap Explosion T2
            }
            else if (tier == 4)
            {
                SpawnVisualProjectile(target.Center, Vector2.Zero, 978, 26); // Volcano
                SpawnVisualProjectile(target.Center, Vector2.Zero, 696, 26); // Trap Explosion T3
            }
            else if (tier == 5)
            {
                SpawnVisualProjectile(target.Center, Vector2.Zero, 612, 28); // Solar Explosion
                SpawnRingProjectiles(target.Center, 296, 2, 30f, 28);      // 2x Inferno Blast
            }
            else if (tier == 6)
            {
                SpawnVisualProjectile(target.Center, Vector2.Zero, 612, 28); // Solar Explosion
                SpawnRingProjectiles(target.Center, 296, 3, 42f, 30);      // 3x Inferno Blast
            }
            else
            {
                // Tier 7: Ledakan Puncak Kosmis (Volcano + Solar + Inferno + 4x Daybreak)
                SpawnVisualProjectile(target.Center, Vector2.Zero, 978, 30);
                SpawnVisualProjectile(target.Center, Vector2.Zero, 612, 30);
                SpawnVisualProjectile(target.Center, Vector2.Zero, 296, 32);
                SpawnRingProjectiles(target.Center, 953, 4, 48f, 32);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            int debuffId = GetMobDebuffId(tier);
            attackerNpc.AddBuff(debuffId, (3 + tier) * 60);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            if (tier >= 4)
            {
                Vector2 dir = GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
                SpawnVisualProjectile(owner.TPlayer.Center, dir * 7f, 1097, 25); // Foxparks Fireball balasan
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || attackerPlayer == null || !attackerPlayer.Active) return;

            int debuffId = GetMobDebuffId(tier);
            attackerPlayer.SetBuff(debuffId, 180 + (tier * 30));

            if (tier >= 4)
            {
                Vector2 dir = GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                SpawnVisualProjectile(owner.TPlayer.Center, dir * 7f, 1097, 25);
            }
        }

        private static Vector2 GetAimDirection(Vector2 from, Vector2 to)
        {
            Vector2 diff = to - from;
            return diff.LengthSquared() > 1f ? Vector2.Normalize(diff) : Vector2.UnitX;
        }

        private static void SpawnRingProjectiles(Vector2 center, int projId, int count, float radius, int timeLeft)
        {
            for (int i = 0; i < count; i++)
            {
                double angle = (Math.PI * 2.0 / count) * i;
                Vector2 offset = new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                Vector2 vel = new Vector2((float)Math.Cos(angle) * 2.5f, (float)Math.Sin(angle) * 2.5f);
                SpawnVisualProjectile(center + offset, vel, projId, timeLeft);
            }
        }

        private static void SpawnVisualProjectile(Vector2 pos, Vector2 vel, int projId, int timeLeft)
        {
            int idx = Projectile.NewProjectile(Projectile.GetNoneSource(), pos, vel, projId, 0, 0f, Main.myPlayer);
            if (idx >= 0 && idx < Main.maxProjectiles)
            {
                Main.projectile[idx].direction = vel.X >= 0 ? 1 : -1;
                Main.projectile[idx].spriteDirection = Main.projectile[idx].direction;
                Main.projectile[idx].timeLeft = timeLeft;
                NetMessage.SendData((int)PacketTypes.ProjectileNew, -1, -1, null, idx);
            }
        }
    }
}
