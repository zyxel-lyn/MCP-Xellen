using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpXellen.Core;

/// <summary>Root configuration (registry) for Xellen Core. Stored at D:\MCP-Bridge\config\xellen.json.
/// Tokens/passwords are NEVER stored here — only file PATH references (AuthTokenFile).</summary>
public sealed class XellenConfig
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("migratedFrom")] public string MigratedFrom { get; set; } = "";
    [JsonPropertyName("createdUtc")] public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("updatedUtc")] public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("log")] public LogSettings Log { get; set; } = new();
    [JsonPropertyName("orphanSweep")] public OrphanSweepSettings OrphanSweep { get; set; } = new();
    [JsonPropertyName("childPolicy")] public ChildPolicySettings ChildPolicy { get; set; } = new();
    /// <summary>Process cmdline substrings that must NEVER be terminated by any Xellen sweep/policy.</summary>
    [JsonPropertyName("protectedProcessPatterns")] public List<string> ProtectedProcessPatterns { get; set; } = DefaultProtectedPatterns();
    [JsonPropertyName("servers")] public List<ServerConfig> Servers { get; set; } = new();
    [JsonPropertyName("projects")] public List<ProjectConfig> Projects { get; set; } = new();
    [JsonPropertyName("nativeTransport")] public NativeTransportSettings NativeTransport { get; set; } = new();
    [JsonPropertyName("browserManager")] public BrowserManagerConfig BrowserManager { get; set; } = new();

    public static List<string> DefaultProtectedPatterns() => new()
    {
        // Wildcard kill is forbidden. These patterns are re-checked immediately before every kill.
        "construct3-mcp",
        "_bmcp",
        "@browsermcp",
        "reactbits",
        "shadcn",
        "@ant-design",
        "\\Google\\Chrome\\Application",
        "ZCode",
        "ZCODE",
    };
}

public sealed class LogSettings
{
    [JsonPropertyName("directory")] public string Directory { get; set; } = @"D:\MCP-Bridge\logs";
    [JsonPropertyName("baseFileName")] public string BaseFileName { get; set; } = "xellen-core";
    [JsonPropertyName("maxFileBytes")] public long MaxFileBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
    [JsonPropertyName("maxBackups")] public int MaxBackups { get; set; } = 5;
    [JsonPropertyName("minLevel")] public string MinLevel { get; set; } = "Info"; // Debug|Info|Warn|Error
}

public sealed class OrphanSweepSettings
{
    /// <summary>"auto" (kill provably-owned orphans), "report" (log only), "off".</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = "auto";
    [JsonPropertyName("startupDelaySeconds")] public int StartupDelaySeconds { get; set; } = 45;
    [JsonPropertyName("intervalSeconds")] public int IntervalSeconds { get; set; } = 300;
}

public sealed class ChildPolicySettings
{
    /// <summary>Session-children caps apply ONLY to children of processes tracked by ProcessHost.</summary>
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    /// <summary>SAFETY-CRITICAL. Must stay false. Automatic culling is doubly gated: (1) this flag
    /// AND (2) a real idle-proof provider (ProcessHost.IdleProofProvider, which is null in P1.x).
    /// P1.x cannot prove a session child is idle from outside (gateway-owned TCP sessions are not
    /// attributable to a child; missing stdout/stderr output does NOT imply idle), so culling never
    /// executes even if this flag is true. Old configs without this field deserialize to false,
    /// so an upgrade can never start killing active children.</summary>
    [JsonPropertyName("autoCullEnabled")] public bool AutoCullEnabled { get; set; } = false;
    /// <summary>Hard ceiling for a session child's lifetime (minutes). Informational until idle-aware termination exists.</summary>
    [JsonPropertyName("maxChildLifetimeMinutes")] public int MaxChildLifetimeMinutes { get; set; } = 240;
    /// <summary>Max concurrent session children (direct wrappers) per tracked server. RAM circuit breaker.</summary>
    [JsonPropertyName("maxConcurrentChildren")] public int MaxConcurrentChildren { get; set; } = 24;
    /// <summary>Never cull a child younger than this when the concurrency cap is exceeded (protect active work).</summary>
    [JsonPropertyName("minAgeForCullMinutes")] public int MinAgeForCullMinutes { get; set; } = 30;
}

