#!/usr/bin/env python3
"""MCP Xellen — Z.ai Website Agent Mode client adapter.

Stdlib-only (tidak butuh pip install). Menangani: initialize, notifications/initialized,
tools/list, tools/call, JSON + SSE, session id, expiry/reconnect, retry ber-backoff.

Aturan keamanan:
- Token dibaca dari file (bukan prompt/argumen) dan TIDAK PERNAH dicetak.
- Token hanya dikirim ke endpoint yang dipasangkan (header Authorization).

Contoh:
  python zai_mcp_client.py --endpoint http://127.0.0.1:8300/prj/topiora/mcp \
      --token-file project-topiora-token.txt --mode list
  python zai_mcp_client.py --endpoint ... --token-file ... --mode call \
      --tool dc-staging-native__list_processes --args-json '{}'
"""
from __future__ import annotations

import argparse
import json
import sys
import time
import uuid
from urllib import error as urlerror
from urllib import request as urlrequest


class McpClientError(Exception):
    """Kesalahan protokol/HTTP yang sudah diketahui (body tanpa rahasia)."""


class McpHttpClient:
    def __init__(self, endpoint: str, token: str, protocol: str = "2025-06-18",
                 timeout: float = 30.0, force_sse: bool = False, verbose: bool = False):
        self.endpoint = endpoint
        self.token = token
        self.protocol = protocol
        self.timeout = timeout
        self.force_sse = force_sse
        self.verbose = verbose
        self.session_id: str | None = None
        self._initialized = False
        self._next_id = 1

    # ---------------------------------------------------------------- low level

    def _headers(self) -> dict:
        h = {
            "Authorization": f"Bearer {self.token}",
            "Accept": "text/event-stream" if self.force_sse else "application/json, text/event-stream",
            "Content-Type": "application/json",
        }
        if self.session_id:
            h["Mcp-Session-Id"] = self.session_id
        if self._initialized and not self.force_sse:
            h["MCP-Protocol-Version"] = self.protocol
        return h

    def _post(self, payload: dict) -> dict | None:
        """POST JSON-RPC. Returns parsed response dict, or None for 202 (notification accepted).
        Captures Mcp-Session-Id from the initialize response headers (session is issued there).
        Raises McpClientError dengan HTTP status/body (tanpa nilai token)."""
        req = urlrequest.Request(self.endpoint, data=json.dumps(payload).encode(),
                                 headers=self._headers(), method="POST")
        try:
            resp = urlrequest.urlopen(req, timeout=self.timeout)
        except urlerror.HTTPError as e:
            body = ""
            try:
                body = e.read().decode(errors="replace")
            except Exception:
                pass
            raise McpClientError(f"HTTP {e.code}: {body[:400]}") from None

        # Session id is issued by the server in the initialize RESPONSE headers.
        sid = resp.headers.get("Mcp-Session-Id")
        if sid:
            self.session_id = sid

        if resp.status == 202:
            return None
        ctype = (resp.headers.get("Content-Type") or "").lower()
        raw = resp.read().decode(errors="replace")
        if "text/event-stream" in ctype:
            # Streamable HTTP SSE: kumpulkan baris data:, gabungkan jadi satu JSON.
            data_lines = [ln[5:].strip() for ln in raw.splitlines() if ln.startswith("data:")]
            joined = "".join(data_lines)
            if not joined:
                return None
            return json.loads(joined)
        if not raw.strip():
            return None
        return json.loads(raw)

    def _request(self, method: str, params: dict | None = None) -> dict:
        rid = self._next_id
        self._next_id += 1
        payload = {"jsonrpc": "2.0", "id": rid, "method": method}
        if params is not None:
            payload["params"] = params
        return self._post(payload) or {}

    def _notify(self, method: str, params: dict | None = None) -> None:
        self._post({"jsonrpc": "2.0", "method": method, "params": params} if params else
                   {"jsonrpc": "2.0", "method": method})

    # ---------------------------------------------------------------- lifecycle

    def initialize(self) -> dict:
        if self.force_sse:
            # SSE-only mode: kirim header version via protocol param (header MCP-Protocol-Version
            # tidak dikirim karena accept json tidak ada).
            pass
        res = self._request("initialize", {
            "protocolVersion": self.protocol,
            "capabilities": {},
            "clientInfo": {"name": "zai-adapter", "version": "1.0.0"},
        })
        if "error" in res:
            raise McpClientError(f"initialize error: {res['error'].get('message', '?')}")
        negotiated = res.get("result", {}).get("protocolVersion")
        if negotiated:
            self.protocol = negotiated
        self._initialized = True
        if self.verbose:
            print("[adapter] initialize ok (session set, token tidak dicetak)")
        return res.get("result", {})

    def initialized(self) -> None:
        self._notify("notifications/initialized")

    def handshake(self) -> dict:
        """initialize + notifications/initialized. Dipanggil otomatis saat koneksi baru."""
        result = self.initialize()
        self.initialized()
        return result

    def _ensure_session(self) -> None:
        if not self._initialized or not self.session_id:
            self.handshake()

    # ---------------------------------------------------------------- operations

    def tools_list(self) -> list[dict]:
        self._ensure_session()
        res = self._request("tools/list")
        if "error" in res:
            raise McpClientError(f"tools/list error: {res['error'].get('message', '?')}")
        return res.get("result", {}).get("tools", [])

    def call_tool(self, name: str, arguments: dict | None = None) -> dict:
        self._ensure_session()
        res = self._request("tools/call", {"name": name, "arguments": arguments or {}})
        if "error" in res:
            raise McpClientError(f"tools/call error: {res['error'].get('message', '?')}")
        return res.get("result", {})

    def call_tool_with_reconnect(self, name: str, arguments: dict | None = None,
                                 retries: int = 2) -> dict:
        """tools/call dengan reconnect otomatis saat sesi kedaluwarsa (HTTP 404 / session expired)."""
        attempt = 0
        while True:
            try:
                return self.call_tool(name, arguments)
            except McpClientError as e:
                msg = str(e)
                transient = ("404" in msg) or ("session expired" in msg.lower()) or ("timed out" in msg.lower())
                if attempt < retries and ("session expired" in msg.lower() or "404" in msg):
                    self._initialized = False
                    self.session_id = None
                    self.handshake()
                    attempt += 1
                    time.sleep(1 + attempt)
                    continue
                if attempt < retries and ("timed out" in msg.lower() or "URLError" in msg or "unreachable" in msg.lower()):
                    time.sleep(2 * (attempt + 1))
                    attempt += 1
                    continue
                raise McpClientError(f"{msg} (setelah {attempt} retry; transient={transient})") from None


