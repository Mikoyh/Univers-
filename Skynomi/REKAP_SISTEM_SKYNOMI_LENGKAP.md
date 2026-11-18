================================================================================
REKAP LENGKAP SISTEM SKYNOMI 2.0 (TERRARIA 1.4.5 & TSHOCK 6.x / .NET 9.0)
SISTEM EXP TERPUSAT, LEVEL 1-1000, 10 TINGKATAN KASTA, JOBS & ELEMEN
================================================================================
Tanggal Penyusunan : 2026-10-21
Platform Runtime   : TShock 6.0+ / 6.1.0+ (Terraria 1.4.5 Series)
Target Kompilasi   : C# 13 / .NET 9.0 (net9.0)
Arsitektur         : Single Centralized EXP Engine (Satu Sistem EXP Inti Terpadu)
Database           : LiteDB Embedded (skynomi.db, skynomi_ranks.db, skynomi_jobs.db)
Client Compability : 100% Vanilla Terraria Client (Server-Side Packet Interception)
================================================================================

DAFTAR ISI REKAP:
1. Ringkasan Eksekutif & Filosofi Sistem Terpadu (Single EXP Engine)
2. Sistem Grand Leveling 1 s/d 1000 (Skala 10x Lipat & MMORPG Hard Level Cap)
3. Matriks 10 Tingkatan Kasta Server (Rank 0 s/d Rank 9)
4. Evaluasi 5 Syarat Kualifikasi Naik Kasta (/rankup)
   - Syarat 1: Level Karakter
   - Syarat 2: Jam Terbang / Playtime Aktif (Konversi Jam -> Hari Terraria)
   - Syarat 3: Personal Boss Kills (Killed with Own Hands / Non-AFK)
   - Syarat 4: Minimum Pengerjaan Jobs (ATURAN KONDISIONAL EMAS)
   - Syarat 5: Penguasaan Elemen (Element Tags & Dual Combos)
5. Rekap Komprehensif Seluruh Benefit Tiap Kasta (Rank 0 s/d Rank 9)
6. Sistem Aetherial Resonance Elemen & Visual Large Gems di Atas Kepala
   - Visual Display Large Gems Vanilla Packet Interception
   - Penempatan Gems Sesuai Elemen (Torrent, Glacial, Omnia, Ignis, Volt, Bramble)
   - Mekanik Durasi 5 Detik & Meledak Saat Kombo
   - Solusi Masalah Large Gems Fisik di Inventory Player
   - 8 Matriks Reaksi Kombo Dual Elemen & Easter Egg Omnia
7. Sistem Profesi Pekerjaan (Jobs Vocation) & Perlindungan Anti-Dupe
8. Panduan Daftar Command Chat Server TShock (Pemain & Staff/Admin)
9. Struktur File Direktori ZIP & Panduan Kompilasi Server (.NET 9.0)

================================================================================
BAGIAN 1: RINGKASAN EKSEKUTIF & FILOSOFI SISTEM TERPADU (SINGLE EXP ENGINE)
================================================================================
Seluruh ekosistem RPG Terraria server ini menggunakan SATU SISTEM EXP INTI 
(Single Centralized Core EXP). Tidak ada cabang EXP terpisah yang membingungkan 
antara kasta, level, dan progres karakter. 

Semua aktivitas pemain bermuara pada satu angka EXP akumulasi:
- Mengobrol di chat server memberikan EXP (dengan anti-spam cooldown).
- Memburu monster malam, underground, dan dungeon memberikan EXP.
- Menumbangkan raid bos Terraria memberikan lonjakan EXP masif (Boss Multiplier 35x).
- Menjalankan pesanan profesi/pekerjaan (Jobs) memberikan EXP dan Gold Coin.

EXP ini langsung menaikkan Level karakter (1 s/d 1000). Namun, level ini tidak bisa 
berdiri sendiri tanpa diimbangi oleh kasta/rank pemain. Inilah inti dari mekanisme 
MMORPG Level Lock yang kita terapkan.

