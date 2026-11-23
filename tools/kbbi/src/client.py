"""Klien KBBI resmi dengan cache JSON per-kata.

Prinsip:
- Satu kata diambil SEKALI dari server resmi, lalu disimpan di data/cache/.
- Request diberi jeda (rate limit) supaya tidak membebani server Badan Bahasa.
- Kalau jaringan mati, cache dipakai apa adanya.
- Kegagalan jaringan TIDAK di-cache (supaya bisa dicoba lagi nanti).
"""

from __future__ import annotations

import json
import os
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

from .parser import Entry, parse_entry

BASE = "https://kbbi.kemendikdasmen.go.id"
ENTRY_PATH = "/entri/{word}"

# Sopan terhadap server: minimal 1.5 detik antar request.
MIN_INTERVAL_S = 1.5

_UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/120.0 Safari/537.36"
)

_last_request_at = 0.0


def _cache_dir() -> Path:
    env = os.environ.get("KBBI_CACHE_DIR")
    if env:
        return Path(env)
    return Path(__file__).resolve().parent.parent / "data" / "cache"


def _cache_path(word: str) -> Path:
    # nama file aman: huruf/angka/_/-, sisanya di-encode
    safe = urllib.parse.quote(word.lower().strip(), safe="")
    return _cache_dir() / f"{safe}.json"


def _throttle() -> None:
    global _last_request_at
    delta = time.monotonic() - _last_request_at
    if delta < MIN_INTERVAL_S:
        time.sleep(MIN_INTERVAL_S - delta)
    _last_request_at = time.monotonic()


def _read_cache(word: str) -> Entry | None:
    p = _cache_path(word)
    if not p.exists():
        return None
    try:
        raw = json.loads(p.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return None
    e = Entry(
        lemma=raw.get("lemma", word),
        syllables=raw.get("syllables", ""),
        found=raw.get("found", False),
        tesaurus_url=raw.get("tesaurus_url"),
        source_url=raw.get("source_url", ""),
        fetched_at=raw.get("fetched_at", ""),
    )
    from .parser import Definition

    e.definitions = [
        Definition(text=d.get("text", ""), labels=d.get("labels", []))
        for d in raw.get("definitions", [])
    ]
    return e


def _write_cache(entry: Entry) -> None:
    d = _cache_dir()
    d.mkdir(parents=True, exist_ok=True)
    _cache_path(entry.lemma).write_text(
        json.dumps(entry.to_dict(), ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


def _fetch(word: str, timeout: float = 20.0) -> str:
    url = BASE + ENTRY_PATH.format(word=urllib.parse.quote(word.lower().strip()))
    _throttle()
    req = urllib.request.Request(
        url,
        headers={"User-Agent": _UA, "Accept": "text/html,application/xhtml+xml"},
    )
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read().decode("utf-8", "ignore")


def lookup(word: str, use_cache: bool = True) -> Entry:
    """Cari satu kata. Urutan: cache -> jaringan -> cache lama (kalau gagal)."""
    word = word.strip().lower()
    if not word:
        raise ValueError("kata tidak boleh kosong")

    if use_cache:
        cached = _read_cache(word)
        if cached is not None:
            return cached

    now = datetime.now(timezone.utc).isoformat(timespec="seconds")
    url = BASE + ENTRY_PATH.format(word=urllib.parse.quote(word))

    try:
        page = _fetch(word)
    except (urllib.error.URLError, TimeoutError, OSError) as exc:
        # Jaringan gagal: jangan cache, kembalikan penanda offline.
        stale = _read_cache(word)
        if stale is not None:
            setattr(stale, "stale", True)
            return stale
        raise ConnectionError(f"gagal menghubungi KBBI: {exc}") from exc

    entry = parse_entry(page, word, url, now)

    # Hanya cache hasil yang valid (ditemukan ATAU benar-benar tidak ada).
    if entry.found or "tidak ditemukan" in page.lower():
        _write_cache(entry)

    return entry


def lookup_many(words: list[str], use_cache: bool = True) -> list[Entry]:
    out: list[Entry] = []
    for w in words:
        try:
            out.append(lookup(w, use_cache=use_cache))
        except ConnectionError:
            out.append(Entry(lemma=w, found=False, source_url=""))
    return out
