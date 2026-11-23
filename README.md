# MCP Xellen

**Universal AI Gateway & Control Center untuk Windows** — satu endpoint MCP
terautentikasi, banyak tool, bisa dipakai agent lokal maupun remote.

MCP Xellen menjalankan server MCP (Desktop Commander dan lainnya) lewat STDIO,
membungkusnya jadi **Streamable HTTP**, mengikat gateway ke loopback, dan
melindunginya dengan Bearer token yang dibuat per instalasi. Endpoint bisa
dipublikasikan lewat Tailscale Funnel supaya agent remote seperti Z.ai
Website Agent Mode bisa menjangkaunya.

> **Status:** Xellen Core terverifikasi (40 PASS / 0 FAIL / 1 SKIPPED,
> 13 Nov 2026). Belum ada release binary — masih source build.

---

## Daftar isi

- [Arsitektur](#arsitektur)
- [Komponen](#komponen)
- [Tool bawaan](#tool-bawaan)
- [Prasyarat](#prasyarat)
- [Build \& test](#build--test)
- [Tailscale Funnel](#tailscale-funnel)
- [Z.ai Website Agent Mode](#zai-website-agent-mode)
- [Verifikasi lokal](#verifikasi-lokal)
- [Model keamanan](#model-keamanan)
- [Troubleshooting](#troubleshooting)
- [Pekerjaan terbuka](#pekerjaan-terbuka)

---

## Arsitektur

```text
Agent remote (Z.ai Agent Mode) / agent lokal
        |
        | HTTPS + Authorization: Bearer <token>
        v
Tailscale Funnel :8443            (opsional, untuk akses internet)
        |
        v
127.0.0.1:8023/mcp                (gateway, hanya loopback)
        |
        v
Supergateway (STDIO -> Streamable HTTP)   atau   Native transport :8300
        |
        | STDIO
        v
MCP server backend
        |
        v
File, proses, dan terminal user Windows
```

Ini **gateway lokal**, bukan layanan cloud. Komputer harus menyala, tidak
sleep, dan online agar endpoint remote bisa melayani request.

## Komponen

```
MCP-Xellen/
├── desktop-commander-local/app/     # Xellen Core (C# .NET 10)
│   ├── McpXellen.Desktop.csproj
│   ├── Program.cs
│   └── Xellen/
│       ├── Core/          # Registry, ProcessHost, AuthStore, EventBus
│       ├── Transport/     # native Streamable HTTP + Supergateway parity
│       ├── SelfTest/      # 41 test regresi
│       ├── UI/            # WinForms launcher
│       └── Browser/       # Browser manager (P3, belum selesai)
├── tools/
│   ├── kbbi/              # MCP server KBBI VI Daring (Python)
│   ├── scholar/           # MCP server literatur ilmiah (OpenAlex/Crossref/DOAJ)
│   ├── zai-adapter/       # adapter Python untuk Z.ai
│   └── README-TA-TOOLS.md # ringkasan KBBI + Scholar + skill parafrase
├── docs/                  # 17 dokumen: arsitektur, security, rollback, audit
├── tests/                 # regression + bukti hasil (results.json)
├── MCP_XELLEN_PRD.md      # product requirements
├── MCP_XELLEN_FULL_ARCHITECTURE_AUDIT.md
├── P1_*.md                # 4 laporan fase P1
└── MCP-Xellen-publication/  # paket publikasi awal (WinForms + installer)
```

**Xellen Core** terdiri dari 25 file C#:

| Modul | Isi |
|---|---|
| `Core/` | Process lifecycle, registry v2, AuthStore (TTL + rotate), EventBus, redaction, rate limit |
| `Transport/` | Native Streamable HTTP (health, session, tools/list, tools/call, 401/403) + parity dengan Supergateway |
| `SelfTest/` | 41 test: lifecycle T1–T16, WMI T17–T32, auth T33, redaction T34, eventbus T35, registry T36, transport T37, isolation T39, rate limit T40, parity T41, UI smoke T42 |
| `UI/` | WinForms launcher (Verify Gateway, Copy Z.ai Prompt, Locate Token) |

## Tool bawaan

### KBBI — `tools/kbbi/`

MCP server untuk **KBBI VI Daring resmi** (`kbbi.kemendikdasmen.go.id`).
Domain lama `kbbi.kemdikbud.go.id` sudah mati (NXDOMAIN) sejak pemecahan
kementerian.

| Tool | Fungsi |
|---|---|
| `kbbi_lookup` | Arti lengkap + kelas kata + pemenggalan |
| `kbbi_check_baku` | Cek bentuk baku (ringkas) |
| `kbbi_batch` | Cek banyak kata sekaligus |
| `kbbi_syllables` | Pemenggalan suku kata |

Selain definisi, parser menangkap metadata yang penting untuk penulisan formal:

- **bentuk tidak baku** — `analisis` → tidak baku: `analisa`
- **lemma dasar** — `menganalisis` → dasar: `analisis`

```bash
cd tools/kbbi
pip install -r requirements.txt
python server.py                 # stdio untuk MCP client
python -m src.server --demo      # uji tanpa MCP
```

Cache di `data/cache/` — satu kata diambil sekali (10,1 dtk → 0,058 dtk untuk
8 kata), rate limit 1,5 dtk/request ke server Badan Bahasa.

### Scholar — `tools/scholar/`

Pencarian literatur ilmiah dari tiga sumber terbuka, **tanpa API key**:

| Sumber | Kekuatan |
|---|---|
| **OpenAlex** | 250jt+ karya, abstrak, hitungan kutipan |
| **Crossref** | metadata DOI, kanonik untuk sitasi |
| **DOAJ** | hanya open-access, terkurasi |

| Tool | Fungsi |
|---|---|
| `scholar_search` | Cari di 3 sumber, dedupe DOI/judul |
| `scholar_doi` | Metadata lengkap satu DOI |
| `scholar_cite` | Sitasi gaya APA |
| `scholar_oa_only` | Hanya yang bisa diunduh penuh |

```bash
cd tools/scholar
python server.py                 # stdio untuk MCP client
python -m tools.scholar.server --demo
```

Ini pengganti Perplexity untuk jurnal, tapi cara kerjanya berbeda: Perplexity
merangkum dengan LLM berbayar, Scholar mengambil **metadata akurat langsung
dari sumbernya**. Untuk sitasi laporan, metadata justru yang dibutuhkan.

OpenAlex bekerja paling baik dengan **bahasa Inggris** — terjemahkan kata kunci
Indonesia dulu (`analisis sentimen` → `sentiment analysis`).

Semua tool punya retry untuk 429/5xx dan cache pencarian 1 jam. Isi
`SCHOLAR_MAILTO` dengan emailmu untuk kuota OpenAlex/Crossref yang lebih longgar.

**Tidak dipakai:** Scopus, ScienceDirect, Web of Science — berlangganan dan
melarang scraping. Tidak ada jalur legal tanpa langganan kampus.

### Parafrase akademik — skill, bukan MCP

Aturan kerja untuk memparafrase teks laporan langsung di tempat: baca file →
parafrase kalimat per kalimat → cek kata yang diubah ke KBBI → tampilkan diff →
tulis balik setelah disetujui. Tiga mode: **KONSERVATIF** (default),
**MODERAT**, **AGRESIF**.

Sengaja bukan MCP: MCP parafrase hanya membungkus LLM dengan LLM — biaya
dobel, manfaat nol. Yang kurang bukan alat, tapi aturan.

### Aceh Camera MCP Bridge

Bridge project-scoped dengan 8 tool terbatas untuk repo
`C:\laragon\www\aceh-camera`. Detail lengkap di
[`docs/ACEH-CAMERA-MCP-BRIDGE-README.md`](docs/ACEH-CAMERA-MCP-BRIDGE-README.md).

## Prasyarat

| Komponen | Versi |
|---|---|
| OS | Windows 10/11 x64 |
| .NET SDK | 10.0.x (untuk build dari source) |
| .NET Desktop Runtime | 10.x (untuk app framework-dependent) |
| Node.js | 22.12.0+ |
| Tailscale | Client Windows, sudah login (opsional) |
| Python | 3.11+ (untuk `tools/kbbi` dan `tools/scholar`) |

Dependensi runtime yang dipin oleh paket publikasi:
`@wonderwhy-er/desktop-commander@0.3.1`, `supergateway@4.2.1`.

## Build & test

```bash
cd desktop-commander-local/app
dotnet build -c Release
dotnet publish -c Release -o publish-p1
./publish-p1/MCP-Xellen.exe --selftest
```

Hasil test ditulis ke `tests/p1/results.json`. Run terakhir
(13 Nov 2026, exit 0):

```
40 PASS · 0 FAIL · 1 SKIPPED (T42 UI smoke, headless)
```

Detail per kelompok ada di [`docs/TEST_REPORT.md`](docs/TEST_REPORT.md).
Verifikasi independen atas klaim sesi implementasi ada di
[`docs/INDEPENDENT_FINAL_EVIDENCE_REVIEW.md`](docs/INDEPENDENT_FINAL_EVIDENCE_REVIEW.md).

## Tailscale Funnel

Gateway mendengarkan di `127.0.0.1:8023` (native transport `:8300` opt-in).
Untuk membuka akses internet:

```bash
tailscale funnel --bg --https=8443 http://127.0.0.1:8023
tailscale funnel status
```

Salin hostname HTTPS yang muncul, endpoint-nya:

```
https://YOUR-DEVICE.YOUR-TAILNET.ts.net:8443/mcp
```

Matikan listener saja (tanpa mengganggu Funnel lain):

```bash
tailscale funnel --https=8443 off
```

Jangan pakai `tailscale funnel reset` kalau ada layanan Funnel lain di device
yang sama.

> **Catatan 13 Nov:** saat audit terakhir, `tailscale funnel status` melaporkan
> *"No serve config"* — jalur Z.ai via internet dalam kondisi terputus dan
> perlu diaktifkan ulang.

## Z.ai Website Agent Mode

Alur ini untuk **website Z.ai**, bukan ZCode atau client desktop.

1. Pastikan gateway hidup dan statusnya sehat.
2. Pastikan endpoint publik bisa dijangkau lewat Funnel.
3. Klik **Copy Z.ai Magic Prompt** di aplikasi.
4. Buka [chat.z.ai](https://chat.z.ai/) → Agent Mode.
5. Tempel prompt tadi.
6. Klik **Locate Token File**, unggah `mcp-xellen-token.txt` ke sesi tersebut.
7. Biarkan agent membaca token tanpa mencetaknya.
8. Agent harus menjalankan `initialize`, `notifications/initialized`, lalu
   `tools/list` — pastikan daftar tool muncul sebelum memanggil tool apa pun.

Ini workflow **prompt-driven HTTP client**, bukan konektor MCP native Z.ai.
Ketersediaannya bergantung pada Agent Mode yang masih mendukung Python/Bash
dan upload file.

## Verifikasi lokal

Klik **Verify Gateway** di aplikasi. Sukses berarti:

- `GET http://127.0.0.1:8023/healthz` mengembalikan respons sukses.
- POST MCP tanpa Bearer token ditolak **HTTP 401**.

Ini **tidak** membuktikan koneksi Z.ai berhasil. Hanya handshake MCP dan
`tools/list` yang membuktikan koneksi end-to-end.

## Model keamanan

- Gateway mengikat ke loopback (`127.0.0.1`), bukan semua interface.
- Request MCP wajib Bearer token acak 256-bit per instalasi.
- Token disimpan lokal (`%LOCALAPPDATA%\MCP Xellen\secrets\`) dan
  **dikecualikan dari Git**.
- Tailscale Funnel menyediakan transport HTTPS publik; Bearer token tetap
  menjadi kredensial tingkat aplikasi.
- Token memberi akses ke semua tool backend yang diekspos.
- Desktop Commander berjalan dengan izin akun Windows yang meluncurkannya.

> **Penting:** instruksi di prompt untuk meminta konfirmasi sebelum mengubah
> file, menghapus, commit, atau push adalah **panduan perilaku**, bukan batas
> persetujuan yang ditegakkan server. Jangan menganggapnya sandbox. Prompt
> injection atau client lain bisa memanggil tool tanpa mengikuti instruksi itu.

Untuk isolasi lebih kuat: pakai akun Windows terpisah, atau batasi direktori
yang diizinkan Desktop Commander sendiri.

Rencana hardening dan rotasi token ada di
[`docs/P0_SECURITY_HARDENING_RECOVERY_PLAN.md`](docs/P0_SECURITY_HARDENING_RECOVERY_PLAN.md).

## Troubleshooting

| Gejala | Periksa |
|---|---|
| App bilang komponen hilang | Jalankan ulang installer; cek Node.js dan file token |
| Gateway tidak mau start | `%LOCALAPPDATA%\MCP Xellen\logs\mcp-xellen.log` |
| Health check lokal gagal | App terbuka? port 8023 dipakai proses lain? |
| Funnel tidak tersedia | `tailscale status` dan `tailscale funnel status`; cek policy tailnet |
| Z.ai dapat HTTP 401 | Upload file token yang cocok; jangan pakai token instalasi lain |
| Z.ai tidak bisa menjangkau endpoint | Laptop menyala, online, Funnel aktif, URL berakhiran `:8443/mcp` |
| Sesi kedaluwarsa | Jalankan ulang handshake `initialize` untuk sesi ID baru |

**Hibernate/sleep:** gateway dan Funnel tidak bisa melayani selama Windows
suspended. Setelah resume, tunggu Wi-Fi dan Tailscale tersambung, lalu pastikan
MCP Xellen jalan. Endpoint dan token tidak perlu dibuat ulang.

## Pekerjaan terbuka

Dari [`docs/INDEPENDENT_FINAL_EVIDENCE_REVIEW.md`](docs/INDEPENDENT_FINAL_EVIDENCE_REVIEW.md) §13–14:

1. **Keputusan owner** — aktifkan ulang Tailscale Funnel; putuskan rotasi token
   browsermcp (rencana A/B di `docs/P0_SECURITY_HARDENING_RECOVERY_PLAN.md`)
2. **P2** — SSE resumability, token-bucket rate limit, audit-log auth failure
3. **Shadow-run Avalonia** manual (`--ui`) untuk membuka blokir T42 (UI smoke)
4. **tools/call parity** Desktop Commander lewat sesi Z.ai nyata
5. **Perbaikan minor** — FileVersion csproj kosong, duplikasi docs, detektor
   proses self-match
6. **Browser Manager (P3)** — belum diimplementasi

## Lisensi

MIT. Komponen pihak ketiga tetap di bawah lisensinya masing-masing — lihat
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

MCP Xellen mengintegrasikan, tetapi tidak menggantikan atau mengklaim karya atas:

- [Desktop Commander](https://github.com/wonderwhy-er/DesktopCommanderMCP)
- [Supergateway](https://github.com/supercorp-ai/supergateway)
- [Tailscale](https://tailscale.com/)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [KBBI VI Daring](https://kbbi.kemendikdasmen.go.id) — Badan Pengembangan dan
  Pembinaan Bahasa