public sealed class RestartPolicySettings
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("maxAttempts")] public int MaxAttempts { get; set; } = 5;
    [JsonPropertyName("backoffBaseSeconds")] public int BackoffBaseSeconds { get; set; } = 5;
    [JsonPropertyName("backoffMaxSeconds")] public int BackoffMaxSeconds { get; set; } = 300;
    /// <summary>Running continuously for this long resets the backoff attempt counter.</summary>
    [JsonPropertyName("stableResetSeconds")] public int StableResetSeconds { get; set; } = 120;
}

public sealed class NativeTransportSettings
{
    /// <summary>Native Streamable HTTP host (binds 127.0.0.1). Disabled by default in config so
    /// existing installs opt in deliberately; SelfTest starts it explicitly on a test port.</summary>
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = false;
    /// <summary>Candidate port from the audit; the host probes availability and picks the next
    /// free port (8300-8349) without touching any existing listener.</summary>
    [JsonPropertyName("preferredPort")] public int PreferredPort { get; set; } = 8300;
    [JsonPropertyName("sessionTimeoutMinutes")] public int SessionTimeoutMinutes { get; set; } = 30;
    [JsonPropertyName("rateLimitPerMinute")] public int RateLimitPerMinute { get; set; } = 120;
    [JsonPropertyName("maxBodyBytes")] public int MaxBodyBytes { get; set; } = 2 * 1024 * 1024;
}

public sealed class BrowserManagerConfig
{
    /// <summary>Opt-in: browser sessions hanya dibuat lewat tab Browser; production default OFF.</summary>
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = false;
    [JsonPropertyName("headless")] public bool Headless { get; set; } = true;
    [JsonPropertyName("idleTimeoutMinutes")] public int IdleTimeoutMinutes { get; set; } = 15;
    [JsonPropertyName("maxActiveSessions")] public int MaxActiveSessions { get; set; } = 3;
    [JsonPropertyName("agentPortsBase")] public int AgentPortsBase { get; set; } = 8500;
    [JsonPropertyName("wrapperPortsBase")] public int WrapperPortsBase { get; set; } = 8600;
    [JsonPropertyName("profileRoot")] public string ProfileRoot { get; set; } = @"D:\MCP-Bridge\browser-profiles";
    [JsonPropertyName("agentInfraVersion")] public string AgentInfraVersion { get; set; } = "1.2.29";
    /// <summary>Informational in v1: hard lifetime cap is NOT enforced (active sessions are never forcibly
    /// stopped, matching ChildPolicySettings semantics). Only idle timeout is enforced.</summary>
    [JsonPropertyName("sessionTimeoutMinutes")] public int SessionTimeoutMinutes { get; set; } = 30;
    /// <summary>Root for per-session token files (secrets\browser-sessions\<sessionId>\token.txt).</summary>
    [JsonPropertyName("secretsRoot")] public string SecretsRoot { get; set; } = @"D:\MCP-Bridge\secrets\browser-sessions";
    /// <summary>Persisted ownership-fingerprint state used by the startup orphan sweep.</summary>
    [JsonPropertyName("statePath")] public string StatePath { get; set; } = @"D:\MCP-Bridge\config\browser-sessions-state.json";
    /// <summary>Optional explicit browser executable (agent-infra --executable-path). Empty = agent-infra
    /// BrowserFinder default. Empirical note 13 Nov 2026: the default finder failed on this machine
    /// (Chrome is a per-user install); a puppeteer probe proved launch works with the explicit path.</summary>
    [JsonPropertyName("browserExecutablePath")] public string BrowserExecutablePath { get; set; } = "";
}

