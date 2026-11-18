================================================================================
REKAP LENGKAP SISTEM ELEMEN AETHERIAL SKYNOMI 2.0
TERRARIA 1.4.5 & TSHOCK 6.x (.NET 9.0 / C# 13)
SINKRONISASI LEVEL UTAMA 1 - 1000, RANK PROGRESSION, LARGE GEMS, & REAKSI KOMBO
================================================================================
Dokumen ini merangkum secara komprehensif seluruh arsitektur Sistem Elemen Aetherial,
mulai dari pembagian 5 elemen inti + 1 rahasia, sinkronisasi tingkatan level elemen 
dengan Level Karakter Utama (Skala 1 - 1000), milestone pembukaan Tag Elemen dan 
Reaksi Kombo, pemetaan visual Large Gem (Player.gem Packet 4), hingga matriks 
reaksi dual-element dan panduan perintah server.

--------------------------------------------------------------------------------
BAGIAN 1: FILOSOFI DASAR & KETERHUBUNGAN SINGLE EXP ENGINE
--------------------------------------------------------------------------------
1. INTEGRASI TANPA CABANG:
   Elemen BUKANLAH sistem EXP terpisah yang berdiri sendiri. Elemen terikat 
   langsung dengan Single EXP Engine Skynomi. EXP yang diperoleh dari pertempuran, 
   pekerjaan (jobs), dan bossing mengalir ke satu akun karakter.
   
2. SKALA LEVEL 1 S.D. 1000:
   Sesuai pembaruan Skynomi 2.0, level karakter menggunakan skala 1 s.d. 1000
   dengan rasio granular 10x lipat (Level 1000 setara dengan kedalaman level 100 klasik).
   Kekuatan elemental pemain berkembang selaras mengikuti 10 tingkatan level utama ini.

3. HARD CAP LEVEL TERKUNCI DENGAN RANK:
   Pemain tidak dapat menaikkan level elemen maupun level karakter jika belum 
   berhasil naik kasta (Rankup). Kasta Rank mengunci batas level maksimum pemain.

--------------------------------------------------------------------------------
BAGIAN 2: KRONOLOGI SINKRONISASI LEVEL UTAMA (1-1000) & 9 KASTA COSMIC
--------------------------------------------------------------------------------
Sistem elemen terintegrasi langsung dengan Single EXP Engine & Kasta Cosmic:

[Fase 1: Pra-Elemen & Pembentukan Jiwa]
- Level 1 - 99   (Rank 1: Mortal):
  Elemen belum bangkit (Dormant). Pemain berfokus pada eksplorasi dasar dan tutorial.
  Batas Dompet Saldo: 50 Gold (Biaya Rankup: 10 Gold).
- Level 100 - 249 (Rank 2: Awakened):
  Elemen jiwa dibangkitkan pertama kali di akhir Tutorial Warp melalui perintah
  otomatis RegionCmd '/element awaken'. Pemain mendapatkan salah satu dari 5 elemen 
  inti (atau 0.000001% easter egg Omnia).
  Memberikan pasif Weapon Synergy tipis (+2.0% DMG). Tag elemen belum bisa dipasang.
  Batas Dompet Saldo: 2.5 Platinum (Biaya Rankup: 50 Gold).
- Level 250 - 399 (Rank 3: Transcendant):
  Energi elemen mulai mengalir stabil (Weapon Synergy +3.5%). Tag masih terkunci.
  Batas Dompet Saldo: 10 Platinum (Biaya Rankup: 2 Platinum).

[Fase 2: [TAG BUKA] MILESTONE PEMBUKAAN TAG ELEMEN PERTAMA KALI]
- Level 400 - 549 (Rank 4: Ascendant):
  ★ MILESTONE BESAR: Kemampuan menempelkan Tag Elemen ke musuh RESMI TERBUKA!
  * Peluang Pasang Tag: 12% setiap serangan mengenai musuh.
  * Durasi Tag di Monster: 4 detik (disertai partikel debuff vanilla di tubuh mob).
  * Visual Large Gem: Paket Player.gem Packet 4 AKTIF! Permata besar berputar 
    gagah di atas kepala pemain selama 4 detik saat tag berhasil terpasang.
  * Lockout Cooldown: 8 detik internal cooldown untuk pemain yang sama.
  * Solo Marked Target Bonus: +5.0% bonus damage jika memukul musuh yang bertag.
  * Batas Dompet Saldo: 35 Platinum (Biaya Rankup: 8 Platinum).
  * SYARAT RANKUP KE RANK 5: Minimal pernah mengaktifkan Tag Elemen sebanyak 25 KALI!

[Fase 3: [KOMBO BUKA] MILESTONE PEMBUKAAN REAKSI KOMBO DUAL-ELEMENT]
- Level 550 - 699 (Rank 5: Aether):
  ★ MILESTONE BESAR: Reaksi Kombo 2 Elemen (Dual-Element Reaction) RESMI TERBUKA!
  * Pemain kini dapat memicu reaksi spektakuler jika menyerang monster yang telah 
    diberi tag oleh elemen berbeda (misal: Ignis + Glacial = Thermal Shock 2x DMG).
  * Peluang Pasang Tag: 15% (Standar Utama).
  * Durasi Tag: 5 detik | Lockout Cooldown: 8 detik.
  * Solo Marked Target: +10.0% DMG (Maksimal Solo Cap +15.0%).
  * Large Gem otomatis lenyap seketika saat reaksi kombo dipicu.
  * Batas Dompet Saldo: 100 Platinum (Biaya Rankup: 25 Platinum).
  * SYARAT RANKUP KE RANK 6: Minimal memicu Reaksi Kombo Elemen sebanyak 35 KALI!

[Fase 4: Akselerasi & Puncak End-Game]
- Level 700 - 819 (Rank 6: Stellar):
  ★ MULTI-JOB PRIVILEGE: Bisa menjalankan 2 profesi sekaligus (Miner + Fisher)!
  Lockout internal berkurang menjadi 7.5 detik. Bonus kombo reaksi +10% damage.
  Batas Dompet Saldo: 300 Platinum (Biaya Rankup: 75 Platinum).
  SYARAT RANKUP KE RANK 7: Minimal memicu Reaksi Kombo sebanyak 60 KALI!
- Level 820 - 919 (Rank 7: Astral):
  Durasi tag 5.5s, lockout 7.0s, bonus kombo +15% damage, job cooldown -50%.
  Batas Dompet Saldo: 1,000 Platinum (Biaya Rankup: 200 Platinum).
  SYARAT RANKUP KE RANK 8: Minimal memicu Reaksi Kombo sebanyak 120 KALI!
- Level 920 - 979 (Rank 8: Cosmic):
  ★ TRIPLE-JOB PRIVILEGE: Mampu menjalankan 3 profesi sekaligus!
  Lockout 6.0s, sinergi senjata +9%, bonus kombo +20% damage. Akses pedang Zenith.
  Batas Dompet Saldo: 3,500 Platinum (Biaya Rankup: 500 Platinum).
  SYARAT RANKUP KE RANK 9: Minimal memicu Reaksi Kombo sebanyak 200 KALI!
- Level 980 - 1000 (Rank 9: Univers):
  ★ KASTA PUNCAK TERTINGGI UNIVERS (SOVEREIGN APEX):
  Penguasaan kosmis mutlak. Tag proc 20%, durasi 6s, lockout tercepat 5.0 detik,
  sinergi senjata +10%, bonus kombo +30% (dan 5x damage untuk Omnia Secret).
  Batas Dompet Saldo: 15,000 Platinum (Biaya Rankup: 1,500 Platinum).
  SYARAT PENCAPAIAN APEX: Membunuh seluruh pantheon bos termasuk Daytime Empress of Light!

--------------------------------------------------------------------------------
BAGIAN 3: TABEL LENGKAP 9 TINGKATAN PROGRESI KASTA COSMIC & ELEMEN (LV. 1 - 1000)
--------------------------------------------------------------------------------
+------+-------------+--------------------+------+--------+--------+---------+--------+--------+----------+----------+--------------------+
| Tier | Level Range | Kasta Cosmic       | Proc | Durasi | Lockout| Synergy | Solo   | SoloCap| Large Gem| Kombo    | Limit Dompet (CR)  |
+------+-------------+--------------------+------+--------+--------+---------+--------+--------+----------+----------+--------------------+
| T1   | Lv. 1 - 29  | Rank 1: Mortal     |  0%  |   0s   |   0s   |  +1.0%  |   0%   |  +1.0% | Nonaktif | Terkunci | 2,500 Credits      |
| T2   | Lv. 30 - 74 | Rank 2: Awakened   |  0%  |   0s   |   0s   |  +2.0%  |   0%   |  +2.5% | Nonaktif | Terkunci | 10,000 Credits     |
| T3   | Lv. 75 - 159| Rank 3:Transcendant|  0%  |   0s   |   0s   |  +3.5%  |   0%   |  +4.0% | Nonaktif | Terkunci | 45,000 Credits     |
| T4   | Lv.160 - 299| Rank 4: Ascendant  | 12%  |   4s   |   8s   |  +4.5%  |  +3.5% |  +8.0% | AKTIF    | Solo Tag | 180,000 Credits    |
| T5   | Lv.300 - 499| Rank 5: Aether     | 15%  |   5s   |   8s   |  +5.5%  |  +6.5% | +12.0% | AKTIF    | DUAL-OPEN| 750,000 Credits    |
| T6   | Lv.500 - 719| Rank 6: Stellar    | 16%  |   5s   |  7.5s  |  +6.5%  |  +9.5% | +16.0% | AKTIF    | +10% DMG | 3,000,000 Credits  |
| T7   | Lv.720 - 879| Rank 7: Astral     | 17%  |  5.5s  |  7.0s  |  +7.5%  | +11.5% | +19.0% | AKTIF    | +15% DMG | 12,000,000 Credits |
| T8   | Lv.880 - 979| Rank 8: Cosmic     | 18%  |  6.0s  |  6.0s  |  +8.5%  | +13.5% | +22.0% | AKTIF    | +20% DMG | 50,000,000 Credits |
| T9   | Lv.980 -1000| Rank 9: Univers    | 20%  |  6.0s  |  5.0s  | +10.0%  | +15.0% | +25.0% | AKTIF    | +30% DMG | 250,000,000 Credits|
+------+-------------+--------------------+------+--------+--------+---------+--------+--------+----------+----------+--------------------+

Catatan Kolom:
- Proc     : Peluang serangan memasang tag elemen pada monster.
- Durasi   : Lama tag menempel pada target (detik).
- Lockout  : Internal cooldown jeda penempelan tag ulang oleh player yang sama.
- Synergy  : Ekstra persen damage saat memakai senjata bertema sama dengan elemen.
- Solo     : Ekstra damage saat menyerang target yang memiliki tag elemen sendiri.
- SoloCap  : Batas maksimum penumpukan bonus damage solo player.
- Large Gem: Menampilkan permata mengambang di atas kepala player (Packet 4).
- Kombo    : Status pembukaan dan bonus amplifikasi damage reaksi kombo 2 elemen.

--------------------------------------------------------------------------------
BAGIAN 4: PROFIL 5 ELEMEN INTI & 1 ELEMEN RAHASIA (OMNIA)
--------------------------------------------------------------------------------
1. IGNIS (Api / Flame & Ember)
   - Karakteristik: Amplifikasi damage burst tinggi dan efek bakar terus-menerus.
   - Sinergi Senjata: Senjata berunsur api (Flame, Fire, Molten, Hellfire).
   - Drop Rate: 20% (Peluang awakening di tutorial).

2. GLACIAL (Es / Cryo & Frost)
   - Karakteristik: Pengendalian area, perlambatan pergerakan, dan pembekuan total (Hard Stun).
   - Sinergi Senjata: Senjata berunsur es/salju (Frost, Ice, Blizzard, Snow).
   - Drop Rate: 20%.

3. TORRENT (Air / Hydro & Current)
   - Karakteristik: Konduktor utama medan pertempuran, efek basah, pembawa reaksi berantai.
   - Sinergi Senjata: Senjata bertema air/laut (Water, Tide, Ocean, Aqua).
   - Drop Rate: 20%.

4. VOLT (Petir / Electro & Thunder)
   - Karakteristik: Serangan berantai (chain lightning), kecepatan frekuensi tinggi, pengikisan zirah.
   - Sinergi Senjata: Senjata bertema listrik (Thunder, Lightning, Spark, Storm).
   - Drop Rate: 20%.

5. BRAMBLE (Racun & Alam / Flora & Bio)
   - Karakteristik: Penanaman Tactical Seed untuk kombo sekunder (Overgrowth, Burgeon, Hyperbloom).
   - Sinergi Senjata: Senjata bertema alam/racun (Poison, Jungle, Leaf, Seed).
   - Drop Rate: 20%.

6. OMNIA (Aether Secret - Easter Egg Langka!)
   - Peluang: 0.000001% (1 banding 100.000.000 roll).
   - Karakteristik: Singularitas kosmis multiversal. Mampu bereaksi dengan SEMUA elemen 
     dan menghasilkan 5x LIPAT INSTANT DAMAGE (500% DAMAGE MULTIPLIER).

--------------------------------------------------------------------------------
BAGIAN 5: PEMETAAN VISUAL LARGE GEM VANILLA (PACKET 4) & DEBUFF MONSTER
--------------------------------------------------------------------------------
Sistem ini menggunakan aset murni vanilla Terraria sehingga 100% kompatibel tanpa mod client:

1. IGNIS:
   - Visual Player : Large Ruby (Item ID 1526, Player.gem = 5) -> Permata Ruby Merah Segi Delapan.
   - Debuff Monster: On Fire! (Buff ID 24) -> Kobaran lidah api oranye membakar monster.
   - CombatText    : [c/f97316:♦ IGNIS TAG]

2. GLACIAL:
   - Visual Player : Large Diamond (Item ID 1527, Player.gem = 6) -> Berlian Cyan/Putih Dingin.
   - Debuff Monster: Frostburn (Buff ID 44) -> Uap beku dan kristal es biru menyelimuti target.
   - CombatText    : [c/38bdf8:♦ GLACIAL TAG]

3. TORRENT:
   - Visual Player : Large Sapphire (Item ID 1524, Player.gem = 3) -> Permata Safir Biru Samudra.
   - Debuff Monster: Wet (Buff ID 103) -> Percikan dan tetesan air deras mengalir dari tubuh target.
   - CombatText    : [c/06b6d4:♦ TORRENT TAG]

4. VOLT:
   - Visual Player : Large Amethyst (Item ID 1522, Player.gem = 1) -> Kristal Amethyst Ungu Berkedip.
   - Debuff Monster: Electrified (Buff ID 144) -> Percikan kilatan listrik menyengat tubuh target.
   - CombatText    : [c/a855f7:♦ VOLT TAG]

5. BRAMBLE:
   - Visual Player : Large Emerald (Item ID 1525, Player.gem = 4) -> Permata Zamrud Hijau Daun.
   - Debuff Monster: Poisoned (Buff ID 20) -> Gelembung racun hijau meletup di sekeliling target.
   - CombatText    : [c/10b981:♦ BRAMBLE TAG]

6. OMNIA:
   - Visual Player : Large Amber (Item ID 1814, Player.gem = 7) -> Bola Amber Emas Berpendar Kosmis.
   - Debuff Monster: Shadowflame (Buff ID 153) -> Aura bayangan kosmis dan api hitam ungu.
   - CombatText    : [c/e879f9:★ OMNIA TAG ★]

SIKLUS HIDUP LARGE GEM:
- Muncul berputar di atas kepala pemain seketika saat tag berhasil dipasang (proc).
- Bertahan selama durasi tag aktif (4 - 6 detik tergantung level).
- Begitu terjadi Reaksi Kombo 2 Elemen oleh rekan party, Large Gem SEKETIKA LENYAP.
- Jika tag habis tanpa dipicu kombo, Large Gem hilang secara otomatis.

--------------------------------------------------------------------------------
BAGIAN 6: MATRIKS REAKSI DUAL-ELEMENT & FORMULA DAMAGE
--------------------------------------------------------------------------------
Reaksi dipicu ketika serangan elemen kedua mengenai monster yang telah bertag elemen lain:

1. IGNIS + GLACIAL -> THERMAL SHOCK
   - Role         : Meta DPS / Amplifying
   - Multiplier   : 2.0x (Total Strike Damage menjadi 200% / +100% Burst).
   - Efek Visual  : Teks melayang merah membara '[c/f97316:THERMAL SHOCK! +DMG]'.

2. IGNIS + TORRENT -> SCALDING
   - Role         : Meta DPS / Vaporizing
   - Multiplier   : 1.85x (+85% Instant Burst Damage).
   - Efek Tambahan: Mengikis resistensi pertahanan elemen target.

3. GLACIAL + TORRENT -> PERMAFROST
   - Role         : Crowd Control / Tactical Lockdown
   - Multiplier   : 1.40x (+40% Burst Damage).
   - Efek Tambahan: Hard Stun total selama 3.5 detik (Boss kecepatan gerak berkurang 70%).

4. IGNIS + VOLT -> PLASMA BLAST
   - Role         : Area Clearing / Kinetic AOE
   - Multiplier   : 1.50x (+50% Burst Damage).
   - Efek Tambahan: Ledakan Knockback gelombang kejut ke monster dalam radius 8 blok.

5. GLACIAL + VOLT -> STATIC DECAY
   - Role         : Utility / Defense Shred
   - Multiplier   : 1.40x (+40% Burst Damage).
   - Efek Tambahan: Armor Shatter mengurangi 30% pertahanan boss selama 8 detik.

6. TORRENT + VOLT -> IONIZED STREAM
   - Role         : Area Spread / Chain Lightning
   - Multiplier   : 1.40x (+40% Burst Damage).
   - Efek Tambahan: Sambaran kilat memantul hingga ke 3 musuh di sekitar target.

7. TORRENT + BRAMBLE -> OVERGROWTH
   - Role         : Tactical Seed Planting
   - Multiplier   : 1.30x (+30% Burst Damage).
   - Efek Tambahan: Memunculkan Tactical Seed di tanah yang dapat diledakkan dengan 
     Ignis (Burgeon +120% AOE) atau Volt (Hyperbloom +100% Homing Shard).

8. OMNIA + SEMUA ELEMEN -> MULTIVERSAL COLLAPSE (EASTER EGG)
   - Role         : Cosmic Singularity Destruction
   - Multiplier   : 5.0x (500% DAMAGE MULTIPLIER / 5 KALI LIPAT DAMAGE ASLI!).
   - Efek Tambahan: Universal Resonance yang memicu ledakan dimensional maha dahsyat.

--------------------------------------------------------------------------------
BAGIAN 7: KEAMANAN DATABASE GUARD & ANTI-REROLL REGIONCMD
--------------------------------------------------------------------------------
1. PERMANEN SEKALI AWAKEN:
   Saat pemain menginjak Warp Tutorial, perintah '/element awaken' dieksekusi.
   Plugin mengecek kolom 'AssignedElement' di database. Jika sudah terisi, eksekusi 
   dibatalkan seketika. Pemain TIDAK BISA melakukan re-roll dengan menginjak ulang region!

2. PERINTAH REGIONCMD TSOCK (PILIH SALAH SATU):
   - Opsi Mode Senyap (Recommended):
     /region addcmd <nama_region> element awaken -s
   - Opsi Cabut Permission:
     /region addcmd <nama_region> element awaken
     /region addcmd <nama_region> user addperm %username% !skynomi.element.awaken

--------------------------------------------------------------------------------
BAGIAN 8: DAFTAR LENGKAP PERINTAH SERVER (TSHOCK COMMANDS)
--------------------------------------------------------------------------------
[A] PERINTAH PEMAIN:
- /element              : Melihat status elemen sendiri, level elemen, pasif, dan stats.
- /element reactions    : Menampilkan matriks panduan 8 reaksi kombo elemen di chat.
- /element awaken       : Membangkitkan elemen pertama kali saat berada di Warp Tutorial.

[B] PERINTAH STAFF / ADMIN:
- /setelement <player> <ignis|glacial|torrent|volt|bramble|omnia>
  Mengatur elemen pemain secara langsung tanpa melalui tutorial.
- /setelementlevel <player> <1-1000>
  Mengatur level elemen pemain secara instan (1 s/d 1000).
- /resetelement <player>
  Mereset profil elemen pemain menjadi kosong sehingga dapat mengulang proses awaken.

================================================================================
AKHIR DOKUMEN REKAP SISTEM ELEMEN AETHERIAL SKYNOMI 2.0
DIPRODUKSI UNTUK SERVER TERRARIA 1.4.5 & TSHOCK 6.x (.NET 9.0)
================================================================================
