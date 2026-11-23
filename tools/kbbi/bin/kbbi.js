#!/usr/bin/env node
/**
 * Launcher MCP KBBI.
 *
 * Menjalankan tools/kbbi/server.py dengan interpreter Python yang punya paket
 * `mcp`. Urutan pencarian:
 *   1. MCP_XELLEN_PYTHON  (env override)
 *   2. python / py -3 / python3 di PATH
 *
 * Fail loud, bukan "Connection closed" tanpa penjelasan.
 */

const { spawn, spawnSync } = require("node:child_process");
const path = require("node:path");
const fs = require("node:fs");

const ROOT = path.resolve(__dirname, "..");
const SERVER = path.join(ROOT, "server.py");

function candidates() {
  const out = [];
  if (process.env.MCP_XELLEN_PYTHON) {
    out.push({ cmd: process.env.MCP_XELLEN_PYTHON, args: [] });
  }
  if (process.platform === "win32") {
    out.push({ cmd: "python", args: [] });
    out.push({ cmd: "py", args: ["-3"] });
  } else {
    out.push({ cmd: "python3", args: [] });
    out.push({ cmd: "python", args: [] });
  }
  return out;
}

function hasMcp(cmd, baseArgs) {
  const r = spawnSync(cmd, [...baseArgs, "-c", "import mcp"], {
    stdio: "ignore",
    timeout: 15000,
  });
  return r.status === 0;
}

function pick() {
  for (const c of candidates()) {
    try {
      if (hasMcp(c.cmd, c.args)) return c;
    } catch {
      /* lanjut */
    }
  }
  return null;
}

if (!fs.existsSync(SERVER)) {
  console.error(`[mcp-kbbi] server.py tidak ditemukan di ${SERVER}`);
  process.exit(1);
}

const py = pick();
if (!py) {
  console.error(
    "[mcp-kbbi] Tidak menemukan Python dengan paket 'mcp'.\n" +
      "  pip install mcp\n" +
      "  atau set MCP_XELLEN_PYTHON=/path/to/python"
  );
  process.exit(1);
}

const child = spawn(py.cmd, [...py.args, SERVER, ...process.argv.slice(2)], {
  stdio: "inherit",
  env: process.env,
});

child.on("exit", (code, signal) => {
  if (signal) process.kill(process.pid, signal);
  else process.exit(code ?? 0);
});
