"""MCP server: KBBI VI Daring (sumber resmi Badan Bahasa).

Tools:
  kbbi_lookup      - cari satu kata, kembalikan definisi + kelas kata
  kbbi_check_baku  - cek apakah kata ditemukan di KBBI (untuk validasi ejaan)
  kbbi_batch       - cek banyak kata sekaligus (untuk audit laporan)
  kbbi_syllables   - pemenggalan suku kata saja

Jalankan:
  python server.py                 # stdio (untuk MCP client)
  python -m src.server --demo      # uji cepat tanpa MCP

Kompatibel dengan mcp v1 (`FastMCP`) dan v2 (`MCPServer`).
"""

from __future__ import annotations

import json
import sys
from typing import Any

# --- dukungan mcp v1 & v2 ------------------------------------------------
try:  # mcp v1.x
    from mcp.server.fastmcp import FastMCP as _Server  # type: ignore
    _API = "v1/FastMCP"
except ImportError:
    try:  # mcp v2.x
        from mcp.server.mcpserver import MCPServer as _Server  # type: ignore
        _API = "v2/MCPServer"
    except ImportError:
        _Server = None  # type: ignore[assignment]
        _API = "none"

from .client import lookup, lookup_many

INSTRUCTIONS = (
    "Kamus Besar Bahasa Indonesia (KBBI VI Daring), sumber resmi "
    "Badan Pengembangan dan Pembinaan Bahasa. Gunakan untuk memeriksa ejaan baku, "
    "arti, kelas kata, dan pemenggalan suku kata pada penulisan formal/laporan."
)


def _fmt(entry) -> dict[str, Any]:
    return {
        "kata": entry.lemma,
        "ditemukan": entry.found,
        "pemenggalan": entry.syllables,
        "definisi": [
            {"teks": d.text, "kelas_kata": d.labels} for d in entry.definitions
        ],
        "bentuk_tidak_baku": entry.nonstandard,
        "lemma_dasar": entry.root_lemma,
        "tesaurus": entry.tesaurus_url,
        "sumber": entry.source_url,
    }


if _Server is not None:
    mcp = _Server("kbbi", instructions=INSTRUCTIONS)

    @mcp.tool()
    def kbbi_lookup(kata: str) -> str:
        """Cari satu kata di KBBI. Kembalikan arti, kelas kata, dan pemenggalan."""
        entry = lookup(kata)
        return json.dumps(_fmt(entry), ensure_ascii=False, indent=2)

    @mcp.tool()
    def kbbi_check_baku(kata: str) -> str:
        """Cek apakah kata ada di KBBI (indikasi bentuk baku). Ringkas."""
        entry = lookup(kata)
        if entry.found:
            arti = entry.definitions[0].text if entry.definitions else "(tanpa definisi)"
            extra = ""
            if entry.nonstandard:
                extra = f" | bentuk tidak baku: {', '.join(entry.nonstandard)}"
            return f"BAKU: {entry.lemma} ({entry.syllables}) — {arti}{extra}"
        return f"TIDAK DITEMUKAN di KBBI: {kata} (mungkin tidak baku atau salah eja)"

    @mcp.tool()
    def kbbi_batch(kata: list[str]) -> str:
        """Cek banyak kata sekaligus. Cocok untuk audit ejaan satu bab laporan."""
        entries = lookup_many(kata)
        hasil = [
            {"kata": e.lemma, "ditemukan": e.found, "tidak_baku": e.nonstandard}
            for e in entries
        ]
        tidak = [h["kata"] for h in hasil if not h["ditemukan"]]
        return json.dumps(
            {
                "total": len(hasil),
                "tidak_ditemukan": tidak,
                "detail": hasil,
            },
            ensure_ascii=False,
            indent=2,
        )

    @mcp.tool()
    def kbbi_syllables(kata: str) -> str:
        """Pemenggalan suku kata menurut KBBI (mis. 'rumah' -> 'ru.mah')."""
        entry = lookup(kata)
        if not entry.found:
            return f"tidak ditemukan: {kata}"
        return entry.syllables


def main() -> None:
    if len(sys.argv) > 1 and sys.argv[1] == "--demo":
        for w in ["rumah", "analisis", "ngga", "menganalisis"]:
            e = lookup(w)
            print(f"\n{w}: found={e.found} syllables={e.syllables!r}")
            if e.nonstandard:
                print(f"   bentuk tidak baku: {e.nonstandard}")
            if e.root_lemma:
                print(f"   lemma dasar: {e.root_lemma}")
            for d in e.definitions[:2]:
                print(f"   [{','.join(d.labels)}] {d.text[:110]}")
        return

    if _Server is None:
        sys.exit("Paket 'mcp' belum terpasang. Jalankan: pip install mcp")

    print(f"[kbbi-mcp] transport=stdio api={_API}", file=sys.stderr)
    mcp.run()  # stdio transport


if __name__ == "__main__":
    main()
