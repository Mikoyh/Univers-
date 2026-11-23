/*
 * ==============================================================================
 * AfkMentionModule.cs - Modul Auto-AFK, Mention (@username) Glowing & Vibration
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

namespace Univers.AfkMention
{
    public class PlayerAfkState
    {
        public string AccountName { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;
        public bool IsAfk { get; set; } = false;
        public DateTime AfkStartTime { get; set; } = DateTime.UtcNow;
    }

    public static class AfkMentionModule
    {
        private static readonly ConcurrentDictionary<int, PlayerAfkState> _afkStates = new();
        private static System.Timers.Timer? _afkCheckTimer;
        private const int AfkThresholdSeconds = 180; // 3 menit tanpa aktivitas

        public static void Initialize(string savePath)
        {
            ServerApi.Hooks.ServerChat.Register(UniversPlugin.Instance, OnServerChat);
            PlayerHooks.PlayerCommand += OnPlayerCommand;
            GetDataHandlers.PlayerUpdate += OnPlayerUpdate;
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.NetLeave.Register(UniversPlugin.Instance, OnPlayerLeave);

            _afkCheckTimer = new System.Timers.Timer(10000); // Cek setiap 10 detik
            _afkCheckTimer.Elapsed += CheckAfkTimeout;
            _afkCheckTimer.AutoReset = true;
            _afkCheckTimer.Start();

            Log.Success("[Univers.AfkMention] Modul Auto-AFK & Mention Glowing aktif!");
        }

        public static void Dispose()
        {
            ServerApi.Hooks.ServerChat.Deregister(UniversPlugin.Instance, OnServerChat);
            PlayerHooks.PlayerCommand -= OnPlayerCommand;
            GetDataHandlers.PlayerUpdate -= OnPlayerUpdate;
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.NetLeave.Deregister(UniversPlugin.Instance, OnPlayerLeave);

            _afkCheckTimer?.Stop();
            _afkCheckTimer?.Dispose();
            _afkStates.Clear();
        }

        private static void OnPlayerGreet(GreetPlayerEventArgs e)
        {
            var player = TShock.Players[e.Who];
            if (player == null || !player.Active) return;

            _afkStates[player.Index] = new PlayerAfkState
            {
                AccountName = player.IsLoggedIn && player.Account != null ? player.Account.Name : player.Name,
                PlayerName = player.Name,
                LastActiveTime = DateTime.UtcNow,
                IsAfk = false
            };
        }

        private static void OnPlayerLeave(LeaveEventArgs e)
        {
            _afkStates.TryRemove(e.Who, out _);
        }

        private static void OnPlayerUpdate(object? sender, GetDataHandlers.PlayerUpdateEventArgs e)
        {
            var player = e.Player;
            if (player == null || !player.Active) return;

            // Jika ada pergerakan atau aksi nyata, catat aktivitas
            TouchActivity(player);
        }

        public static void TouchActivity(TSPlayer player)
        {
            if (player == null || !player.Active) return;

            if (_afkStates.TryGetValue(player.Index, out var state))
            {
                state.LastActiveTime = DateTime.UtcNow;
                if (state.IsAfk)
                {
                    state.IsAfk = false;
                    TSPlayer.All.SendMessage($"[AFK] Player {player.Name} kembali aktif dari mode AFK.", Color.Cyan);
                }
            }
            else
            {
                _afkStates[player.Index] = new PlayerAfkState
                {
                    AccountName = player.IsLoggedIn && player.Account != null ? player.Account.Name : player.Name,
                    PlayerName = player.Name,
                    LastActiveTime = DateTime.UtcNow,
                    IsAfk = false
                };
            }
        }

        private static void CheckAfkTimeout(object? sender, System.Timers.ElapsedEventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            foreach (var kvp in _afkStates)
            {
                int idx = kvp.Key;
                var state = kvp.Value;
                var player = TShock.Players[idx];

                if (player == null || !player.Active) continue;

                if (!state.IsAfk && (now - state.LastActiveTime).TotalSeconds >= AfkThresholdSeconds)
                {
                    state.IsAfk = true;
                    state.AfkStartTime = now;
                    TSPlayer.All.SendMessage($"[AFK] Player {player.Name} otomatis masuk mode AFK (Idle).", Color.Gray);
                }
            }
        }

        private static void OnServerChat(ServerChatEventArgs args)
        {
            if (args.Handled) return;
            var player = TShock.Players[args.Who];
            if (player == null || !player.Active) return;

            TouchActivity(player);
            string rawText = args.Text;
            if (string.IsNullOrWhiteSpace(rawText)) return;

            // Cek apakah ada mention @username di dalam chat
            ProcessMentionsAndGlowing(player, rawText);
        }

        private static void OnPlayerCommand(PlayerCommandEventArgs args)
        {
            if (args.Handled || args.Player == null) return;
            var player = args.Player;
            TouchActivity(player);

            string cmd = args.CommandName?.ToLowerInvariant() ?? string.Empty;
            if (cmd == "whisper" || cmd == "w" || cmd == "m" || cmd == "tell" || cmd == "whis")
            {
                if (args.Parameters.Count >= 2)
                {
                    string targetName = args.Parameters[0];
                    var target = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                    if (target != null && _afkStates.TryGetValue(target.Index, out var targetState) && targetState.IsAfk)
                    {
                        player.SendErrorMessage($"[AFK BOT] Player '{target.Name}' sedang dalam mode AFK dan tidak berada di depan layar.");
                    }
                }
            }
        }

        private static void ProcessMentionsAndGlowing(TSPlayer sender, string text)
        {
            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active) continue;

                string mentionTag = $"@{p.Name}";
                if (text.Contains(mentionTag, StringComparison.OrdinalIgnoreCase))
                {
                    // Kirim getaran / combat text / status text menyala ke player yang di-mention
                    try
                    {
                        p.SendData(
                            PacketTypes.CreateCombatTextExtended,
                            $"[MENTION] Oleh {sender.Name}!",
                            (int)Color.Gold.PackedValue,
                            p.X,
                            p.Y - 35f
                        );

                        p.SendInfoMessage($"[c/facc15:[✦ MENTION ✦]] Kamu di-mention oleh {sender.Name} dalam chat!");

                        // Cek apakah target sedang AFK, kirim auto reply ke sender
                        if (_afkStates.TryGetValue(p.Index, out var state) && state.IsAfk)
                        {
                            sender.SendErrorMessage($"[AFK BOT] Player '{p.Name}' sedang AFK saat ini.");
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
