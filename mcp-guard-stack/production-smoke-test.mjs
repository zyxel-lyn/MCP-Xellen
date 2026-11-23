import assert from "node:assert/strict";
import fs from "node:fs/promises";
import { Client, StreamableHTTPClientTransport } from "@modelcontextprotocol/client";

const apiKey = (await fs.readFile("D:\\MCP-Bridge\\secrets\\mcp-api-key.txt", "utf8")).trim();
const endpoint = new URL("http://127.0.0.1:8020/mcp");

const unauthenticated = await fetch(endpoint, {
  method: "POST",
  headers: { "Content-Type": "application/json", "Accept": "application/json" },
  body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "initialize", params: {
    protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "unauth-test", version: "1.0.0" }
  }})
});
assert.equal(unauthenticated.status, 401);

const client = new Client({ name: "aceh-camera-production-smoke-test", version: "1.0.0" });
const transport = new StreamableHTTPClientTransport(endpoint, {
  requestInit: { headers: { Authorization: "Bearer " + apiKey } }
});

try {
  await client.connect(transport);
  const { tools } = await client.listTools();
  const names = tools.map((tool) => tool.name).sort();
  assert.deepEqual(names, [
    "create_project_directory",
    "delete_project_file",
    "edit_project_file",
    "execute_project_command",
    "list_project_tree",
    "read_project_file",
    "search_project_text",
    "write_project_file"
  ].sort());

  const tree = await client.callTool({ name: "list_project_tree", arguments: { relativePath: ".", depth: 2 } });
  assert.notEqual(tree.isError, true);
  const treeText = tree.content?.[0]?.text || "";
  assert.match(treeText, /composer\.json/);
  assert.doesNotMatch(treeText, /"path":\s+"\.env"/);

  const composer = await client.callTool({ name: "read_project_file", arguments: { relativePath: "composer.json" } });
  assert.notEqual(composer.isError, true);
  assert.match(composer.content?.[0]?.text || "", /laravel\/framework/);

  const envDenied = await client.callTool({ name: "read_project_file", arguments: { relativePath: ".env" } });
  assert.equal(envDenied.isError, true);

  const vendorDenied = await client.callTool({ name: "read_project_file", arguments: { relativePath: "vendor/autoload.php" } });
  assert.equal(vendorDenied.isError, true);

  const traversalDenied = await client.callTool({ name: "read_project_file", arguments: { relativePath: "../../Windows/System32/drivers/etc/hosts" } });
  assert.equal(traversalDenied.isError, true);

  const gitStatus = await client.callTool({ name: "execute_project_command", arguments: { action: "git_status" } });
  assert.notEqual(gitStatus.isError, true);

  console.log("PASS: remote-facing local gateway requires API key (HTTP 401 without key).");
  console.log("PASS: exactly 8 project tools exposed through Supergateway + MCP Guard.");
  console.log("PASS: allowlisted read-only git_status command executes.");
  console.log("PASS: project source tree and composer.json readable.");
  console.log("PASS: .env, vendor, and path traversal remain blocked.");
} finally {
  await client.close();
}
