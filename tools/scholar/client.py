"""Klien sumber literatur ilmiah — gratis, tanpa API key.

Sumber:
  - OpenAlex    : 250jt+ karya, primary. Tanpa key, rate limit longgar.
  - Crossref    : metadata DOI, kanonik untuk sitasi.
  - DOAJ        : hanya open-access, terkurasi.

Semua yang di sini terbuka dan gratis. Tidak ada scraping situs berbayar
(Scopus/ScienceDirect) — itu melanggar ToS dan tidak bisa diandalkan.
"""

from __future__ import annotations

import json
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field, asdict
from typing import Any

# Email untuk polite pool OpenAlex/Crossref. Ganti dengan emailmu kalau mau
# kuota lebih longgar: set env SCHOLAR_MAILTO.
import os

MAILTO = os.environ.get("SCHOLAR_MAILTO", "")

_UA = "mcpxellen-scholar/0.1 (https://github.com/zyxel-lyn/MCP-Xellen)"

CACHE_TTL_S = 3600  # cache pencarian 1 jam


@dataclass
class Paper:
    title: str
    year: int | None = None
    doi: str | None = None
    url: str | None = None
    authors: list[str] = field(default_factory=list)
    venue: str | None = None
    citations: int | None = None
    abstract: str | None = None
    open_access: bool = False
    source: str = ""
    pdf_url: str | None = None

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    def citation_apa(self) -> str:
        """Sitasi gaya APA ringkas."""
        if self.authors:
            if len(self.authors) == 1:
                who = self.authors[0]
            elif len(self.authors) <= 6:
                who = ", ".join(self.authors[:-1]) + ", & " + self.authors[-1]
            else:
                who = ", ".join(self.authors[:6]) + ", et al."
        else:
            who = "(tanpa penulis)"
        yr = f"({self.year})" if self.year else "(t.t.)"
        ven = f" *{self.venue}*." if self.venue else ""
        link = f" {self.url}" if self.url else ""
        return f"{who} {yr}. {self.title}.{ven}{link}"


_cache: dict[str, tuple[float, Any]] = {}


def _cache_get(key: str):
    hit = _cache.get(key)
    if not hit:
        return None
    ts, val = hit
    if time.time() - ts > CACHE_TTL_S:
        _cache.pop(key, None)
        return None
    return val


def _cache_put(key: str, val: Any) -> None:
    _cache[key] = (time.time(), val)


def _http_json(url: str, retries: int = 3, timeout: float = 25.0) -> dict:
    """GET JSON dengan retry untuk 429/5xx."""
    last: Exception | None = None
    for attempt in range(retries):
        try:
            req = urllib.request.Request(
                url,
                headers={"User-Agent": _UA, "Accept": "application/json"},
            )
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return json.loads(r.read())
        except urllib.error.HTTPError as e:
            last = e
            if e.code in (429, 500, 502, 503, 504):
                time.sleep(2 ** attempt)  # 1s, 2s, 4s
                continue
            raise
        except (urllib.error.URLError, TimeoutError) as e:
            last = e
            time.sleep(2 ** attempt)
    raise ConnectionError(f"gagal mengambil {url}: {last}")


def _with_mailto(url: str) -> str:
    if not MAILTO:
        return url
    sep = "&" if "?" in url else "?"
    return f"{url}{sep}mailto={urllib.parse.quote(MAILTO)}"


# ── OpenAlex ──────────────────────────────────────────────────────────────
def _openalex_to_paper(w: dict) -> Paper:
    # rekonstruksi abstrak dari inverted index (format OpenAlex)
    inv = w.get("abstract_inverted_index") or {}
    abstract = None
    if inv:
        positions: list[tuple[int, str]] = []
        for word, idxs in inv.items():
            for i in idxs:
                positions.append((i, word))
        abstract = " ".join(word for _, word in sorted(positions))[:2000]

    authors = []
    for a in (w.get("authorships") or [])[:12]:
        nm = (a.get("author") or {}).get("display_name")
        if nm:
            authors.append(nm)

    loc = (w.get("primary_location") or {})
    src = (loc.get("source") or {}) or {}
    oa = (w.get("open_access") or {})

    return Paper(
        title=(w.get("title") or w.get("display_name") or "").strip(),
        year=w.get("publication_year"),
        doi=(w.get("doi") or "").replace("https://doi.org/", "") or None,
        url=w.get("doi") or loc.get("landing_page_url"),
        authors=authors,
        venue=src.get("display_name"),
        citations=w.get("cited_by_count"),
        abstract=abstract,
        open_access=bool(oa.get("is_oa")),
        source="openalex",
        pdf_url=oa.get("oa_url"),
    )


