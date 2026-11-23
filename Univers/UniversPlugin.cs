/*
 * ==============================================================================
 * UniversPlugin.cs - Gerbang Utama (Main Entry Point) Plugin TShock 6.x / .NET 9.0
 * Target: Terraria 1.4.5 Series & TShock 6.1.0+ (C# 13)
 * ==============================================================================
 * Mengintegrasikan 5 modul dalam satu lifecycle server:
 *  1. Skynomi.Economy    - Shard Ledger, Dompet, Bank & Limit Kasta (Original Core by Keyou)
 *  2. Skynomi.Ranks      - 9 Tingkatan Kasta Cosmic, Boss Tracker & Item Lock (Original Core by Keyou)
 *  3. Univers.Exp        - Level Karakter (1 s/d 1000) Exponential
 *  4. Univers.Jobs       - 5 Profesi Quota & Cooldown
 *  5. Univers.Aetherial  - Resonansi Elemen Jiwa, Tag Gem & Reaksi Kombo
 * ==============================================================================
 */

using System;
using System.IO;
using System.Collections.Generic;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Univers.Modules;
using Univers.Exp;
using Univers.Jobs;
using Univers.Aetherial;
using Univers.Shop;
using Univers.MultiWorld;
using Univers.Housing;
using Univers.Survival;
using Skynomi.Ranks;
using Skynomi.Economy;
using Univers.Chat;

namespace Univers
{
    [ApiVersion(2, 1)]
    public class UniversPlugin : TerrariaPlugin
    {
        public override string Name => "Univers Core";
        public override string Author => "Univers Team";
        public override string Description => "Univers MMORPG Core: Skynomi Economy & Ranks, EXP, Jobs & Aetherial Elements";
        public override Version Version => new Version(2, 0, 0);

        public static UniversPlugin Instance { get; private set; } = null!;

        public UniversPlugin(Main game) : base(game)
        {
            Instance = this;
            Order = 1;
        }

        public static string GetEffectiveSavePath()
        {
            string? master = Environment.GetEnvironmentVariable("UNIVERS_MASTER_SAVEPATH");
            string raw = !string.IsNullOrWhiteSpace(master) ? master : TShock.SavePath;
            try
            {
                return Path.GetFullPath(raw);
            }
            catch
            {
                return raw;
            }
        }

        public override void Initialize()
        {
            Instance = this;
            GeneralHooks.ReloadEvent += OnReload;

            string savePath = GetEffectiveSavePath();
            Directory.CreateDirectory(Path.Combine(savePath, "Skynomi"));
            Directory.CreateDirectory(Path.Combine(savePath, "Univers"));

            MigrateLegacyModuleData(savePath);

            try
            {
                // [1] Modul Ekonomi (Skynomi)
                var eco = new EconomyModule();
                eco.Initialize();

                // [2] Modul EXP (Univers, Lv. 1 - 1000)
                var exp = new ExpModule();
                exp.Initialize();

                // [3] Modul Ranks (Skynomi, 9 Kasta & Item Lock)
                var ranks = new RankModule();
                ranks.Initialize();

                // [4] Modul Jobs (Univers)
                JobModule.Initialize(savePath);

                // [5] Modul Aetherial Resonance (Univers)
                AetherialModule.Initialize(savePath);

                // [6] Modul Shop & Offline Auction House (Univers)
                ShopModule.Initialize(savePath);

                // [7] Modul MultiWorld & Gateway Proxy (Univers)
                MultiWorldModule.Initialize(savePath);

                // [8] Modul Housing & Kavling RP (Univers)
                HousingModule.Initialize(savePath);

                // [9] Modul Survival Don't Starve RPG (Univers)
                SurvivalModule.Initialize(savePath);

                // [10] Modul Chat Mentions & Auto-AFK (Univers)
                var chatMod = new ChatModule();
                chatMod.Initialize();
                ServerApi.Hooks.Update.Register(this, OnUpdate);

                TShock.Log.ConsoleInfo("[Univers Core] 10 Modul Terintegrasi Berhasil Diaktifkan pada .NET 9.0!");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Univers Core] Gagal menginisialisasi modul: {ex}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GeneralHooks.ReloadEvent -= OnReload;
                try { ServerApi.Hooks.Update.Deregister(this, OnUpdate); } catch { }

                try { ChatModule.Instance?.Dispose(); } catch { }
                try { SurvivalModule.Dispose(); } catch { }
                try { HousingModule.Dispose(); } catch { }
                try { MultiWorldModule.Dispose(); } catch { }
                try { ShopModule.Dispose(); } catch { }
                try { AetherialModule.Dispose(); } catch { }
                try { JobModule.Dispose(); } catch { }
                try { RankModule.Instance?.Dispose(); } catch { }
                try { ExpModule.Instance?.Dispose(); } catch { }
                try { EconomyModule.Instance?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        private void OnUpdate(EventArgs args)
        {
            try
            {
                ChatModule.PumpMainThread();
            }
            catch { }
        }

        private void OnReload(ReloadEventArgs e)
        {
            try
            {
                EconomyModule.Instance?.Reload(e);
                RankModule.Instance?.Reload(e);
                ExpModule.Instance?.Reload(e);
                ShopModule.Reload();
                MultiWorldModule.Reload();
                HousingModule.Reload();
                SurvivalModule.Reload();
                ChatModule.Instance?.Reload(e);
                TShock.Log.ConsoleInfo("[Univers Core] Konfigurasi seluruh modul berhasil dimuat ulang.");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Univers Core] Gagal reload konfigurasi: {ex}");
            }
        }

        private static void MigrateLegacyModuleData(string savePath)
        {
            string oldDir = Path.Combine(savePath, "Skynomi");
            string newDir = Path.Combine(savePath, "Univers");

            var migrations = new (string OldName, string NewName)[]
            {
                ("ExpConfig.json", "ExpConfig.json"),
                ("skynomi_exp.db", "univers_exp.db"),
                ("JobConfig.json", "JobConfig.json"),
                ("skynomi_jobs.db", "univers_jobs.db"),
                ("AetherialConfig.json", "AetherialConfig.json"),
                ("skynomi_aetherial.db", "univers_aetherial.db")
            };

            foreach (var (oldName, newName) in migrations)
            {
                string src = Path.Combine(oldDir, oldName);
                string dst = Path.Combine(newDir, newName);
                try
                {
                    if (File.Exists(src) && !File.Exists(dst))
                    {
                        File.Copy(src, dst, false);
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleWarn($"[Univers Core] Gagal migrasi {oldName} -> {newName}: {ex.Message}");
                }
            }
        }
    }
}

namespace Univers.Modules
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

namespace Univers.Utils
{
    public class UtilsModule { }
}

namespace Univers.Database
{
    public class DatabaseModule { }
}
