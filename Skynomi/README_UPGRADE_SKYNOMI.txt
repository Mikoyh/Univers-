
# PANDUAN LENGKAP UPGRADE SKYNOMI 2.0 (TSHOCK 6.x & TERRARIA 1.4.5 SERIES - .NET 9.0)

Selamat! Ini adalah pembaruan resmi untuk Skynomi Core (oleh Keyou & AI Studio Server Architect).

================================================================================
1. APA YANG DIPERBAIKI UNTUK TSHOCK 6.x & TERRARIA 1.4.5?
================================================================================
- TSHOCK 6.x & .NET 9.0 MIGRATION:
  TShock 6.0 resmi merilis migrasi penuh ke .NET 9.0 untuk menyambut Terraria versi 1.4.5.
  Plugin lama yang masih memakai .NET 6/8 akan ditolak oleh TShock 6.x server.
  Skynomi 2.0 ini 100% dikompilasi menggunakan target .NET 9.0 ('net9.0') dan package TShock 6.1.0+!
- FITUR EXP & LEVELING SEIMBANG (SKALA 1 s/d 1000 - 10x LIPAT LEBIH GRANULAR):
  * Skala level diperluas 10x lipat (1 s/d 1000) agar pemain 10x lebih sering merasakan kepuasan "DING" naik level.
  * Level 1 langsung tembus ke Level 2 hanya dengan membunuh 1 Green Slime (+18 EXP)!
  * Kalahkan Boss Pre-Hardmode / Hardmode memberi ~800 - 2.500 EXP (naik 2-5 level sekaligus).
  * Level 1000 Apex Cap membutuhkan total waktu dan tempo yang setara dengan Level 100 klasik (~2.575 EXP per level di endgame).
- TAMPILAN CHAT SPESIAL SESUAI PERMINTAAN:
  Format: [i:<badgeId>] [Lv.<Level>] [Prefix][Nama]: Pesan
  Ikon item Terraria ([i:ID]) otomatis berkembang sesuai tingkat level pemain:
  * Level 1 - 99    : Copper Broadsword ([i:3507]) - Starter Phase
  * Level 100 - 249 : Night's Edge ([i:273]) - Pre-Hardmode Mastery
  * Level 250 - 499 : Terra Blade ([i:757]) - Hardmode & Tag Elemen Unlock (Lv.350)
  * Level 500 - 749 : Solar Fragment ([i:3456]) - Kombo Reaksi Unlock (Lv.500)
  * Level 750 - 949 : Meowmere ([i:3063]) - Endgame Lunar & Overdrive
  * Level 950 - 1000: Zenith Apex ([i:4956] - Ultimate Absolute Server Level Cap!)

================================================================================
2. CARA PASANG KE REPOSITORY SKYNOMI
================================================================================
Jika kamu ingin langsung mengompilasi plugin:
1. Ganti file 'Skynomi.csproj' di folder repo temanmu dengan 'Skynomi.csproj' baru (.NET 9.0).
2. Tambahkan folder 'Exp/' di dalam folder 'Skynomi/' yang berisi:
   - ExpModule.cs
   - ExpApi.cs       (Jembatan API & Event OnLevelUp untuk Plugin Rank)
   - ExpConfig.cs
   - ExpDatabase.cs
   - ExpCommands.cs
3. Jalankan 'dotnet build -c Release'.
4. File 'Skynomi.dll' siap dipindahkan ke folder 'ServerPlugins/' di server Terraria!

================================================================================
3. DAFTAR COMMAND LENGKAP
================================================================================
Pemain Biasa (Permission: skynomi.exp.use):
- /exp atau /level   -> Cek level, progres bar EXP, dan EXP yang dibutuhkan
- /toplvl atau /exptop -> Papan peringkat 10 pemain level tertinggi di server

Admin Server (Permission: skynomi.exp.admin):
- /expadmin add <player> <amount> -> Tambah EXP pemain
- /expadmin set <player> <level>  -> Atur level pemain langsung
- /expadmin reload                -> Muat ulang file ExpConfig.json tanpa restart server

