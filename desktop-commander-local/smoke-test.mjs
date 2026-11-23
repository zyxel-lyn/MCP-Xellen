import fs from 'node:fs/promises';

const key = (await fs.readFile('D:/MCP-Bridge/secrets/mcp-api-key.txt', 'utf8')).trim();
const endpoint = 'http://127.0.0.1:8021/mcp';
const headers = {
  Authorization: `Bearer ${key}`,
  Accept: 'application/json, text/event-stream',
  'Content-Type': 'application/json'
};

async function rpc(body, sessionId) {
  const h = { ...headers };
  if (sessionId) h['Mcp-Session-Id'] = sessionId;
  const response = await fetch(endpoint, { method: 'POST', headers: h, body: JSON.stringify(body) });
  const text = await response.text();
  if (!response.ok) throw new Error(`HTTP ${response.status}: ${text.slice(0, 300)}`);
  const dataLine = text.split(/\r?\n/).find(line => line.startsWith('data:'));
  if (!dataLine) return { response, payload: null, raw: text };
  return { response, payload: JSON.parse(dataLine.slice(5).trim()), raw: text };
}

const init = await rpc({
  jsonrpc: '2.0', id: 1, method: 'initialize',
  params: {
    protocolVersion: '2024-11-05',
    capabilities: {},
    clientInfo: { name: 'mcp-xellen-smoke-test', version: '1.0.0' }
  }
});
const sessionId = init.response.headers.get('mcp-session-id');
if (!sessionId) throw new Error('MCP session ID missing');
await rpc({ jsonrpc: '2.0', method: 'notifications/initialized' }, sessionId);
const result = await rpc({ jsonrpc: '2.0', id: 2, method: 'tools/list', params: {} }, sessionId);
const tools = result.payload?.result?.tools ?? [];
console.log(JSON.stringify({
  initialize: 'OK',
  toolsList: 'OK',
  toolCount: tools.length,
  tools: tools.map(tool => tool.name)
}, null, 2));
