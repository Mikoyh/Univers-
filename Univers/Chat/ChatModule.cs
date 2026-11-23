/*
 * ==============================================================================
 * ChatModule.cs - Modul Mentions (@tag) dengan Glowing Chat & Alert SFX/Visual, serta Auto-AFK
 * Target: Terraria 1.4.5 Series / TShock 6.x (.NET 9.0 / C# 13)
 * ==============================================================================
 */

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
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
        public string Description => "Glowing @mentions with visual/audio alert & automated AFK idle detection";
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
        private static readonly ConcurrentDictionary<string, string> _lastWhisperer = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private static System.Timers.Timer? _afkCheckTimer;

        public void Initialize()
        {
            Instance = this;
            Config = ChatConfig.Read();

            ServerApi.Hooks.ServerChat.Register(UniversPlugin.Instance, OnServerChat);
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.ServerLeave.Register(UniversPlugin.Instance, OnServerLeave);
            GetDataHandlers.TileEdit += OnTileEdit;
            PlayerHooks.PlayerCommand += OnPlayerCommand;
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;

            _afkCheckTimer = new System.Timers.Timer(10000);
            _afkCheckTimer.Elapsed += CheckAfkTick;
            _afkCheckTimer.AutoReset = true;
            _afkCheckTimer.Start();

            ChatCommands.Initialize();

            Log.Success("[Univers.Chat] Modul Mention (@tag), Glowing Chat & Auto-AFK Berhasil Diaktifkan!");
        }

        public void Dispose()
        {
            ServerApi.Hooks.ServerChat.Deregister(UniversPlugin.Instance, OnServerChat);
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.ServerLeave.Deregister(UniversPlugin.Instance, OnServerLeave);
            GetDataHandlers.TileEdit -= OnTileEdit;
            PlayerHooks.PlayerCommand -= OnPlayerCommand;
            PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;

            _afkCheckTimer?.Stop();
            _afkCheckTimer?.Dispose();

            _afkProfiles.Clear();
            _lastWhisperer.Clear();
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

        private static void OnServerLeave(LeaveEventArgs e)
        {
            var player = TShock.Players[e.Who];
            if (player == null || string.IsNullOrWhiteSpace(player.Name)) return;

            _afkProfiles.TryRemove(player.Name, out _);
            _lastWhisperer.TryRemove(player.Name, out _);
        }

        private static void OnTileEdit(object? sender, GetDataHandlers.TileEditEventArgs e)
        {
            if (e.Player != null && e.Player.Active)
            {
                TouchActivity(e.Player);
            }
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
            if (player.TPlayer != null) info.LastPosition = player.TPlayer.position;

            if (isAfk)
            {
                TSPlayer.All.SendInfoMessage($"[AFK] Player '{player.Name}' masuk mode AFK: {info.Reason}");
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

                bool hasMovedOrUsed =
                    Vector2.Distance(info.LastPosition, p.TPlayer.position) > 16f ||
                    p.TPlayer.controlUseItem ||
                    p.TPlayer.controlJump;

                if (hasMovedOrUsed)
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
                    string pName = p.Name;
                    string reason = info.Reason;
                    QueueMainThread(() =>
                    {
                        TSPlayer.All.SendInfoMessage($"[AFK] Player '{pName}' otomatis masuk mode AFK ({reason}).");
                    });
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
                string pName = player.Name;
                QueueMainThread(() =>
                {
                    TSPlayer.All.SendInfoMessage($"[AFK] Player '{pName}' kembali aktif dari AFK.");
                });
            }
        }

        public static string ProcessMentionsInMessage(TSPlayer sender, string rawText)
        {
            if (sender == null || string.IsNullOrWhiteSpace(rawText) || !Config.EnableMentionSystem)
            {
                return rawText;
            }

            string processedText = rawText;
            string hex = string.IsNullOrWhiteSpace(Config.MentionColorHex) ? "facc15" : Config.MentionColorHex;

            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active || string.IsNullOrWhiteSpace(p.Name)) continue;

                string pattern = $@"(?<!\w)@{Regex.Escape(p.Name)}(?!\w)";
                if (Regex.IsMatch(rawText, pattern, RegexOptions.IgnoreCase))
                {
                    string glowingMention = $"[c/{hex}:@{p.Name}]";
                    processedText = Regex.Replace(
                        processedText,
                        pattern,
                        _ => glowingMention,
                        RegexOptions.IgnoreCase
                    );

                    var targetPlayer = p;
                    var senderPlayer = sender;
                    QueueMainThread(() =>
                    {
                        if (targetPlayer == null || !targetPlayer.Active || targetPlayer.TPlayer == null) return;

                        try
                        {
                            int projIdx = Projectile.NewProjectile(
                                Projectile.GetNoneSource(),
                                targetPlayer.TPlayer.Top,
                                new Vector2(0f, -3.5f),
                                ProjectileID.ConfettiGun,
                                0,
                                0f,
                                Main.myPlayer
                            );
                            if (projIdx >= 0 && projIdx < Main.maxProjectiles)
                            {
                                Main.projectile[projIdx].timeLeft = 10;
                                NetMessage.SendData((int)PacketTypes.ProjectileNew, targetPlayer.Index, -1, null, projIdx);
                            }
                        }
                        catch { }

                        targetPlayer.SendData(
                            PacketTypes.CreateCombatTextExtended,
                            $"MENTION DARI {senderPlayer.Name}!",
                            (int)Color.Gold.PackedValue,
                            targetPlayer.TPlayer.Center.X,
                            targetPlayer.TPlayer.Center.Y - 28f
                        );

                        targetPlayer.SendInfoMessage($"[c/{hex}:[][c/{hex}:MENTION][c/{hex}:]] Kamu di-tag oleh {senderPlayer.Name}: \"{rawText}\"");

                        var targetAfk = GetAfkInfo(targetPlayer.Name);
                        if (targetAfk.IsAfk)
                        {
                            senderPlayer.SendErrorMessage($"[AFK Auto-Reply] Player '{targetPlayer.Name}' sedang AFK ({targetAfk.Reason}). Pesan kamu akan dibaca saat kembali.");
                        }
                    });
                }
            }

            return processedText;
        }

        private static void OnServerChat(ServerChatEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Text)) return;

            var sender = TShock.Players[e.Who];
            if (sender == null || !sender.Active) return;

            TouchActivity(sender);

            if (e.Handled) return;

            string rawText = e.Text.Trim();
            if (rawText.StartsWith("/") || rawText.StartsWith(".")) return;
            if (!Config.EnableMentionSystem) return;

            string processedText = ProcessMentionsInMessage(sender, rawText);
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
            if (cmd == "w" || cmd == "whisper" || cmd == "tell" || cmd == "m")
            {
                if (args.Parameters.Count > 0)
                {
                    string targetName = args.Parameters[0];
                    var target = TShock.Players.FirstOrDefault(p =>
                        p != null && p.Active && p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));

                    if (target != null)
                    {
                        _lastWhisperer[target.Name] = args.Player.Name;
                        _lastWhisperer[args.Player.Name] = target.Name;

                        var targetAfk = GetAfkInfo(target.Name);
                        if (targetAfk.IsAfk)
                        {
                            args.Player.SendErrorMessage($"[AFK Auto-Reply] Player '{target.Name}' sedang AFK ({targetAfk.Reason}). Pesan pribadi tidak dapat dibalas sekarang.");
                        }
                    }
                }
            }
            else if (cmd == "r" || cmd == "reply")
            {
                if (_lastWhisperer.TryGetValue(args.Player.Name, out string? lastTargetName) &&
                    !string.IsNullOrWhiteSpace(lastTargetName))
                {
                    var target = TShock.Players.FirstOrDefault(p =>
                        p != null && p.Active && p.Name.Equals(lastTargetName, StringComparison.OrdinalIgnoreCase));

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
