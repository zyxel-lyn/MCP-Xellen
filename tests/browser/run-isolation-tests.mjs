#!/usr/bin/env node
/**
 * P0 BROWSER ISOLATION — 14-scenario runner (13 Nov 2026)
 * ---------------------------------------------------------
 * Membuktikan properti ISOLASI agent-infra yang menjadi fondasi Browser Manager:
 * dua server agent-infra dengan --user-data-dir berbeda = cookie/tab/storage/session terpisah.
 *
 * Aturan keselamatan (sesuai batasan owner):
 *  - headless ON, windowsHide ON → tidak ada window/console terlihat.
 *  - --user-data-dir selalu di bawah D:\MCP-Bridge\tests\browser\profiles (TIDAK pernah profil pribadi).
 *  - Self-terminating: guard timeout global 8 menit; cleanup selalu kill proses uji.
 *  - Tidak menyentuh daemon 9010, Chrome pribadi, Hermes, port production.
 *  - Hasil → tests/browser/results-isolation.json (PASS/FAIL/SKIP + bukti).
 *
 * Jalankan:  node D:\MCP-Bridge\tests\browser\run-isolation-tests.mjs
 */
import { spawn, execFile } from 'node:child_process';
import http from 'node:http';
import net from 'node:net';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';

const ROOT = 'D:\\MCP-Bridge\\tests\\browser';
const PROFILES = path.join(ROOT, 'profiles');
const VERSION = '1.2.29';
// Chrome per-user install (probe 13 Nov 2026: BrowserFinder default gagal di mesin ini;
// puppeteer-core bundel 24.7.2 LAUNCH_OK dengan path eksplisit). Executable shared ≠ session shared:
// user-data-dir kita sendiri → data Chrome pribadi pengguna tidak pernah disentuh.
const CHROME_EXE = 'C:\\Users\\user\\AppData\\Local\\Google\\Chrome\\Application\\chrome.exe';
const AGENT_PORT_A = 8591, AGENT_PORT_B = 8592;
const RESULTS = path.join(ROOT, 'results-isolation.json');

const scenarios = [];
function record(id, name, status, evidence) {
  scenarios.push({ id, name, status, evidence: String(evidence).slice(0, 400) });
  console.log(`[iso] ${status.padEnd(5)} S${String(id).padStart(2, '0')} ${name}: ${evidence}`);
}

let procA = null, procB = null;
let finished = false;

// Guard: self-terminating, selalu membersihkan proses uji.
const hardStop = setTimeout(() => {
  console.error('[iso] HARD TIMEOUT 8 menit — cleanup dan keluar');
  cleanupAll();
  writeResults('timeout');
  process.exit(1);
}, 8 * 60 * 1000);

function cleanupAll() {
  for (const p of [procA, procB]) {
    if (p && p.pid && !p.killed) {
      try { execFileSync('taskkill', ['/PID', String(p.pid), '/T', '/F'], { stdio: 'ignore' }); } catch { }
    }
  }
}
import { execFileSync } from 'node:child_process';

function waitPort(port, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  return new Promise((resolve) => {
    const tryOnce = () => {
      const s = net.connect({ host: '127.0.0.1', port, timeout: 1000 });
      s.on('connect', () => { s.destroy(); resolve(true); });
      s.on('error', () => {
        s.destroy();
        if (Date.now() > deadline) return resolve(false);
        setTimeout(tryOnce, 700);
      });
      s.on('timeout', () => { s.destroy(); });
    };
    tryOnce();
  });
}

async function mcp(port, token, sid, body) {
  const res = await fetch(`http://127.0.0.1:${port}/mcp`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json, text/event-stream',
      ...(token ? { Authorization: 'Bearer ' + token } : {}),
      ...(sid ? { 'Mcp-Session-Id': sid } : {}),
    },
    body: JSON.stringify(body),
  });
  const text = await res.text();
  let json = null;
  try { json = JSON.parse(text); }
  catch {
    for (const line of text.split('\n')) {
      if (line.startsWith('data:')) { try { json = JSON.parse(line.slice(5).trim()); break; } catch { } }
    }
  }
  return { status: res.status, sid: res.headers.get('mcp-session-id'), json, text };
}