================================================================================
BAGIAN 2: SISTEM GRAND LEVELING 1 s/d 1000 (SKALA 10x LIPAT)
================================================================================
1. Mengapa Level 1 - 1000? (Sensasi Ding Lebih Sering)
   - Skala level ditingkatkan 10x lipat dari rentang klasik 1-100 menjadi 1-1000.
   - Tujuan Desain: Memberikan sensasi psikologis "Ding!" (naik level) yang jauh 
     lebih sering dan memuaskan bagi pemain tanpa membuat proses grinding membosankan.
   - Kurva EXP terkalibrasi proporsional: EXP yang dibutuhkan per sub-level dibuat 
     terjangkau, sehingga waktu tempuh mencapai Level 1000 setara dengan tempo bermain 
     mencapai Level 100 klasik.

2. Mekanik Batas Level MMORPG (Hard Cap / Level Stoppage):
   - ATURAN MUTLAK: Level seorang pemain AKAN TERKUNCI (berhenti bertambah EXP/Level) 
     jika pemain tersebut belum menaikkan Kasta/Rank-nya ke tingkat berikutnya!
   - Contoh Konkret:
     * Pemain berada di Rank 0 (Novice). Batas maksimum levelnya adalah Level 100.
     * Ketika pemain mencapai Level 100, bar EXP akan penuh 100% dan TIDAK BISA 
       menjadi Level 101 meskipun pemain membunuh ribuan monster atau ratusan bos.
     * Pemain WAJIB mengetik '/rankup' dan memenuhi seluruh 5 syarat kasta untuk 
       membuka kunci batas level berikutnya (Level 100 -> 200 di Rank 1).
   - Manfaat Sistem:
     * Mencegah anomali pemain berlevel tinggi tapi berstatus kasta rendah.
     * Memastikan progresi karakter, jam terbang, dan penguasaan konten berjalan seimbang.

3. EXP Multiplier Server Global per Kasta:
   Setiap kali pemain berhasil naik kasta, mereka dihadiahi pengali perolehan EXP:
   - Rank 1 (Mortal)       : 1.00x (EXP Normal)
   - Rank 2 (Awakened)     : 1.15x (+15% Bonus EXP)
   - Rank 3 (Transcendant) : 1.35x (+35% Bonus EXP)
   - Rank 4 (Ascendant)    : 1.60x (+60% Bonus EXP)
   - Rank 5 (Aether)       : 1.95x (+95% Bonus EXP)
   - Rank 6 (Stellar)      : 2.35x (+135% Bonus EXP)
   - Rank 7 (Astral)       : 2.90x (+190% Bonus EXP)
   - Rank 8 (Cosmic)       : 3.75x (+275% Bonus EXP)
   - Rank 9 (Univers)      : 5.00x (+400% Godlike Server EXP)

================================================================================
BAGIAN 3: MATRIKS 9 TINGKATAN KASTA COSMIC SERVER (RANK 1 s/d RANK 9)
================================================================================
Daftar 9 Kasta Resmi Bertema Cosmic untuk Server MMORPG Univers:
1. Mortal       -> Kasta awal fana manusia biasa.
2. Awakened     -> Jiwa terbangun, menerima elemen takdir di Tutorial Warp.
3. Transcendant -> Melampaui batas mortal, menaklukkan parasit chasm & queen bee.
4. Ascendant    -> Ksatria pembasmi WoF pembuka Hardmode & Large Gems Tag Display.
5. Aether       -> Penyelaras dimensi eter, membuka Reaksi Kombo Dual-Element.
6. Stellar      -> Pahlawan bertabur cahaya bintang, membuka Multi-Job (2 jobs).
7. Astral       -> Penakluk galaksi astral, memotong cooldown job 50% & gear Fishron.
8. Cosmic       -> Penguasa tata surya kosmik, membuka Triple-Jobs (3 jobs) & Zenith.
9. Univers      -> Puncak tertinggi semesta berdaulat (Sovereign Apex), Cap Lv.1000!

