/*
 * =====================================================================
 *  AetherialApi.cs - Inter-plugin API interface for other developers
 * =====================================================================
 */

using System;
using Terraria;
using TShockAPI;

namespace Skynomi.Aetherial
{
    public static class AetherialApi
    {
        public static event Action<TSPlayer, NPC, string, int>? OnElementalReaction;


        public static string GetPlayerElement(TSPlayer player)
        {
            if (player == null || !player.IsLoggedIn) return "";
            var profile = AetherialModule.Database.GetOrCreateProfile(player.Account.Name);
            return profile?.AssignedElement ?? "";
        }

        public static void InvokeReaction(TSPlayer attacker, NPC target, string reactionName, int finalDamage)
        {
            OnElementalReaction?.Invoke(attacker, target, reactionName, finalDamage);
        }
    }
}
