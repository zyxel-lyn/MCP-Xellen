"""Gateway HTTP untuk MCP Xellen tools (tahap 2).

Mengekspos KBBI + Scholar lewat satu endpoint Streamable HTTP dengan token Bearer
per project, supaya klien remote seperti z.ai bisa memanggilnya.

    # 1. buat token untuk satu project (dicetak sekali, disimpan di secrets/)
    python tools/gateway/xellen_gateway.py --issue zai-demo

    # 2. jalankan gateway (default hanya loopback)
    python tools/gateway/xellen_gateway.py --port 8300

    # 3. uji
    curl -s -H "Authorization: Bearer <token>" http://127.0.0.1:8300/healthz

Arsitektur: gateway mengambil daftar tool dari tiap server stdio saat start,
lalu meneruskan tools/call ke server asalnya. Tidak ada logika bisnis di sini;
semua tetap di tools/kbbi dan tools/scholar.

Kenapa Python, bukan MCP-Xellen Core (C#): alur yang dibutuhkan z.ai hanyalah
"stdio -> Streamable HTTP + token". Versi ini bisa diuji di mesin mana pun tanpa
build .NET, dan Funnel tidak perlu disentuh sampai jaringan lokal hijau.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import secrets
import sys
from contextlib import AsyncExitStack
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent.parent.parent
SECRETS = ROOT / "secrets"

# Server yang diekspos. `prefix` menjaga nama tool tetap unik bila nanti ada
# server lain dengan nama tool yang sama.
SERVERS = {
    "kbbi": {
        "script": ROOT / "tools" / "kbbi" / "server.py",
        "desc": "KBBI VI Daring resmi (kbbi_lookup, kbbi_check_baku, kbbi_batch, kbbi_syllables)",
        "prefix": "kbbi_",
    },
    "scholar": {
        "script": ROOT / "tools" / "scholar" / "_entry.py",
        "desc": "Literatur ilmiah: OpenAlex, Crossref, DOAJ (scholar_search, scholar_cite, ...)",
        "prefix": "scholar_",
    },
}

START_TIMEOUT = 180


# --------------------------------------------------------------------- token


class TokenStore:
    """Token per project: {"projects": {"nama": "xel_..."}}."""

    def __init__(self, path: Path, projects: dict[str, str] | None = None) -> None:
        self.path = path
        self.projects = projects or {}

    @classmethod
    def load(cls, path: Path) -> "TokenStore":
        if not path.exists():
            return cls(path)
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            raise SystemExit(f"{path} bukan JSON valid: {exc}") from exc
        projects = data.get("projects") or {}
        if not isinstance(projects, dict):
            raise SystemExit(f"{path}: key 'projects' harus objek")
        return cls(path, dict(projects))

    def save(self) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(
            json.dumps({"projects": self.projects}, indent=2) + "\n", encoding="utf-8"
        )
        try:
            os.chmod(self.path, 0o600)
        except OSError:
            pass

    def issue(self, project: str) -> str:
        token = "xel_" + secrets.token_urlsafe(32)
        self.projects[project] = token
        self.save()
        return token

    def project_for(self, token: str) -> str | None:
        for name, value in self.projects.items():
            if value and secrets.compare_digest(value, token):
                return name
        return None


# ------------------------------------------------------------------- upstream


class Upstream:
    """Satu server stdio: proses anak + sesi MCP yang meneruskan tools/*."""

    def __init__(self, name: str, spec: dict[str, Any], python_exe: str) -> None:
        self.name = name
        self.spec = spec
        self.python_exe = python_exe
        self.tools: list[Any] = []
        self._stack: AsyncExitStack | None = None
        self._session: Any = None

    async def start(self) -> None:
        from mcp import StdioServerParameters
        from mcp.client.session import ClientSession
        from mcp.client.stdio import stdio_client

        script = str(self.spec["script"])
        if not Path(script).exists():
            raise RuntimeError(f"{self.name}: script tidak ditemukan ({script})")

        params = StdioServerParameters(command=self.python_exe, args=[script], env=None)
        self._stack = AsyncExitStack()
        read, write = await asyncio.wait_for(
            self._stack.enter_async_context(stdio_client(params)), timeout=START_TIMEOUT
        )
        self._session = await self._stack.enter_async_context(ClientSession(read, write))
        await asyncio.wait_for(self._session.initialize(), timeout=START_TIMEOUT)
        listed = await self._session.list_tools()
        self.tools = [self._rename(t) for t in listed.tools]

    def _rename(self, tool: Any) -> Any:
        # Nama tool sudah berprefiks di tiap server (kbbi_/scholar_), jadi
        # dibiarkan apa adanya agar dokumentasi tetap cocok dengan aslinya.
        return tool

    async def call(self, tool_name: str, arguments: dict[str, Any]) -> Any:
        return await self._session.call_tool(tool_name, arguments)

    async def close(self) -> None:
        if self._stack is not None:
            await self._stack.aclose()
            self._stack = None
            self._session = None


# -------------------------------------------------------------------- gateway


def build_app(tokens: TokenStore, upstreams: dict[str, Upstream], server_name: str):
    """Membuat aplikasi Starlette dengan middleware token di depan transport MCP."""
    from mcp.server.mcpserver import MCPServer
    from starlette.middleware.base import BaseHTTPMiddleware
    from starlette.responses import JSONResponse, PlainTextResponse

    mcp = MCPServer(server_name)

    # Daftarkan semua tool dari upstream. Handler menutup nama asal tool.
    for up in upstreams.values():
        for tool in up.tools:
            name = tool.name
            owner = next(n for n, u in upstreams.items() if any(t.name == name for t in u.tools))
            # SDK mcp 2.x menamai field ini `input_schema` (1.x: `inputSchema`).
            params = (
                getattr(tool, "input_schema", None)
                or getattr(tool, "inputSchema", None)
                or {}
            )

            def _make(_name: str, _owner: str):
                # Signature dibangun dari JSON Schema tool asal, supaya skema yang
                # terlihat klien identik dengan upstream dan argumen tidak dibungkus
                # menjadi satu parameter bernama "arguments".
                async def handler(**arguments: Any) -> Any:
                    result = await upstreams[_owner].call(_name, arguments)
                    return _flatten(result)

                return handler

            fn = _make(name, owner)
            fn.__name__ = name
            if isinstance(params, dict) and isinstance(params.get("properties"), dict):
                _apply_json_schema(fn, params, name)

            mcp.add_tool(
                fn,
                name=name,
                description=getattr(tool, "description", None) or f"tool {name}",
                structured_output=False,
            )

    app = mcp.streamable_http_app(streamable_http_path="/mcp", stateless_http=True)

    from starlette.routing import Route

    async def healthz(_request):
        return JSONResponse(
            {
                "ok": True,
                "projects": sorted(tokens.projects),
                "tools": {name: [t.name for t in up.tools] for name, up in upstreams.items()},
            }
        )

    # Health check tidak butuh token, jadi didaftarkan sebelum middleware
    # (middleware tetap melewati path ini secara eksplisit).
    app.router.routes.insert(0, Route("/healthz", healthz, methods=["GET"]))

    class TokenGate(BaseHTTPMiddleware):
        async def dispatch(self, request, call_next):
            path = request.url.path
            if path in ("/healthz", "/"):
                return await call_next(request)
            header = request.headers.get("authorization", "")
            token = ""
            if header.lower().startswith("bearer "):
                token = header[7:].strip()
            elif request.query_params.get("Authorization"):
                # Bentuk yang dipakai z.ai untuk varian SSE: ?Authorization=<token>
                token = request.query_params["Authorization"].strip()
            project = tokens.project_for(token) if token else None
            if project is None:
                return PlainTextResponse(
                    "unauthorized", status_code=401, headers={"WWW-Authenticate": "Bearer"}
                )
            request.state.project = project
            return await call_next(request)

    app.add_middleware(TokenGate)

    return app


_JSON_TYPE_MAP = {
    "string": str,
    "integer": int,
    "number": float,
    "boolean": bool,
    "array": list,
    "object": dict,
}


def _apply_json_schema(fn: Any, schema: dict[str, Any], tool_name: str) -> None:
    """Bangun __signature__ dari JSON Schema agar skema tool sama dengan upstream.

    MCPServer menurunkan skema input dari signature fungsi. Tanpa ini, semua
    argumen akan dibungkus jadi satu parameter bernama `arguments`.
    """
    import inspect

    props: dict[str, Any] = schema.get("properties") or {}
    required = set(schema.get("required") or [])
    params: list[inspect.Parameter] = []
    annotations: dict[str, Any] = {}

    for pname, spec in props.items():
        ptype = _JSON_TYPE_MAP.get(str((spec or {}).get("type")), Any)
        annotations[pname] = ptype
        default = inspect.Parameter.empty if pname in required else None
        params.append(
            inspect.Parameter(
                pname,
                inspect.Parameter.KEYWORD_ONLY,
                default=default,
                annotation=ptype,
            )
        )

    fn.__signature__ = inspect.Signature(params, return_annotation=None)
    fn.__annotations__ = {**annotations, "return": None}


def _flatten(result: Any) -> str:
    parts: list[str] = []
    for item in getattr(result, "content", []) or []:
        text = getattr(item, "text", None)
        if text:
            parts.append(text)
    return "\n".join(parts) if parts else json.dumps({"result": str(result)})


# ----------------------------------------------------------------------- main


async def _serve(args) -> int:
    tokens = TokenStore.load(args.tokens)
    if not tokens.projects:
        print(
            "Belum ada token. Buat dulu: --issue <nama-project>",
            file=sys.stderr,
        )
        return 1

    python_exe = args.python or sys.executable
    upstreams = {name: Upstream(name, spec, python_exe) for name, spec in SERVERS.items()}

    for name, up in upstreams.items():
        try:
            await up.start()
            print(f"upstream {name}: {len(up.tools)} tool -> {', '.join(t.name for t in up.tools)}")
        except Exception as exc:  # noqa: BLE001 - laporkan apa adanya
            print(f"upstream {name}: GAGAL ({exc})", file=sys.stderr)
            for other in upstreams.values():
                await other.close()
            return 1

    app = build_app(tokens, upstreams, server_name="mcp-xellen-tools")

    import uvicorn

    print(f"gateway : http://{args.host}:{args.port}/mcp")
    print(f"health  : http://{args.host}:{args.port}/healthz")
    print(f"projects: {', '.join(sorted(tokens.projects))}")
    config = uvicorn.Config(app, host=args.host, port=args.port, log_level=args.log_level)
    server = uvicorn.Server(config)
    try:
        await server.serve()
    finally:
        for up in upstreams.values():
            await up.close()
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Gateway HTTP MCP Xellen tools")
    parser.add_argument("--host", default="127.0.0.1", help="alamat bind (default loopback)")
    parser.add_argument("--port", type=int, default=8300)
    parser.add_argument("--tokens", type=Path, default=SECRETS / "gateway-tokens.json")
    parser.add_argument("--python", help="interpreter yang punya paket mcp (default: python ini)")
    parser.add_argument("--log-level", default="info")
    parser.add_argument("--issue", metavar="PROJECT", help="buat token baru untuk project, lalu keluar")
    args = parser.parse_args(argv)

    if args.issue:
        tokens = TokenStore.load(args.tokens)
        token = tokens.issue(args.issue)
        print(f"project : {args.issue}")
        print(f"token   : {token}")
        print(f"file    : {args.tokens}")
        print("\nSimpan sekarang; token tidak ditampilkan lagi.")
        return 0

    return asyncio.run(_serve(args))


if __name__ == "__main__":
    raise SystemExit(main())
