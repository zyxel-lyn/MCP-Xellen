# PETA

Satu halaman untuk menjawab: "benda ini letaknya di mana, dan dipakai oleh apa?"

## Tiga benda, tiga rumah

| Benda | Apa itu | Letak | Dipakai oleh |
|---|---|---|---|
| **Skill** | Petunjuk kerja (cara melakukan sesuatu) | `C:\Users\user\AppData\Local\hermes\skills\` | Hermes saja |
| **MCP server** | Alat yang bisa dipanggil agent (KBBI, Scholar) | `D:\Wildanun Mukhallid\Project\MCP-Xellen\tools\` | klien mana pun yang didaftarkan |
| **Paket npm** | Kotak kirim agar orang lain bisa memasang alat | <https://www.npmjs.com/package/mcp-xellen-tools> | siapa pun |

Skill bukan bagian dari paket npm. npm hanya membawa alat.

## Alur: Hermes

```
Anda -> Hermes
          |- baca skill        (skills\parafrase-akademik, laporan-usk, artikel-ilmiah-pencarian)
          \- panggil alat      (tools\kbbi, tools\scholar  lewat MCP stdio)
```

Pendaftaran alat ke Hermes:

```bash
npx mcp-xellen-tools install hermes         # kbbi + scholar
hermes mcp test kbbi                        # harus "Connected"
```

## Alur: klien lokal lain

```
Anda jalankan installer -> installer tulis file konfigurasi -> klien baca file itu -> klien panggil Python lokal
```

| Klien | File yang ditulis |
|---|---|
| Claude Code | `.mcp.json` di folder kerja |
| Cursor | `.cursor/mcp.json` atau `~/.cursor/mcp.json` |
| VS Code | konfigurasi MCP global (`code --add-mcp`) |
| Codex CLI | `~/.codex/config.toml` |

Tidak ada skill di jalur ini. Hanya alat.

## Alur: klien remote (z.ai)

```
z.ai (di internet) -> HTTP + Authorization: Bearer -> gateway Xellen -> Python lokal
```

Belum tersedia. z.ai tidak bisa menjalankan proses di komputer ini, jadi butuh
gateway HTTP. Rencana: `install --remote <url>` setelah gateway terbukti hidup.

## Skill yang relevan untuk proyek ini

| Skill | Dipakai untuk |
|---|---|
| `parafrase-akademik` | parafrase laporan dengan validasi KBBI |
| `laporan-usk` | struktur dan format laporan USK |
| `artikel-ilmiah-pencarian` | mencari literatur dan sitasi APA |
| `hermes-agent` | mengurus MCP, config, dan MCP server Hermes |

Skill hidup di profil Hermes, bukan di repo ini. Menambah skill tidak mengubah
isi paket npm.

## Ringkasan satu baris

Hermes memegang skill **dan** alat; klien lain hanya memegang alat lewat file
konfigurasi; z.ai butuh gateway karena tidak bisa menjalankan alat di komputer ini.
