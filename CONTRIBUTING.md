# Panduan Kontribusi MusicBay

Semua perubahan masuk lewat pull request (PR) ke `master`. Pemilik repo yang me-review, me-merge, dan merilis.

Kalau kamu memakai AI, minta ia membaca [AGENTS.md](AGENTS.md) dulu.

## Persiapan (sekali saja)

Butuh [.NET SDK](global.json) 10.0.401 dengan workload Android, Android SDK API 36, dan JDK 17 atau lebih baru.

1. Fork repo ini di GitHub.
2. Clone fork-mu dan sambungkan ke repo utama:

```bash
git clone https://github.com/USERNAME-KAMU/MusicBay.git
cd MusicBay
git remote add upstream https://github.com/ssenwako/MusicBay.git
dotnet workload restore Seeker/Seeker.csproj
```

## Alur kerja

**1. Sebelum mengerjakan apa pun, buka atau klaim Issue.** Tulis file dan fitur yang akan kamu sentuh. Ini cara paling ampuh mencegah konflik, karena dua orang tidak akan mengerjakan bagian yang sama.

**2. Buat branch baru dari `master` terbaru.** Satu branch untuk satu perubahan.

```bash
git fetch upstream
git switch -c fix/scroll-playlist upstream/master
```

Awali nama branch dengan `fix/`, `feat/`, atau `docs/`.

**3. Kerjakan, lalu commit kecil-kecil.** Pesan commit dalam bahasa Inggris, bentuk perintah, dan menjelaskan apa yang berubah, misalnya `Fix playlist scroll on short screens`.

**4. Sebelum push, samakan dengan `master` terbaru.** Lakukan juga setiap kali mulai kerja lagi, supaya konflik tetap kecil.

```bash
git fetch upstream
git rebase upstream/master
```

**5. Build dan uji** (lihat di bawah), lalu push dan buka PR:

```bash
git push --force-with-lease origin fix/scroll-playlist
```

`--force-with-lease` hanya untuk branch milikmu, setelah rebase. Jangan pernah force push ke `master`.

**6. Isi PR dengan singkat:** apa yang berubah, kenapa, cara mengujinya, dan screenshot gelap serta terang kalau ada perubahan tampilan.

**7. Tunggu CI hijau dan review.** Perbaikan dari review cukup ditambahkan ke branch yang sama. Setelah di-merge, hapus branch-nya dan mulai branch baru untuk pekerjaan berikutnya.

## Agar tidak konflik

- Satu PR untuk satu hal. Jangan gabungkan perbaikan bug dengan refactor.
- Jangan memformat ulang, mengganti nama, atau merapikan kode yang tidak kamu ubah.
- Jangan ubah `versionCode` atau `versionName`. Versi diatur pemilik repo saat rilis.
- Teks baru di `Seeker/Resources/values/strings.xml` ditambahkan di dekat teks yang berkaitan, bukan di baris paling akhir, karena di sana paling sering bentrok.
- `Seeker/Main/MainActivity.cs`, `Seeker/Resources/values/styles.xml`, dan `Seeker/Resources/values/themes.xml` sering disentuh. Ubah seperlunya, dan sebutkan di Issue.
- Saat konflik, baca kedua sisi lalu pertahankan maksud keduanya. Kalau ragu, tanyakan di PR.

## Build dan uji

Gunakan konfigurasi `Debug Mock`, yang memakai server Soulseek palsu, jadi tidak perlu akun. Selalu `Clean` dulu, karena build bertahap bisa menghasilkan APK yang crash saat dibuka.

```bash
dotnet build Seeker/Seeker.csproj -c "Debug Mock" -f net10.0-android36.0 -p:AndroidKeyStore=false -p:EmbedAssembliesIntoApk=true -t:Clean
```

```bash
dotnet build Seeker/Seeker.csproj -c "Debug Mock" -f net10.0-android36.0 -p:AndroidKeyStore=false -p:EmbedAssembliesIntoApk=true -t:SignAndroidPackage
```

Hasilnya ada di `Seeker/bin/Debug Mock/net10.0-android36.0/app.musicbay-Signed.apk`. Kalau MusicBay rilis sudah terpasang, uninstall dulu karena kuncinya berbeda.

Jalankan unit test yang sama dengan CI:

```bash
dotnet test UnitTestCommon/UnitTestCommon.csproj --filter "TestCategory!=RealData&FullyQualifiedName!~ImportQTConfig&FullyQualifiedName!~TestRealUserParsingBrowseResponseTestData"
```

Kalau menambah logika yang bisa diuji tanpa Android, taruh di `Common/` dan tambahkan test di `UnitTestCommon/`. Perubahan tampilan wajib dicek di tema gelap dan terang.

## Aturan yang tidak boleh dilanggar

- **Jangan commit rahasia:** kunci, kata sandi, token, atau file `.jks`. Hanya pemilik repo yang memegang kunci rilis, dan PR tidak butuh kunci itu.
- **Jangan buat rilis atau tag.** Rilis hanya dilakukan pemilik repo.
- **Jangan cantumkan AI sebagai penulis.** Tanpa baris `Co-Authored-By` atau catatan "generated with" dari AI di commit dan PR. Commit memakai nama dan email GitHub-mu sendiri.
- **Patuhi lisensi.** Proyek ini GPL-3.0-only dengan Additional Terms di [LICENSE](LICENSE):
  - Jangan hapus catatan hak cipta yang sudah ada.
  - Saat mengubah file sumber, tambahkan di bawah catatan yang ada:
    ```text
    Copyright (c) <tahun> <nama>
    Modified: <ringkasan perubahan>
    ```
  - File baru memakai header lisensi seperti `Seeker/Player/AppUpdater.cs`.
  - Jangan ubah pemberitahuan modifikasi di README dan layar About.
- **Jangan tambahkan fitur yang mendorong pelanggaran hak cipta,** termasuk di teks dan screenshot.
