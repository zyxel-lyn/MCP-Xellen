# MCP Xellen — Universal AI Gateway & Control Center

Gateway MCP untuk Windows: satu endpoint, banyak tool, akses terautentikasi
lewat HTTP, bisa dipakai agent lokal maupun remote (Z.ai Agent Mode via Funnel).

> **Status:** Xellen Core terverifikasi (40 PASS / 0 FAIL / 1 skipped pada
> 13 Nov 2026). Belum di-commit ke GitHub — kondisi terkini hanya di disk.

## Isi repository

```
MCP-Xellen/
├── MCP_XELLEN_PRD.md                      # product requirements
├── MCP_XELLEN_FULL_ARCHITECTURE_AUDIT.md  # audit arsitektur (47 KB)
├── MCP_XELLEN_MASTER_IMPLEMENTATION_PROMPT.md
├── P1_*.md                                # 4 laporan fase P1
├── docs/                                  # 16 dokumen (security, rollback, dll.)
├── desktop-commander-local/
│   └── app/                               # source C# Xellen Core + build
│       └── McpXellen.Desktop.csproj
├── MCP-Xellen-publication/                # repo publik (ter-commit s.d. 3 Nov)
├── tools/
│   ├── kbbi/                              # MCP KBBI (Python) — lihat di bawah
│   └── zai-adapter/                       # adapter Python untuk Z.ai
├── tests/                                 # regression + bukti (results.json)
├── backups/                               # 4 snapshot pra-perubahan
├── config/  logs/  secrets/               # runtime (jangan di-commit)
└── mcp-guard-stack/  construct3-mcp/      # komponen lain
```

## Tool: KBBI (`tools/kbbi/`)

MCP server untuk **KBBI VI Daring resmi** — sumber
`kbbi.kemendikdasmen.go.id` (domain lama `kbbi.kemdikbud.go.id` sudah mati /
NXDOMAIN sejak pemecahan Kementerian).

**Tools:** `kbbi_lookup`, `kbbi_check_baku`, `kbbi_batch`, `kbbi_syllables`

Jalankan mandiri:

```bash
cd tools/kbbi
pip install -r requirements.txt
python server.py                  # stdio untuk MCP client
python -m src.server --demo       # uji tanpa MCP
```

Terdaftar di Hermes sebagai MCP server `kbbi`:

```
command: <venv>/python.exe
args:    D:/Wildanun Mukhallid/Project/MCP-Xellen/tools/kbbi/server.py
```

Ciri parser yang berguna untuk laporan: menangkap **bentuk tidak baku**
(`analisis` → tidak baku: `analisa`) dan **lemma dasar** (`menganalisis` →
`analisis`) langsung dari judul entri KBBI.

Cache: `tools/kbbi/data/cache/` — satu kata diambil sekali, setelah itu instan.
Rate limit 1,5 detik/request ke server Badan Bahasa.

## Build & test Xellen Core

```bash
cd desktop-commander-local/app
dotnet build -c Release
dotnet publish -c Release -o publish-p1
./publish-p1/MCP-Xellen.exe --selftest
```

Prasyarat: .NET SDK 10.0.401 (terpasang di mesin ini), Node 22.12+.

## Pekerjaan yang masih terbuka

Dari `docs/INDEPENDENT_FINAL_EVIDENCE_REVIEW.md` §13–14:

1. **Keputusan owner:** aktifkan ulang Tailscale Funnel (saat audit:
   *"No serve config"* — jalur Z.ai via internet terputus); putuskan rotasi
   token browsermcp (rencana A/B di `docs/P0_SECURITY_HARDENING_RECOVERY_PLAN.md`)
2. **P2:** SSE resumability, token-bucket rate limit, audit-log auth failure
3. **Shadow-run Avalonia** manual (`--ui`) untuk membuka blokir T42 (UI smoke)
4. **tools/call parity** Desktop Commander lewat sesi Z.ai nyata
5. **Perbaikan minor:** FileVersion csproj kosong, duplikasi docs, detektor
   proses self-match
6. **Commit & push** hasil 11–13 Nov ke GitHub — repo publik masih di 3 Nov

## Riwayat

Folder ini sebelumnya `D:\MCP-Bridge`, dipindah ke sini dan `MCP-Bridge`
dihapus pada 23 Nov 2026. Tidak ada riwayat git di root lama — versi ter-commit
hanya di `MCP-Xellen-publication/` (commit terakhir 3 Nov).
