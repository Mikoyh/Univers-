using System;
using TShockAPI;
using Skynomi.Modules;

namespace Skynomi.Exp
{
    /// <summary>
    /// Event data yang dikirimkan saat pemain berhasil naik level.
    /// Plugin Rank eksternal dapat memanfaatkan data ini untuk promosi grup/rank secara instan.
    /// </summary>
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

    /// <summary>
    /// ==============================================================================
    /// DOKUMENTASI API & INDEX DATABASE UNTUK PLUGIN LAIN / PLUGIN RANK
    /// ==============================================================================
    /// [1] INDEX DATABASE LITEDB:
    ///     - Path File Database: tshock/Skynomi/skynomi.db
    ///     - Nama Collection   : "players_exp"
    ///     - Index Kunci Utama : "AccountName" (Unique Index, String)
    ///     
    ///     Struktur PlayerExpData:
    ///     - AccountName (string) -> INDEX KUNCI (Nama Akun TShock)
    ///     - Level (int)          -> Level Pemain Saat Ini
    ///     - CurrentExp (long)    -> Sisa EXP Menuju Level Selanjutnya
    ///     - TotalExp (long)      -> Total Seluruh EXP yang Pernah Didapat
    ///     - MobsKilled (int)     -> Total Monster Dibunuh
    ///     - BossesKilled (int)   -> Total Boss Dibunuh
    ///
    /// [2] CONTOH KODE C# MENGHUBUNGKAN DARI PLUGIN RANK (EVENT SUBSCRIBER):
    ///     // Di dalam class Plugin Rank kamu:
    ///     public override void Initialize()
    ///     {
    ///         Skynomi.Exp.ExpApi.OnLevelUp += MyRankPlugin_OnLevelUp;
    ///     }
    ///
    ///     private void MyRankPlugin_OnLevelUp(Skynomi.Exp.PlayerLeveledUpEventArgs e)
    ///     {
    ///         // Logika naik rank milikmu sendiri:
    ///         if (e.NewLevel >= 10 && e.Player.Group.Name == "default")
    ///         {
    ///             TShock.UserAccounts.SetUserGroup(TShock.UserAccounts.GetUserByName(e.AccountName), "Warrior");
    ///             e.Player.SendSuccessMessage($"[RANK UP] Selamat! Level kamu {e.NewLevel}, rank kamu naik ke Warrior!");
    ///         }
    ///     }
    ///
    /// [3] MENGAMBIL LEVEL LANGSUNG DARI PLUGIN LAIN:
    ///     int playerLevel = Skynomi.Exp.ExpApi.GetLevel(player);
    ///     long totalExp = Skynomi.Exp.ExpApi.GetTotalExp(player.Account.Name);
    /// ==============================================================================
    /// </summary>
    public static class ExpApi
    {
        /// <summary>
        /// Event yang dipicu setiap kali pemain naik level.
        /// Sangat cocok digunakan oleh plugin Rank untuk mempromosikan pemain berdasarkan level.
        /// </summary>
        public static event Action<PlayerLeveledUpEventArgs>? OnLevelUp;

        internal static void InvokeLevelUp(TSPlayer player, string accountName, int oldLevel, int newLevel, long totalExp)
        {
            try
            {
                OnLevelUp?.Invoke(new PlayerLeveledUpEventArgs(player, accountName, oldLevel, newLevel, totalExp));
            }
            catch (Exception ex)
            {
                Log.Error("[Skynomi.Exp] Terjadi kesalahan pada subscriber OnLevelUp eksternal: " + ex.Message);
            }
        }

        /// <summary>
        /// Mengambil level pemain saat ini berdasarkan nama akun TShock.
        /// </summary>
        public static int GetLevel(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName) || ExpModule.Database == null) return 1;
            return ExpModule.Database.GetOrCreate(accountName).Level;
        }

        /// <summary>
        /// Mengambil level pemain saat ini dari objek TSPlayer.
        /// </summary>
        public static int GetLevel(TSPlayer player)
        {
            if (player == null || !player.IsLoggedIn) return 1;
            return GetLevel(player.Account.Name);
        }

        /// <summary>
        /// Mengambil progres EXP saat ini untuk level berjalan.
        /// </summary>
        public static long GetCurrentExp(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName) || ExpModule.Database == null) return 0;
            return ExpModule.Database.GetOrCreate(accountName).CurrentExp;
        }

        /// <summary>
        /// Mengambil total akumulasi EXP karir pemain dari awal.
        /// </summary>
        public static long GetTotalExp(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName) || ExpModule.Database == null) return 0;
            return ExpModule.Database.GetOrCreate(accountName).TotalExp;
        }

        /// <summary>
        /// Mengambil EXP yang dibutuhkan untuk naik dari suatu level ke level berikutnya.
        /// </summary>
        public static long GetRequiredExpForLevel(int level)
        {
            return ExpModule.Config.CalculateRequiredExp(level);
        }

        /// <summary>
        /// Mengambil format teks tag chat di sebelah kiri nama pemain.
        /// Contoh hasil: "[i:273] [c/a855f7:[Lv.12]]"
        /// </summary>
        public static string GetChatTag(TSPlayer player)
        {
            if (player == null || !player.IsLoggedIn) return "";
            int lvl = GetLevel(player);
            int badgeId = ExpModule.Config.GetBadgeIdForLevel(lvl);
            string hex = ExpModule.Config.GetLevelColorForLevel(lvl);
            return $"[i:{badgeId}] [c/{hex}:[Lv.{lvl}]]";
        }

        /// <summary>
        /// Menambahkan EXP ke pemain dari plugin eksternal (misal: sistem quest / hadiah voting).
        /// </summary>
        public static void AddExp(TSPlayer player, int amount)
        {
            if (player == null || !player.IsLoggedIn || amount <= 0) return;
            var data = ExpModule.Database.GetOrCreate(player.Account.Name);
            ExpModule.AddExp(player, data, amount, true);
        }
    }
}
