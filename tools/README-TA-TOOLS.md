# Alat tulis laporan — KBBI, literatur, parafrase

Tiga kemampuan untuk penulisan laporan TA/skripsi. Semua gratis dan tanpa
API key.

| Alat | Bentuk | Sumber |
|---|---|---|
| **KBBI** | MCP server (4 tool) | `kbbi.kemendikdasmen.go.id` |
| **Scholar** | MCP server (4 tool) | OpenAlex, Crossref, DOAJ |
| **Parafrase** | Skill Hermes | — (pakai KBBI untuk validasi) |

---

## 1. KBBI — `tools/kbbi/`

Kamus Besar Bahasa Indonesia VI Daring, sumber resmi Badan Bahasa.

| Tool | Fungsi |
|---|---|
| `kbbi_lookup` | Arti + kelas kata + pemenggalan |
| `kbbi_check_baku` | Cek bentuk baku (ringkas) |
| `kbbi_batch` | Cek banyak kata sekaligus |
| `kbbi_syllables` | Pemenggalan suku kata |

**Yang membedakan dari kamus biasa:** parser menangkap metadata yang sering
luput padahal penting untuk laporan —

- **bentuk tidak baku**: `analisis` → tidak baku `analisa`, `metode` → `metoda`
- **lemma dasar**: `menganalisis` → dasar `analisis`

Cache lokal di `data/cache/` — kata diambil sekali. Terukur: 8 kata 10,1 dtk
saat pertama, **0,058 dtk** setelah cache (170× lebih cepat).

Rate limit 1,5 dtk/request — sopan ke server Badan Bahasa.

---

## 2. Scholar — `tools/scholar/`

Pencarian literatur ilmiah dari tiga sumber terbuka. **Tanpa API key.**

| Sumber | Kekuatan | Batasan |
|---|---|---|
| **OpenAlex** | 250jt+ karya, abstrak, kutipan | — |
| **Crossref** | Metadata DOI, kanonik untuk sitasi | rate limit 429 dari IP ini |
| **DOAJ** | Hanya open-access, terkurasi | koleksi lebih kecil |

| Tool | Fungsi |
|---|---|
| `scholar_search` | Cari di 3 sumber, dedupe DOI/judul |
| `scholar_doi` | Metadata lengkap satu DOI |
| `scholar_cite` | Sitasi gaya APA |
| `scholar_oa_only` | Hanya yang bisa diunduh penuh |

**Catatan penting:** ini pengganti Perplexity untuk jurnal, tapi cara kerjanya
beda. Perplexity merangkum dengan LLM berbayar; Scholar mengambil **metadata
akurat langsung dari sumbernya**. Untuk sitasi laporan, metadata justru yang
kamu butuhkan — bukan rangkuman.

**Sumber yang tidak dipakai:** Scopus, ScienceDirect, Web of Science — semua
berlangganan dan melarang scraping. Tidak ada jalur legal untuk mengaksesnya
tanpa langganan kampus.

Contoh hasil nyata: query `indonesian sentiment analysis` → 9 paper unik dari
3 sumber, lengkap dengan DOI, tahun, jumlah kutipan, status open-access.

### Saran query

OpenAlex bekerja paling baik dengan **bahasa Inggris**. Kalau topikmu
berbahasa Indonesia, terjemahkan dulu kata kuncinya:

```
"analisis sentimen" → "sentiment analysis"
"pengenalan wajah"  → "face recognition"
```

---

## 3. Parafrase Akademik — skill `parafrase-akademik`

Bukan MCP. Ini **skill** — aturan kerja yang saya ikuti saat kamu minta
parafrase.

**Kenapa skill, bukan MCP:** MCP parafrase hanya akan membungkus LLM dengan
LLM — biaya dobel, manfaat nol. Saya sudah bisa memparafrase; yang kurang
hanya aturannya. Dan masalah "tidak mau bolak-balik ke web" diselesaikan oleh
**alur** (baca file → parafrase → cek KBBI → diff → tulis balik), bukan oleh
tool tambahan.

### Alur

```
"parafrase bagian 3.2 laporan saya"
  → saya baca laporan.docx / .md langsung
  → parafrase kalimat per kalimat
  → cek tiap kata yang saya Ubah ke KBBI
  → tampilkan diff SEBELUM → SESUDAH
  → kamu setuju, baru saya tulis balik
```

### Tiga mode

| Mode | Untuk | Yang boleh diubah |
|---|---|---|
| **KONSERVATIF** (default) | Metodologi, hasil, kesimpulan | Struktur kalimat + sinonim aman. Istilah teknis, angka, sitasi utuh. |
| **MODERAT** | Tinjauan pustaka, pendahuluan | Gabung/pecah kalimat, ubah urutan. Fakta tetap. |
| **AGRESIF** | Abstrak, ringkasan | Tulis ulang penuh. Data tetap. |

### Aturan keras

Angka, satuan, nama metode, nama alat, sitasi `(Nama, 2024)`, tabel, gambar,
rumus — **tidak pernah diubah**. Parafrase tidak boleh menambah klaim.

### Batas jujur

- **Tidak** mengecek plagiarisme. Parafrase ≠ bebas plagiat; ide tetap
  harus disitasi.
- **Tidak** menjamin lolos Turnitin. Yang dicek hanya ejaan baku.
- Gambar sebelum/sesudah di README antislop itu khas QuillBot. Skill ini
  meniru **disiplinnya** (mode, aturan keras, diff), bukan mesinnya.

---

## Cara pakai di Hermes

Ketiga alat sudah terdaftar:

```bash
hermes mcp list          # kbbi, scholar
hermes skills list       # parafrase-akademik
```

Di sesi baru, `skills.auto_load` memuat `parafrase-akademik` + `antislop`
otomatis. Untuk sesi yang sedang berjalan: `/reload-mcp`.

Contoh perintah:

- *"cek ejaan paragraf ini pakai KBBI"*
- *"cari 5 paper tentang LSTM untuk deteksi plagiat, yang open access saja"*
- *"parafrase bagian metodologi, mode moderat"*
- *"buatkan sitasi APA dari DOI 10.1109/icaiit.2019.8834536"*
