#!/usr/bin/env node
/**
 * Installer satu perintah untuk MCP Xellen tools.
 *
 *   npx mcp-xellen-tools check                        # cek prasyarat
 *   npx mcp-xellen-tools install                      # Hermes: kbbi + scholar
 *   npx mcp-xellen-tools install hermes kbbi           # Hermes: hanya KBBI
 *   npx mcp-xellen-tools install claude                # tulis .mcp.json project
 *   npx mcp-xellen-tools install cursor --global       # ~/.cursor/mcp.json
 *   npx mcp-xellen-tools install vscode                # code --add-mcp
 *   npx mcp-xellen-tools install codex                 # ~/.codex/config.toml
 *   npx mcp-xellen-tools install print                 # JSON siap tempel
 *   npx mcp-xellen-tools where                         # path instalasi
 *
 * Semua target memakai transport stdio: klien men-spawn Python lokal, tanpa
 * token. Klien remote seperti z.ai butuh gateway HTTP, belum ada di sini.
 */

const { spawnSync } = require("node:child_process");
const path = require("node:path");
const fs = require("node:fs");
const os = require("node:os");

const ROOT = path.resolve(__dirname, "..");
const TOOLS = {
  kbbi: {
    script: path.join(ROOT, "tools", "kbbi", "server.py"),
    desc: "KBBI VI Daring resmi",
  },
  scholar: {
    script: path.join(ROOT, "tools", "scholar", "_entry.py"),
    desc: "Literatur ilmiah (OpenAlex/Crossref/DOAJ)",
  },
};

const TARGETS = {
  hermes: { kind: "hermes", label: "Hermes Agent" },
  claude: { kind: "file", label: "Claude Code", file: () => path.join(process.cwd(), ".mcp.json") },
  cursor: {
    kind: "file",
    label: "Cursor",
    file: () => path.join(process.cwd(), ".cursor", "mcp.json"),
    global: () => path.join(os.homedir(), ".cursor", "mcp.json"),
  },
  vscode: { kind: "vscode", label: "VS Code" },
  codex: { kind: "toml", label: "Codex CLI", file: () => path.join(os.homedir(), ".codex", "config.toml") },
  print: { kind: "print", label: "JSON siap tempel" },
};

const ALIASES = { code: "vscode", "vs-code": "vscode", "claude-code": "claude", raw: "print", json: "print" };

const C = {
  reset: "\x1b[0m",
  dim: "\x1b[2m",
  green: "\x1b[32m",
  red: "\x1b[31m",
  yellow: "\x1b[33m",
  cyan: "\x1b[36m",
  bold: "\x1b[1m",
};

const ok = (m) => console.log(`${C.green}✓${C.reset} ${m}`);
const bad = (m) => console.log(`${C.red}✗${C.reset} ${m}`);
const info = (m) => console.log(`${C.dim}${m}${C.reset}`);
const warn = (m) => console.log(`${C.yellow}!${C.reset} ${m}`);

const IS_WIN = process.platform === "win32";

function candidates() {
  const out = [];
  if (process.env.MCP_XELLEN_PYTHON) {
    out.push({ cmd: process.env.MCP_XELLEN_PYTHON, args: [] });
  }
  if (IS_WIN) {
    out.push({ cmd: "python", args: [] });
    out.push({ cmd: "py", args: ["-3"] });
  } else {
    out.push({ cmd: "python3", args: [] });
    out.push({ cmd: "python", args: [] });
  }
  return out;
}