public sealed class ServerConfig
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("command")] public string Command { get; set; } = "";
    [JsonPropertyName("arguments")] public List<string> Arguments { get; set; } = new();
    [JsonPropertyName("workingDirectory")] public string WorkingDirectory { get; set; } = "";
    [JsonPropertyName("environment")] public Dictionary<string, string> Environment { get; set; } = new();
    /// <summary>stdio-supergateway | stdio | http-upstream (informational in P1).</summary>
    [JsonPropertyName("transport")] public string Transport { get; set; } = "stdio-supergateway";
    [JsonPropertyName("port")] public int? Port { get; set; }
    [JsonPropertyName("healthPath")] public string? HealthPath { get; set; }
    /// <summary>PATH to the token file. The token value itself is never stored or logged.</summary>
    [JsonPropertyName("authTokenFile")] public string? AuthTokenFile { get; set; }
    [JsonPropertyName("autoStart")] public bool AutoStart { get; set; } = false;
    [JsonPropertyName("restartPolicy")] public RestartPolicySettings RestartPolicy { get; set; } = new();
    [JsonPropertyName("stopTimeoutSeconds")] public int StopTimeoutSeconds { get; set; } = 10;
    /// <summary>Cmdline substrings that prove a process belongs to THIS server (used by orphan sweep).</summary>
    [JsonPropertyName("ownershipPatterns")] public List<string> OwnershipPatterns { get; set; } = new();
    /// <summary>Cmdline substrings identifying session children spawned by this server.</summary>
    [JsonPropertyName("childProcessPatterns")] public List<string> ChildProcessPatterns { get; set; } = new();
}

/// <summary>Load/save/migrate/backup of xellen.json. All writes are preceded by an automatic backup.</summary>
public static class ConfigStore
{
    /// <summary>When set (self-test sandbox), ALL config/state IO is redirected under this dir
    /// so automated tests can never read or write the production registry.</summary>
    public static string? BaseDirOverride { get; set; }
    public static string ConfigDir => BaseDirOverride ?? @"D:\MCP-Bridge\config";
    public static string ConfigPath => Path.Combine(ConfigDir, "xellen.json");
    public static string BackupDir => Path.Combine(ConfigDir, "backups");
    public static string RuntimeStatePath => Path.Combine(ConfigDir, "runtime-state.json");
    private const int MaxBackupsKept = 10;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static XellenConfig LoadOrCreate()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(BackupDir);
        if (File.Exists(ConfigPath))
        {
            var cfg = JsonSerializer.Deserialize<XellenConfig>(File.ReadAllText(ConfigPath), JsonOpts);
            if (cfg != null) return cfg;
        }
        var migrated = CreateDefaultConfig();
        Save(migrated);
        return migrated;
    }

    /// <summary>Default config reproduces the current hardcoded staging gateway byte-for-byte
    /// (verified against the live supergateway command line of PID 8332 on 2026-11-11).</summary>
    public static XellenConfig CreateDefaultConfig()
    {
        var dcEntry = @"D:\MCP-Bridge\desktop-commander-local\package\dist\index.js";
        var gw = @"D:\MCP-Bridge\mcp-guard-stack\node_modules\supergateway\dist\index.js";
        var token = @"D:\MCP-Bridge\secrets\mcp-xellen-staging-token.txt";
        var stdioArg = "node \"" + dcEntry + "\" --no-onboarding";
        var cfg = new XellenConfig
        {
            MigratedFrom = "hardcoded-Program.cs-2026-11-11",
            Servers =
            {
                new ServerConfig
                {
                    Id = "dc-staging-8023",
                    Name = "Desktop Commander (Staging 8023)",
                    Command = @"C:\Program Files\nodejs\node.exe",
                    Arguments = new List<string>
                    {
                        gw, "--stdio", stdioArg, "--outputTransport", "streamableHttp",
                        "--port", "8023", "--host", "127.0.0.1", "--apiKeyFile", token,
                        "--stateful", "--sessionTimeout", "1800000", "--healthEndpoint", "/healthz",
                    },
                    WorkingDirectory = @"D:\MCP-Bridge",
                    Transport = "stdio-supergateway",
                    Port = 8023,
                    HealthPath = "/healthz",
                    AuthTokenFile = token,
                    AutoStart = true,
                    StopTimeoutSeconds = 10,
                    OwnershipPatterns = new List<string>
                    {
                        @"desktop-commander-local\package\dist\index.js",
                        @"mcp-guard-stack\node_modules\supergateway\dist\index.js",
                    },
                    ChildProcessPatterns = new List<string>
                    {
                        @"desktop-commander-local\package\dist\index.js",
                    },
                },
            },
        };
        return cfg;
    }

    public static void Save(XellenConfig cfg)
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(BackupDir);
        cfg.UpdatedUtc = DateTime.UtcNow;
        // Automatic backup of the previous version before every change.
        if (File.Exists(ConfigPath))
        {
            var bak = Path.Combine(BackupDir, $"xellen-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(ConfigPath, bak, overwrite: true);
            // Retention: keep only the newest MaxBackupsKept backups.
            var olds = Directory.GetFiles(BackupDir, "xellen-*.json")
                .OrderByDescending(f => f).Skip(MaxBackupsKept).ToList();
            foreach (var o in olds) { try { File.Delete(o); } catch { } }
        }
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, JsonOpts));
    }

    public static RuntimeState LoadRuntimeState()
    {
        try
        {
            if (File.Exists(RuntimeStatePath))
            {
                var st = JsonSerializer.Deserialize<RuntimeState>(File.ReadAllText(RuntimeStatePath), JsonOpts);
                if (st != null) return st;
            }
        }
        catch { }
        return new RuntimeState();
    }

    public static void SaveRuntimeState(RuntimeState st)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(RuntimeStatePath, JsonSerializer.Serialize(st, JsonOpts));
        }
        catch { }
    }
}