def search_openalex(query: str, limit: int = 10, year_from: int | None = None,
                    open_access_only: bool = False) -> list[Paper]:
    key = f"oa:{query}:{limit}:{year_from}:{open_access_only}"
    cached = _cache_get(key)
    if cached is not None:
        return cached

    parts = [
        f"search={urllib.parse.quote(query)}",
        f"per_page={min(limit, 50)}",
        "sort=relevance_score:desc",
    ]
    filters = []
    if year_from:
        filters.append(f"from_publication_date:{year_from}-01-01")
    if open_access_only:
        filters.append("is_oa:true")
    if filters:
        parts.append("filter=" + ",".join(filters))

    url = _with_mailto("https://api.openalex.org/works?" + "&".join(parts))
    j = _http_json(url)
    papers = [_openalex_to_paper(w) for w in j.get("results", [])]
    _cache_put(key, papers)
    return papers


def get_by_doi(doi: str) -> Paper | None:
    """Ambil metadata satu paper dari DOI (OpenAlex)."""
    doi = doi.strip().replace("https://doi.org/", "")
    j = _http_json(_with_mailto(f"https://api.openalex.org/works/https://doi.org/{doi}"))
    if not j or j.get("error"):
        return None
    return _openalex_to_paper(j)


# ── Crossref ──────────────────────────────────────────────────────────────
def search_crossref(query: str, limit: int = 10) -> list[Paper]:
    key = f"cr:{query}:{limit}"
    cached = _cache_get(key)
    if cached is not None:
        return cached

    url = _with_mailto(
        "https://api.crossref.org/works?"
        f"query={urllib.parse.quote(query)}&rows={min(limit,20)}"
    )
    try:
        j = _http_json(url, retries=2)
    except ConnectionError:
        return []

    out: list[Paper] = []
    for it in (j.get("message") or {}).get("items", []):
        authors = []
        for a in (it.get("author") or [])[:12]:
            nm = " ".join(filter(None, [a.get("given"), a.get("family")]))
            if nm:
                authors.append(nm)
        out.append(
            Paper(
                title=((it.get("title") or [""])[0] or "").strip(),
                year=((it.get("issued") or {}).get("date-parts") or [[None]])[0][0],
                doi=it.get("DOI"),
                url=f"https://doi.org/{it.get('DOI')}" if it.get("DOI") else None,
                authors=authors,
                venue=((it.get("container-title") or [""])[0] or None),
                citations=it.get("is-referenced-by-count"),
                source="crossref",
            )
        )
    _cache_put(key, out)
    return out


# ── DOAJ ──────────────────────────────────────────────────────────────────
def search_doaj(query: str, limit: int = 10) -> list[Paper]:
    key = f"doaj:{query}:{limit}"
    cached = _cache_get(key)
    if cached is not None:
        return cached

    url = (
        "https://doaj.org/api/search/articles/"
        f"{urllib.parse.quote(query)}?pageSize={min(limit,20)}"
    )
    try:
        j = _http_json(url, retries=2)
    except ConnectionError:
        return []

    out: list[Paper] = []
    for r in j.get("results", []):
        b = r.get("bibjson") or {}
        ids = {i.get("type"): i.get("id") for i in (b.get("identifier") or [])}
        doi = ids.get("doi")
        links = b.get("link") or []
        pdf = next((l.get("url") for l in links if l.get("type") == "fulltext"), None)
        out.append(
            Paper(
                title=(b.get("title") or "").strip(),
                year=int(b["year"]) if str(b.get("year", "")).isdigit() else None,
                doi=doi,
                url=f"https://doi.org/{doi}" if doi else None,
                authors=[a.get("name") for a in (b.get("author") or []) if a.get("name")],
                venue=(b.get("journal") or {}).get("title"),
                citations=None,
                abstract=(b.get("abstract") or "")[:2000] or None,
                open_access=True,
                source="doaj",
                pdf_url=pdf,
            )
        )
    _cache_put(key, out)
    return out


# ── Gabungan ──────────────────────────────────────────────────────────────
def search_all(query: str, limit: int = 10, sources: list[str] | None = None) -> dict:
    """Cari di beberapa sumber, dedupe berdasarkan DOI/judul."""
    sources = sources or ["openalex", "crossref", "doaj"]
    buckets: dict[str, list[Paper]] = {}

    if "openalex" in sources:
        buckets["openalex"] = search_openalex(query, limit)
    if "crossref" in sources:
        buckets["crossref"] = search_crossref(query, limit)
    if "doaj" in sources:
        buckets["doaj"] = search_doaj(query, limit)

    seen: set[str] = set()
    merged: list[Paper] = []
    for src in sources:
        for p in buckets.get(src, []):
            key = (p.doi or p.title).lower().strip()
            if not key or key in seen:
                continue
            seen.add(key)
            merged.append(p)

    return {
        "query": query,
        "total_per_source": {k: len(v) for k, v in buckets.items()},
        "unique": len(merged),
        "papers": merged,
    }
