using System;
using TShockAPI;
using Univers.Modules;

namespace Univers.Exp
{
    public class PlayerLeveledUpEventArgs : EventArgs
    {
        public TSPlayer Player { get; }
        public string AccountName { get; }
        public int OldLevel { get; }
        public int NewLevel { get; }
        public long TotalExp { get; }

        public PlayerLeveledUpEventArgs(TSPlayer player, string accountName, int oldLevel, int newLevel, long totalExp)
        {
            Player = player;
            AccountName = accountName;
            OldLevel = oldLevel;
            NewLevel = newLevel;
            TotalExp = totalExp;
        }
    }

    public static class ExpApi
    {
        public static event Action<PlayerLeveledUpEventArgs>? OnLevelUp;

        /// <summary>
        /// Event sinkronisasi level universal (terpicu saat level naik normal, turun/naik via /expadmin set, atau SetLevel).
        /// Parameter: (accountName, newLevel, isAdminSet)
        /// </summary>
        public static event Action<string, int, bool>? OnLevelChanged;

        internal static void InvokeLevelUp(TSPlayer player, string accountName, int oldLevel, int newLevel, long totalExp)
        {
            try
            {
                OnLevelUp?.Invoke(new PlayerLeveledUpEventArgs(player, accountName, oldLevel, newLevel, totalExp));
            }
            catch (Exception ex)
            {
                Log.Error("[Univers.Exp] Terjadi kesalahan pada subscriber OnLevelUp eksternal: " + ex.Message);
            }
        }

        internal static void InvokeLevelChanged(string accountName, int newLevel, bool isAdminSet)
        {
            try
            {
                OnLevelChanged?.Invoke(accountName, newLevel, isAdminSet);
            }
            catch (Exception ex)
            {
                Log.Error("[Univers.Exp] Terjadi kesalahan pada subscriber OnLevelChanged eksternal: " + ex.Message);
            }
        }

        public static int GetLevel(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName) || ExpModule.Database == null) return 1;
            return ExpModule.Database.GetOrCreate(accountName).Level;
        }

        public static int GetLevel(TSPlayer player)
        {
            if (player == null || !player.IsLoggedIn) return 1;
            return GetLevel(player.Account.Name);
        }

        public static long GetCurrentExp(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName) || ExpModule.Database == null) return 0;
            return ExpModule.Database.GetOrCreate(accountName).CurrentExp;
        }

        public static long GetTotalExp(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName) || ExpModule.Database == null) return 0;
            return ExpModule.Database.GetOrCreate(accountName).TotalExp;
        }

        public static long GetRequiredExpForLevel(int level)
        {
            return ExpModule.Config.CalculateRequiredExp(level);
        }

        public static string GetChatTag(TSPlayer player)
        {
            if (player == null || !player.IsLoggedIn) return "";
            int lvl = GetLevel(player);
            int badgeId = ExpModule.Config.GetBadgeIdForLevel(lvl);
            string hex = ExpModule.Config.GetLevelColorForLevel(lvl);
            return $"[i:{badgeId}] [c/{hex}:[][c/{hex}:{lvl}][c/{hex}:]]";
        }

        public static void AddExp(TSPlayer player, int amount)
        {
            if (player == null || !player.Active || !player.IsLoggedIn || amount <= 0) return;
            var data = ExpModule.Database.GetOrCreate(player.Account.Name);
            ExpModule.AddExp(player, data, amount, true);
        }

        /// <summary>
        /// Mengatur level karakter secara langsung dan menyinkronkan ke seluruh modul (termasuk Aetherial ElementLevel).
        /// </summary>
        public static void SetLevel(string accountName, int newLevel, TSPlayer? onlinePlayer = null, bool notifySubscribers = true)
        {
            if (string.IsNullOrWhiteSpace(accountName) || ExpModule.Database == null) return;

            int clampedLevel = Math.Clamp(newLevel, 1, 1000);
            var data = ExpModule.Database.GetOrCreate(accountName);
            int oldLevel = data.Level;

            data.Level = clampedLevel;
            data.CurrentExp = 0;
            ExpModule.Database.Save(data);

            if (notifySubscribers)
            {
                if (onlinePlayer != null && onlinePlayer.Active && onlinePlayer.IsLoggedIn && clampedLevel > oldLevel)
                {
                    InvokeLevelUp(onlinePlayer, accountName, oldLevel, clampedLevel, data.TotalExp);
                }
                InvokeLevelChanged(accountName, clampedLevel, true);
            }
        }
    }
}
