using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpXellen.Core;

/// <summary>A project: identity, scoped client token, allowed servers, allowed roots, status.</summary>
public sealed class ProjectConfig
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("clientIdentityId")] public string ClientIdentityId { get; set; } = "";
    [JsonPropertyName("allowedServers")] public List<string> AllowedServers { get; set; } = new();
    [JsonPropertyName("allowedRoots")] public List<string> AllowedRoots { get; set; } = new();
    [JsonPropertyName("createdUtc")] public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("status")] public string Status { get; set; } = "active";
}

/// <summary>
/// Central registry over XellenConfig: servers (existing), projects (new), with idempotent
/// schema migration v1→v2 (backup created automatically by ConfigStore.Save before every write).
/// Projects are paired with an AuthStore client identity (scoped token, hash-only persistence).
/// </summary>
public static class Registry
{
    private static XellenConfig _config = null!;
    private static readonly object Gate = new();
    public static event Action? Changed;

    public static XellenConfig Config { get { lock (Gate) return _config; } }

    public static void Initialize(XellenConfig config)
    {
        lock (Gate)
        {
            _config = config;
            bool dirty = false;
            if (config.SchemaVersion < 2)
            {
                config.SchemaVersion = 2;
                config.MigratedFrom += "|v1→v2 (projects/auth) 2026-11-13";
                dirty = true;
            }
            if (dirty) ConfigStore.Save(config);
        }
        LogService.Info("registry", null, $"Registry initialized: servers={config.Servers.Count}, projects={config.Projects.Count}, schemaVersion={config.SchemaVersion}");
    }

    public static List<ProjectConfig> ListProjects() { lock (Gate) return Config.Projects.ToList(); }

    public static ProjectConfig? GetProject(string idOrSlug)
    {
        lock (Gate) return Config.Projects.FirstOrDefault(p => p.Id == idOrSlug || p.Slug == idOrSlug);
    }

    public static ServerConfig? GetServer(string id)
    {
        lock (Gate) return Config.Servers.FirstOrDefault(s => s.Id == id);
    }

    /// <summary>Find the project whose client identity matches — used by the transport to map an
    /// authenticated project token back to its project record.</summary>
    public static ProjectConfig? GetProjectByIdentity(string identityId)
    {
        lock (Gate) return Config.Projects.FirstOrDefault(p => p.ClientIdentityId == identityId);
    }

    /// <summary>Idempotent additive migration: ensure the native-transport DC server definition
    /// exists (lazy-started by the bridge, never auto-started, never touching the 8023 chain).</summary>
    public static void EnsureNativeServerDefinition()
    {
        lock (Gate)
        {
            if (Config.Servers.Any(s => s.Id == "dc-staging-native")) return;
            var dcEntry = @"D:\MCP-Bridge\desktop-commander-local\package\dist\index.js";
            Config.Servers.Add(new ServerConfig
            {
                Id = "dc-staging-native",
                Name = "Desktop Commander (native STDIO bridge)",
                Command = @"C:\Program Files\nodejs\node.exe",
                Arguments = new List<string> { dcEntry, "--no-onboarding" },
                WorkingDirectory = @"D:\MCP-Bridge",
                Transport = "stdio-native",
                AutoStart = false,
                OwnershipPatterns = new List<string> { @"desktop-commander-local\package\dist\index.js" },
                ChildProcessPatterns = new List<string> { @"desktop-commander-local\package\dist\index.js" },
            });
            ConfigStore.Save(Config);
        }
        LogService.Info("registry", null, "Native server definition 'dc-staging-native' ensured (lazy).");
    }

    public static (ProjectConfig Project, IssuedToken Token) CreateProject(
        string slug, string name, IEnumerable<string> allowedServers, IEnumerable<string>? allowedRoots = null, TimeSpan? ttl = null)
    {
        slug = Slugify(slug) ?? throw new ArgumentException("Slug tidak valid (huruf kecil/angka/dash, 3-32).");
        lock (Gate)
        {
            if (Config.Projects.Any(p => p.Slug == slug)) throw new InvalidOperationException($"Slug '{slug}' sudah dipakai.");
        }
        var project = new ProjectConfig
        {
            Id = "prj-" + Guid.NewGuid().ToString("N")[..10],
            Slug = slug,
            Name = string.IsNullOrWhiteSpace(name) ? slug : name,
            AllowedServers = allowedServers.ToList(),
            AllowedRoots = allowedRoots?.ToList() ?? new List<string>(),
        };
        var issued = AuthStore.Issue($"client:{slug}", AuthKind.Project, project.AllowedServers, ttl);
        project.ClientIdentityId = issued.Identity.Id;
        lock (Gate)
        {
            Config.Projects.Add(project);
            ConfigStore.Save(Config);
        }
        Changed?.Invoke();
        LogService.Info("registry", project.Id, $"Project created slug={slug} allowedServers=[{string.Join(",", project.AllowedServers)}]");
        return (project, issued);
    }

    public static bool DeleteProject(string id)
    {
        lock (Gate)
        {
            var p = Config.Projects.FirstOrDefault(x => x.Id == id || x.Slug == id);
            if (p == null) return false;
            if (!string.IsNullOrEmpty(p.ClientIdentityId)) AuthStore.Revoke(p.ClientIdentityId);
            Config.Projects.Remove(p);
            ConfigStore.Save(Config);
        }
        Changed?.Invoke();
        LogService.Warn("registry", id, "Project deleted (client identity revoked).");
        return true;
    }

    public static string? Slugify(string input)
    {
        var chars = input.Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : (c == ' ' || c == '_' || c == '-' ? '-' : '\0'))
            .Where(c => c != '\0').ToArray();
        var s = new string(chars);
        while (s.Contains("--")) s = s.Replace("--", "-");
        s = s.Trim('-');
        return (s.Length >= 3 && s.Length <= 32) ? s : null;
    }
}
