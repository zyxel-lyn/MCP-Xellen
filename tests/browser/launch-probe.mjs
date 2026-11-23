#!/usr/bin/env node
// Probe: reproduc launch persis seperti agent-infra 1.2.29 (puppeteer-core bundel 24.7.2)
// dengan dumpio:true agar stderr Chrome terlihat. Headless, self-terminating, tanpa window.
import { createRequire } from 'node:module';
const require = createRequire('C:/Users/user/AppData/Roaming/npm/node_modules/@agent-infra/mcp-server-browser/dist/index.cjs');
const puppeteer = require('puppeteer-core');

const EXE = 'C:\\Users\\user\\AppData\\Local\\Google\\Chrome\\Application\\chrome.exe';
const baseArgs = [
  '--no-sandbox', '--mute-audio', '--disable-gpu', '--disable-blink-features=AutomationControlled',
  '--disable-infobars', '--disable-background-timer-throttling', '--disable-popup-blocking',
  '--disable-backgrounding-occluded-windows', '--disable-renderer-backgrounding',
  '--disable-window-activation', '--disable-focus-on-load', '--no-default-browser-check',
  '--disable-web-security', '--disable-features=IsolateOrigins,site-per-process',
  '--disable-site-isolation-trials', '--window-size=1280,890',
];

async function attempt(name, opts) {
  console.log(`\n=== ${name} ===`);
  let browser;
  try {
    browser = await puppeteer.launch({ dumpio: true, timeout: 30000, ...opts });
    const page = await browser.newPage();
    await page.goto('about:blank', { timeout: 15000 });
    console.log(`${name}: LAUNCH_OK title="${await page.title()}"`);
    await browser.close();
    return true;
  } catch (e) {
    console.log(`${name}: LAUNCH_FAIL ${e && e.message ? e.message.split('\n').slice(0, 6).join(' | ') : e}`);
    try { if (browser) await browser.close(); } catch { }
    return false;
  }
}

const ud = (n) => `D:\\MCP-Bridge\\tests\\browser\\probe-profile-${n}`;
let ok = false;
ok = (await attempt('A-plain-headless', { browser: 'chrome', executablePath: EXE, headless: true, userDataDir: ud('a'), args: baseArgs })) || ok;
if (!ok) ok = (await attempt('B-minimal-flags', { browser: 'chrome', executablePath: EXE, headless: true, userDataDir: ud('b'), args: ['--no-sandbox', '--disable-gpu'] })) || ok;
if (!ok) ok = (await attempt('C-headless-new-flag', { browser: 'chrome', executablePath: EXE, headless: 'new', userDataDir: ud('c'), args: baseArgs })) || ok;
if (!ok) ok = (await attempt('D-no-args', { browser: 'chrome', executablePath: EXE, headless: true, userDataDir: ud('d') })) || ok;
console.log(`\nPROBE_RESULT=${ok ? 'SOME_LAUNCH_OK' : 'ALL_FAILED'}`);
process.exit(ok ? 0 : 1);
