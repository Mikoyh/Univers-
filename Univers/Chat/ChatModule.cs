/*
 * ==============================================================================
 * ChatModule.cs - Modul Mentions (@tag) dengan Glowing Chat & Getaran/Alert, serta Auto-AFK
 * Target: Terraria 1.4.5 Series / TShock 6.x (.NET 9.0 / C# 13)
 * ==============================================================================
 */

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Univers.Modules;

namespace Univers.Chat
{
    public class PlayerAfkInfo
    {
        public bool IsAfk { get; set; } = false;
        public string Reason { get; set; } = "Idle (Tanpa Aktivitas)";
        public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;
        public Vector2 LastPosition { get; set; } = Vector2.Zero;
    }

    public class ChatModule : IModule, IDependent, IDisposable, IReloadable
    {
        public string Name => "Univers Chat & AFK System";
        public string Description => "Glowing @mentions with vibration alert & automated AFK idle detection";
        public Version Version => new Version(2, 0, 0);
        public string Author => "Univers Team";

        public IReadOnlyList<Type> RequiredModules => new[]
        {
            typeof(Univers.Utils.UtilsModule),
            typeof(Univers.Database.DatabaseModule)
        };

        public static ChatModule Instance { get; private set; } = null!;
        public static ChatConfig Config { get; private set; } = new ChatConfig();

        private static readonly ConcurrentDictionary<string, PlayerAfkInfo> _afkProfiles = new(StringComparer.OrdinalIgnoreCase);
        private static System.Timers.Timer? _afkCheckTimer;

        public void Initialize()
        {
            Instance = this;
            Config = ChatConfig.Read();

            ServerApi.Hooks.ServerChat.Register(UniversPlugin.Instance, OnServerChat);
            PlayerHooks.PlayerCommand += OnPlayerCommand;
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnPlayerGreet);

            _afkCheckTimer = new System.Timers.Timer(10000); // Check every 10 seconds
            _afkCheckTimer.Elapsed += CheckAfkTick;
            _afkCheckTimer.AutoReset = true;
            _afkCheckTimer.Start();

            ChatCommands.Initialize();

            Log.Success("[Univers.Chat] Modul Mention (@tag), Glowing Chat & Auto-AFK Berhasil Diaktifkan!");
        }

        public void Dispose()
        {
            ServerApi.Hooks.ServerChat.Deregister(UniversPlugin.Instance, OnServerChat);
            PlayerHooks.PlayerCommand -= OnPlayerCommand;
            PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnPlayerGreet);

            _afkCheckTimer?.Stop();
            _afkCheckTimer?.Dispose();

