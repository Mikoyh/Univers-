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

        // Bersihkan SELURUH chat lama di #verify saat bot nyala (biar cara verify gak tenggelam).
        // Yang di-SKIP otomatis: pesan bot, pemilik server (guild owner), dan ProtectUserIds.
        public bool PurgeVerifyChannelOnStartup { get; set; } = false;

        // ID User Discord (punya lu / admin) yang chat-nya TIDAK boleh ikut terhapus saat sweep.
        public List<string> ProtectUserIds { get; set; } = new();

        // ID Role Discord (mis. role Owner/Admin) — SEMUA member ber-role ini chat-nya diamankan saat sweep.
        // Cocok kalau lu & partner punya role sama. Boleh isi beberapa role sekaligus.
        public List<string> ProtectRoleIds { get; set; } = new();

        // AUTO-SYNC ROLE RANK & ELEMENT KE DISCORD
        // Bot otomatis bikin role (jika belum ada) & menyimpan ID-nya di RankRoleIds / ElementRoleIds.
        // Role cukup dibuat sekali; setelah itu lu bebas rename / pindah posisi role — ID tetap sama.
        public bool SyncRankRoles { get; set; } = false;
        public bool SyncElementRoles { get; set; } = false;
        public bool SyncWorldRoles { get; set; } = false;

        // Interval polling sinkronisasi real-time (detik). 0 = matikan polling (sync hanya saat verify).
        public int RoleSyncPollSeconds { get; set; } = 60;

        // Peta RankId -> RoleId Discord (dikelola otomatis oleh bot; boleh lu isi manual).
        public Dictionary<string, string> RankRoleIds { get; set; } = new();

        // Peta ElementId (VOLT/IGNIS/...) -> RoleId Discord (dikelola otomatis oleh bot).
        public Dictionary<string, string> ElementRoleIds { get; set; } = new();

        // Peta WorldId (lobby/survival) -> RoleId Discord (otomatis mendeteksi role bernama Lobby/Survival).
        public Dictionary<string, string> WorldRoleIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Warna default Embed Discord per World (Hex RGB) jika role Discord belum punya warna khusus.
        public Dictionary<string, string> WorldEmbedColors { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["lobby"] = "38bdf8",
            ["survival"] = "22c55e"
        };

        // Peta override emoji custom Discord (Key: ItemID atau nama item, Value: <:nama:id>).
        // Jika kosong, bot otomatis mengambil & mencocokkan custom emoji dari server Discord.
        public Dictionary<string, string> CustomEmojiMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Sinkronisasi Nickname Discord setelah verifikasi (opsional)
        public bool SyncNicknameWithTShockName { get; set; } = false;
        public string NicknameFormat { get; set; } = "{AccountName}";

        // Pengaturan Relay Chat & Event
        public bool UseEmbedsForRelay { get; set; } = true;
        public bool UseLevelSwordEmojisInRelay { get; set; } = true;
        public bool RelayInGameChatToDiscord { get; set; } = true;
        public bool RelayDiscordChatToInGame { get; set; } = true;
        public bool RelayJoinLeaveToDiscord { get; set; } = true;
        public bool RelayBossDefeatsToDiscord { get; set; } = true;
        public bool RelayTownNpcArrivals { get; set; } = true;
        public bool RelayLobbyTownNpcArrivals { get; set; } = false;
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
                        cfg.ProtectUserIds ??= new List<string>();
                        cfg.ProtectRoleIds ??= new List<string>();
                        cfg.RankRoleIds = new Dictionary<string, string>(cfg.RankRoleIds ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        cfg.ElementRoleIds = new Dictionary<string, string>(cfg.ElementRoleIds ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        cfg.WorldRoleIds = new Dictionary<string, string>(cfg.WorldRoleIds ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        cfg.WorldEmbedColors = new Dictionary<string, string>(cfg.WorldEmbedColors ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        if (!cfg.WorldEmbedColors.ContainsKey("lobby")) cfg.WorldEmbedColors["lobby"] = "38bdf8";
                        if (!cfg.WorldEmbedColors.ContainsKey("survival")) cfg.WorldEmbedColors["survival"] = "22c55e";
                        cfg.CustomEmojiMap = new Dictionary<string, string>(cfg.CustomEmojiMap ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        if (cfg.RoleSyncPollSeconds < 0) cfg.RoleSyncPollSeconds = 0;
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
