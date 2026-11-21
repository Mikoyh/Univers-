/*
 * ==============================================================================
 * EconomyConfig.cs - Konfigurasi Sistem Ekonomi Modular Skynomi
 * ==============================================================================
 */

using System;
using System.IO;
using Newtonsoft.Json;
using TShockAPI;
using Univers;

namespace Skynomi.Economy
{
    public class EconomyConfig
    {
        public string CurrencyName { get; set; } = "Shard";
        public string CurrencySymbol { get; set; } = "SHARD";
        public string CurrencyColorHex { get; set; } = "c084fc"; // Ungu Kosmik (Cosmic Purple)
        public long DefaultStartingBalance { get; set; } = 500;
        public long DefaultStartingBankBalance { get; set; } = 0;
        public bool EnableWalletCap { get; set; } = true;
        public bool RouteExcessToBank { get; set; } = true; // Jika dompet penuh, kelebihan masuk otomatis ke bank
        public double TransferFeePercent { get; set; } = 0.0; // Pajak transfer antar pemain (0% default)
        public long MinimumPayAmount { get; set; } = 10;

        public static string ConfigPath => Path.Combine(UniversPlugin.GetEffectiveSavePath(), "Skynomi", "EconomyConfig.json");

        public static EconomyConfig Read()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    var cfg = new EconomyConfig();
                    Write(cfg);
                    return cfg;
                }
                return JsonConvert.DeserializeObject<EconomyConfig>(File.ReadAllText(ConfigPath)) ?? new EconomyConfig();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Skynomi.Economy] Gagal membaca EconomyConfig.json: {ex.Message}");
                return new EconomyConfig();
            }
        }

        public static void Write(EconomyConfig config)
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Skynomi.Economy] Gagal menyimpan EconomyConfig.json: {ex.Message}");
            }
        }
    }
}