            _afkProfiles.Clear();
            Log.Info("[Univers.Chat] Modul Chat & AFK dibongkar (Disposed).");
        }

        public void Reload(ReloadEventArgs e)
        {
            Config = ChatConfig.Read();
            Log.Info("[Univers.Chat] Konfigurasi Chat & AFK dimuat ulang.");
        }

        private static void OnPlayerGreet(GreetPlayerEventArgs e)
        {
            var player = TShock.Players[e.Who];
            if (player == null || !player.Active || string.IsNullOrWhiteSpace(player.Name)) return;

            _afkProfiles[player.Name] = new PlayerAfkInfo
            {
                LastActiveTime = DateTime.UtcNow,
                LastPosition = player.TPlayer != null ? player.TPlayer.position : Vector2.Zero
            };
        }

        private static void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
        {
            var player = e.Player;
            if (player == null || !player.Active || string.IsNullOrWhiteSpace(player.Name)) return;

            _afkProfiles[player.Name] = new PlayerAfkInfo
            {
                LastActiveTime = DateTime.UtcNow,
                LastPosition = player.TPlayer != null ? player.TPlayer.position : Vector2.Zero
            };
        }

        public static PlayerAfkInfo GetAfkInfo(string playerName)
        {
            return _afkProfiles.GetOrAdd(playerName, _ => new PlayerAfkInfo());
        }

        public static void SetAfk(TSPlayer player, bool isAfk, string reason = "Manual AFK")
        {
            if (player == null || !player.Active) return;
            var info = GetAfkInfo(player.Name);
            info.IsAfk = isAfk;
            info.Reason = string.IsNullOrWhiteSpace(reason) ? "Sedang AFK" : reason;
            info.LastActiveTime = DateTime.UtcNow;

            if (isAfk)
            {
                TSPlayer.All.SendInfoMessage($"[AFK] Player '{player.Name}' telah masuk mode AFK: {info.Reason}");
            }
            else
            {
                TSPlayer.All.SendInfoMessage($"[AFK] Player '{player.Name}' kembali aktif.");
            }
        }

        private static void CheckAfkTick(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (!Config.EnableAutoAfk) return;

            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active || !p.IsLoggedIn || p.TPlayer == null) continue;

                var info = GetAfkInfo(p.Name);
                if (info.IsAfk) continue;

                // Cek apakah posisi berubah atau baru aktif
                float dist = Vector2.Distance(info.LastPosition, p.TPlayer.position);
                if (dist > 16f) // Bergerak lebih dari 1 tile
                {
                    info.LastPosition = p.TPlayer.position;
                    info.LastActiveTime = DateTime.UtcNow;
                    continue;
                }

                double idleMinutes = (DateTime.UtcNow - info.LastActiveTime).TotalMinutes;
                if (idleMinutes >= Config.AutoAfkMinutes)
                {
                    info.IsAfk = true;
                    info.Reason = $"Idle selama {(int)idleMinutes} menit";
                    TSPlayer.All.SendInfoMessage($"[AFK] Player '{p.Name}' otomatis masuk mode AFK ({info.Reason}).");
                }
            }
        }

        public static void TouchActivity(TSPlayer player)
        {
            if (player == null || !player.Active || string.IsNullOrWhiteSpace(player.Name)) return;
            var info = GetAfkInfo(player.Name);
            info.LastActiveTime = DateTime.UtcNow;
            if (player.TPlayer != null) info.LastPosition = player.TPlayer.position;

            if (info.IsAfk)
            {
                info.IsAfk = false;
                TSPlayer.All.SendInfoMessage($"[AFK] Player '{player.Name}' kembali aktif dari AFK.");
            }
        }

        private static void OnServerChat(ServerChatEventArgs e)
        {
            if (e.Handled || string.IsNullOrWhiteSpace(e.Text)) return;

            var sender = TShock.Players[e.Who];
            if (sender == null || !sender.Active) return;

            TouchActivity(sender);

            string rawText = e.Text.Trim();
            if (rawText.StartsWith("/") || rawText.StartsWith(".")) return; // Biarkan command ditangani TShock

            if (!Config.EnableMentionSystem) return;

            // Cek mention @namauser
            string processedText = rawText;
            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active || string.IsNullOrWhiteSpace(p.Name)) continue;

                string mentionTag = $"@{p.Name}";
                if (rawText.Contains(mentionTag, StringComparison.OrdinalIgnoreCase))
                {
                    // Format glowing text untuk mentioned user
                    string glowingMention = $"[c/{Config.MentionColorHex}:@{p.Name}]";
                    processedText = System.Text.RegularExpressions.Regex.Replace(
                        processedText,
                        $"@{p.Name}",
                        glowingMention,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    );

                    // Kirim getaran / alert / combat text dan suara ke target player
                    _mainThreadQueue.Enqueue(() =>
                    {
                        // Suara ping / level up agar berdering di HP/PC player
                        NetMessage.PlaySound(29, (int)p.TPlayer.Center.X, (int)p.TPlayer.Center.Y, 105, 1f, 0f);

                        // Combat text di kepala player tanda di-mention
                        p.SendData(
                            PacketTypes.CreateCombatTextExtended,
                            $"MEMO DARI {sender.Name}!",
                            (int)Color.Gold.PackedValue,
                            p.TPlayer.Center.X,
                            p.TPlayer.Center.Y - 24
                        );

                        p.SendInfoMessage($"[UNIVERS MENTION] Kamu di-mention oleh {sender.Name}: \"{rawText}\"");

                        // Cek apakah target sedang AFK -> kirim balasan otomatis ke sender
                        var targetAfk = GetAfkInfo(p.Name);
                        if (targetAfk.IsAfk)
                        {
                            sender.SendErrorMessage($"[AFK Auto-Reply] Player '{p.Name}' sedang AFK ({targetAfk.Reason}). Pesan kamu akan dibaca saat kembali.");
                        }
                    });
                }
            }

            // Jika ada perubahan mention, ganti teks chat yang disiarkan
            if (processedText != rawText)
            {
                e.Handled = true;
                string chatBroadcast = $"[c/38bdf8:<{sender.Name}>] {processedText}";
                foreach (var rcpt in TShock.Players)
                {
                    if (rcpt != null && rcpt.Active)
                    {
                        rcpt.SendMessage(chatBroadcast, Color.White);
                    }
                }
            }
        }

        private static void OnPlayerCommand(PlayerCommandEventArgs args)
        {
            if (args.Handled || args.Player == null) return;
            TouchActivity(args.Player);

            string cmd = args.CommandName?.ToLowerInvariant() ?? string.Empty;
            // Cek whisper / private message cmd: w, whisper, tell, m, r, reply
            if (cmd == "w" || cmd == "whisper" || cmd == "tell" || cmd == "m" || cmd == "r" || cmd == "reply")
            {
                if (args.Parameters.Count > 0)
                {
                    string targetNameOrMsg = args.Parameters[0];
                    TSPlayer? target = null;
                    if (cmd != "r" && cmd != "reply")
                    {
                        target = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.Name.Equals(targetNameOrMsg, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        // Untuk /r atau /reply, TShock menyimpan last whisperer. Kita cek apakah player tersebut afk.
                        // Jika target tidak ditemukan via parameter pertama, scan semua player yang aktif atau biarkan TShock handle.
                    }

                    if (target != null)
                    {
                        var targetAfk = GetAfkInfo(target.Name);
                        if (targetAfk.IsAfk)
                        {
                            args.Player.SendErrorMessage($"[AFK Auto-Reply] Player '{target.Name}' sedang AFK ({targetAfk.Reason}). Pesan pribadi tidak dapat dibalas sekarang.");
                        }
                    }
                }
            }
        }

        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        public static void QueueMainThread(Action action)
        {
            if (action != null) _mainThreadQueue.Enqueue(action);
        }

        public static void PumpMainThread()
        {
            int count = 0;
            while (count < 20 && _mainThreadQueue.TryDequeue(out var action))
            {
                count++;
                try { action?.Invoke(); } catch { }
            }
        }
    }
}
