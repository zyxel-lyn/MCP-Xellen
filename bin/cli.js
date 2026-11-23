#!/usr/bin/env node
/**
 * Installer satu perintah untuk MCP Xellen tools.
 *
 *   npx mcp-xellen-tools install      # daftarkan kbbi + scholar ke Hermes
 *   npx mcp-xellen-tools install kbbi # hanya KBBI
 *   npx mcp-xellen-tools check        # cek prasyarat (python + paket mcp)
 *   npx mcp-xellen-tools where        # tampilkan path instalasi
 *
 * Yang dia lakukan: cari Python yang punya paket `mcp`, lalu jalankan
 * `hermes mcp add` dengan command & args yang benar untuk OS ini.
 */

const { spawnSync } = require("node:child_process");
const path = require("node:path");
const fs = require("node:fs");

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

function testPython(cmd, baseArgs) {
  // versi
  const v = spawnSync(cmd, [...baseArgs, "--version"], {
    encoding: "utf8",
    timeout: 15000,
    windowsHide: true,
  });
  if (v.status !== 0) return null;
  const version = (v.stdout || v.stderr || "").trim();

  // paket mcp
  const m = spawnSync(cmd, [...baseArgs, "-c", "import mcp"], {
    stdio: "ignore",
    timeout: 15000,
    windowsHide: true,
  });
  if (m.status !== 0) return { cmd, baseArgs, version, hasMcp: false };

  // Path ABSOLUT — kalau `hermes` memanggil `python` dari PATH-nya sendiri,
  // bisa jatuh ke interpreter lain yang tidak punya paket mcp.
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

function hasHermes() {
  const r = spawnSync("hermes", ["--version"], {
    encoding: "utf8",
    timeout: 20000,
    windowsHide: true,
    shell: process.platform === "win32",
  });
  return r.status === 0;
}

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

  hasHermes() ? ok("hermes CLI tersedia") : bad("hermes CLI tidak ditemukan di PATH");

  console.log();
  info("Siap dipasang. Jalankan: npx mcp-xellen-tools install");
  return 0;
}

function cmdInstall(which) {
  const targets = which ? [which] : Object.keys(TOOLS);
  for (const t of targets) {
    if (!TOOLS[t]) {
      bad(`tool tidak dikenal: ${t} (pilihan: ${Object.keys(TOOLS).join(", ")})`);
      return 1;
    }
  }

  const pys = findPythons();
  const ready = pys.find((p) => p.hasMcp);
  if (!ready) {
    bad("Tidak ada Python dengan paket 'mcp'. Jalankan dulu: npx mcp-xellen-tools check");
    return 1;
  }

  const pyExe = ready.absolute || ready.cmd;
  console.log(`${C.bold}Memasang MCP Xellen tools${C.reset}`);
  info(`python: ${pyExe}\n`);

  let failed = 0;
  for (const t of targets) {
    const spec = TOOLS[t];
    if (!fs.existsSync(spec.script)) {
      bad(`${t}: script tidak ditemukan`);
      failed++;
      continue;
    }

    // Hapus dulu kalau sudah ada, supaya idempoten.
    spawnSync("hermes", ["mcp", "remove", t], {
      stdio: "ignore",
      timeout: 60000,
      windowsHide: true,
      shell: process.platform === "win32",
      input: "y\n",
    });

    // Path bisa mengandung spasi. `--args` memecah nilainya, jadi path
    // dibungkus tanda kutip agar tetap satu argumen.
    const quotedScript = /\s/.test(spec.script) ? `"${spec.script}"` : spec.script;

    const r = spawnSync(
      "hermes",
      ["mcp", "add", t, "--command", pyExe, "--args", quotedScript],
      {
        encoding: "utf8",
        timeout: 180000,
        windowsHide: true,
        shell: process.platform === "win32",
        input: "Y\n",
      }
    );

    // `add` bisa keluar 0 walau koneksi gagal — verifikasi sendiri.
    const verify = spawnSync("hermes", ["mcp", "test", t], {
      encoding: "utf8",
      timeout: 120000,
      windowsHide: true,
      shell: process.platform === "win32",
    });
    const verified = /Connected/.test((verify.stdout || "") + (verify.stderr || ""));

    if (verified) {
      ok(`${t} — ${spec.desc}`);
    } else {
      bad(`${t} gagal terhubung`);
      const msg = ((verify.stdout || "") + (verify.stderr || "") + (r.stdout || ""))
        .split("\n")
        .filter((l) => l.trim() && !/^\s*$/.test(l))
        .slice(-4)
        .join("\n");
      if (msg.trim()) info(msg);
      failed++;
    }
  }

  console.log();
  if (failed === 0) {
    ok("Semua terpasang. Jalankan /reload-mcp di sesi Hermes, atau mulai sesi baru.");
    return 0;
  }
  bad(`${failed} tool gagal dipasang`);
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
  install [kbbi|scholar]   Daftarkan ke Hermes (default: keduanya)
  check                    Cek prasyarat Python + paket mcp
  where                    Tampilkan path instalasi
  help                     Tampilan ini

${C.cyan}Contoh:${C.reset}
  npx mcp-xellen-tools check
  npx mcp-xellen-tools install
  npx mcp-xellen-tools install kbbi

${C.dim}Kalau Python-mu tidak terdeteksi, set MCP_XELLEN_PYTHON=/path/to/python${C.reset}
`);
  return 0;
}

const [, , sub, arg] = process.argv;
let code;
switch ((sub || "help").toLowerCase()) {
  case "install":
    code = cmdInstall(arg);
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
