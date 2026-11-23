using McpXellen.Transport;

namespace McpXellen.Core;

/// <summary>Shared singletons wired at startup; replaceable by tests (fresh instances).</summary>
public static class XellenRuntime
{
    public static StdioMcpBridge Bridge { get; set; } = new();
}