function testPython(cmd, baseArgs) {
  const v = spawnSync(cmd, [...baseArgs, "--version"], {
    encoding: "utf8",
    timeout: 15000,
    windowsHide: true,
  });
  if (v.status !== 0) return null;
  const version = (v.stdout || v.stderr || "").trim();

  const m = spawnSync(cmd, [...baseArgs, "-c", "import mcp"], {
    stdio: "ignore",
    timeout: 15000,
    windowsHide: true,
  });
  if (m.status !== 0) return { cmd, baseArgs, version, hasMcp: false };

  // Path ABSOLUT: klien stdio mewarisi environment yang sudah difilter, jadi
  // `python` polos bisa jatuh ke interpreter lain tanpa paket mcp.
  const exe = spawnSync(cmd, [...baseArgs, "-c", "import sys;print(sys.executable)"], {
    encoding: "utf8",
    timeout: 15000,
    windowsHide: true,
  });
  const absPath = (exe.stdout || "").trim().split("\n").pop().trim();
  return {
    cmd,
    baseArgs,
    version,
    hasMcp: true,
    absolute: absPath && fs.existsSync(absPath) ? absPath : null,
  };
}

function findPythons() {
  const found = [];
  for (const c of candidates()) {
    try {
      const r = testPython(c.cmd, c.args);
      if (r) found.push(r);
    } catch {
      /* lewati */
    }
  }
  return found;
}

function hasBin(name) {
  const r = spawnSync(name, ["--version"], {
    encoding: "utf8",
    timeout: 20000,
    windowsHide: true,
    shell: IS_WIN,
  });
  return r.status === 0;
}

/* ---------------------------------------------------------------- check */

function cmdCheck() {
  console.log(`${C.bold}Cek prasyarat MCP Xellen${C.reset}\n`);

  const pys = findPythons();
  if (pys.length === 0) {
    bad("Python tidak ditemukan di PATH");
    info("  Pasang Python 3.11+ dari https://python.org");
    return 1;
  }

  let ready = null;
  for (const p of pys) {
    const mark = p.hasMcp ? `${C.green}siap${C.reset}` : `${C.yellow}tanpa paket mcp${C.reset}`;
    console.log(`  ${p.cmd} ${p.baseArgs.join(" ")} → ${p.version} [${mark}]`);
    if (p.hasMcp && !ready) ready = p;
  }

  if (!ready) {
    bad("Tidak ada Python dengan paket 'mcp'");
    info(`  Perbaiki: ${pys[0].cmd} -m pip install mcp`);
    info("  Atau set MCP_XELLEN_PYTHON ke interpreter yang sudah punya mcp");
    return 1;
  }
  ok(`Python siap: ${ready.cmd} ${ready.baseArgs.join(" ")}`);

  for (const [name, t] of Object.entries(TOOLS)) {
    fs.existsSync(t.script) ? ok(`${name}: ${t.desc}`) : bad(`${name}: script hilang (${t.script})`);
  }

  hasBin("hermes") ? ok("hermes CLI tersedia") : warn("hermes CLI tidak ada (target hermes tidak bisa dipakai)");
  hasBin("code") ? ok("perintah code tersedia") : info("code tidak ada (target vscode membutuhkannya)");

  console.log();
  info(
    "Siap dipasang. Pilihan target:\n" +
      "  npx mcp-xellen-tools install            Hermes Agent\n" +
      "  npx mcp-xellen-tools install claude     Claude Code (.mcp.json)\n" +
      "  npx mcp-xellen-tools install cursor     Cursor\n" +
      "  npx mcp-xellen-tools install vscode     VS Code\n" +
      "  npx mcp-xellen-tools install codex      Codex CLI\n" +
      "  npx mcp-xellen-tools install print      JSON mentah"
  );
  return 0;
}

/* ---------------------------------------------------------------- helper */

function serverSpec(tool, pyExe) {
  return { command: pyExe, args: [TOOLS[tool].script], env: {} };
}

function specFor(tools, pyExe) {
  const out = {};
  for (const t of tools) out[t] = serverSpec(t, pyExe);
  return out;
}

function quoteArg(p) {
  return /\s/.test(p) ? `"${p}"` : p;
}

function readJsonSafe(file, fallback) {
  if (!fs.existsSync(file)) return fallback;
  try {
    return JSON.parse(fs.readFileSync(file, "utf8"));
  } catch {
    return null;
  }
}

