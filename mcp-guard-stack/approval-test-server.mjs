import { McpServer } from "@modelcontextprotocol/server";
import { serveStdio } from "@modelcontextprotocol/server/stdio";

const server = new McpServer({ name: "mcp-guard-approval-test", version: "1.0.0" });
server.registerTool("approval_test", {
  title: "Approval gate test",
  description: "Safe test tool. It performs no filesystem, process, or network changes.",
  inputSchema: {}
}, async () => ({
  content: [{ type: "text", text: "APPROVAL_EXECUTED: dummy action only; no files changed." }]
}));

serveStdio(() => server);
