/*
 * ==============================================================================
 * Skynomi 2.0 - Cosmic Rank Progression & Item Tier Lock Module
 * TShock 6.x & Terraria 1.4.5 Series (.NET 9.0 / C# 13)
 * ==============================================================================
 * 9 Tingkatan Kasta Cosmic (Skala Level 1-1000 & Skynomi Shard Economy):
 *   1. Mortal       -> Cap Lv.30, Limit Dompet: 2,500 Shard (Biaya: 500 Shard) [Mulai Mudah!]
 *   2. Awakened     -> Cap Lv.75, Limit Dompet: 10,000 Shard (Biaya: 2,500 Shard)
 *   3. Transcendant -> Cap Lv.160, Limit Dompet: 45,000 Shard (Biaya: 10,000 Shard)
 *   4. Ascendant    -> Cap Lv.300, Limit Dompet: 180,000 Shard (Biaya: 45,000 Shard) [TAG & GEM BUKA]
 *   5. Aether       -> Cap Lv.500, Limit Dompet: 750,000 Shard (Biaya: 180,000 Shard) [DUAL-COMBO BUKA]
 *   6. Stellar      -> Cap Lv.720, Limit Dompet: 3,000,000 Shard (Biaya: 750,000 Shard) [MULTI-JOB BUKA]
 *   7. Astral       -> Cap Lv.880, Limit Dompet: 12,000,000 Shard (Biaya: 3,000,000 Shard)
 *   8. Cosmic       -> Cap Lv.980, Limit Dompet: 50,000,000 Shard (Biaya: 12,000,000 Shard) [TRIPLE-JOB]
 *   9. Univers      -> Cap Lv.1000, Limit Dompet: 250,000,000 Shard (Biaya: 50,000,000 Shard) [SOVEREIGN APEX]
 *
 * Fitur & Proteksi:
 * - Syarat Rankup Multifaktor:
 *   1. Level EXP Akun (sinkron dengan Skynomi.Exp.ExpApi)
 *   2. Jam Terbang / Playtime Aktif (Personal)
 *   3. Boss Progression Personal (Ditebas sendiri, bukan global)
 *   4. Pengerjaan Profesi Jobs (Kondisional: jika daftar butuh X misi; jika tidak bebas)
 *   5. Penguasaan Elemen (Element Tags & Dual-Element Combos)
 *   6. Biaya Skynomi Shard Kenaikan Kasta (Dipotong otomatis via Skynomi.Economy, warna ungu kosmik [c/c084fc:...])
 * - Limit Saldo Dompet Dinamis (Wallet Cap per Rank):
 *   Mencegah penimbunan Shard berlebihan di rank rendah secara stabil.
 * - Integrasi Skynomi Economy Ledger (Auto-route excess to Bank).
 * - Item Tier Lock Enforcement:
 *   Membatasi pemakaian Senjata, Tools, Armor, dan Aksesoris berdasarkan Rank.
 * - Database LiteDB terstruktur di skynomi.db
 * - Commands: /rank, /rankup, /ranks, /bosses, /benefit, /setrank, /giveboss
 * ==============================================================================
 */

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Skynomi.Modules;
using Skynomi.Economy;

namespace Skynomi.Ranks
{
    public class RankModule : IModule, IDependent, IDisposable, IReloadable
    {
        public string Name => "Rank Progression & Cosmic Economy";
        public string Description => "9 Cosmic-Themed ranks with playtime, boss tracker, item tier lock, and wallet cap";
        public Version Version => new Version(2, 0, 0);
        public string Author => "Keyou & Kimoy";

        public IReadOnlyList<Type> RequiredModules => new[]
        {
            typeof(Utils.UtilsModule),
            typeof(Skynomi.Database.DatabaseModule),
            typeof(Skynomi.Exp.ExpModule),
            typeof(Skynomi.Economy.EconomyModule)
        };

        public static RankModule Instance { get; private set; } = null!;
        public static RankConfig Config { get; private set; } = new RankConfig();
        public static RankDatabase Database { get; private set; } = null!;

        private static System.Timers.Timer? _playtimeTimer;

        public void Initialize()
        {
            Instance = this;
            Config = RankConfig.Read();
            Database = new RankDatabase();

            // Register Server Hooks
            ServerApi.Hooks.NpcKilled.Register(SkynomiPlugin.Instance, OnNpcKilled);
            ServerApi.Hooks.NetGetData.Register(SkynomiPlugin.Instance, OnGetData);
            ServerApi.Hooks.NetGreetPlayer.Register(SkynomiPlugin.Instance, OnPlayerGreet);

            // Inisialisasi Playtime Heartbeat Timer (setiap 30 detik menambah 30s ke active player + cek batas saldo dompet)
            _playtimeTimer = new System.Timers.Timer(30000);
            _playtimeTimer.Elapsed += HeartbeatTick;
            _playtimeTimer.AutoReset = true;
            _playtimeTimer.Start();

            // Register Commands
            RankCommands.Initialize();

            Log.Success("[Skynomi.Ranks] Module Cosmic Rank Progression & Wallet Cap aktif untuk TShock 6.x (.NET 9.0)!");
        }