/* ---------------------------------------------------------------- hermes */

function installHermes(tools, pyExe) {
  let failed = 0;
  for (const t of tools) {
    const spec = TOOLS[t];
    if (!fs.existsSync(spec.script)) {
      bad(`hermes: ${t} script tidak ditemukan`);
      failed++;
      continue;
    }

    spawnSync("hermes", ["mcp", "remove", t], {
      stdio: "ignore",
      timeout: 60000,
      windowsHide: true,
      shell: IS_WIN,
      input: "y\n",
    });

    const r = spawnSync(
      "hermes",
      ["mcp", "add", t, "--command", pyExe, "--args", quoteArg(spec.script)],
      {
        encoding: "utf8",
        timeout: 180000,
        windowsHide: true,
        shell: IS_WIN,
        input: "Y\n",
      }
    );

    // `add` bisa keluar 0 walau koneksi gagal: verifikasi sendiri.
    const verify = spawnSync("hermes", ["mcp", "test", t], {
      encoding: "utf8",
      timeout: 120000,
      windowsHide: true,
      shell: IS_WIN,
    });
    const verified = /Connected/.test((verify.stdout || "") + (verify.stderr || ""));

    if (verified) {
      ok(`hermes: ${t} (${spec.desc})`);
    } else {
      bad(`hermes: ${t} gagal terhubung`);
      const msg = ((verify.stdout || "") + (verify.stderr || "") + (r.stdout || ""))
        .split("\n")
        .filter((l) => l.trim())
        .slice(-4)
        .join("\n");
      if (msg.trim()) info(msg);
      failed++;
    }
  }
  return failed;
}

/* ------------------------------------------------------------------ file */

function installFile(name, def, tools, pyExe, useGlobal) {
  if (useGlobal && !def.global) {
    bad(`${name}: tidak punya konfigurasi global, jalankan tanpa --global`);
    return 1;
  }
  const file = useGlobal ? def.global() : def.file();
  const existing = readJsonSafe(file, {});
  if (existing === null) {
    bad(`${name}: ${file} bukan JSON valid, tidak saya sentuh`);
    return 1;
  }
  const merged = {
    ...existing,
    mcpServers: { ...(existing.mcpServers || {}), ...specFor(tools, pyExe) },
  };
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(merged, null, 2) + "\n", "utf8");
  ok(`${name}: ${file}`);
  for (const t of tools) info(`  ${t} → ${TOOLS[t].script}`);
  info("  Muat ulang klien agar server terbaca.");
  return 0;
}

/* ----------------------------------------------------------------- toml */

function installCodex(tools, pyExe) {
  const file = TARGETS.codex.file();
  let text = fs.existsSync(file) ? fs.readFileSync(file, "utf8") : "";
  let added = 0;

  for (const t of tools) {
    if (new RegExp(`^\\[mcp_servers\\.${t}\\]`, "m").test(text)) {
      warn(`codex: ${t} sudah ada di ${file}, dilewati`);
      continue;
    }
    text +=
      `\n[mcp_servers.${t}]\n` +
      `command = ${JSON.stringify(pyExe)}\n` +
      `args = [${JSON.stringify(TOOLS[t].script)}]\n`;
    added++;
  }

  if (added === 0) return 0;
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text, "utf8");
  ok(`codex: ${added} server ditulis ke ${file}`);
  return 0;
}

/* ---------------------------------------------------------------- vscode */

/** `code` di Windows berupa code.cmd; spawnSync tanpa shell tidak menemukannya. */
function codeBin() {
  return IS_WIN ? "code.cmd" : "code";
}

