import assert from "node:assert/strict";
import fs from "node:fs/promises";
import { setTimeout as delay } from "node:timers/promises";
import { Client, StreamableHTTPClientTransport } from "@modelcontextprotocol/client";

const apiKey = (await fs.readFile("D:\\MCP-Bridge\\secrets\\mcp-api-key.txt", "utf8")).trim();
const uiToken = (await fs.readFile("D:\\MCP-Bridge\\secrets\\mcp-approval-ui-token.txt", "utf8")).trim();

const client = new Client({ name: "mcp-guard-approval-e2e", version: "1.0.0" });
const transport = new StreamableHTTPClientTransport(new URL("http://127.0.0.1:8021/mcp"), {
  requestInit: { headers: { Authorization: "Bearer " + apiKey } }
});

const authDenied = await fetch("http://127.0.0.1:8021/mcp", {
  method: "POST",
  headers: { "Content-Type": "application/json", "Accept": "application/json", "Content-Length": "2" },
  body: "{}"
});
assert.equal(authDenied.status, 401, "API key must be required.");

await client.connect(transport);
const listed = await client.listTools();
assert.deepEqual(listed.tools.map((x) => x.name), ["approval_test"], "Only test tool should be exposed.");
console.log("PASS: gateway API key required and only approval_test is exposed.");

let callCompleted = false;
const callPromise = client.callTool({ name: "approval_test", arguments: {} }).then((value) => {
  callCompleted = true;
  return value;
});

let pendingItem;
for (let i = 0; i < 80; i += 1) {
  const response = await fetch("http://127.0.0.1:3101/api/pending", {
    headers: { Authorization: "Bearer " + uiToken }
  });
  assert.equal(response.status, 200, "Approval API token must authorize local dashboard API.");
  const queue = await response.json();
  pendingItem = queue.find((item) => item.tool_name === "approval_test");
  if (pendingItem) break;
  await delay(250);
}
assert.ok(pendingItem, "Approval request did not appear in queue.");
assert.equal(pendingItem.status, "pending");
assert.equal(callCompleted, false, "Tool call must remain blocked before approval.");
console.log("PASS: MCP Guard held the tool call pending human approval.");

const approval = await fetch("http://127.0.0.1:3101/api/approve/" + pendingItem.id, {
  method: "POST",
  headers: { Authorization: "Bearer " + uiToken }
});
assert.equal(approval.status, 200, "Local approval action should succeed.");
const approvalResult = await approval.json();
assert.equal(approvalResult.status, "approved");

const result = await callPromise;
assert.notEqual(result.isError, true);
assert.match(result.content?.[0]?.text || "", /APPROVAL_EXECUTED/);
console.log("PASS: after approval, Guard released the held request.");
console.log("PASS: approval recorded with signed receipt ID:", Boolean(approvalResult.receipt_id));

await client.close();
