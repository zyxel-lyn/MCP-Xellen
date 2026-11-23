import readline from "node:readline";
const rl = readline.createInterface({ input: process.stdin, terminal: false });
const send = (m) => process.stdout.write(JSON.stringify(m) + "\n");
const tools = [
  { name: "echo", description: "echo input text", inputSchema: { type: "object", properties: { text: { type: "string" } }, required: ["text"] } },
  { name: "fail_echo", description: "always isError", inputSchema: { type: "object", properties: {} } },
];
rl.on("line", (line) => {
  let m; try { m = JSON.parse(line); } catch { return; }
  if (!m || m.jsonrpc !== "2.0") return;
  if (m.method === "initialize") { send({ jsonrpc: "2.0", id: m.id, result: { protocolVersion: "2025-06-18", capabilities: { tools: {} }, serverInfo: { name: "synthetic", version: "1.0.0" } } }); }
  else if (m.method === "tools/list") { send({ jsonrpc: "2.0", id: m.id, result: { tools } }); }
  else if (m.method === "tools/call") {
    const name = m.params?.name;
    if (name === "echo") send({ jsonrpc: "2.0", id: m.id, result: { content: [{ type: "text", text: "echo:" + (m.params.arguments?.text ?? "") }] } });
    else if (name === "fail_echo") send({ jsonrpc: "2.0", id: m.id, result: { content: [{ type: "text", text: "intentional failure" }], isError: true } });
    else send({ jsonrpc: "2.0", id: m.id, error: { code: -32602, message: "unknown tool" } });
  }
  else if (m.id !== undefined) { send({ jsonrpc: "2.0", id: m.id, result: {} }); }
});