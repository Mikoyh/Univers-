using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace Univers.Aetherial.Elements
{
    public class OmniaElement : IElementProfile
    {
        public string ElementId => "OMNIA";
        public string HexColor => "ef4444";            // Merah Crimson Kosmik
        public int LargeGemItemId => ItemID.LargeRuby;

        // Evolusi Debuff Kosmik: Tier 1-3 ShadowFlame | Tier 4-6 Betsy's Curse | Tier 7 Daybroken + All
        public int GetMobDebuffId(int tier)
        {
            if (tier >= 7) return BuffID.Daybreak;
            if (tier >= 4) return BuffID.BetsysCurse;
            return BuffID.ShadowFlame;
        }

        // Secret Element 0.000001%: Bersinergi dengan SEMUA senjata!
        public bool IsWeaponSynergy(Item held)
        {
            return held != null && !held.IsAir && held.type > 0;
        }

        // Helper khusus: Cincin kristal merah darah (813 & 814) berputar mengelilingi tubuh player
        private static void SpawnOmniaCrimsonAura(TSPlayer owner, int count, float radius)
        {
            if (owner == null || !owner.Active || owner.TPlayer == null) return;
            Vector2 center = owner.TPlayer.Center;

            for (int i = 0; i < count; i++)
            {
                float angle = MathHelper.TwoPi * i / count;
                Vector2 offset = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                Vector2 tangentVel = new Vector2(-(float)Math.Sin(angle), (float)Math.Cos(angle)) * 5f;

                int projId = (i % 2 == 0) ? 813 : 814; // 813 = BloodNautilusTears, 814 = BloodNautilusShot
                ElementVisualHelper.SpawnVisualProjectile(center + offset, tangentVel, projId, 32);
            }
        }

        // [SKILL 10 DETIK - TIER 2+]: Tembakan Ruby Bolt + Blood Thorn + Eye of Cthulhu + Full Aura Merah!
        public void OnTriggerElementSkill(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2 || attacker == null || target == null || !target.active) return;

            int boltCount = Math.Clamp(tier + 2, 3, 8);
            Vector2 baseDir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            for (int i = 0; i < boltCount; i++)
            {
                float spread = (i - (boltCount - 1) / 2f) * 0.12f;
                Vector2 shotDir = baseDir.RotatedBy(spread);
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 13f, ProjectileID.RubyBolt, 35);
            }

            // Duri darah (756 = SharpTears) & Awan Hujan Darah (244 = BloodCloudRaining)
            ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 24f, baseDir * 9f, 756, 30);
            if (tier >= 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(new Vector2(target.Center.X, target.Center.Y - 90f), Vector2.Zero, 244, 34);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 811, tier + 1, 34f, 28, 3.5f); // 811 = BloodShot
            }
            if (tier >= 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 26f, baseDir * 10.5f, 983, 32); // 983 = TrueExcalibur
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, baseDir * 11f, ProjectileID.FinalFractal, 32);
            }
            if (tier >= 6)
            {
                // 1130 = TheEyeOfCthulhuDash (Serbuan Eye of Cthulhu Merah!)
                ElementVisualHelper.SpawnVisualProjectile(target.Center - baseDir * 40f, baseDir * 13f, 1130, 34);
            }

            target.AddBuff(BuffID.ShadowFlame, 420);
            target.AddBuff(BuffID.BetsysCurse, 420);
            if (tier >= 5) target.AddBuff(BuffID.Daybreak, 420);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            SpawnOmniaCrimsonAura(attacker, Math.Min(8, tier + 2), 48f + (tier * 3f));
            attacker.SetBuff(BuffID.Inferno, 420);
            attacker.SetBuff(BuffID.Wrath, 420);
            attacker.SetBuff(BuffID.Rage, 420);
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 8f, 811, 26); // 811 = BloodShot
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 8.5f, 591, 24); // 591 = MinecartMechLaser
                SpawnOmniaCrimsonAura(attacker, 4, 46f);
            }
            else if (tier <= 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 8.5f, 756, 28); // 756 = Blood Thorn
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 9f, 983, 28);   // 983 = TrueExcalibur
                ElementVisualHelper.SpawnRainProjectiles(target.Center, 245, 4, 56f, 110f, 11f, 24);       // 245 = BloodRain
                SpawnOmniaCrimsonAura(attacker, 6, 52f);
            }
            else if (tier <= 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 9.5f, 983, 30);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 9f, 756, 30);
                ElementVisualHelper.SpawnVisualProjectile(new Vector2(target.Center.X, target.Center.Y - 85f), Vector2.Zero, 244, 30);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 389, 3, 32f, 26, 4.5f); // 389 = MiniRetinaLaser
                SpawnOmniaCrimsonAura(attacker, 7, 58f);
            }
            else
            {
                // Tier 7 (Lategame): Eye of Cthulhu Dash + Tebasan Merah + Blood Thorn + Hujan Darah + 8 Aura!
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 36f, dir * 12f, 1130, 32);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 10f, 983, 32);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 9.5f, 756, 30);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 814, 4, 36f, 30, 4f);
                SpawnOmniaCrimsonAura(attacker, 8, 64f);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier <= 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 8.5f, 756, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 811, 4, 28f, 26, 3.5f);
                SpawnOmniaCrimsonAura(attacker, 5, 50f);
            }
            else if (tier <= 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 9.5f, 983, 30);
                ElementVisualHelper.SpawnVisualProjectile(new Vector2(target.Center.X, target.Center.Y - 90f), Vector2.Zero, 244, 32);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 756, 3, 34f, 28, 4f);
                SpawnOmniaCrimsonAura(attacker, 7, 58f);
            }
            else
            {
                // Tier 6-7: Penghakiman Merah Absolut (Eye of Cthulhu + Awan Darah + Tebasan Merah + 6x Blood Shot + 8 Aura!)
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 38f, dir * 12.5f, 1130, 34);
                ElementVisualHelper.SpawnVisualProjectile(new Vector2(target.Center.X, target.Center.Y - 95f), Vector2.Zero, 244, 34);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 22f, dir * 10f, 983, 34);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 756, 4, 42f, 32, 4.5f);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, 814, 6, 48f, 34, 4.5f);
                SpawnOmniaCrimsonAura(attacker, 8, 66f);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            attackerNpc.AddBuff(BuffID.ShadowFlame, 360);
            attackerNpc.AddBuff(BuffID.BetsysCurse, 360);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
            ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 10f, 756, 28); // 756 = Blood Thorn balasan
            ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 11f, 983, 28); // 983 = TrueExcalibur
            SpawnOmniaCrimsonAura(owner, 8, 56f);
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(BuffID.ShadowFlame, 240);
            attackerPlayer.SetBuff(BuffID.Ichor, 240);

            Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
            ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 10f, 756, 28);
            ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 11f, 814, 28);
            SpawnOmniaCrimsonAura(owner, 8, 56f);
        }
    }
}
