import path from "node:path";
import { Client } from "@modelcontextprotocol/client";
import { StdioClientTransport } from "@modelcontextprotocol/client/stdio";

const bridgeRoot = path.resolve("D:/MCP-Bridge/mcp-guard-stack");
const serverPath = path.join(bridgeRoot, "universal-mcp", "stdio-server.mjs");
const transport = new StdioClientTransport({
  command: process.execPath,
  args: [serverPath],
  cwd: bridgeRoot,
  stderr: "pipe"
});
const client = new Client({ name: "universal-mcp-smoke-test", version: "0.1.0" }, { capabilities: {} });

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
  if (!workspaceText.includes("ethereal-todolist")) throw new Error("Workspace listing did not include expected Laravel project.");
  const projectResult = await client.callTool({
    name: "inspect_project",
    arguments: { projectPath: "C:\\laragon\\www\\ethereal-todolist" }
  });
  const projectText = projectResult.content?.find((item) => item.type === "text")?.text ?? "";
  if (!projectText.includes("Laravel/PHP")) throw new Error("Laravel framework detection failed: " + projectText);
  const dotnetResult = await client.callTool({
    name: "inspect_project",
    arguments: { projectPath: "D:\\Unity\\Projects\\1" }
  });
  const dotnetText = dotnetResult.content?.find((item) => item.type === "text")?.text ?? "";
  if (!dotnetText.includes(".NET/C#")) throw new Error(".NET framework detection failed: " + dotnetText);
  const flutterResult = await client.callTool({
    name: "inspect_project",
    arguments: { projectPath: "D:\\Wildanun Mukhallid\\Project\\jadwal-kuliah" }
  });
  const flutterText = flutterResult.content?.find((item) => item.type === "text")?.text ?? "";
  if (!flutterText.includes("Flutter/Dart")) throw new Error("Flutter framework detection failed: " + flutterText);
  const gitResult = await client.callTool({
    name: "universal_run_project_command",
    arguments: {
      projectPath: "D:\\Wildanun Mukhallid\\Project\\iPhone-Gaming-Console-Lab",
      executable: "git",
      args: ["status", "--short", "--branch"],
      timeoutMs: 10000
    }
  });
  const gitText = gitResult.content?.find((item) => item.type === "text")?.text ?? "";
  if (!gitText.includes("EXIT CODE: 0")) throw new Error("Git command execution failed: " + gitText);
  const npmResult = await client.callTool({
    name: "universal_run_project_command",
    arguments: {
      projectPath: "C:\\laragon\\www\\ethereal-todolist",
      executable: "npm",
      args: ["--version"],
      timeoutMs: 10000
    }
  });
  const npmText = npmResult.content?.find((item) => item.type === "text")?.text ?? "";
  if (!npmText.includes("EXIT CODE: 0")) throw new Error("npm command execution failed: " + npmText);
  console.log(JSON.stringify({
    status: "PASS",
    toolCount: names.length,
    universalTools: required,
    workspaceListing: "PASS",
    frameworkDetection: ["Laravel/PHP", ".NET/C#", "Flutter/Dart"],
    commandExecution: ["git status", "npm --version"]
  }, null, 2));
} finally {
  await client.close().catch(() => {});
}
