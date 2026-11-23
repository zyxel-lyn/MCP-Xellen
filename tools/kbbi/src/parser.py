"""Parser halaman entri KBBI VI Daring (kbbi.kemendikdasmen.go.id).

Struktur yang diparsing (terverifikasi 2025):
    <h2 style="margin-bottom:3px">ru.mah </h2>
    <small ...><i>...</i></small><br/>
    <p><a href="...tesaurus...">Tesaurus</a></p>
    <ol>
      <li><font color="red"><i><span title="Nomina: kata benda">n</span></i></font>bangunan untuk tempat tinggal</li>
      ...
    </ol>

Halaman "tidak ditemukan" TIDAK memuat <h2> entri; sebaliknya ada teks
"tidak ditemukan" di body. Parser mengembalikan found=False untuk itu.
"""

from __future__ import annotations

import html
import re
from dataclasses import dataclass, field, asdict
from typing import Any

# Batas aman: kalau halaman memuat >200 definisi, sesuatu salah.
MAX_DEFINITIONS = 200


@dataclass
class Definition:
    text: str
    labels: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return {"text": self.text, "labels": self.labels}


@dataclass
class Entry:
    lemma: str                    # kata yang dicari, sudah dinormalisasi
    syllables: str = ""           # bentuk ber-titik: "ru.mah"
    definitions: list[Definition] = field(default_factory=list)
    tesaurus_url: str | None = None
    found: bool = False
    source_url: str = ""
    fetched_at: str = ""
    # KBBI menandai varian tidak baku langsung di judul entri, mis.
    #   "a.na.li.sis bentuk tidak baku: analisa"
    nonstandard: list[str] = field(default_factory=list)
    # untuk kata turunan: "analisis » meng.a.na.li.sis"
    root_lemma: str = ""

    def to_dict(self) -> dict[str, Any]:
        return {
            "lemma": self.lemma,
            "syllables": self.syllables,
            "definitions": [d.to_dict() for d in self.definitions],
            "tesaurus_url": self.tesaurus_url,
            "found": self.found,
            "source_url": self.source_url,
            "fetched_at": self.fetched_at,
            "nonstandard": self.nonstandard,
            "root_lemma": self.root_lemma,
        }


def _strip_tags(s: str) -> str:
    s = re.sub(r"<[^>]+>", "", s)
    return re.sub(r"\s+", " ", html.unescape(s)).strip()


def _extract_labels(li_html: str) -> list[str]:
    """Label kelas kata diambil dari title= atau teks <span> di dalam <i>."""
    labels: list[str] = []
    for m in re.finditer(r'<i>\s*<span[^>]*title="([^"]+)"[^>]*>([^<]*)</span>', li_html, re.I):
        title, inner = m.group(1), m.group(2)
        # "Nomina: kata benda" -> pakai kode pendek "n" kalau ada
        code = inner.strip()
        full = title.split(":")[0].strip()
        labels.append(code or full)
    if not labels:
        # fallback: <font><i>n</i></font>
        for m in re.finditer(r"<font[^>]*>\s*<i>\s*([^<]{1,20}?)\s*</i>\s*</font>", li_html, re.I):
            cand = m.group(1).strip()
            if cand:
                labels.append(cand)
    return labels


def parse_entry(page: str, lemma: str, source_url: str, fetched_at: str) -> Entry:
    entry = Entry(
        lemma=lemma.strip().lower(),
        source_url=source_url,
        fetched_at=fetched_at,
    )

    # --- deteksi "tidak ditemukan" ---
    # Halaman hasil kosong: ada teks "tidak ditemukan" / tidak ada <h2> entri.
    h2 = re.search(r'<h2[^>]*margin-bottom:\s*3px[^>]*>(.*?)</h2>', page, re.S | re.I)
    if not h2:
        return entry  # found tetap False

    syllables = _strip_tags(h2.group(1))
    if not syllables:
        return entry

    # Judul entri KBBI kadang memuat metadata tambahan:
    #   "a.na.li.sis bentuk tidak baku: analisa"
    #   "analisis » meng.a.na.li.sis bentuk tidak baku: menganalisa"
    # Ambil bagian yang benar-benar pemenggalan (deret huruf/titik).
    m_syl = re.search(r"[A-Za-z\u00C0-\u024F]+(?:\.[A-Za-z\u00C0-\u024F]+)+", syllables)
    if m_syl:
        entry.syllables = m_syl.group(0)
    else:
        # kata satu suku kata: "dan" (tanpa titik)
        m_plain = re.match(r"^([A-Za-z\u00C0-\u024F\-]{1,60})", syllables)
        if not m_plain:
            return entry
        entry.syllables = m_plain.group(1)

    # "bentuk tidak baku: analisa, analisa2" -> daftar varian tak baku
    m_nonstd = re.search(r"bentuk tidak baku\s*:\s*([^<]+)", syllables, re.I)
    if m_nonstd:
        raw = re.sub(r"[.;]$", "", m_nonstd.group(1)).strip()
        entry.nonstandard = [v.strip() for v in re.split(r"[,/]", raw) if v.strip()]

    # "analisis » meng.a.na.li.sis" -> lemma dasar
    m_root = re.match(r"^(.+?)\s*»", syllables)
    if m_root:
        entry.root_lemma = m_root.group(1).strip()

    entry.found = True

    # --- URL tesaurus (opsional) ---
    tes = re.search(r'href="(https://tesaurus\.[^"]+)"', page, re.I)
    if tes:
        entry.tesaurus_url = html.unescape(tes.group(1))

    # --- definisi: <ol> pertama setelah entri ---
    ol = re.search(r"<ol>(.*?)</ol>", page, re.S | re.I)
    if ol:
        items = re.findall(r"<li>(.*?)</li>", ol.group(1), re.S | re.I)
        for li in items[:MAX_DEFINITIONS]:
            labels = _extract_labels(li)
            # buang semua markup label sebelum mengambil teks definisi
            cleaned = re.sub(r"<font[^>]*>.*?</font>", "", li, flags=re.S | re.I)
            text = _strip_tags(cleaned)
            if text:
                entry.definitions.append(Definition(text=text, labels=labels))

    return entry


def looks_not_found(page: str) -> bool:
    """Deteksi eksplisit halaman 'tidak ditemukan'."""
    low = page.lower()
    return "tidak ditemukan" in low or "tidak ada entri" in low