+-----+-----------------------+---------------+-----------+-----------+-------------+------------+-----------------+-----------------+
| Rnk | Nama Kasta Cosmic     | Prefix Chat   | Rentang   | Playtime  | Bos Wajib   | Max Tier   | Limit Dompet    | Biaya Naik      |
|     |                       |               | Level     | Minimal   | Minimal     | Item Lock  | Saldo Shard     | Kasta (/rankup) |
|     |                       |               |           |           |             |            | ([c/c084fc:...])| ([c/c084fc:...])|
+-----+-----------------------+---------------+-----------+-----------+-------------+------------+-----------------+-----------------+
|  1  | Mortal                | [Mortal]      | 1 - 30    | 0 Menit   | 0 Bos       | Tier 0     | 2,500 Shard     | 500 Shard       |
|  2  | Awakened              | [Awakened]    | 30 - 75   | 20 Menit  | 2 Bos       | Tier 1     | 10,000 Shard    | 2,500 Shard     |
|  3  | Transcendant          | [Transcendant]| 75 - 160  | 60 Menit  | 5 Bos       | Tier 2     | 45,000 Shard    | 10,000 Shard    |
|  4  | Ascendant             | [Ascendant]   | 160 - 300 | 3 Jam     | 7 Bos (+WoF)| Tier 3     | 180,000 Shard   | 45,000 Shard    |
|  5  | Aether                | [Aether]      | 300 - 500 | 8 Jam     | 4 HM Bos    | Tier 4     | 750,000 Shard   | 180,000 Shard   |
|  6  | Stellar               | [Stellar]     | 500 - 720 | 20 Jam    | 4 Bos       | Tier 5     | 3,000,000 Shard | 750,000 Shard   |
|  7  | Astral                | [Astral]      | 720 - 880 | 50 Jam    | 5 Bos       | Tier 6     | 12,000,000 Shard| 3,000,000 Shard |
|  8  | Cosmic                | [Cosmic]      | 880 - 980 | 120 Jam   | 4 Bos       | Tier 7     | 50,000,000 Shard| 12,000,000 Shard|
|  9  | Univers               | [Univers]     | 980 - 1000| 360 Jam   | 11 Bos (+EoL| Tier 8     | 250,000,000 Shard| 50,000,000 Shard|
|     | (Sovereign Apex)      |               | (MAX CAP) | (21,600m) |   Daytime!) | (Semua)    | (Kedaulatan)    | (Puncak Semesta)|
+-----+-----------------------+---------------+-----------+-----------+-------------+------------+-----------------+-----------------+

================================================================================
BAGIAN 4: EVALUASI 6 SYARAT KUALIFIKASI NAIK KASTA (/rankup)
================================================================================
Untuk naik kasta menggunakan perintah '/rankup', sistem server akan secara otomatis 
mengevaluasi 6 parameter kualifikasi karakter:

1. SYARAT 1: MINIMUM LEVEL KARAKTER (Skala 1 - 1000, Kurva Non-Linear)
   - Karakter harus telah mencapai level batas kasta saat ini.
   - Mortal ke Awakened dimulai ramah pemula di Lv.30, lalu bertahap meningkat hingga Lv.1000.

2. SYARAT 2: PLAYTIME AKTIF (Jam Terbang Nyata di Server)
   - Playtime dihitung per detik saat pemain aktif di server (AFK terlindungi).
   - Dikonversi ke format hari dan jam ramah pemain:
     * 20 Menit   -> "20 Menit"
     * 60 Menit   -> "1 Jam 0 Menit"
     * 180 Menit  -> "3 Jam 0 Menit"
     * 480 Menit  -> "8 Jam 0 Menit"
     * 1,200 Menit -> "20 Jam 0 Menit"
     * 3,000 Menit -> "2 Hari 2 Jam"
     * 7,200 Menit -> "5 Hari 0 Jam"
     * 21,600 Menit -> "15 Hari 0 Jam" (Syarat Kasta Univers!)

3. SYARAT 3: BIAYA UANG SHARD KENAIKAN PANGKAT (Rankup Cost)
   - Menggunakan mata uang digital server: SHARD (SHARD).
   - Tampilan chat dihiasi warna ungu kosmik: [c/c084fc:X Shard].
   - Saldo dipotong otomatis dan aman di Main Thread Terraria saat /rankup berhasil.

