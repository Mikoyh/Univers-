/*
 * =====================================================================
 *  DiscordConfig.cs - Konfigurasi Discord Relay & TShock Authenticator
 *  Namespace: Univers.Discord (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TShockAPI;

namespace Univers.Discord
{
    public class DiscordConfig
    {
        public bool Enabled { get; set; } = true;

        // Token Bot Discord (ambil dari Discord Developer Portal -> Bot, JANGAN dibagikan!)
        public string BotToken { get; set; } = "";

        // ID Server (Guild) Discord tempat bot berjalan
        public string GuildId { get; set; } = "";

        // ID Channel utama untuk Chat Relay Terraria <-> Discord
        public string RelayChannelId { get; set; } = "";

        // ID Channel khusus #verify (opsional; jika kosong, !verify bisa dipakai di RelayChannel & DM)
        public string VerifyChannelId { get; set; } = "";

        // ID Role Discord yang otomatis diberikan setelah verifikasi TShock berhasil
        public string VerifiedRoleId { get; set; } = "";

        // Daftar ID Role Admin Discord yang boleh menjalankan !cmd / !forceunlink di Discord
        public List<string> AdminRoleIds { get; set; } = new();

        // AUTHENTICATOR GATE: Wajib verifikasi akun TShock agar bisa chat di RelayChannel Discord
        public bool RequireVerifiedAccountToChat { get; set; } = true;
        public bool DeleteUnverifiedMessagesInRelay { get; set; } = true;
        public bool AutoDeleteVerifyMessages { get; set; } = true;
        public int UnverifiedWarningAutoDeleteSeconds { get; set; } = 10;

        // Sinkronisasi Nickname Discord setelah verifikasi (opsional)
        public bool SyncNicknameWithTShockName { get; set; } = false;
        public string NicknameFormat { get; set; } = "{AccountName}";

        // Pengaturan Relay Chat & Event
        public bool RelayInGameChatToDiscord { get; set; } = true;
        public bool RelayDiscordChatToInGame { get; set; } = true;
        public bool RelayJoinLeaveToDiscord { get; set; } = true;
        public bool RelayBossDefeatsToDiscord { get; set; } = true;
        public bool SanitizeTerrariaTagsFromDiscord { get; set; } = true;

        // Keamanan & Rate-Limit
        public int VerifyCodeExpiryMinutes { get; set; } = 5;
        public int DiscordChatCooldownSeconds { get; set; } = 2;
        public string CommandPrefix { get; set; } = "!";
        public bool AllowDiscordConsoleCommands { get; set; } = false;

        public bool HasValidBotCredentials()
        {
            return Enabled &&
                   !string.IsNullOrWhiteSpace(BotToken) &&
                   !BotToken.Contains("PUT_YOUR", StringComparison.OrdinalIgnoreCase);
        }

        public static DiscordConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonConvert.DeserializeObject<DiscordConfig>(json);
                    if (cfg != null)
                    {
                        cfg.AdminRoleIds ??= new List<string>();
                        if (cfg.VerifyCodeExpiryMinutes < 1) cfg.VerifyCodeExpiryMinutes = 5;
                        if (cfg.DiscordChatCooldownSeconds < 0) cfg.DiscordChatCooldownSeconds = 2;
                        if (string.IsNullOrWhiteSpace(cfg.CommandPrefix)) cfg.CommandPrefix = "!";
                        if (string.IsNullOrWhiteSpace(cfg.NicknameFormat)) cfg.NicknameFormat = "{AccountName}";
                        cfg.Save(path);
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.Discord] Gagal membaca DiscordConfig.json, menggunakan default: {ex.Message}");
            }

            var fresh = new DiscordConfig();
            fresh.Save(path);
            return fresh;
        }

        public void Save(string path)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Univers.Discord] Gagal menyimpan DiscordConfig.json: {ex.Message}");
            }
        }
    }
}
