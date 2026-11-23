# kbbi-mcp

MCP server untuk **KBBI VI Daring resmi** — sumber data langsung dari
`kbbi.kemendikdasmen.go.id` (Badan Pengembangan dan Pembinaan Bahasa,
Kementerian Pendidikan Dasar dan Menengah).

Dibuat untuk penulisan laporan TA/skripsi: cek ejaan baku, arti, kelas kata,
dan pemenggalan suku kata tanpa bolak-balik ke browser.

## Kenapa domain ini

Domain lama `kbbi.kemdikbud.go.id` sudah **NXDOMAIN** (diverifikasi via DNS
publik), akibat pemecahan Kementerian. Yang aktif sekarang:

| Domain | Status |
|---|---|
| `kbbi.kemdikbud.go.id` | ❌ mati |
| `kbbi.kemendikdasmen.go.id` | ✅ **aktif** — dipakai server ini |
| `badanbahasa.kemendikdasmen.go.id` | ✅ aktif |

## Cara kerja

```
lookup(kata) → cek cache JSON → (kalau belum) HTTP GET /entri/<kata> → parse → simpan cache
```

- **Satu kata diambil sekali**. Setelah itu dari cache, selamanya.
- **Jeda 1,5 detik** antar request ke server Badan Bahasa — sopan, tidak membebani.
- **Gagal jaringan tidak di-cache**, jadi bisa dicoba lagi nanti.
- Cache disimpan di `data/cache/<kata>.json`, mudah dibaca dan dihapus.

## Pasang

```bash
pip install -r requirements.txt
```

## Uji cepat (tanpa MCP)

```bash
python -m src.server --demo
```

Keluaran yang diharapkan:

```
rumah: found=True syllables='ru.mah'
   [n] bangunan untuk tempat tinggal
   [n] bangunan pada umumnya (seperti gedung)

analisis: found=True syllables='a.na.li.sis'
   [n] penyelidikan terhadap suatu peristiwa ...
```

## Daftarkan sebagai MCP server di Hermes

```bash
hermes mcp add kbbi --command "python" --args "-m,src.server" --cwd "D:/Wildanun Mukhallid/Project/ta-tools/kbbi-mcp"
```

Atau tambahkan manual ke `config.yaml`:

```yaml
mcp_servers:
  kbbi:
    command: python
    args: ["-m", "src.server"]
    cwd: "D:/Wildanun Mukhallid/Project/ta-tools/kbbi-mcp"
```

## Tools yang tersedia

| Tool | Fungsi | Contoh hasil |
|---|---|---|
| `kbbi_lookup` | Cari kata lengkap | `{kata, ditemukan, pemenggalan, definisi[], tesaurus, sumber}` |
| `kbbi_check_baku` | Cek bentuk baku (ringkas) | `BAKU: analisis (a.na.li.sis) — penyelidikan...` |
| `kbbi_batch` | Cek banyak kata sekaligus | `{total, tidak_ditemukan[], detail[]}` |
| `kbbi_syllables` | Pemenggalan saja | `ru.mah` |

## Yang ikut ter-parse dari halaman KBBI

Selain definisi dan kelas kata, parser juga menangkap dua metadata yang
sering luput padahal penting untuk laporan:

**1. Bentuk tidak baku** — KBBI menandainya langsung di judul entri:

```
a.na.li.sis bentuk tidak baku: analisa
```

→ masuk ke field `nonstandard: ["analisa"]`

**2. Lemma dasar kata turunan** — ditandai dengan `»`:

```
analisis » meng.a.na.li.sis
```

→ masuk ke field `root_lemma: "analisis"`

Contoh nyata: `menganalisis` → `root_lemma: "analisis"`,
`nonstandard: ["menganalisa"]`. Ini yang membuat tool ini berguna untuk
memeriksa ejaan bab metodologi.

## Struktur

```
kbbi-mcp/
├── src/
│   ├── parser.py      # HTML -> Entry dataclass
│   ├── client.py      # HTTP + cache + rate limit
│   ├── server.py      # MCP tools
│   └── __init__.py
├── data/
│   └── cache/         # hasil per-kata (dibuat otomatis)
├── requirements.txt
└── package.json
```

## Batasan & etika

- **Bukan API resmi.** Badan Bahasa tidak menyediakan API publik. Server ini
  membaca halaman web publik KBBI Daring — sama seperti yang dilakukan manusia
  lewat browser, hanya dengan cache supaya hemat request.
- **Pakai untuk rujukan, bukan menduplikasi database.** Untuk laporan TA,
  mengutip definisi KBBI dengan sitasi adalah praktik normal. Menyalin seluruh
  isi KBBI ke produk lain bukan.
- **Rate limit 1,5 detik** sengaja dipasang. Kalau kamu butuh ribuan kata,
  jalankan bertahap, bukan sekaligus.
- Struktur HTML KBBI bisa berubah sewaktu-waktu. Kalau parser mulai gagal,
  periksa `src/parser.py` — bagian yang paling rapuh adalah regex `<h2>` dan
  ekstraksi label kelas kata.

## Sitasi untuk laporan

> Badan Pengembangan dan Pembinaan Bahasa. (2016). *Kamus Besar Bahasa
> Indonesia Edisi VI (Daring)*. Kementerian Pendidikan Dasar dan Menengah.
> https://kbbi.kemendikdasmen.go.id