4. SYARAT 4: LIMIT SALDO DOMPET MAKSIMAL (Wallet Cap per Rank)
   - Mencegah pemain di kasta rendah menimbun Shard berlebihan di luar kasta mereka.
   - Surplus saldo otomatis dialihkan ke rekening Bank akun LiteDB agar tidak hangus.

5. SYARAT 5: PERSONAL BOSS KILLS (Tumbang oleh Tangan Sendiri)
   - Hanya mencatat bos yang secara aktif diserang oleh pemain (radius ~3500px).
   - Meliputi King Slime s/d Moon Lord, dan Daytime Empress of Light untuk Rank Univers!

6. SYARAT 6: MINIMUM PENGERJAAN JOBS (ATURAN KONDISIONAL EMAS)
   - JIKA MENDAFTAR JOBS: Pemain wajib menyelesaikan kuota misi profesi mereka.
   - JIKA TIDAK MENDAFTAR JOBS: Syarat pekerjaan otomatis dibatalkan/dianggap lulus!

================================================================================
BAGIAN 5: REKAP KOMPREHENSIF SYARAT, BATASAN & BENEFIT 9 KASTA COSMIC
================================================================================

--- [RANK 1: MORTAL] ---
* Syarat Perolehan  : Pemain baru bergabung (Level 1, 0 Menit Playtime, 0 Bos)
* Batas Dompet      : 2,500 Shard (Biaya Rankup: 500 Shard)
* Batas Level Cap   : Level 1 - 30 (Mulai Mudah & Ramah Pemula)
* Item Tier Allowed : Tier 0 (Kayu Dasar, Copper & Starter Tools)
* Batasan           : Terkunci dari item Tier 1+, Elemen Jiwa belum aktif (Dormant).
* Keuntungan        : 1.0x EXP Server, akses 1 job dasar, /warp spawn & /warp tutorial.

--- [RANK 2: AWAKENED] ---
* Syarat Perolehan  : Level 30, Playtime 20m, Bos: King Slime & Eye of Cthulhu, Biaya: 2,500 Shard.
* Batas Dompet      : 10,000 Shard
* Batas Level Cap   : Terbuka hingga Level 75
* Item Tier Allowed : Tier 1 (Silver, Tungsten, Gold, Platinum, Minishark pemula)
* Batasan           : Limit Shard 10,000, belum bisa memasang Tag Elemen ke monster.
* Keuntungan        : +15% EXP Server, Ritual Awakening Elemen di Warp Tutorial, 
                      +2.0% Sinergi Damage Senjata, +5% Shard Pekerjaan, /warp underground.

--- [RANK 3: TRANSCENDANT] ---
* Syarat Perolehan  : Level 75, Playtime 60m, Bos: King Slime, EoC, Deerclops, EoW/BoC, Queen Bee, Biaya: 10,000 Shard.
* Batas Dompet      : 45,000 Shard
* Batas Level Cap   : Terbuka hingga Level 160
* Item Tier Allowed : Tier 2 (Demonite, Crimtane, Shadow/Crimson Armor, Meteorite, Space Gun)
* Batasan           : Limit Shard 45,000, belum bisa menggunakan perlengkapan Pre-HM terkuat.
* Keuntungan        : +35% EXP Server, +3.5% Sinergi Senjata, Cooldown job -30s, /warp jungle & ocean.

