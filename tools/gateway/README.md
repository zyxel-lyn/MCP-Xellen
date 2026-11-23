# Gateway HTTP (tahap 2)

Membuka KBBI + Scholar lewat satu endpoint MCP Streamable HTTP dengan token
Bearer per project, supaya klien remote seperti z.ai bisa memakainya.

## Kenapa perlu

z.ai berjalan di internet dan tidak bisa men-spawn proses di komputer ini.
Ia hanya bisa memanggil URL. Gateway ini yang menjembatani: masuk lewat HTTP,
keluar sebagai stdio ke server Python yang sudah ada.

```
z.ai  ->  https://<host>:<port>/mcp  ->  gateway  ->  stdio kbbi / scholar
                     (Authorization: Bearer <token>)
```

## Jalankan

```bash
# 1. buat token untuk satu project (dicetak sekali, disimpan di secrets/)
python tools/gateway/xellen_gateway.py --issue zai-demo

# 2. jalankan gateway (default hanya 127.0.0.1)
python tools/gateway/xellen_gateway.py --port 8300

# 3. cek
curl -s http://127.0.0.1:8300/healthz
```

Butuh Python yang punya `mcp`, `starlette`, dan `uvicorn`. Kalau Python di PATH
bukan yang benar, tambahkan `--python <path-absolut>`.

Token disimpan di `secrets/gateway-tokens.json` (di-gitignore). Format:

```json
{ "projects": { "zai-demo": "xel_..." } }
```

## Endpoint

| Path | Auth | Isi |
|---|---|---|
| `/healthz` | tidak | status + daftar tool per upstream |
| `/mcp` | Bearer | Streamable HTTP MCP (initialize, tools/list, tools/call) |

Token juga diterima dalam bentuk query `?Authorization=<token>` untuk klien yang
hanya mendukung varian SSE.

## Yang sudah diuji (e2e nyata)

| Uji | Hasil |
|---|---|
| `initialize` dengan token valid | ok |
| `tools/list` | 8 tool (4 KBBI + 4 Scholar) |
| `tools/call kbbi_lookup {"kata":"analisis"}` | definisi + pemenggalan `a.na.li.sis` |
| `tools/call kbbi_check_baku {"kata":"analisa"}` | menjawab, tidak error |
| token salah `xel_token_palsu` | ditolak (401) |

Jalankan ulang: `python tools/gateway/_e2e_test.py` (gateway harus hidup).

## Catatan penting

- Default bind **loopback**. Jangan pakai `--host 0.0.0.0` atau Funnel sebelum
  token dirotasi dan diuji; endpoint publik bergantung penuh pada Bearer token.
- Skema tool diambil dari server asal (`input_schema` pada SDK mcp 2.x). Jangan
  mendaftarkan handler dengan `**arguments` tanpa menerapkan skema: MCPServer akan
  mempublikasikan satu parameter bernama `arguments` dan klien gagal memanggil.
- `_diag.py` dan `_e2e_test.py` adalah alat uji, bukan bagian runtime.