function installVscode(tools, pyExe) {
  if (!hasBin("code")) {
    bad("perintah `code` tidak ditemukan (VS Code: Command Palette → Shell Command: Install 'code')");
    return 1;
  }
  let failed = 0;
  for (const t of tools) {
    const spec = serverSpec(t, pyExe);
    const json = JSON.stringify({ name: t, command: spec.command, args: spec.args });
    const okRun = runCodeAddMcp(t, json);
    if (okRun.status === 0) {
      ok(`vscode: ${t}`);
    } else {
      bad(`vscode: ${t} gagal`);
      const msg = (okRun.stderr || okRun.stdout || okRun.error?.message || "").trim().split("\n").slice(-2).join("\n");
      if (msg) info(msg);
      failed++;
    }
  }
  info("  Muat ulang jendela VS Code agar server terbaca.");
  return failed;
}

/**
 * Menjalankan `code --add-mcp <json>` dengan aman di kedua OS.
 *
 * Windows: `code` adalah shim code.cmd, dan cmd.exe membuang tanda kutip di
 * dalam argumen, sehingga JSON rusak sebelum sampai ke VS Code. JSON ditulis
 * ke file, dibaca utuh oleh PowerShell, lalu dikirim ke cmd dengan tanda kutip
 * yang sudah di-escape. Terbukti: `Added MCP servers: <name>`.
 */
function runCodeAddMcp(name, json) {
  if (IS_WIN) {
    const tmp = path.join(os.tmpdir(), `xellen-addmcp-${name}-${process.pid}.json`);
    fs.writeFileSync(tmp, json, "utf8");
    const ps =
      `$json = (Get-Content -Raw -LiteralPath '${tmp.replace(/'/g, "''")}').Trim(); ` +
      `$psi = New-Object System.Diagnostics.ProcessStartInfo; ` +
      `$psi.FileName = 'cmd.exe'; ` +
      `$psi.Arguments = '/c code --add-mcp "' + ($json -replace '"','\\"') + '"'; ` +
      `$psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false; ` +
      `$p = [Diagnostics.Process]::Start($psi); ` +
      `$out = $p.StandardOutput.ReadToEnd(); $err = $p.StandardError.ReadToEnd(); $p.WaitForExit(); ` +
      `Write-Output $out; Write-Error $err; exit $p.ExitCode`;
    try {
      return spawnSync(
        "powershell.exe",
        ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", ps],
        { encoding: "utf8", timeout: 60000, windowsHide: true }
      );
    } finally {
      try { fs.unlinkSync(tmp); } catch { /* abaikan */ }
    }
  }
  return spawnSync("code", ["--add-mcp", json], {
    encoding: "utf8",
    timeout: 60000,
    windowsHide: true,
  });
}

/* ----------------------------------------------------------------- print */

function printJson(tools, pyExe) {
  console.log(JSON.stringify({ mcpServers: specFor(tools, pyExe) }, null, 2));
  console.log();
  info("Tempel blok di atas ke konfigurasi MCP klien Anda.");
  info("Klien remote (z.ai) butuh gateway HTTP: lihat GATEWAY.md.");
  return 0;
}

/* --------------------------------------------------------------- install */

function parseArgs(argv) {
  const targets = [];
  const tools = [];
  let global = false;
  for (const a of argv) {
    if (a === "--global" || a === "-g") {
      global = true;
    } else if (a.startsWith("-")) {
      bad(`opsi tidak dikenal: ${a}`);
      return null;
    } else {
      const name = ALIASES[a] || a;
      if (TARGETS[name]) targets.push(name);
      else if (TOOLS[name]) tools.push(name);
      else {
        bad(`argumen tidak dikenal: ${a}`);
        info(`  target: ${Object.keys(TARGETS).join(", ")}`);
        info(`  tool  : ${Object.keys(TOOLS).join(", ")}`);
        return null;
      }
    }
  }
  return {
    targets: targets.length ? [...new Set(targets)] : ["hermes"],
    tools: tools.length ? [...new Set(tools)] : Object.keys(TOOLS),
    global,
  };
}