--- [RANK 4: ASCENDANT] ---
* Syarat Perolehan  : Level 160, Playtime 3 Jam (180m), Bos: Skeletron & Wall of Flesh, 10x Tag Elemen, Biaya: 45,000 Shard.
* Batas Dompet      : 180,000 Shard
* Batas Level Cap   : Terbuka hingga Level 300
* Item Tier Allowed : Tier 3 (Night's Edge, Molten Armor, Hellstone, Cobalt Shield, Muramasa)
* FITUR SPESIAL     : UNLOCK ELEMENT TAG SYSTEM & LARGE GEMS DISPLAY!
                      - Serangan berpeluang 12% memunculkan Large Gem di kepala pemain selama 4-5s!
                      - Solo Marked Target Bonus: +3.5% Damage pada monster ber-tag!
* Keuntungan        : +60% EXP Server, akses item awal Hardmode, /warp underworld & hallow.

--- [RANK 5: AETHER] ---
* Syarat Perolehan  : Level 300, Playtime 8 Jam (480m), Bos: Queen Slime, The Twins, The Destroyer, 25x Tags, 15x Combos, Biaya: 180,000 Shard.
* Batas Dompet      : 750,000 Shard
* Batas Level Cap   : Terbuka hingga Level 500
* Item Tier Allowed : Tier 4 (Mythril, Titanium, Adamantite, Megashark, Excalibur)
* FITUR SPESIAL     : UNLOCK DUAL-ELEMENT COMBO REACTIONS!
                      - Memicu 8 reaksi kombo: Thermal Shock (2x DMG), Scalding (+85%), Permafrost, dll.
                      - Large Gem meledak & lenyap seketika saat reaksi kombo dipicu!
* Keuntungan        : +95% EXP Server, +5.5% Sinergi Senjata, Cooldown job dipotong ke 3 menit.

--- [RANK 6: STELLAR] ---
* Syarat Perolehan  : Level 500, Playtime 20 Jam (1,200m), Bos: Skeletron Prime, Plantera, Pumpking, Mourning Wood, 60x Tags, 35x Combos, Biaya: 750,000 Shard.
* Batas Dompet      : 3,000,000 Shard
* Batas Level Cap   : Terbuka hingga Level 720
* Item Tier Allowed : Tier 5 (Terra Blade, Spectre, Shroomite, Beetle Armor, True Night's Edge/Excalibur)
* FITUR SPESIAL     : MULTI-JOB PRIVILEGE (Bisa Mengambil 2 Jobs Sekaligus!)
                      - Menjalankan 2 profesi bersamaan (misal: Miner + Fisher) untuk pendapatan ganda!
* Keuntungan        : +135% EXP Server, +10% Damage Reaksi Kombo, Cooldown Rod of Discord -15%, /warp temple.

--- [RANK 7: ASTRAL] ---
* Syarat Perolehan  : Level 720, Playtime 50 Jam (3,000m), Bos: Golem, Duke Fishron, Empress of Light, Martian Saucer, Betsy, 120x Tags, 60x Combos, Biaya: 3,000,000 Shard.
* Batas Dompet      : 12,000,000 Shard
* Batas Level Cap   : Terbuka hingga Level 880
* Item Tier Allowed : Tier 6 (Fishron Wings, Tsunami, Razorblade Typhoon, Influx Waver, Flying Dragon)
* FITUR SPESIAL     : JOB COOLDOWN CUT -50%! (Jeda antar order pekerjaan hanya 2.5 menit).
* Keuntungan        : +190% EXP Server, +25% Shard pekerjaan, +15% Damage Reaksi Kombo, Aura Partikel Bintang Astral.

--- [RANK 8: COSMIC] ---
* Syarat Perolehan  : Level 880, Playtime 120 Jam (7,200m), Bos: Lunatic Cultist, Celestial Pillars, Moon Lord, Ice Queen, 250x Tags, 120x Combos, Biaya: 12,000,000 Shard.
* Batas Dompet      : 50,000,000 Shard
* Batas Level Cap   : Terbuka hingga Level 980
* Item Tier Allowed : Tier 7 (Solar, Vortex, Nebula, Stardust Armor, Zenith, Meowmere, Star Wrath)
* FITUR SPESIAL     : TRIPLE JOBS PRIVILEGE (Bisa Mengambil 3 Jobs Sekaligus!)
                      - Mampu menjalankan 3 profesi bersamaan untuk monopoli ekonomi server!
* Keuntungan        : +275% EXP Server, +20% Damage Reaksi Kombo, Aura Partikel 4 Celestial Pillars abadi.

--- [RANK 9: UNIVERS (SOVEREIGN APEX - KASTA PUNCAK)] ---
* Syarat Perolehan  : Level 980, Playtime 360 Jam (21,600m), Membunuh seluruh 11 Boss Pantheon Terraria termasuk DAYTIME EMPRESS OF LIGHT (Daytime One-Shot!), 400x Tags, 200x Combos, Biaya: 50,000,000 Shard.
* Batas Dompet      : 250,000,000 Shard (Kedaulatan Ekonomi Raja Semesta)
* Batas Level Cap   : LEVEL 1000 (ABSOLUTE MAXIMUM SERVER LEVEL CAP)!
* Item Tier Allowed : Tier 8 (Terraprisma, Zenith, Rod of Harmony & Semua Senjata Keramat Dewa tanpa batasan!)
* FITUR SPESIAL     : KASTA PUNCAK BERDAULAT UNIVERS APEX:
                      1. Multiversal Cosmic Surge: Resonansi damage elemen dibatasi ketat maksimal 25.0% di Lv. 1000.
                      2. Zero Job Cooldown: Seluruh order pekerjaan instan tanpa waktu tunggu (0 delay)!
                      3. All Job Master: Bebas ganti profesi kapan saja tanpa penalti 30m leave cooldown.
                      4. Floating Sovereign Crown: Mahkota kristal kosmik melayang abadi di atas kepala karakter.
                      5. Server-Wide Announcement: Broadcast sambutan keagungan Yang Mulia Univers saat login.
                      6. Chat Prefix: [Univers] emas kosmik bercahaya suci & saldo Shard ungu kosmik [c/c084fc:...].

================================================================================
BAGIAN 6: SISTEM VISUAL ELEMEN & LARGE GEMS DI ATAS KEPALA (PACKET INTERCEPTION)
================================================================================
Sistem elemen Aetherial Resonance diimplementasikan menggunakan Server-Side Packet 
Interception (Packet 33 & Packet 27), sehingga 100% kompatibel dengan client Terraria 
vanilla tanpa mewajibkan pemain menginstal mod/tModLoader apapun!

1. DAFTAR VISUAL LARGE GEMS SESUAI ELEMEN:
   Sesuai permintaan konfigurasi spesifik:
   - TORRENT (Air)       -> Large Sapphire (Item ID: 1526)
   - GLACIAL (Es)        -> Large Diamond  (Item ID: 1527)
   - OMNIA (Aether/God)  -> Large Amber    (Item ID: 3643 - bentuk bulat oranye legendaris)
   - IGNIS (Api)         -> Large Ruby     (Item ID: 1525)
   - VOLT (Petir)        -> Large Topaz    (Item ID: 1524)
   - BRAMBLE (Racun/Alam)-> Large Emerald  (Item ID: 1523)

2. MEKANIK DURASI 5 DETIK & PEMBERSIHAN GEMS SAAT KOMBO:
   - Durasi Tag Aktif: Ketika senjata berelemen berhasil memicu Tag (15% proc chance), 
     Large Gem yang bersesuaian akan melayang mengambang di atas kepala target 
     selama tepat 5 detik.
   - PEMBERSIHAN OTOMATIS SAAT KOMBO:
     Jika musuh yang sedang ber-Tag diserang oleh elemen lain dan menghasilkan 
     Dual Reaction (misal: musuh bertag Glacial diserang Ignis -> memicu Thermal Shock), 
     maka Large Gem di atas kepala musuh OTOMATIS DILENYAPKAN / MELEDAK seketika 
     bersamaan dengan munculnya angka damage reaksi dan partikel ledakan!

3. SOLUSI JIKA PEMAIN MEMBAWA LARGE GEMS ASLI DI INVENTORY:
   - Pertanyaan Penting: Bagaimana jika player di inventory-nya memang membawa item 
     fisik Large Gems (misal dari hasil crafting tambang)?
   - Solusi C# Packet Interception:
     Plugin mengintersepsi 'GetDataHandlers.PlayerUpdate' dan menyaring flag 
     'HasLargeGems'. Status gem visual elemen dikelola pada slot data virtual terpisah. 
     Server memastikan bahwa display gem elemen diprioritaskan saat skill aktif, dan 
     ketika tag berakhir/kombo meledak, display kembali disinkronkan dengan isi tas 
     asli pemain tanpa merusak atau menghilangkan item fisik di inventory pemain.

4. MATRIKS 8 REAKSI KOMBO DUAL ELEMEN:
   - Ignis + Glacial   -> THERMAL SHOCK (+100% Instant Burst DMG)
   - Ignis + Torrent   -> SCALDING (+85% Instant Burst DMG)
   - Glacial + Torrent -> PERMAFROST (+40% DMG + Hard Stun 3.5 Detik)
   - Ignis + Volt      -> PLASMA BLAST (+50% DMG + AOE Knockback)
   - Glacial + Volt    -> STATIC DECAY (+40% DMG + -30% Pengikisan Armor Bos)
   - Torrent + Volt    -> IONIZED STREAM (+40% DMG + Petir Berantai / Chain Lightning)
   - Torrent + Bramble -> OVERGROWTH (Benih Taktis -> Meledak Burgeon / Hyperbloom)
   - Omnia + Elemen Apa Saja -> MULTIVERSAL COLLAPSE (5X MULTIVERSAL DMG / +500% Burst!)

5. EASTER EGG OMNIA (AETHER SINGULARITY):
   - Peluang bawaan saat pemain pertama kali warp tutorial adalah 0.000001% (1 in 100,000,000).
   - Menghasilkan visual Large Amber (bulat oranye menyala) dan aura kilatan multiversal.

================================================================================
BAGIAN 7: SISTEM PROFESI PEKERJAAN (JOBS VOCATION) & PROTEKSI ANTI-DUPE
================================================================================
1. 5 Profesi Pekerjaan:
   - MINER       : Menambang Stone, Ores, Gems (Gaji Gold Coin & EXP).
   - FISHER      : Memancing ikan laut/sungai, crate, dan jackpot Golden Carp (1.5 Gold!).
   - HUNTER      : Membasmi monster malam, zombie, spider, dan underworld mobs.
   - LUMBERJACK  : Menebang pohon Normal, Boreal, Palm, Rich Mahogany, dan Pearlwood.
   - HERBALIST   : Memanen bunga herbal obat (Daybloom, Moonglow, Waterleaf, Fireblossom).

2. Proteksi Anti-Dupe PlacedTiles:
   - Sistem mencatat koordinat blok yang diletakkan oleh pemain ke dalam memory cache.
   - Pemain TIDAK AKAN mendapatkan koin atau EXP jika menambang ore atau memotong kayu 
     yang sebelumnya diletakkan sendiri oleh pemain (mencegah eksploitasi pasang-tambang).

3. Progresi Multi-Job:
   - Rank 0 s/d 5 : 1 Profesi aktif.
   - Rank 6 s/d 7 : 2 Profesi aktif bersamaan.
   - Rank 8       : 3 Profesi aktif bersamaan.
   - Rank 9       : Seluruh 5 Profesi aktif bersamaan (All Jobs Master).

================================================================================
BAGIAN 8: PANDUAN DAFTAR COMMAND CHAT SERVER TSHOCK
================================================================================

[A] PERINTAH KASTA & PROGRESI (SKYNOMI.RANKS)
- /rank                 : Melihat kasta pemain saat ini, batas level cap, dan status progres.
- /rankup               : Mengevaluasi 5 syarat dan melakukan kenaikan kasta secara instan.
- /ranks                : Menampilkan daftar seluruh 10 tingkatan kasta di server.
- /benefit [rankIndex]  : Melihat rincian lengkap benefit dari kasta tertentu (0 s/d 9).
- /bosses               : Menampilkan daftar bos yang sudah dan belum dikalahkan oleh pemain.

[B] PERINTAH LEVEL & EXP (SKYNOMI.EXP)
- /level atau /lvl      : Melihat level pemain saat ini, bar EXP menuju level berikutnya, dan kasta.
- /exp                  : Menampilkan rincian total EXP karir dan status pengali server.
- /toplvl               : Melihat papan peringkat 10 pemain dengan level tertinggi di server.

[C] PERINTAH PEKERJAAN (SKYNOMI.JOBS)
- /job list             : Melihat daftar 5 profesi pekerjaan yang tersedia.
- /job join <profesi>   : Mendaftar pekerjaan (misal: /job join miner).
- /job leave <profesi>  : Berhenti dari pekerjaan (terkena jeda cooldown ganti profesi).
- /job stats            : Melihat progres order misi, total koin yang didapat, dan level job.
- /job top              : Papan peringkat pekerja tersukses di server.

[D] PERINTAH ELEMEN (SKYNOMI.AETHERIAL)
- /element              : Melihat status elemen yang dimiliki pemain, tag count, dan combo count.
- /element reactions    : Menampilkan panduan kombo 8 reaksi elemen di chat.

[E] PERINTAH STAFF / ADMIN SERVER:
- /setrank <player> <index>      : Mengatur kasta pemain secara langsung (0-9).
- /giveboss <player> <bossKey>   : Memberikan kredit kill bos tertentu kepada pemain.
- /resetbosses <player>          : Mengosongkan riwayat kill bos pemain.
- /setlevel <player> <level>     : Mengatur level pemain (1-1000).
- /exp add <player> <amount>     : Menambahkan sejumlah EXP ke pemain.
- /setelement <player> <element> : Mengatur elemen pemain (Ignis, Glacial, Torrent, Volt, Bramble, Omnia).
- /setjobstats <player> <count>  : Mengatur jumlah order pekerjaan yang telah diselesaikan.

================================================================================
BAGIAN 9: STRUKTUR FILE DIREKTORI ZIP & PANDUAN KOMPILASI SERVER (.NET 9.0)
================================================================================
File ZIP bundle yang diunduh memiliki struktur lengkap dan siap di-build:

[Direktori Root ZIP]
├── Skynomi.csproj                  <- File project C# targeting .NET 9.0 (net9.0)
├── build.bat                       <- Script build otomatis 1-klik untuk Windows Server
├── build.sh                        <- Script build otomatis untuk Linux VPS
├── README_UPGRADE_SKYNOMI.txt      <- Panduan instalasi dan deployment TShock
├── REKAP_SISTEM_SKYNOMI_LENGKAP.txt <- Dokumen rekapan lengkap ini (Plain Text)
├── REKAP_SISTEM_SKYNOMI_LENGKAP.md  <- Dokumen rekapan lengkap ini (Markdown)
│
├── Exp/                            <- Modul Sistem EXP Terpusat & Leveling (1-1000)
│   ├── ExpModule.cs
│   ├── ExpApi.cs
│   ├── ExpConfig.cs
│   ├── ExpDatabase.cs
│   └── ExpCommands.cs
│
├── Ranks/                          <- Modul 10 Tingkatan Kasta & 5 Syarat Rankup
│   ├── RankModule.cs
│   ├── RankApi.cs
│   ├── RankConfig.cs
│   ├── RankDatabase.cs
│   └── RankCommands.cs
│
├── Jobs/                           <- Modul 5 Profesi Pekerjaan & Proteksi Anti-Dupe
│   ├── JobModule.cs
│   ├── JobApi.cs
│   ├── JobConfig.cs
│   ├── JobDatabase.cs
│   └── JobCommands.cs
│
├── Aetherial/                      <- Modul Elemen, Large Gems Display & Reaksi Kombo
│   ├── AetherialModule.cs
│   ├── AetherialApi.cs
│   ├── AetherialConfig.cs
│   ├── ElementDatabase.cs
│   └── AetherialCommands.cs
│
└── Configs/                        <- Konfigurasi Modular JSON
    └── RankConfig.json             <- Tempat modifikasi nama rank, batas tier item, dll.

LANGKAH KOMPILASI:
1. Pastikan server memiliki .NET 9.0 SDK terinstal ('dotnet --version' menghasilkan 9.0.xxx).
2. Ekstrak seluruh isi file ZIP ke dalam folder plugin server TShock.
3. Jalankan 'build.bat' (Windows) atau 'bash build.sh' (Linux VPS).
4. Hasil kompilasi 'Skynomi.dll' di dalam folder 'bin/Release/net9.0/' dipindahkan 
   ke folder 'ServerPlugins/' milik server TShock 6.x.
5. Jalankan server Terraria dan selamat menikmati ekosistem RPG Skynomi 2.0 yang sempurna!
================================================================================
