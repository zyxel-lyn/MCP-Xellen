import assert from "node:assert/strict";
import fs from "node:fs/promises";

const token = (await fs.readFile("D:\\MCP-Bridge\\secrets\\mcp-approval-ui-token.txt", "utf8")).trim();
const base = "http://127.0.0.1:3100";

const denied = await fetch(base + "/api/pending");
assert.equal(denied.status, 401, "Approval API must reject requests without token.");

const authorized = await fetch(base + "/api/pending", {
  headers: { Authorization: "Bearer " + token }
});
assert.equal(authorized.status, 200, "Local approval API must accept the private token.");

const pending = await authorized.json();
assert.ok(Array.isArray(pending));

console.log("PASS: approval API rejects unauthenticated requests.");
console.log("PASS: approval API accepts local protected token.");
console.log("PASS: dashboard queue endpoint returns a valid queue.");
