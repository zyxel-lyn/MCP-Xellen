import path from "node:path";
import { Client } from "@modelcontextprotocol/client";
import { StdioClientTransport } from "@modelcontextprotocol/client/stdio";

const bridgeRoot = path.resolve("D:/MCP-Bridge/mcp-guard-stack");
const guardPath = path.join(bridgeRoot, "node_modules", "@permission-protocol", "mcp-guard", "bin", "mcp-guard");
const serverPath = path.join(bridgeRoot, "universal-mcp", "stdio-server.mjs");
const configPath = path.join(bridgeRoot, "universal-mcp", "pp.config.yaml");
const transport = new StdioClientTransport({
  command: process.execPath,
  args: [guardPath, "--config", configPath, "--agent-id", "zai-universal-smoke", "--operator", "local-operator", "--approval-port", "0", "--", process.execPath, serverPath],
  cwd: bridgeRoot,
  stderr: "pipe"
});
const client = new Client({ name: "universal-mcp-guarded-smoke-test", version: "0.1.0" }, { capabilities: {} });

try {
  await client.connect(transport);
  const listed = await client.listTools();
  const names = listed.tools.map((tool) => tool.name);
  const required = [
    "list_workspaces", "inspect_project", "universal_list_project_tree",
    "universal_read_project_file", "universal_search_project_text",
    "universal_write_project_file", "universal_edit_project_file",
    "universal_delete_project_file", "universal_create_project_directory",
    "universal_run_project_command"
  ];
  const missing = required.filter((name) => !names.includes(name));
  if (missing.length) throw new Error("Missing tools: " + missing.join(", "));
  const workspaceResult = await client.callTool({ name: "list_workspaces", arguments: {} });
  const workspaceText = workspaceResult.content?.find((item) => item.type === "text")?.text ?? "";
  if (!workspaceText.includes("ethereal-todolist")) throw new Error("Guarded workspace listing failed.");
  const projectResult = await client.callTool({
    name: "inspect_project",
    arguments: { projectPath: "C:\\laragon\\www\\ethereal-todolist" }
  });
  const projectText = projectResult.content?.find((item) => item.type === "text")?.text ?? "";
  if (!projectText.includes("Laravel/PHP")) throw new Error("Guarded Laravel detection failed: " + projectText);
  console.log(JSON.stringify({
    status: "PASS",
    guard: "PASS",
    toolCount: names.length,
    readTools: "PASS",
    approvalGatedTools: required.filter((name) => name.includes("write") || name.includes("edit") || name.includes("delete") || name.includes("directory") || name.includes("command"))
  }, null, 2));
} finally {
  await client.close().catch(() => {});
}
