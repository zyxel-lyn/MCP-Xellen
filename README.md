# mcp-xellen-tools

Dua MCP server untuk kerja tulis-menulis akademik di Indonesia, dipasang lewat satu perintah `npx`.

| Server | Binari | Isi |
|---|---|---|
| **KBBI** | `mcp-kbbi` | KBBI VI Daring resmi: `kbbi_lookup`, `kbbi_check_baku`, `kbbi_batch`, `kbbi_syllables` |
| **Scholar** | `mcp-scholar` | Literatur ilmiah dari OpenAlex, Crossref, dan DOAJ: `scholar_search`, `scholar_cite`, `scholar_doi`, `scholar_oa_only` |

Sumber KBBI: `kbbi.kemendikdasmen.go.id` (domain lama `kbbi.kemdikbud.go.id` sudah tidak aktif).
Scholar tidak butuh API key; semuanya memakai endpoint publik.

## Pasang

Pilih klien tujuannya. Tanpa argumen, installer memasang ke Hermes Agent.

```bash
npx mcp-xellen-tools check                     # cek Python + paket mcp
npx mcp-xellen-tools install                   # Hermes Agent
npx mcp-xellen-tools install claude            # Claude Code  -> .mcp.json
npx mcp-xellen-tools install cursor            # Cursor       -> .cursor/mcp.json
npx mcp-xellen-tools install cursor --global   # Cursor global
npx mcp-xellen-tools install vscode            # VS Code      -> code --add-mcp
npx mcp-xellen-tools install codex             # Codex CLI    -> ~/.codex/config.toml
npx mcp-xellen-tools install print             # JSON mentah
```

Target dan tool bisa digabung:

```bash
npx mcp-xellen-tools install hermes kbbi       # Hermes, hanya KBBI
npx mcp-xellen-tools install claude cursor     # Claude Code + Cursor
npx mcp-xellen-tools install print scholar     # JSON, hanya Scholar
```

Butuh Node 18+ dan Python 3.10+ yang punya paket `mcp`
(`pip install mcp httpx beautifulsoup4`). Kalau Python ada di lokasi tidak biasa,
set `MCP_XELLEN_PYTHON` ke interpreter tersebut. Installer menyimpan **path
absolut** interpreter, karena klien MCP menjalankan server dengan environment
yang sudah difilter.

## Cara kerja per klien

| Target | Yang dilakukan installer | Verifikasi |
|---|---|---|
| `hermes` | `hermes mcp add` lalu `hermes mcp test` | harus "Connected" |
| `claude` | menulis `.mcp.json` di folder kerja | file ditulis, restart klien |
| `cursor` | menulis `.cursor/mcp.json` (atau `~/.cursor/mcp.json`) | file ditulis |
| `vscode` | `code --add-mcp` (konfigurasi MCP global VS Code) | exit code 0 |
| `codex` | menambah blok `[mcp_servers.kbbi]` di `~/.codex/config.toml` | blok yang sudah ada tidak ditimpa |
| `print` | mencetak JSON ke stdout | tempel manual |

Semua target memakai transport **stdio**: klien men-spawn Python lokal. Tidak ada
token, tidak ada port, tidak ada server yang perlu dihidupkan lebih dulu.

## Klien remote (z.ai)

z.ai dan klien berbasis web tidak bisa men-spawn proses lokal. Mereka memanggil
MCP lewat HTTP dengan header `Authorization`. Paket ini belum menyediakannya:
dibutuhkan gateway HTTP (MCP Xellen Core) yang mengekspos tool yang sama pada
endpoint `/mcp` dengan token per project. Detailnya di `GATEWAY.md`.

## Pakai tanpa installer

```json
{
  "mcpServers": {
    "kbbi": { "command": "<python-absolut>", "args": ["<paket>/tools/kbbi/server.py"] },
    "scholar": { "command": "<python-absolut>", "args": ["<paket>/tools/scholar/_entry.py"] }
  }
}
```

Path `<paket>` bisa dilihat dengan `npx mcp-xellen-tools where`.

Contoh pemakaian: memeriksa bentuk baku (`analisa` vs `analisis`), memenggal suku
kata, mencari literatur akses terbuka, lalu menyusun sitasi APA dari DOI.

## Lisensi

MIT. Repo GitHub dengan dokumentasi gateway lengkap: <https://github.com/zyxel-lyn/MCP-Xellen>
