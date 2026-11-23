"""Entry point MCP server Scholar (literatur ilmiah)."""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

# mode: dijalankan sebagai skrip langsung -> tambahkan parent agar "tools.scholar"
# bisa diimport; tapi server.py juga harus jalan berdiri sendiri.
PARENT = ROOT.parent
if str(PARENT) not in sys.path:
    sys.path.insert(0, str(PARENT))

try:
    from scholar.server import main  # noqa: E402
except ImportError:
    from tools.scholar.server import main  # noqa: E402

if __name__ == "__main__":
    main()