================================================================================
4. INDEX DATABASE LITEDB & CARA MENGHUBUNGKAN KE PLUGIN RANK EKSTERNAL
================================================================================
Plugin ini didesain MURNI sebagai sistem level & EXP (tanpa mencampuri urusan rank),
sehingga kamu atau temanmu bisa membuat plugin Rank terpisah dengan sangat mudah!

[A] INDEX DATABASE LITEDB (skynomi.db):
- File Path Database : tshock/Skynomi/skynomi.db
- Collection Name    : "players_exp"
- Primary Key Index  : "AccountName" (Unique Index, String - Nama Akun TShock)
- Skema Data:
  * AccountName (string) -> INDEX KUNCI UTAMA
  * Level (int)          -> Level Pemain (1, 2, 3...)
  * CurrentExp (long)    -> Sisa EXP level saat ini
  * TotalExp (long)      -> Akumulasi seluruh EXP karir
  * MobsKilled (int)     -> Total monster dibunuh
  * BossesKilled (int)   -> Total boss dikalahkan

[B] CARA MENGHUBUNGKAN PLUGIN RANK C# (EVENT LISTENER LEVEL-UP):
Cukup tambahkan referensi Skynomi.dll ke project plugin Rank kamu:

public override void Initialize()
{
    // Subscribe ke event level up dari Skynomi.Exp
    Skynomi.Exp.ExpApi.OnLevelUp += OnPlayerLevelUp;
}

private void OnPlayerLevelUp(Skynomi.Exp.PlayerLeveledUpEventArgs e)
{
    // Logika Naik Rank Kustom Kamu:
    if (e.NewLevel >= 10 && e.Player.Group.Name == "default")
    {
        TShock.UserAccounts.SetUserGroup(TShock.UserAccounts.GetUserByName(e.AccountName), "Petualang");
        e.Player.SendSuccessMessage($"[RANK UP] Selamat {e.Player.Name}! Kamu mencapai Level {e.NewLevel} dan naik rank ke Petualang!");
    }
    else if (e.NewLevel >= 25 && e.Player.Group.Name == "Petualang")
    {
        TShock.UserAccounts.SetUserGroup(TShock.UserAccounts.GetUserByName(e.AccountName), "Kesatria");
        e.Player.SendSuccessMessage($"[RANK UP] Selamat {e.Player.Name}! Level {e.NewLevel} membawa kamu ke Rank Kesatria!");
    }
}

[C] FUNGSI API LAINNYA YANG BISA DIPANGGIL LANGSUNG:
- int lvl = Skynomi.Exp.ExpApi.GetLevel(player);
- long totalExp = Skynomi.Exp.ExpApi.GetTotalExp(player.Account.Name);
- string badgeTag = Skynomi.Exp.ExpApi.GetChatTag(player); // misal "[i:273] [c/a855f7:[Lv.15]]"
- Skynomi.Exp.ExpApi.AddExp(player, 500); // Beri EXP dari sistem quest/event eksternal!

================================================================================
5. SISTEM PLAYTIME, PERSONAL BOSSKILL & ITEM TIER LOCK (SKYNOMI.RANKS)
================================================================================
Fitur baru yang sudah terintegrasi dan siap pakai di dalam folder 'Ranks/':
1. Syarat Kenaikan Kasta (/rankup):
   - Level EXP Minimum (dari Skynomi.Exp)
   - Playtime Tracker Per-Player (akumulasi waktu bermain aktif di server)
   - Personal Boss Defeat Tracker (KHUSUS pemain yang ikut mengalahkan boss, bukan global server! Mulai dari King Slime, WoF, Mech Bosses, Moon Lord, hingga Daytime Empress of Light!)
