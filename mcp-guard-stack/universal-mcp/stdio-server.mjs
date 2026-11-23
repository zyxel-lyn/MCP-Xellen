import { serveStdio } from "@modelcontextprotocol/server/stdio";
import { createReadOnlyMcpServer } from "../../aceh-camera-readonly/server.mjs";
import { registerUniversalTools } from "./server.mjs";

const server = createReadOnlyMcpServer();
registerUniversalTools(server);

serveStdio(() => server, {
  onerror: (error) => console.error("[zai-universal-mcp]", error.message)
});
