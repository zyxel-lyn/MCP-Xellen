import assert from "node:assert/strict";
import fs from "node:fs/promises";
import { setTimeout as delay } from "node:timers/promises";
import { Client, StreamableHTTPClientTransport } from "@modelcontextprotocol/client";

const root = "C:\\laragon\\www\\aceh-camera";
const relativePath = ".mcp-approval-gate-test.txt";
const absolutePath = root + "\\" + relativePath;
const endpoint = "http://127.0.0.1:8020/mcp";
const apiKey = (await fs.readFile("D:\\MCP-Bridge\\secrets\\mcp-api-key.txt", "utf8")).trim();
const uiToken = (await fs.readFile("D:\\MCP-Bridge\\secrets\\mcp-approval-ui-token.txt", "utf8")).trim();

async function exists(file) {
  try { await fs.access(file); return true; }
  catch { return false; }
}

const unauthenticated = await fetch(endpoint, {
  method: "POST",
  headers: { "Content-Type": "application/json", "Accept": "application/json" },
  body: "{}"
});
assert.equal(unauthenticated.status, 401);
console.log("PASS: MCP endpoint rejects requests without API key.");

const client = new Client({ name: "aceh-camera-final-security-test", version: "1.0.0" });
const transport = new StreamableHTTPClientTransport(new URL(endpoint), {
  requestInit: { headers: { Authorization: "Bearer " + apiKey } }
});

async function approveCall(toolName, args, beforeApprovalCheck) {
  let completed = false;
  const promise = client.callTool({ name: toolName, arguments: args }).then((result) => {
    completed = true;
    return result;
  });

  let pending;
  for (let i = 0; i < 80; i += 1) {
    const response = await fetch("http://127.0.0.1:3100/api/pending", {
      headers: { Authorization: "Bearer " + uiToken }
    });
    assert.equal(response.status, 200);
    const queue = await response.json();
    pending = queue.find((item) => item.tool_name === toolName && item.status === "pending");
    if (pending) break;
    await delay(250);
  }

  assert.ok(pending, toolName + " did not enter approval queue.");
  assert.equal(completed, false, toolName + " must remain held before approval.");
  if (beforeApprovalCheck) await beforeApprovalCheck();

  const approved = await fetch("http://127.0.0.1:3100/api/approve/" + pending.id, {
    method: "POST",
    headers: { Authorization: "Bearer " + uiToken }
  });
  assert.equal(approved.status, 200);
  const decision = await approved.json();
  assert.equal(decision.status, "approved");
  assert.ok(decision.receipt_id);

  const result = await promise;
  assert.notEqual(result.isError, true, toolName + " should complete after approval.");
  return result;
}

try {
  assert.equal(await exists(absolutePath), false, "Temporary test file must not already exist.");
  await client.connect(transport);

  const { tools } = await client.listTools();
  const names = tools.map((tool) => tool.name).sort();
  assert.deepEqual(names, [
    "create_project_directory",
    "delete_project_file",
    "edit_project_file",
    "list_project_tree",
    "read_project_file",
    "search_project_text",
    "write_project_file"
  ].sort());
  console.log("PASS: exactly 7 whitelisted tools are exposed.");

  const tree = await client.callTool({ name: "list_project_tree", arguments: { relativePath: ".", depth: 2 } });
  assert.notEqual(tree.isError, true);
  assert.match(tree.content?.[0]?.text || "", /composer\.json/);
  assert.doesNotMatch(tree.content?.[0]?.text || "", /"path":\s+"\.env"/);

  const composer = await client.callTool({ name: "read_project_file", arguments: { relativePath: "composer.json" } });
  assert.notEqual(composer.isError, true);
  assert.match(composer.content?.[0]?.text || "", /laravel\/framework/);

  for (const relative of [".env", "vendor/autoload.php", "../../Windows/System32/drivers/etc/hosts"]) {
    const denied = await client.callTool({ name: "read_project_file", arguments: { relativePath: relative } });
    assert.equal(denied.isError, true, relative + " must be blocked.");
  }
  console.log("PASS: source read works; .env, vendor, and path traversal are blocked.");

  await approveCall("write_project_file", { relativePath, content: "alpha" }, async () => {
    assert.equal(await exists(absolutePath), false, "Write must not happen before approval.");
  });
  assert.equal(await fs.readFile(absolutePath, "utf8"), "alpha");
  console.log("PASS: create-file action waited for approval.");

  await approveCall("edit_project_file", {
    relativePath, oldText: "alpha", newText: "beta", expectedReplacements: 1
  }, async () => {
    assert.equal(await fs.readFile(absolutePath, "utf8"), "alpha", "Edit must not happen before approval.");
  });
  assert.equal(await fs.readFile(absolutePath, "utf8"), "beta");
  console.log("PASS: edit-file action waited for approval.");

  await approveCall("delete_project_file", { relativePath }, async () => {
    assert.equal(await fs.readFile(absolutePath, "utf8"), "beta", "Delete must not happen before approval.");
  });
  assert.equal(await exists(absolutePath), false);
  console.log("PASS: delete-file action waited for approval.");
  console.log("PASS: all approved changes generated signed receipts; test file removed.");
} finally {
  if (await exists(absolutePath)) await fs.unlink(absolutePath);
  await client.close();
}
