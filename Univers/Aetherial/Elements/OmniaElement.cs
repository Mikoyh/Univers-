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
        public string HexColor => "e879f9";            // Magenta Singularitas Kosmik
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

        // [SKILL 10 DETIK - TIER 2+]: Tembakan Ruby Bolt + Zenith (FinalFractal) + Full Aura!
        public void OnTriggerElementSkill(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2 || attacker == null || target == null || !target.active) return;

            int boltCount = Math.Clamp(tier, 2, 6);
            Vector2 baseDir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            for (int i = 0; i < boltCount; i++)
            {
                float spread = (i - (boltCount - 1) / 2f) * 0.14f;
                Vector2 shotDir = baseDir.RotatedBy(spread);
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 13f, ProjectileID.RubyBolt, 35);
            }

            if (tier >= 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, baseDir * 10f, ProjectileID.FinalFractal, 30);
            }

            target.AddBuff(BuffID.ShadowFlame, 420);
            target.AddBuff(BuffID.BetsysCurse, 420);
            if (tier >= 5) target.AddBuff(BuffID.Daybreak, 420);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            attacker.SetBuff(BuffID.Inferno, 360);
            attacker.SetBuff(BuffID.DryadsWard, 360);
            attacker.SetBuff(BuffID.Wrath, 360);
            attacker.SetBuff(BuffID.Rage, 360);
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 7f, ProjectileID.RainbowRodBullet, 26);
            }
            else if (tier <= 4)
            {
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.LunarFlare, 2, 40f, 120f, 11f, 26);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 7f, ProjectileID.NebulaArcanum, 26);
            }
            else
            {
                ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.StarWrath, 3, 64f, 130f, 12f, 28);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 8f, ProjectileID.FinalFractal, 30);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.RainbowCrystalExplosion, 30);
            ElementVisualHelper.SpawnRainProjectiles(target.Center, ProjectileID.StarWrath, 4, 80f, 135f, 12f, 30);
            ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FinalFractal, 4, 46f, 32, 4f);
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            attackerNpc.AddBuff(BuffID.ShadowFlame, 360);
            attackerNpc.AddBuff(BuffID.BetsysCurse, 360);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
            ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 9f, ProjectileID.FinalFractal, 26);
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(BuffID.ShadowFlame, 240);
            attackerPlayer.SetBuff(BuffID.Ichor, 240);

            Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
            ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 9f, ProjectileID.RainbowRodBullet, 26);
        }
    }
}
