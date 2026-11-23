import assert from "node:assert/strict";
import fs from "node:fs/promises";
import { setTimeout as delay } from "node:timers/promises";
import { Client, StreamableHTTPClientTransport } from "@modelcontextprotocol/client";

const root = "C:\\laragon\\www\\aceh-camera";
const relativePath = ".mcp-approval-gate-test.txt";
const absolutePath = root + "\\" + relativePath;
const apiKey = (await fs.readFile("D:\\MCP-Bridge\\secrets\\mcp-api-key.txt", "utf8")).trim();
const uiToken = (await fs.readFile("D:\\MCP-Bridge\\secrets\\mcp-approval-ui-token.txt", "utf8")).trim();

async function exists(file) {
  try { await fs.access(file); return true; }
  catch { return false; }
}

const client = new Client({ name: "aceh-camera-guarded-file-ops-test", version: "1.0.0" });
const transport = new StreamableHTTPClientTransport(new URL("http://127.0.0.1:8020/mcp"), {
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
  assert.ok(decision.receipt_id, "Approval must have a signed receipt.");

  const result = await promise;
  assert.notEqual(result.isError, true, toolName + " should complete after approval.");
  return result;
}

try {
  assert.equal(await exists(absolutePath), false, "Test file must not already exist.");

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

  const unauthorized = await fetch("http://127.0.0.1:8020/mcp", {
    method: "POST",
    headers: { "Content-Type": "application/json", "Accept": "application/json" },
    body: "{}"
  });
  assert.equal(unauthorized.status, 401);
  console.log("PASS: API key required and 7 explicitly whitelisted tools available.");

  await approveCall("write_project_file", {
    relativePath,
    content: "alpha"
  }, async () => {
    assert.equal(await exists(absolutePath), false, "Write must not occur before approval.");
  });
  assert.equal(await fs.readFile(absolutePath, "utf8"), "alpha");
  console.log("PASS: file creation remained pending until approval, then executed.");

  await approveCall("edit_project_file", {
    relativePath,
    oldText: "alpha",
    newText: "beta",
    expectedReplacements: 1
  }, async () => {
    assert.equal(await fs.readFile(absolutePath, "utf8"), "alpha", "Edit must not occur before approval.");
  });
  assert.equal(await fs.readFile(absolutePath, "utf8"), "beta");
  console.log("PASS: file edit remained pending until approval, then executed.");

  await approveCall("delete_project_file", {
    relativePath
  }, async () => {
    assert.equal(await fs.readFile(absolutePath, "utf8"), "beta", "Delete must not occur before approval.");
  });
  assert.equal(await exists(absolutePath), false);
  console.log("PASS: file deletion remained pending until approval, then executed.");
  console.log("PASS: each approved operation produced a signed receipt.");
} finally {
  if (await exists(absolutePath)) await fs.unlink(absolutePath);
  await client.close();
}