2. Item Tier Lock Enforcement:
   - Pemain tidak bisa memakai/mengayunkan senjata, tools, armor, atau equipment di atas tier kasta mereka.
   - Konfigurasi modular di 'RankConfig.json' (ItemId -> TierNumber).
3. Commands:
   - /rank, /rankup, /ranks, /bosses, /setrank <player> <index>, /giveboss <player> <bossKey>

================================================================================
6. SISTEM JOBS & VOCATION PROFESI (SKYNOMI.JOBS)
================================================================================
Fitur pekerjaan modular terintegrasi di dalam folder 'Jobs/':
1. Profesi Pekerjaan:
   - Miner: Menambang Stone & Ore mendapatkan koin Terraria & EXP langsung.
   - Fisher: Memancing ikan laut/sungai, crate, dan jackpot Golden Carp (1.5 Gold!).
   - Hunter: Membasmi monster malam, spider cave, dan underworld mobs.
   - Lumberjack: Menebang berbagai varietas kayu Terraria.
   - Farmer / Herbalist: Memanen tanaman obat Daybloom, Moonglow, dll.
2. Anti-Dupe Protection:
   - Cache memori pelacak blok yang dipasang pemain (PlacedTiles) agar pemain tidak bisa menaruh ore dan menambangnya ulang untuk mengeksploitasi koin.
3. Leveling Pekerjaan:
   - Semakin sering bekerja, level job naik dan memberikan bonus upah multiplier (+2.5% per level)!
4. Commands:
   - /job list, /job join <miner|fisher>, /job leave <job>, /job stats, /job top

================================================================================
7. SISTEM AETHERIAL RESONANCE & ELEMEN COMBAT (SKYNOMI.AETHERIAL)
================================================================================
Modul sistem elemen mandiri berbasis Server-Side Packet Interception (100% Vanilla Client Compatible):
1. 5 Elemen Utama:
   - IGNIS (Api): Damage over time & burst amplifikasi tinggi
   - GLACIAL (Es): Kontrol pergerakan, pembekuan & crowd control (Hard Stun)
   - TORRENT (Air): Sinergi area, konduktor pemicu & manipulasi medan
   - VOLT (Petir): Serangan berantai (Chain Lightning), kecepatan & pengikisan armor
   - BRAMBLE (Racun/Alam): Efek taktis bertahap & benih taktis kombo sekunder
2. Easter Egg Elemen Rahasia:
   - OMNIA (Aether Singularity): Peluang 0.000001% (1 in 100,000,000) saat Tutorial Warp!
   - Memberikan 5X MULTIVERSAL DAMAGE (+500% instant burst) & Universal Resonance dengan SEMUA elemen!
3. Matriks Reaksi Elemen:
   - Ignis + Glacial   -> THERMAL SHOCK (+100% Instant Burst DMG)
   - Ignis + Torrent   -> SCALDING (+85% Instant Burst DMG)
   - Glacial + Torrent -> PERMAFROST (+40% DMG + Hard Stun 3.5s)
   - Ignis + Volt      -> PLASMA BLAST (+50% DMG + AOE Knockback)
   - Glacial + Volt    -> STATIC DECAY (+40% DMG + -30% Boss Armor)
   - Torrent + Volt    -> IONIZED STREAM (+40% DMG + Chain Lightning)
   - Torrent + Bramble -> OVERGROWTH (Tactical Seed -> Burgeon / Hyperbloom)
   - Omnia + Apapun    -> MULTIVERSAL COLLAPSE (5X MULTIVERSAL DMG)
4. Mekanik Lockout 8 Detik:
   - Proc Chance: 15% per hit menempelkan Custom Tag elemen.
   - Durasi Tag di mob: 5 detik.
   - Personal Cooldown user: +3 detik (Total lockout: 8 detik).
5. Solo Player Balance:
   - Elemental Synergy (+5% DMG) + Marked Target Bonus (+10% DMG) = Maks +15% Solo Bonus.
6. Commands:
   - /element, /element awaken, /element reactions, /setelement <player> <element>
