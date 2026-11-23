# ZAI ADAPTER REPORT — 13 November 2026

> **Bukti utama:** e2e test nyata (`logs/zai-adapter-e2e.txt`, run 04:02): **list 2 tools LOLOS · tools/call JSON LOLOS (`echo:zai-e2e-json`) · tools/call SSE LOLOS (`echo:zai-e2e-sse`) · wrong-token ditolak 401 LOLOS** — semua lewat native transport + project token, tanpa menyentuh production.

## 1. Masalah yang diselesaikan
Magic Prompt lama (±18 baris, Program.cs) memaksa Z.ai MENULIS client Python baru SETIAP sesi (variasi implementasi, rawan salah). Adapter menggantinya dengan **satu file client deterministik** yang sudah teruji.

## 2. Yang dibangun
| Komponen | File | Fungsi |
|---|---|---|
| Python adapter (stdlib only, tanpa pip install) | `tools/zai-adapter/zai_mcp_client.py` | initialize + session capture, notifications/initialized, tools/list, tools/call, **JSON + SSE parsing**, reconnect otomatis saat session expired/404, retry backoff, token tidak pernah dicetak |
| Demo transport harness | `Xellen/Transport/TransportDemo.cs` + mode `--transport-demo` | men-start native transport di sandbox test + project token untuk uji end-to-end tanpa menyentuh production |
| Generator paket di UI | tombol **Generate Z.ai Adapter Package** (tab Projects) | menyalin adapter + membuat `INSTRUKSI.md` berisi endpoint project & bootstrap instruction 5 baris (pengganti Magic Prompt) |
| Bootstrap template | `tools/zai-adapter/INSTRUKSI_TEMPLATE.md` | perbandingan langsung vs Magic Prompt lama |

## 3. Hasil e2e (run 04:02:25, exit 0 tiap langkah yang diharapkan)
```
--- MODE LIST ---      [adapter] tools tersedia: 2 (prefiks server benar)          → EXIT 0
--- MODE CALL json ---  echo:zai-e2e-json                                          → EXIT 0
--- MODE CALL sse ---   echo:zai-e2e-sse   (Accept text/event-stream, SSE parse)   → EXIT 0
--- NEGATIVE ---        HTTP 401 Unauthorized (wrong token ditolak)                → EXIT 1 (benar)
--- healthz demo ---    200
```

## 4. Bug nyata yang ditemukan & diperbaiki berkat e2e ini
1. **Adapter tidak menangkap `Mcp-Session-Id` dari header respons initialize** → request berikutnya 400 "missing session". FIX: `_post` menyimpan header sesi (SSE/JSON sama-sama berheader). Ini bug adapter, bukan transport — transport sudah benar (T37 membuktikan).
2. **TransportDemo crash saat rerun** (slug sudah dipakai di config sandbox persist) → kini idempotent: update-or-add server def + reuse/hapus-rebuild project + token fresh tiap run.
3. 2 stray GUI window dari binary lama (publish-p1 tanpa TransportDemo menolak argumen tak dikenal dan membuka UI) — ditutup by PID (path publish-p1), production 30704 tetap hidup.

## 5. Cara pakai (alur baru untuk Z.ai)
1. Tab Projects → pilih project → **Generate Z.ai Adapter Package** → folder `secrets\adapter-{slug}\` berisi `zai_mcp_client.py` + `INSTRUKSI.md`.
2. Upload 2 file ke sandbox Z.ai: `zai_mcp_client.py` + token file project.
3. Z.ai jalankan: `python zai_mcp_client.py --endpoint <endpoint> --token-file <token>.txt --mode list` lalu `--mode call --tool <serverId>__<tool> --args-json '{...}'`.
4. Reconnect/expiry ditangani adapter; token tidak pernah dicetak.

## 6. Batasan jujur
- Adapter **bukan integrasi native Z.ai** — tetap berjalan di sandbox Z.ai sebagai client HTTP; tanpa dukungan platform, file upload tetap diperlukan (PRD §5.5 jujur).
- Diuji terhadap **native transport localhost** (LAN). Jalur internet (Funnel) belum diuji dengan adapter ini — butuh route Funnel baru + persetujuan owner.
- `--mode call` pada Desktop Commander nyata via compat 8023 belum diuji dari adapter (DC aktif di compat; perizinan owner).
