/*
 * =====================================================================
 *  JobApi.cs - Inter-plugin API interface for other developers
 * =====================================================================
 */

using System;
using TShockAPI;

namespace Skynomi.Jobs
{
    public static class JobApi
    {
        public static event Action<TSPlayer, string, int>? OnJobShiftCompleted;
        public static event Action<TSPlayer, string, int>? OnJobLevelUp;


        public static bool IsPlayerWorking(TSPlayer player, string jobId)
        {
            if (player == null || !player.IsLoggedIn) return false;
            var profile = JobModule.Database.GetOrCreateProfile(player.Account.Name);
            return profile != null && profile.HasJob(jobId);
        }

        public static int GetPlayerJobLevel(TSPlayer player, string jobId)
        {
            if (player == null || !player.IsLoggedIn) return 0;
            var profile = JobModule.Database.GetOrCreateProfile(player.Account.Name);
            return profile?.GetJobLevel(jobId) ?? 0;
        }

        public static bool IsJobOnCooldown(TSPlayer player, string jobId, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            if (player == null || !player.IsLoggedIn) return false;
            var profile = JobModule.Database.GetOrCreateProfile(player.Account.Name);
            if (profile == null) return false;

            var shift = profile.GetOrCreateShift(jobId);
            if (shift.IsOnCooldown)
            {
                remaining = shift.ShiftCooldownUntil - DateTime.UtcNow;
                return true;
            }
            return false;
        }
    }
}
