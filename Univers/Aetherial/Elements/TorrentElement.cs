using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace Univers.Aetherial.Elements
{
    public class TorrentElement : IElementProfile
    {
        public string ElementId => "TORRENT";
        public string HexColor => "06b6d4";               // Biru Samudra / Cyan
        public int LargeGemItemId => ItemID.LargeSapphire;

        // Evolusi Debuff: Tier 1-3 Wet (103) | Tier 4-6 Slow (32) | Tier 7 Betsy's Curse (203 - -40 DEF)
        public int GetMobDebuffId(int tier)
        {
            if (tier >= 7) return BuffID.BetsysCurse; // Tekanan Palung Laut (-40 Defense!)
            if (tier >= 4) return BuffID.Slow;        // Terhambat Arus Air
            return BuffID.Wet;                        // Tetesan Air Biru
        }

        public bool IsWeaponSynergy(Item held)
        {
            if (held == null || string.IsNullOrEmpty(held.Name)) return false;
            string n = held.Name;
            return n.Contains("Water") || n.Contains("Aqua") || n.Contains("Tide") ||
                   n.Contains("Ocean") || n.Contains("Bubble") || n.Contains("Flairon") ||
                   n.Contains("Tempest") || n.Contains("Tsunami") || n.Contains("Typhoon") ||
                   n.Contains("Trident") || n.Contains("Shark") || n.Contains("Abyss");
        }

        // [SKILL 10 DETIK - TIER 2+]: Menembakkan Sapphire Bolt biru air & memicu efek arus!
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
                ElementVisualHelper.SpawnVisualProjectile(attacker.TPlayer.Center, shotDir * 11f, ProjectileID.SapphireBolt, 35);
            }

            int debuffId = GetMobDebuffId(tier);
            target.AddBuff(debuffId, (4 + tier) * 60);
            target.AddBuff(BuffID.Wet, (4 + tier) * 60);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, target.whoAmI);

            // Tier 4+: Aktifkan kelincahan air (Water Walking) + Swiftness di Tier 6+!
            if (tier >= 4)
            {
                attacker.SetBuff(BuffID.WaterWalking, 360);
            }
            if (tier >= 6)
            {
                attacker.SetBuff(BuffID.Swiftness, 360);
            }
        }

        public void OnTagVisual(TSPlayer attacker, NPC target, int tier)
        {
            if (tier < 2) return;
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier == 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 6f, ProjectileID.WaterStream, 22);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 6f, ProjectileID.WaterBolt, 25);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, dir * 4f, ProjectileID.WaterStream, 20);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 6.5f, ProjectileID.WaterBolt, 26);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.Bubble, 3, 20f, 22, 2f);
                attacker.SetBuff(BuffID.WaterWalking, 300);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FlaironBubble, 3, 24f, 26, 2.5f);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 20f, dir * 5f, ProjectileID.Bubble, 24);
                attacker.SetBuff(BuffID.WaterWalking, 360);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 6.5f, ProjectileID.Typhoon, 26);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FlaironBubble, 3, 28f, 26, 2.5f);
                attacker.SetBuff(BuffID.WaterWalking, 360);
                attacker.SetBuff(BuffID.Swiftness, 300);
            }
            else
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 24f, dir * 7f, ProjectileID.Typhoon, 28);
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 6f, ProjectileID.MiniSharkron, 26);
                attacker.SetBuff(BuffID.WaterWalking, 420);
                attacker.SetBuff(BuffID.Swiftness, 360);
            }
        }

        public void OnComboVisual(TSPlayer attacker, NPC target, int tier)
        {
            Vector2 dir = ElementVisualHelper.GetAimDirection(attacker.TPlayer.Center, target.Center);

            if (tier <= 2)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 16f, dir * 6f, ProjectileID.WaterStream, 24);
            }
            else if (tier == 3)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 6f, ProjectileID.WaterBolt, 26);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.Bubble, 3, 22f, 24, 2f);
            }
            else if (tier == 4)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FlaironBubble, 4, 28f, 26, 2.5f);
            }
            else if (tier == 5)
            {
                ElementVisualHelper.SpawnVisualProjectile(target.Center - dir * 18f, dir * 6f, ProjectileID.Typhoon, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FlaironBubble, 3, 32f, 28, 3f);
            }
            else if (tier == 6)
            {
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.Typhoon, 2, 26f, 28, 3f);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.Bubble, 4, 36f, 28, 2.5f);
            }
            else
            {
                // Tier 7: Pusaran Tsunami Samudra
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.Typhoon, 32);
                ElementVisualHelper.SpawnVisualProjectile(target.Center, Vector2.Zero, ProjectileID.Tempest, 28);
                ElementVisualHelper.SpawnRingProjectiles(target.Center, ProjectileID.FlaironBubble, 5, 42f, 32, 3f);
            }
        }

        public void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier)
        {
            if (tier < 2 || attackerNpc == null || !attackerNpc.active || attackerNpc.friendly) return;

            attackerNpc.AddBuff(GetMobDebuffId(tier), (3 + tier) * 60);
            attackerNpc.AddBuff(BuffID.Wet, 240);
            NetMessage.SendData((int)PacketTypes.NpcUpdateBuff, -1, -1, null, attackerNpc.whoAmI);

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerNpc.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 7f, ProjectileID.WaterBolt, 25);
            }
        }

        public void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier)
        {
            if (tier < 2 || attackerPlayer == null || !attackerPlayer.Active) return;

            attackerPlayer.SetBuff(GetMobDebuffId(tier), 180 + (tier * 30));
            attackerPlayer.SetBuff(BuffID.Wet, 240);

            if (tier >= 4)
            {
                Vector2 dir = ElementVisualHelper.GetAimDirection(owner.TPlayer.Center, attackerPlayer.TPlayer.Center);
                ElementVisualHelper.SpawnVisualProjectile(owner.TPlayer.Center, dir * 7f, ProjectileID.WaterBolt, 25);
            }
        }
    }
}
