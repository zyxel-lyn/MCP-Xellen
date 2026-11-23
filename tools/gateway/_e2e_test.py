"""Uji end-to-end gateway: token, tools/list, tools/call, dan token salah.

Memakai klien MCP resmi (streamable HTTP), jadi yang diuji adalah jalur yang
benar-benar dipakai z.ai, bukan curl manual. API klien di SDK mcp 2.x bernama
`streamable_http_client` (1.x: `streamablehttp_client`).
"""

import asyncio
import json
import sys
from pathlib import Path

URL = "http://127.0.0.1:8300/mcp"
TOKENS = Path(r"D:\Wildanun Mukhallid\Project\MCP-Xellen\secrets\gateway-tokens.json")


def _text(result) -> str:
    parts = [getattr(c, "text", "") for c in (getattr(result, "content", []) or [])]
    return "\n".join(p for p in parts if p)


async def run(token: str, label: str) -> int:
    import httpx2

    from mcp.client.session import ClientSession
    from mcp.client.streamable_http import streamable_http_client

    print(f"\n--- {label} ---")
    try:
        async with httpx2.AsyncClient(
            headers={"Authorization": f"Bearer {token}"}, timeout=120
        ) as client:
            async with streamable_http_client(URL, http_client=client) as streams:
                read, write = streams[0], streams[1]
                async with ClientSession(read, write) as session:
                    await asyncio.wait_for(session.initialize(), timeout=30)
                    print("initialize: ok")

                    tools = await asyncio.wait_for(session.list_tools(), timeout=30)
                    names = [t.name for t in tools.tools]
                    print(f"tools/list: {len(names)} -> {', '.join(names)}")

                    lookup = await asyncio.wait_for(
                        session.call_tool("kbbi_lookup", {"kata": "analisis"}), timeout=90
                    )
                    print("kbbi_lookup('analisis'):")
                    print(_text(lookup)[:400] or "(kosong)")

                    baku = await asyncio.wait_for(
                        session.call_tool("kbbi_check_baku", {"kata": "analisa"}), timeout=90
                    )
                    print("kbbi_check_baku('analisa'):")
                    print(_text(baku)[:300] or "(kosong)")
        return 0
    except Exception as exc:  # noqa: BLE001
        print(f"GAGAL: {type(exc).__name__}: {exc}")
        return 1


async def main() -> int:
    projects = json.loads(TOKENS.read_text(encoding="utf-8"))["projects"]
    token = projects["zai-demo"]

    rc = await run(token, "token valid (zai-demo)")
    bad = await run("xel_token_palsu", "token salah")
    verdict = "LULUS" if rc == 0 and bad != 0 else "GAGAL"
    print(f"\nVERDICT: {verdict} (valid={rc}, salah={bad})")
    return 0 if verdict == "LULUS" else 1


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