        public void Dispose()
        {
            ServerApi.Hooks.NpcKilled.Deregister(SkynomiPlugin.Instance, OnNpcKilled);
            ServerApi.Hooks.NetGetData.Deregister(SkynomiPlugin.Instance, OnGetData);
            ServerApi.Hooks.NetGreetPlayer.Deregister(SkynomiPlugin.Instance, OnPlayerGreet);

            _playtimeTimer?.Stop();
            _playtimeTimer?.Dispose();

            Log.Info("[Skynomi.Ranks] Module Cosmic Rank Progression dibongkar (Disposed).");
        }

        public void Reload(ReloadEventArgs e)
        {
            Config = RankConfig.Read();
            Log.Info("[Skynomi.Ranks] Konfigurasi Cosmic Rank Progression dimuat ulang.");
        }

        private void OnPlayerGreet(GreetPlayerEventArgs e)
        {
            var player = TShock.Players[e.Who];
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            // Inisialisasi profil rank player di database jika belum ada
            var isNewPlayer = RankModule.Database.GetOrCreate(player.Account.Name);
            
            // [SOLUSI MUTLAK PERMISSION OWNER & ALL GROUPS]
            // Menyuntikkan perizinan dasar langsung ke grup aktif apa pun yang sedang dipakai pemain
            if (player.Group != null && !string.IsNullOrEmpty(player.Group.Name))
            {
                var basicPermissions = new List<string> 
                { 
                    "skynomi.rank.use",     // Izin perintah /rank, /benefit, /ranks
                    "skynomi.rank.up",      // Izin perintah /rankup
                    "skynomi.job.use",      // Izin perintah /job
                    "skynomi.job",
                    "skynomi.exp.use",      // Izin perintah /exp dan /toplvl
                    "skynomi.element.use"   // Izin perintah /element
                };

                List<string> permsToAdd = new List<string>();
                foreach (var perm in basicPermissions)
                {
                    // Jika grup tersebut belum punya permission-nya, masukkan ke daftar suntik
                    if (!player.Group.HasPermission(perm))
                    {
                        permsToAdd.Add(perm);
                    }
                }

                if (permsToAdd.Count > 0)
                {
                    try
                    {
                        // Daftarkan langsung ke database grup aktif TShock secara real-time
                        TShock.Groups.AddPermissions(player.Group.Name, permsToAdd);
                        TShock.Log.ConsoleInfo($"[Skynomi.Automation] Berhasil otomatis menyuntik {permsToAdd.Count} perizinan ke grup '{player.Group.Name}' via login {player.Name}.");
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleError($"[Skynomi.Automation] Gagal menyuntik permission ke grup {player.Group.Name}: {ex.Message}");
                    }
                }
            }

            EnforceWalletCap(player);
        }



        #region Playtime Tracker & Economy Heartbeat