/// <summary>Persisted runtime bookkeeping (last known PIDs, sweep counters). Not secrets, not config.</summary>
public sealed class RuntimeState
{
    [JsonPropertyName("servers")] public Dictionary<string, ServerRuntimeInfo> Servers { get; set; } = new();
    [JsonPropertyName("orphanSweep")] public OrphanSweepStats OrphanSweep { get; set; } = new();
}

public sealed class ServerRuntimeInfo
{
    [JsonPropertyName("lastStartedUtc")] public DateTime? LastStartedUtc { get; set; }
    [JsonPropertyName("lastExitUtc")] public DateTime? LastExitUtc { get; set; }
    [JsonPropertyName("lastExitCode")] public int? LastExitCode { get; set; }
    [JsonPropertyName("lastPid")] public uint? LastPid { get; set; }
    [JsonPropertyName("restartCountTotal")] public int RestartCountTotal { get; set; }
}

public sealed class OrphanSweepStats
{
    [JsonPropertyName("lastRunUtc")] public DateTime? LastRunUtc { get; set; }
    [JsonPropertyName("totalDetected")] public int TotalDetected { get; set; }
    [JsonPropertyName("totalKilled")] public int TotalKilled { get; set; }
    [JsonPropertyName("lastFindings")] public List<string> LastFindings { get; set; } = new();
    /// <summary>Failure tracking: a failed enumeration must NEVER update LastRunUtc/success counters.
    /// UI shows ERROR/UNKNOWN while ConsecutiveFailures > 0.</summary>
    [JsonPropertyName("consecutiveFailures")] public int ConsecutiveFailures { get; set; }
    [JsonPropertyName("lastFailureUtc")] public DateTime? LastFailureUtc { get; set; }
    [JsonPropertyName("lastFailureError")] public string? LastFailureError { get; set; }
}