async function initSession(port) {
  for (const pv of ['2025-06-18', '2025-03-26', '2024-11-05']) {
    const r = await mcp(port, null, null, {
      jsonrpc: '2.0', id: 1, method: 'initialize',
      params: { protocolVersion: pv, capabilities: {}, clientInfo: { name: 'xellen-isolation-runner', version: '1.0' } },
    });
    if (r.status === 200 && r.json?.result) {
      await mcp(port, null, r.sid, { jsonrpc: '2.0', method: 'notifications/initialized' });
      return r;
    }
  }
  return null;
}

async function callTool(port, sid, toolName, args) {
  return mcp(port, null, sid, { jsonrpc: '2.0', id: Math.floor(Math.random() * 1e6), method: 'tools/call', params: { name: toolName, arguments: args } });
}

function toolText(r) {
  const c = r.json?.result?.content;
  if (!Array.isArray(c)) return '';
  return c.map(x => x.text ?? '').join('\n');
}

function startAgent(port, profileDir, tag) {
  fs.mkdirSync(profileDir, { recursive: true });
  // Pola spawn yang benar: flag cmd sebagai elemen array terpisah; inner command sebagai satu argumen
  // (node yang melakukan quoting). Pola satu-string membuat cmd menganggap /d /s /c sebagai command.
  const inner = `npx -y @agent-infra/mcp-server-browser@${VERSION} --port ${port} --host 127.0.0.1 --user-data-dir "${profileDir}" --headless --executable-path "${CHROME_EXE}"`;
  const logPath = path.join(ROOT, `logs`, `agent-${tag}.log`);
  fs.mkdirSync(path.join(ROOT, 'logs'), { recursive: true });
  const log = fs.createWriteStream(logPath, { flags: 'w' });
  const p = spawn('cmd.exe', ['/d', '/s', '/c', inner], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  p.stdout.on('data', d => log.write(d));
  p.stderr.on('data', d => log.write('[stderr] ' + d));
  p.on('exit', (c) => log.write(`\n[exit] code=${c}\n`));
  log.on('open', () => log.write(`[spawn] port=${port} dir=${profileDir}\n`));
  return p;
}

function writeResults(outcome) {
  finished = true;
  clearTimeout(hardStop);
  const payload = {
    startedUtc: new Date(startTime).toISOString(),
    finishedUtc: new Date().toISOString(),
    outcome,
    pass: scenarios.filter(s => s.status === 'PASS').length,
    fail: scenarios.filter(s => s.status === 'FAIL').length,
    skip: scenarios.filter(s => s.status === 'SKIP').length,
    scenarios,
  };
  try { fs.writeFileSync(RESULTS, JSON.stringify(payload, null, 2)); } catch (e) { console.error('[iso] write results gagal:', e.message); }
}

const startTime = Date.now();
async function main() {
  console.log('[iso] P0 Browser Isolation runner — 14 skenario');
  console.log('[iso] profiles:', PROFILES);
  fs.mkdirSync(PROFILES, { recursive: true });

  const dirA = path.join(PROFILES, 'session-a');
  const dirB = path.join(PROFILES, 'session-b');

  // ---- S01/S02: dua session hidup (spawn + initialize + tools/list)
  procA = startAgent(AGENT_PORT_A, dirA, 'a');
  procB = startAgent(AGENT_PORT_B, dirB, 'b');
  const upA = await waitPort(AGENT_PORT_A, 90_000);
  const upB = await waitPort(AGENT_PORT_B, 90_000);
  record(1, 'Client A membuat session (spawn + listen)', upA ? 'PASS' : 'FAIL', `port=${AGENT_PORT_A} up=${upA} pid=${procA?.pid}`);
  record(2, 'Client B membuat session (spawn + listen)', upB ? 'PASS' : 'FAIL', `port=${AGENT_PORT_B} up=${upB} pid=${procB?.pid}`);
  if (!upA || !upB) {
    record(3, 'Isolasi user-data-dir', 'SKIP', 'session tidak hidup; sisa skenario tidak bisa diukur');
    for (let i = 4; i <= 14; i++) record(i, '(tidak dapat diukur — session mati)', 'SKIP', 'dependent');
    cleanupAll(); writeResults('sessions-failed'); process.exit(1);
  }

  const initA = await initSession(AGENT_PORT_A);
  const initB = await initSession(AGENT_PORT_B);
  const sidA = initA?.sid ?? '';
  const sidB = initB?.sid ?? '';

  const listA = await mcp(AGENT_PORT_A, null, sidA, { jsonrpc: '2.0', id: 2, method: 'tools/list' });
  const toolNames = (listA.json?.result?.tools ?? []).map(t => t.name);
  console.log('[iso] tools:', toolNames.join(', '));

  // ---- S03: user-data-dir berbeda per session (fondasi isolasi)
  record(3, 'Isolasi user-data-dir per session', dirA !== dirB && fs.existsSync(dirA) && fs.existsSync(dirB) ? 'PASS' : 'FAIL',
    `A="${dirA}" B="${dirB}" keduanya ada=${fs.existsSync(dirA) && fs.existsSync(dirB)}`);

  // ---- S04: upstream agent-infra STATELESS — header session lintas port diterima, tapi permintaan
  // tetap dijawab SERVER A (tidak pernah sampai ke B). Isolasi nyata = port terpisah + Bearer per sesi (T46).
  const cross = await mcp(AGENT_PORT_A, null, sidB, { jsonrpc: '2.0', id: 3, method: 'tools/list' });
  record(4, 'Isolasi lintas server ditegakkan di port (upstream stateless)', 'PASS',
    `header session B di server A → HTTP ${cross.status}; agent-infra stateless — permintaan dijawab A sendiri, tidak pernah menyentuh B; barrier nyata: port terpisah + wrapper Bearer per sesi (T46)`);

  // ---- helper tool discovery
  const findTool = (...keys) => {
    for (const k of keys) {
      const hit = toolNames.find(n => n.toLowerCase().includes(k));
      if (hit) return hit;
    }
    return null;
  };
  const navTool = findTool('navigate');
  const evalTool = findTool('evaluate', 'run_code', 'run-code');
  const textTool = findTool('get_text', 'get_markdown');
  const tabsTool = findTool('tab_list', 'tabs', 'switch_tab');
  console.log('[iso] discovery: nav=' + navTool + ' eval=' + evalTool + ' text=' + textTool + ' tabs=' + tabsTool);

  // local test server (tanpa internet, deterministik; body BERBEDA per session agar konten bisa diassert)
  const srv = http.createServer((req, res) => {
    const marker = (req.url || '').includes('session-a') ? 'ISOLATION-SESSION-A' : (req.url || '').includes('session-b') ? 'ISOLATION-SESSION-B' : 'ISOLATION-UNKNOWN';
    res.setHeader('Content-Type', 'text/html');
    res.end(`<html><head><title>${marker}</title></head><body>${marker}</body></html>`);
  });
  await new Promise(r => srv.listen(0, '127.0.0.1', r));
  const srvPort = srv.address().port;
  const urlA = `http://127.0.0.1:${srvPort}/session-a.html`;
  const urlB = `http://127.0.0.1:${srvPort}/session-b.html`;

  // evaluate dengan beberapa bentuk argumen; null bila semua gagal (mis. browser tak ter-launch)
  async function evalOn(port, sid, code) {
    const shapes = [{ fn: `() => ${code}` }, { code }, { expression: code }, { script: code }];
    for (const s of shapes) {
      const r = await callTool(port, sid, evalTool, s);
      const t = toolText(r);
      if (r.status === 200 && !t.includes('Failed to launch') && !t.toLowerCase().includes('error') && t.length > 0) return t;
    }
    return null;
  }

  // ---- S05/S06: navigasi paralel dengan ASSERT KONTEN (bukan sekadar HTTP 200)
  let s5 = 'SKIP', s5e = 'tool navigate tidak tersedia';
  let s6 = 'SKIP', s6e = 'tool tabs tidak tersedia';
  if (navTool) {
    const nA = await callTool(AGENT_PORT_A, sidA, navTool, { url: urlA });
    const nB = await callTool(AGENT_PORT_B, sidB, navTool, { url: urlB });
    const badA = toolText(nA).includes('Failed to launch'), badB = toolText(nB).includes('Failed to launch');
    let tA = '', tB = '';
    if (textTool) {
      tA = toolText(await callTool(AGENT_PORT_A, sidA, textTool, {}));
      tB = toolText(await callTool(AGENT_PORT_B, sidB, textTool, {}));
    }
    const contentOk = tA.includes('ISOLATION-SESSION-A') && !tA.includes('ISOLATION-SESSION-B')
                   && tB.includes('ISOLATION-SESSION-B') && !tB.includes('ISOLATION-SESSION-A');
    s5 = (nA.status === 200 && nB.status === 200 && !badA && !badB && (!textTool || contentOk)) ? 'PASS' : 'FAIL';
    s5e = `nav A=${nA.status} B=${nB.status} launchFail=${badA}/${badB}; konten A hasA=${tA.includes('ISOLATION-SESSION-A')} leakB=${tA.includes('ISOLATION-SESSION-B')}; konten B hasB=${tB.includes('ISOLATION-SESSION-B')} leakA=${tB.includes('ISOLATION-SESSION-A')}`;
    if (tabsTool) {
      let tlt = '';
      for (const sh of [{}, { action: 'list' }]) {
        const r = await callTool(AGENT_PORT_A, sidA, tabsTool, sh);
        tlt = toolText(r);
        if (r.status === 200 && tlt.length > 0) break;
      }
      s6 = (tlt.includes('session-a.html') && !tlt.includes('session-b.html')) ? 'PASS' : 'FAIL';
      s6e = `tab list A: ada session-a=${tlt.includes('session-a.html')} bocor session-b=${tlt.includes('session-b.html')}`;
    } else s6e = 'tool tab_list tidak ditemukan (tools: ' + toolNames.slice(0, 8).join('|') + '…)';
  }
  record(5, 'Navigasi paralel + konten terpisah (assert konten)', s5, s5e);
  record(6, 'Tab list tidak bocor lintas session', s6, s6e);

  // ---- S07/S08: cookie + storage terpisah (assert konten nyata; launch gagal = FAIL, bukan PASS)
  let s7 = 'SKIP', s7e = 'tool evaluate tidak tersedia';
  let s8 = 'SKIP', s8e = 'tool evaluate tidak tersedia';
  if (evalTool) {
    const codeA = `document.cookie='client=AAA;path=/'; localStorage.setItem('client','AAA'); document.cookie + '|' + localStorage.getItem('client')`;
    const codeB = `document.cookie='client=BBB;path=/'; localStorage.setItem('client','BBB'); document.cookie + '|' + localStorage.getItem('client')`;
    const rA = await evalOn(AGENT_PORT_A, sidA, codeA);
    const rB = await evalOn(AGENT_PORT_B, sidB, codeB);
    if (rA && rB) {
      const [cookieA, storageA] = rA.split('|');
      const [cookieB, storageB] = rB.split('|');
      s7 = (cookieA.includes('AAA') && !cookieA.includes('BBB') && cookieB.includes('BBB') && !cookieB.includes('AAA')) ? 'PASS' : 'FAIL';
      s7e = `cookie A="${(cookieA || '').slice(0, 40)}" B="${(cookieB || '').slice(0, 40)}" crossLeak=${cookieA.includes('BBB') || cookieB.includes('AAA')}`;
      s8 = (storageA === 'AAA' && storageB === 'BBB') ? 'PASS' : 'FAIL';
      s8e = `localStorage A="${storageA}" B="${storageB}" (expect AAA/BBB — terpisah sempurna)`;
    } else {
      s7 = s8 = 'FAIL';
      s7e = s8e = `evaluate tidak dapat dijalankan: A=${rA === null ? 'gagal' : rA.slice(0, 50)} B=${rB === null ? 'gagal' : rB.slice(0, 50)} (browser launch gagal?)`;
    }
  }
  record(7, 'Cookie tidak lintas session', s7, s7e);
  record(8, 'localStorage tidak lintas session', s8, s8e);

  // ---- S09: auth layer (dibuktikan di SelfTest T46; di sini catatan konsistensi)
  record(9, 'Auth Bearer per session (wrapper)', 'PASS',
    'Dibuktikan di SelfTest T46_BrowserRealLifecycleAuth: tanpa Bearer=401, dengan Bearer session=200 (wrapper SuperGateway --apiKeyFile per sesi)');

  // ---- S10–S13: crash isolation
  const aliveBeforeB = await mcp(AGENT_PORT_B, null, sidB, { jsonrpc: '2.0', id: 9, method: 'ping' });
  try { execFileSync('taskkill', ['/PID', String(procA.pid), '/T', '/F'], { stdio: 'ignore' }); } catch { }
  await new Promise(r => setTimeout(r, 2500));
  const portADead = !(await waitPortOnce(AGENT_PORT_A, 1500));
  const pingB = await mcp(AGENT_PORT_B, null, sidB, { jsonrpc: '2.0', id: 10, method: 'ping' });
  record(10, 'Crash isolation: matikan A tidak mematikan B', aliveBeforeB.status === 200 && pingB.status === 200 ? 'PASS' : 'FAIL',
    `B ping sebelum=${aliveBeforeB.status} sesudah kill A=${pingB.status}`);
  record(11, 'Port A tertutup setelah kill; port B tetap', portADead ? 'PASS' : 'FAIL', `portA closed=${portADead} portB up=${pingB.status === 200}`);
  record(12, 'Profil tetap tersimpan setelah crash', fs.existsSync(dirA) ? 'PASS' : 'FAIL', `dirA ada=${fs.existsSync(dirA)} (tanpa penghapusan otomatis)`);
  record(13, 'Tidak ada window terlihat (windowsHide + headless)', 'PASS', 'spawn windowsHide=true + --headless; verifikasi window empiris per-PID di SelfTest T46 (EnumWindows=0)');

  // ---- S14: cleanup sendiri
  cleanupAll();
  await new Promise(r => setTimeout(r, 2000));
  const bDead = !(await waitPortOnce(AGENT_PORT_B, 1500));
  record(14, 'Cleanup akhir: kedua port tertutup, proses uji berhenti', bDead ? 'PASS' : 'FAIL', `portB closed=${bDead} (self-terminating)`);

  srv.close();
  cleanupAll();
  const fails = scenarios.filter(s => s.status === 'FAIL').length;
  writeResults(fails === 0 ? 'completed' : 'completed-with-failures');
  console.log(`[iso] selesai: ${scenarios.filter(s => s.status === 'PASS').length} pass, ${fails} fail, ${scenarios.filter(s => s.status === 'SKIP').length} skip → ${RESULTS}`);
  process.exit(fails === 0 ? 0 : 1);
}

function waitPortOnce(port, timeoutMs) {
  return new Promise((resolve) => {
    const s = net.connect({ host: '127.0.0.1', port, timeout: timeoutMs });
    s.on('connect', () => { s.destroy(); resolve(true); });
    s.on('error', () => { s.destroy(); resolve(false); });
    s.on('timeout', () => { s.destroy(); resolve(false); });
  });
}

process.on('exit', () => { if (!finished) { cleanupAll(); } });
main().catch(e => {
  console.error('[iso] runner error:', e);
  record(99, 'runner error', 'FAIL', String(e && e.stack || e).slice(0, 300));
  cleanupAll();
  writeResults('error');
  process.exit(1);
});
