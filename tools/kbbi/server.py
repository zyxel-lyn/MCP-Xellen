"""Entry point MCP server KBBI.

Dijalankan sebagai skrip langsung (bukan `-m`), supaya tidak bergantung pada
cwd atau PYTHONPATH yang dipakai MCP client:

    python /path/to/kbbi-mcp/server.py
"""

from __future__ import annotations

import sys
from pathlib import Path

# Pastikan direktori repo ini ada di sys.path, apa pun cwd-nya.
ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from src.server import main  # noqa: E402

if __name__ == "__main__":
    main()
