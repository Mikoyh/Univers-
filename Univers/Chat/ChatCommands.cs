/*
 * ==============================================================================
 * ChatCommands.cs - Perintah Chat & AFK (Manual /afk [reason], /afklist)
 * Target: Terraria 1.4.5 Series / TShock 6.x (.NET 9.0 / C# 13)
 * ==============================================================================
 */

using System;
using System.Linq;
using Microsoft.Xna.Framework;
using TShockAPI;

namespace Univers.Chat
{
    public static class ChatCommands
    {
        public static void Initialize()
        {
            Commands.ChatCommands.Add(new Command("univers.chat.use", CmdAfk, "afk", "away")
            {
                HelpText = "Mengatur status AFK kamu: /afk [alasan]"
            });

            Commands.ChatCommands.Add(new Command("univers.chat.use", CmdAfkList, "afklist", "whoafk")
            {
                HelpText = "Melihat daftar pemain yang sedang AFK di server."
            });
        }

        private static void CmdAfk(CommandArgs args)
        {
            var player = args.Player;
            if (!player.IsLoggedIn)
            {
                player.SendErrorMessage("Kamu harus login akun TShock untuk menggunakan perintah /afk.");
                return;
            }

            var info = ChatModule.GetAfkInfo(player.Name);
            bool newAfkState = !info.IsAfk;
            string reason = args.Parameters.Count > 0 ? string.Join(" ", args.Parameters) : (newAfkState ? "Istirahat / Menepi" : "Kembali");

            ChatModule.SetAfk(player, newAfkState, reason);
        }

        private static void CmdAfkList(CommandArgs args)
        {
            var player = args.Player;
            player.SendMessage("=== [ DAFTAR PEMAIN AFK ] ===", Color.Cyan);

            bool found = false;
            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active || string.IsNullOrWhiteSpace(p.Name)) continue;

                var info = ChatModule.GetAfkInfo(p.Name);
                if (info.IsAfk)
                {
                    found = true;
                    double idleMins = (DateTime.UtcNow - info.LastActiveTime).TotalMinutes;
                    player.SendMessage($" - {p.Name} : {info.Reason} (Idle {(int)idleMins}m)", Color.Yellow);
                }
            }

            if (!found)
            {
                player.SendInfoMessage("Semua pemain sedang aktif bermain (Tidak ada yang AFK).");
            }
        }
    }
}