        private void HeartbeatTick(object? sender, ElapsedEventArgs e)
        {
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active && player.IsLoggedIn)
                {
                    try
                    {
                        var data = Database.GetOrCreate(player.Account.Name);
                        data.PlaytimeSeconds += 30;
                        Database.Update(data);

                        // Enforce wallet limit bila diaktifkan di config
                        if (Config.EnableWalletCap)
                        {
                            EnforceWalletCap(player);
                        }
                    }
                    catch { }
                }
            }
        }

        #endregion

        #region Skynomi Modular Economy Integration

        public static long GetPlayerBalance(TSPlayer player)
        {
            if (player == null || !player.IsLoggedIn) return 0;
            return EconomyApi.GetBalance(player.Account.Name);
        }

        public static bool TryDeductShards(TSPlayer player, long costShards, string reason = "RankUp")
        {
            if (player == null || !player.IsLoggedIn) return false;
            return EconomyApi.TryDebit(player.Account.Name, costShards, reason);
        }

        // Backward compatibility
        public static bool TryDeductCredits(TSPlayer player, long costCredits, string reason = "RankUp") => TryDeductShards(player, costCredits, reason);

        public static void EnforceWalletCap(TSPlayer player)
        {
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            var rankData = Database.GetOrCreate(player.Account.Name);
            var rank = Config.GetRankByIndex(rankData.RankIndex);
            long maxLimit = rank.MaxMoneyLimitShards;

            if (maxLimit <= 0) return; // Uncapped

            // Sinkronisasi limit dompet akun ke kasta aktif & jalankan enforcement
            EconomyApi.SetWalletLimit(player.Account.Name, maxLimit);
            EconomyApi.EnforceWalletCap(player);
        }

        public static string FormatMoneyString(long shards)
        {
            return EconomyApi.FormatColored(shards);
        }

        #endregion

        #region Personal Boss Defeat Tracker

        private void OnNpcKilled(NpcKilledEventArgs e)
        {
            var npc = e.npc;
            if (npc == null || (!npc.boss && npc.type != NPCID.EaterofWorldsHead && npc.type != NPCID.EaterofWorldsBody && npc.type != NPCID.EaterofWorldsTail))
                return;

            string bossKey = IdentifyBossKey(npc);
            if (string.IsNullOrEmpty(bossKey)) return;

            // Catat khusus untuk pemain yang berada di dekat boss (~3500 pixel) atau seluruh server jika boss global
            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active || !player.IsLoggedIn || player.Dead) continue;

                float dist = Vector2.Distance(player.TPlayer.position, npc.position);
                bool inBossFight = dist <= 3500f || npc.type == NPCID.WallofFlesh || npc.type == NPCID.MoonLordCore;

                if (inBossFight)
                {
                    var data = Database.GetOrCreate(player.Account.Name);
                    if (!data.DefeatedBosses.Contains(bossKey))
                    {
                        data.DefeatedBosses.Add(bossKey);
                        Database.Update(data);

                        // Notifikasi personal ke player (ASCII bersih tanpa emoji)
                        player.SendSuccessMessage($"[ + PROGRES PERSONAL ] Kamu telah menumbangkan {npc.FullName}! Tercatat di riwayat rankup!");
                    }
                }
            }
        }

        private string IdentifyBossKey(NPC npc)
        {
            // Deteksi Spesial: Daytime Empress of Light (Secret Mode)
            if (npc.type == NPCID.HallowBoss && Main.dayTime)
            {
                return "DaytimeEmpressOfLight";
            }

            return npc.type switch
            {
                NPCID.KingSlime => "KingSlime",
                NPCID.EyeofCthulhu => "EyeOfCthulhu",
                NPCID.EaterofWorldsHead or NPCID.EaterofWorldsBody or NPCID.EaterofWorldsTail or NPCID.BrainofCthulhu => "EaterOfWorldsOrBrain",
                NPCID.QueenBee => "QueenBee",
                NPCID.SkeletronHead => "Skeletron",
                NPCID.Deerclops => "Deerclops",
                NPCID.WallofFlesh or NPCID.WallofFleshEye => "WallOfFlesh",
                NPCID.QueenSlimeBoss => "QueenSlime",
                NPCID.Retinazer or NPCID.Spazmatism => "TheTwins",
                NPCID.TheDestroyer or NPCID.TheDestroyerBody or NPCID.TheDestroyerTail => "TheDestroyer",
                NPCID.SkeletronPrime => "SkeletronPrime",
                NPCID.Plantera => "Plantera",
                NPCID.Golem or NPCID.GolemHead => "Golem",
                NPCID.DukeFishron => "DukeFishron",
                NPCID.Pumpking => "Pumpking",
                NPCID.MourningWood => "MourningWood",
                NPCID.IceQueen => "IceQueen",
                NPCID.MartianSaucer or NPCID.MartianSaucerCore => "MartianSaucer",
                NPCID.DD2Betsy => "Betsy",
                NPCID.LunarTowerSolar or NPCID.LunarTowerVortex or NPCID.LunarTowerNebula or NPCID.LunarTowerStardust => "CelestialPillars",
                NPCID.HallowBoss => "EmpressOfLight",
                NPCID.CultistBoss => "LunaticCultist",
                NPCID.MoonLordCore or NPCID.MoonLordHead or NPCID.MoonLordHand => "MoonLord",
                _ => ""
            };
        }

        #endregion

        #region Item Restriction & Tier Lock Engine

        private void OnGetData(GetDataEventArgs e)
        {
            if (!Config.EnableItemLock) return;

            var player = TShock.Players[e.Msg.whoAmI];
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            // Packet 5: PlayerSlot (Equip armor, weapon slot change, accessories)
            if (e.MsgID == PacketTypes.PlayerSlot)
            {
                using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                byte playerId = reader.ReadByte();
                short slot = reader.ReadInt16();
                short stack = reader.ReadInt16();
                byte prefix = reader.ReadByte();
                short netId = reader.ReadInt16();

                if (netId <= 0) return;

                int requiredTier = Config.GetItemTier(netId);
                var rankData = Database.GetOrCreate(player.Account.Name);
                var currentRank = Config.GetRankByIndex(rankData.RankIndex);

                if (requiredTier > currentRank.MaxItemTierAllowed)
                {
                    // Item melampaui kasta pemain!
                    e.Handled = true;

                    // Pulihkan slot asli di klien pemain agar item tidak terpasang
                    player.SendData(PacketTypes.PlayerSlot, "", player.Index, slot);

                    var reqRank = Config.GetRankForTier(requiredTier);
                    player.SendErrorMessage($"[ X ITEM LOCKED ] Item ini adalah Tier {requiredTier}. Kamu butuh Rank '{reqRank.Name}' untuk memakainya!");
                    player.SendData(PacketTypes.CreateCombatTextExtended, $"LOCKED! Butuh {reqRank.Name}", (int)Color.Red.PackedValue, player.X, player.Y - 20);
                }
            }
        }

        #endregion
    }
}