function cmdInstall(argv) {
  const parsed = parseArgs(argv);
  if (!parsed) return 1;

  const ready = findPythons().find((p) => p.hasMcp);
  if (!ready) {
    bad("Tidak ada Python dengan paket 'mcp'. Jalankan dulu: npx mcp-xellen-tools check");
    return 1;
  }
  const pyExe = ready.absolute || ready.cmd;

  console.log(`${C.bold}Memasang MCP Xellen tools${C.reset}`);
  info(`python : ${pyExe}`);
  info(`target : ${parsed.targets.join(", ")}`);
  info(`tool   : ${parsed.tools.join(", ")}\n`);

  let failed = 0;
  for (const name of parsed.targets) {
    const def = TARGETS[name];
    switch (def.kind) {
      case "hermes":
        if (!hasBin("hermes")) {
          bad("hermes: CLI tidak ditemukan di PATH");
          failed++;
          break;
        }
        failed += installHermes(parsed.tools, pyExe);
        break;
      case "file":
        failed += installFile(name, def, parsed.tools, pyExe, parsed.global);
        break;
      case "toml":
        failed += installCodex(parsed.tools, pyExe);
        break;
      case "vscode":
        failed += installVscode(parsed.tools, pyExe);
        break;
      case "print":
        failed += printJson(parsed.tools, pyExe);
        break;
      default:
        bad(`${name}: belum didukung`);
        failed++;
    }
  }

  console.log();
  if (failed === 0) {
    if (parsed.targets.includes("hermes")) {
      ok("Selesai. Jalankan /reload-mcp di sesi Hermes, atau mulai sesi baru.");
    } else {
      ok("Selesai tanpa error.");
    }
    return 0;
  }
  bad(`${failed} langkah gagal`);
  return 1;
}

function cmdWhere() {
  console.log(`Paket  : ${ROOT}`);
  for (const [name, t] of Object.entries(TOOLS)) {
    console.log(`${name.padEnd(8)}: ${t.script} ${fs.existsSync(t.script) ? "" : "(HILANG)"}`);
  }
  return 0;
}

function usage() {
  console.log(`
${C.bold}mcp-xellen-tools${C.reset} — MCP server KBBI + literatur ilmiah

${C.cyan}Perintah:${C.reset}
  install [target...] [tool...] [--global]
                           Pasang ke klien. Default target "hermes",
                           tool keduanya.
  check                    Cek prasyarat Python + paket mcp
  where                    Tampilkan path instalasi
  help                     Tampilan ini

${C.cyan}Target:${C.reset}
  hermes    Hermes Agent, lewat \`hermes mcp add\` + verifikasi \`mcp test\`
  claude    Claude Code, menulis .mcp.json di folder kerja
  cursor    Cursor, .cursor/mcp.json (--global: ~/.cursor/mcp.json)
  vscode    VS Code, lewat \`code --add-mcp\`
  codex     Codex CLI, menambah [mcp_servers.*] di ~/.codex/config.toml
  print     Cetak JSON mentah untuk klien lain

${C.cyan}Tool:${C.reset} kbbi, scholar

${C.cyan}Contoh:${C.reset}
  npx mcp-xellen-tools check
  npx mcp-xellen-tools install
  npx mcp-xellen-tools install hermes kbbi
  npx mcp-xellen-tools install claude cursor
  npx mcp-xellen-tools install cursor --global
  npx mcp-xellen-tools install print scholar

${C.dim}Semua target memakai transport stdio (Python lokal, tanpa token).
Klien remote seperti z.ai butuh gateway HTTP: lihat GATEWAY.md.
Kalau Python-mu tidak terdeteksi, set MCP_XELLEN_PYTHON=/path/to/python${C.reset}
`);
  return 0;
}

const [, , sub, ...rest] = process.argv;
let code;
switch ((sub || "help").toLowerCase()) {
  case "install":
    code = cmdInstall(rest);
    break;
  case "check":
  case "doctor":
    code = cmdCheck();
    break;
  case "where":
    code = cmdWhere();
    break;
  default:
    code = usage();
}
process.exit(code);
