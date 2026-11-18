/*
 * ==============================================================================
 * SkynomiPlugin.cs - Gerbang Utama (Main Entry Point) Plugin TShock 6.x / .NET 9.0
 * Target: Terraria 1.4.5 Series & TShock 6.1.0+ (C# 13)
 * ==============================================================================
 * Mengintegrasikan seluruh 5 modul Skynomi dalam satu lifecycle server:
 *  1. Skynomi.Economy   - Shard Ledger, Dompet, Bank & Limit Kasta
 *  2. Skynomi.Exp       - Level Karakter (Lv. 1 s/d 1000) Exponential
 *  3. Skynomi.Ranks     - 9 Tingkatan Kasta Cosmic, Boss Tracker & Item Tier Lock
 *  4. Skynomi.Jobs      - 5 Profesi Quota & Cooldown
 *  5. Skynomi.Aetherial - Resonansi Elemen Jiwa, Tag Gem & Reaksi Kombo 5X OMNIA
 * ==============================================================================
 */

using System;
using System.IO;
using System.Collections.Generic;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Skynomi.Modules;
using Skynomi.Exp;
using Skynomi.Ranks;
using Skynomi.Economy;
using Skynomi.Jobs;
using Skynomi.Aetherial;

namespace Skynomi
{
    [ApiVersion(2, 1)]
    public class SkynomiPlugin : TerrariaPlugin
    {
        public override string Name => "Skynomi Core";
        public override string Author => "Keyou & AI Kimoy";
        public override string Description => "Modular Core Plugin: EXP, Cosmic Ranks, Shard Economy, Jobs & Aetherial Elements";
        public override Version Version => new Version(2, 0, 0);

        public static SkynomiPlugin Instance { get; private set; } = null!;

        public SkynomiPlugin(Main game) : base(game)
        {
            Instance = this;
            Order = 1;
        }

        public override void Initialize()
        {
            Instance = this;
            GeneralHooks.ReloadEvent += OnReload;

            string savePath = TShock.SavePath;
            string configDir = Path.Combine(savePath, "Skynomi");
            Directory.CreateDirectory(configDir);

            try
            {
                // [1] Inisialisasi Modul Ekonomi (Shard Ledger)
                var eco = new EconomyModule();
                eco.Initialize();

                // [2] Inisialisasi Modul EXP (Lv. 1 - 1000)
                var exp = new ExpModule();
                exp.Initialize();

                // [3] Inisialisasi Modul Ranks (9 Kasta Cosmic & Item Lock)
                var ranks = new RankModule();
                ranks.Initialize();

                // [4] Inisialisasi Modul Jobs (Mining, Fishing, Hunting, Lumberjack, Farming)
                JobModule.Initialize(savePath);

                // [5] Inisialisasi Modul Aetherial Resonance (Combat Engine & Elemen OMNIA)
                AetherialModule.Initialize(savePath);

                TShock.Log.ConsoleInfo("[Skynomi Core] 5 Modul Terintegrasi Berhasil Diaktifkan pada .NET 9.0!");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Skynomi Core] Gagal menginisialisasi modul: {ex}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GeneralHooks.ReloadEvent -= OnReload;

                try { AetherialModule.Dispose(); } catch { }
                try { JobModule.Dispose(); } catch { }
                try { RankModule.Instance?.Dispose(); } catch { }
                try { ExpModule.Instance?.Dispose(); } catch { }
                try { EconomyModule.Instance?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        private void OnReload(ReloadEventArgs e)
        {
            try
            {
                EconomyModule.Instance?.Reload(e);
                RankModule.Instance?.Reload(e);
                ExpModule.Instance?.Reload(e);
                TShock.Log.ConsoleInfo("[Skynomi Core] Konfigurasi seluruh modul berhasil dimuat ulang.");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Skynomi Core] Gagal reload konfigurasi: {ex}");
            }
        }
    }

    /// <summary>
    /// Jembatan kompatibilitas jika modul atau script internal lain memanggil SkynomiCorePlugin.Instance
    /// </summary>
    public static class SkynomiCorePlugin
    {
        public static SkynomiPlugin Instance => SkynomiPlugin.Instance;
    }
}

namespace Skynomi.Modules
{
    public interface IModule
    {
        string Name { get; }
        string Description { get; }
        Version Version { get; }
        string Author { get; }
        void Initialize();
    }

    public interface IDependent
    {
        IReadOnlyList<Type> RequiredModules { get; }
    }

    public interface IReloadable
    {
        void Reload(ReloadEventArgs e);
    }

    public static class Log
    {
        public static void Info(string message) => TShock.Log.ConsoleInfo(message);
        public static void Success(string message) => TShock.Log.ConsoleInfo($"[SUCCESS] {message}");
        public static void Warn(string message) => TShock.Log.ConsoleWarn(message);
        public static void Error(string message) => TShock.Log.ConsoleError(message);
    }
}

namespace Skynomi.Utils
{
    public class UtilsModule { }
}

namespace Skynomi.Database
{
    public class DatabaseModule { }
}
