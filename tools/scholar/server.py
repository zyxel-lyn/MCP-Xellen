"""MCP server: pencarian literatur ilmiah (OpenAlex, Crossref, DOAJ).

Tools:
  scholar_search    - cari paper di beberapa sumber sekaligus
  scholar_doi       - ambil metadata lengkap satu DOI
  scholar_cite      - hasilkan sitasi APA dari DOI atau judul
  scholar_oa_only   - cari khusus open-access (bisa diunduh penuh)

Semua sumber gratis & terbuka. Tidak menyentuh Scopus/ScienceDirect.

Jalankan:
  python server.py                 # stdio untuk MCP client
  python -m tools.scholar.server --demo
"""

from __future__ import annotations

import json
import sys
from typing import Any

from .client import (
    Paper,
    get_by_doi,
    search_all,
    search_openalex,
)

INSTRUCTIONS = (
    "Pencarian literatur ilmiah dari sumber terbuka: OpenAlex (250jt+ karya), "
    "Crossref (metadata DOI), dan DOAJ (open-access terkurasi). Gratis, tanpa "
    "API key. Gunakan untuk mencari referensi laporan/skripsi dan membuat sitasi."
)

try:  # mcp v1
    from mcp.server.fastmcp import FastMCP as _Server  # type: ignore
    _API = "v1/FastMCP"
except ImportError:
    try:  # mcp v2
        from mcp.server.mcpserver import MCPServer as _Server  # type: ignore
        _API = "v2/MCPServer"
    except ImportError:
        _Server = None  # type: ignore[assignment]
        _API = "none"


def _brief(p: Paper, i: int) -> dict[str, Any]:
    return {
        "no": i,
        "judul": p.title,
        "tahun": p.year,
        "penulis": p.authors[:4] + (["..."] if len(p.authors) > 4 else []),
        "jurnal": p.venue,
        "doi": p.doi,
        "sitasi": p.citations,
        "open_access": p.open_access,
        "pdf": p.pdf_url,
        "sumber": p.source,
        "url": p.url,
    }


if _Server is not None:
    mcp = _Server("scholar", instructions=INSTRUCTIONS)

    @mcp.tool()
    def scholar_search(query: str, limit: int = 10, year_from: int = 0,
                       open_access_only: bool = False) -> str:
        """Cari paper ilmiah. query dalam bahasa Inggris biasanya lebih banyak hasil.

        limit: 1-50. year_from: 0 = semua tahun. open_access_only: hanya yang
        bisa diunduh penuh.
        """
        if open_access_only:
            papers = search_openalex(
                query, limit=limit, year_from=year_from or None, open_access_only=True
            )
            res = {
                "query": query,
                "total_per_source": {"openalex": len(papers)},
                "unique": len(papers),
                "papers": papers,
            }
        elif year_from:
            papers = search_openalex(query, limit=limit, year_from=year_from)
            res = {
                "query": query,
                "total_per_source": {"openalex": len(papers)},
                "unique": len(papers),
                "papers": papers,
            }
        else:
            res = search_all(query, limit=limit)

        out = {
            "query": res["query"],
            "per_sumber": res["total_per_source"],
            "total_unik": res["unique"],
            "hasil": [_brief(p, i + 1) for i, p in enumerate(res["papers"][:limit])],
        }
        return json.dumps(out, ensure_ascii=False, indent=2)

    @mcp.tool()
    def scholar_doi(doi: str) -> str:
        """Ambil metadata lengkap satu paper dari DOI. Termasuk abstrak."""
        p = get_by_doi(doi)
        if p is None:
            return f"DOI tidak ditemukan: {doi}"
        d = p.to_dict()
        d["sitasi_apa"] = p.citation_apa()
        return json.dumps(d, ensure_ascii=False, indent=2)

    @mcp.tool()
    def scholar_cite(doi: str) -> str:
        """Hasilkan sitasi gaya APA dari satu DOI."""
        p = get_by_doi(doi.strip().replace("https://doi.org/", ""))
        if p is None:
            return f"DOI tidak ditemukan: {doi}"
        return p.citation_apa()

    @mcp.tool()
    def scholar_oa_only(query: str, limit: int = 10) -> str:
        """Cari paper open-access saja — bisa diunduh full text tanpa langganan."""
        papers = search_openalex(query, limit=limit, open_access_only=True)
        out = {
            "query": query,
            "total": len(papers),
            "hasil": [_brief(p, i + 1) for i, p in enumerate(papers)],
        }
        return json.dumps(out, ensure_ascii=False, indent=2)


def main() -> None:
    if len(sys.argv) > 1 and sys.argv[1] == "--demo":
        res = search_all("indonesian sentiment analysis", limit=3)
        print("per sumber:", res["total_per_source"], "| unik:", res["unique"])
        for i, p in enumerate(res["papers"][:5], 1):
            print(f"\n{i}. {p.title[:75]}")
            print(f"   {p.year} | {p.venue} | kutipan {p.citations} | OA={p.open_access}")
            print(f"   DOI: {p.doi}")
        if res["papers"]:
            print("\nSitasi APA:", res["papers"][0].citation_apa()[:200])
        return

    if _Server is None:
        sys.exit("Paket 'mcp' belum terpasang. Jalankan: pip install mcp")

    print(f"[scholar-mcp] transport=stdio api={_API}", file=sys.stderr)
    mcp.run()


if __name__ == "__main__":
    main()
