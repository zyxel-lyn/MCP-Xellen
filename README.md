# mcp-xellen-tools

Dua MCP server untuk kerja tulis-menulis akademik di Indonesia, dipasang lewat satu perintah `npx`.

| Server | Binari | Isi |
|---|---|---|
| **KBBI** | `mcp-kbbi` | KBBI VI Daring resmi: `kbbi_lookup`, `kbbi_check_baku`, `kbbi_batch`, `kbbi_syllables` |
| **Scholar** | `mcp-scholar` | Literatur ilmiah dari OpenAlex, Crossref, dan DOAJ: `scholar_search`, `scholar_cite`, `scholar_doi`, `scholar_oa_only` |

Sumber KBBI: `kbbi.kemendikdasmen.go.id` (domain lama `kbbi.kemdikbud.go.id` sudah tidak aktif).
Scholar tidak butuh API key; semuanya memakai endpoint publik.

## Pasang

```bash
npx mcp-xellen-tools check      # cek Python + paket mcp + hermes CLI
npx mcp-xellen-tools install    # daftarkan kbbi + scholar ke Hermes
npx mcp-xellen-tools install kbbi
npx mcp-xellen-tools where      # tampilkan path server
```

Butuh Node 18+ dan Python 3.10+ yang punya paket `mcp`
(`pip install mcp httpx beautifulsoup4`). Kalau Python ada di lokasi tidak biasa,
set `MCP_XELLEN_PYTHON` ke interpreter tersebut.

## Pakai di klien MCP lain

```json
{
  "mcpServers": {
    "kbbi": { "command": "npx", "args": ["-y", "-p", "mcp-xellen-tools", "mcp-kbbi"] },
    "scholar": { "command": "npx", "args": ["-y", "-p", "mcp-xellen-tools", "mcp-scholar"] }
  }
}
```

Contoh pemakaian: memeriksa bentuk baku (`analisa` vs `analisis`), memenggal suku kata,
mencari literatur akses terbuka, lalu menyusun sitasi APA dari DOI.

## Lisensi

MIT. Repo GitHub dengan dokumentasi gateway lengkap: <https://github.com/zyxel-lyn/MCP-Xellen>
