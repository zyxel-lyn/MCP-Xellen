# Z.ai Adapter — bootstrap instruction TEMPLATE

Ganti nilai dalam <> saat memakai. File ini adalah PENGANTI Magic Prompt panjang.

---
Saya sudah mengupload 2 file: `zai_mcp_client.py` dan `<token-file>.txt`.
Gunakan adapter tersebut (stdlib Python, tanpa pip install) untuk berbicara dengan MCP Xellen.
JANGAN mencetak isi file token; token hanya dipakai sebagai header Authorization Bearer.

1. Koneksi + daftar tools (buktikan koneksi, jangan anggap berhasil dari health check):
   python zai_mcp_client.py --endpoint <ENDPOINT> --token-file <token-file>.txt --mode list
2. Memanggil tool:
   python zai_mcp_client.py --endpoint <ENDPOINT> --token-file <token-file>.txt --mode call --tool <serverId>__<toolName> --args-json '{"text":"contoh"}'
3. Jika adapter melaporkan session expired/404, jalankan ulang perintah yang sama — adapter melakukan reconnect otomatis.

Batasan: endpoint hanya dapat dijangkau dari localhost kecuali Remote Access (public tunnel) diaktifkan owner. Jangan mengirim token ke domain lain.
---

## Perbandingan dengan Magic Prompt lama
| | Magic Prompt lama | Adapter package |
|---|---|---|
| Panjang instruksi agent | ±18 baris detail protokol | 5 baris |
| Client | ditulis manual agent tiap sesi (`scripts/mcp_xellen_client.py`) | file adapter siap-upload (deterministik, sudah teruji) |
| SSE/session/reconnect | implementasi per sesi (variasi) | satu implementasi teruji |
| Token | file upload (tetap) | file upload (tetap) — tidak bisa dihilangkan tanpa dukungan platform Z.ai |
