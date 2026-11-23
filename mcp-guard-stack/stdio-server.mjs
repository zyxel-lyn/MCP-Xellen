import { serveStdio } from "@modelcontextprotocol/server/stdio";
import { createReadOnlyMcpServer } from "../aceh-camera-readonly/server.mjs";

serveStdio(() => createReadOnlyMcpServer(), {
  onerror: (error) => console.error("[aceh-camera-readonly]", error.message)
});