def extract_text(result: dict) -> str:
    parts = []
    for item in result.get("content", []):
        if item.get("type") == "text":
            parts.append(item.get("text", ""))
    text = "\n".join(parts).strip()
    return ("[isError] " if result.get("isError") else "") + text


def main() -> int:
    ap = argparse.ArgumentParser(description="MCP Xellen — Z.ai adapter (stdlib only)")
    ap.add_argument("--endpoint", required=True, help="URL MCP Streamable HTTP")
    ap.add_argument("--token-file", required=True, help="file berisi token (dibaca, tidak dicetak)")
    ap.add_argument("--mode", choices=["list", "call", "demo"], default="list")
    ap.add_argument("--tool", help="nama tool berprefiks: <serverId>__<toolName>")
    ap.add_argument("--args-json", default="{}", help="argumen tool sebagai JSON")
    ap.add_argument("--protocol", default="2025-06-18")
    ap.add_argument("--sse", action="store_true", help="paksa Accept: text/event-stream saja")
    ap.add_argument("--timeout", type=float, default=30.0)
    args = ap.parse_args()

    try:
        with open(args.token_file, "r", encoding="utf-8-sig") as f:
            token = f.read().strip()
    except OSError as e:
        print(f"[adapter] GAGAL membaca token file: {e}")
        return 2
    if not token:
        print("[adapter] Token file kosong.")
        return 2

    client = McpHttpClient(args.endpoint, token, protocol=args.protocol,
                           timeout=args.timeout, force_sse=args.sse, verbose=True)

    try:
        client.handshake()
        if args.mode == "list":
            tools = client.tools_list()
            print(f"[adapter] tools tersedia: {len(tools)}")
            for t in tools:
                desc = (t.get("description") or "").splitlines()[0][:90]
                print(f"  - {t.get('name')}: {desc}")
            return 0
        if args.mode == "call":
            try:
                arguments = json.loads(args.args_json or "{}")
            except json.JSONDecodeError as e:
                print(f"[adapter] --args-json bukan JSON valid: {e}")
                return 2
            result = client.call_tool_with_reconnect(args.tool, arguments)
            print(extract_text(result) or "(hasil kosong)")
            return 0
        # demo
        tools = client.tools_list()
        print(f"[adapter] demo: {len(tools)} tools terlihat")
        return 0
    except McpClientError as e:
        print(f"[adapter] ERROR: {e}")
        return 1
    except Exception as e:  # network/unknown — pesan tanpa rahasia
        print(f"[adapter] ERROR tak terduga: {type(e).__name__}: {e}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
