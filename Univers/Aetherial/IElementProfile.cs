using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI;

namespace Univers.Aetherial
{
    public interface IElementProfile
    {
        string ElementId { get; }
        string HexColor { get; }
        int LargeGemItemId { get; }
        bool IsWeaponSynergy(Item held);

        // Debuff monster yang berevolusi sesuai Tier (1 - 7)
        int GetMobDebuffId(int tier);

        // [SKILL 10 DETIK]: Terbuka mulai Tier 2 untuk semua elemen!
        void OnTriggerElementSkill(TSPlayer attacker, NPC target, int tier) { }

        // Visual saat Tag berhasil menempel ke musuh (Tier 1 - 7)
        void OnTagVisual(TSPlayer attacker, NPC target, int tier);

        // Visual ledakan saat Kombo Reaksi terpicu (Tier 1 - 7)
        void OnComboVisual(TSPlayer attacker, NPC target, int tier);

        // Pasif saat pemilik elemen dipukul Monster (PvE)
        void OnDamagedByNpc(TSPlayer owner, NPC attackerNpc, int damageTaken, int tier) { }

        // Pasif saat pemilik elemen dipukul Player lain (PvP)
        void OnDamagedByPlayer(TSPlayer owner, TSPlayer attackerPlayer, int damageTaken, int tier) { }

        // [DOMAIN EXPANSION]: Ultimate aktif via kombinasi S -> S -> Klik Kiri atau /domain
        bool SupportsDomainExpansion => false;
        string DomainName => "AETHERIAL DOMAIN";
        void OnDomainActivate(TSPlayer owner, Vector2 center, int tier) { }
        void OnDomainPulse(TSPlayer owner, Vector2 center, int tier, int pulseIndex) { }
    }
}
