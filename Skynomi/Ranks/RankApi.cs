using System;
using TShockAPI;
using Skynomi.Modules;

namespace Skynomi.Ranks
{
    public class PlayerRankUpEventArgs : EventArgs
    {
        public TSPlayer Player { get; }
        public string AccountName { get; }
        public int OldRankIndex { get; }
        public int NewRankIndex { get; }

        public PlayerRankUpEventArgs(TSPlayer player, string accountName, int oldRankIndex, int newRankIndex)
        {
            Player = player;
            AccountName = accountName;
            OldRankIndex = oldRankIndex;
            NewRankIndex = newRankIndex;
        }
    }

    public static class RankApi
    {
        public static event Action<PlayerRankUpEventArgs>? OnRankUp;

        internal static void InvokeRankUp(TSPlayer player, string accountName, int oldRankIndex, int newRankIndex)
        {
            try
            {
                OnRankUp?.Invoke(new PlayerRankUpEventArgs(player, accountName, oldRankIndex, newRankIndex));
            }
            catch (Exception ex)
            {
                Log.Error("[Skynomi.Ranks] Error pada subscriber OnRankUp: " + ex.Message);
            }
        }

        public static int GetRankIndex(TSPlayer player)
        {
            if (player == null || !player.IsLoggedIn || RankModule.Database == null) return 0;
            return RankModule.Database.GetOrCreate(player.Account.Name).RankIndex;
        }

        public static int GetPlayerRankIndex(TSPlayer player) => GetRankIndex(player);

        public static string GetRankName(TSPlayer player)
        {
            int idx = GetRankIndex(player);
            return RankModule.Config.GetRankByIndex(idx).Name;
        }

        public static long GetMaxMoneyLimit(TSPlayer player)
        {
            int idx = GetRankIndex(player);
            return RankModule.Config.GetRankByIndex(idx).MaxMoneyLimitCredits;
        }

        public static long GetPlaytimeSeconds(TSPlayer player)
        {
            if (player == null || !player.IsLoggedIn || RankModule.Database == null) return 0;
            return RankModule.Database.GetOrCreate(player.Account.Name).PlaytimeSeconds;
        }

        public static bool HasDefeatedBoss(TSPlayer player, string bossKey)
        {
            if (player == null || !player.IsLoggedIn || RankModule.Database == null) return false;
            return RankModule.Database.GetOrCreate(player.Account.Name).DefeatedBosses.Contains(bossKey);
        }

        public static bool CanUseItem(TSPlayer player, int itemId)
        {
            if (!RankModule.Config.EnableItemLock) return true;
            int reqTier = RankModule.Config.GetItemTier(itemId);
            int playerRankIdx = GetRankIndex(player);
            var rank = RankModule.Config.GetRankByIndex(playerRankIdx);
            return reqTier <= rank.MaxItemTierAllowed;
        }
    }
}
