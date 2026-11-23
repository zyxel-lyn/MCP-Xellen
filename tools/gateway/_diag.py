"""Diagnosa: apakah uvicorn benar-benar bind dan melayani."""

import asyncio
import socket
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import xellen_gateway as gw  # noqa: E402


async def main() -> int:
    cache = Path(r"D:\Wildanun Mukhallid\Project\MCP-Xellen\secrets\gateway-tokens.json")
    tokens = gw.TokenStore.load(cache)
    print("projects:", sorted(tokens.projects))

    python_exe = sys.executable
    ups = {name: gw.Upstream(name, spec, python_exe) for name, spec in gw.SERVERS.items()}
    for name, up in ups.items():
        await up.start()
        print(f"upstream {name}: {[t.name for t in up.tools]}")

    app = gw.build_app(tokens, ups, "probe")
    print("app routes:", [getattr(r, "path", r) for r in app.router.routes])

    # cek port bebas
    s = socket.socket()
    try:
        s.bind(("127.0.0.1", 8302))
        print("port 8302 bebas")
    except OSError as exc:
        print("port 8302 dipakai:", exc)
    finally:
        s.close()

    import uvicorn

    config = uvicorn.Config(app, host="127.0.0.1", port=8302, log_level="debug")
    server = uvicorn.Server(config)
    task = asyncio.create_task(server.serve())
    await asyncio.sleep(4)
    print("started flag:", server.started)
    if server.started:
        task.cancel()
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
