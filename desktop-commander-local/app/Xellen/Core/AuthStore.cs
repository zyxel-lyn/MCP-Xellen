using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpXellen.Core;

public enum AuthKind { Server, Project, Client }

/// <summary>An identity record. ONLY the SHA-256 hash of the token is persisted — the plaintext
/// is returned exactly once at Issue time and never stored, logged, or displayed.</summary>
public sealed class AuthIdentity
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public AuthKind Kind { get; set; }
    [JsonPropertyName("tokenHash")] public string TokenHash { get; set; } = "";
    [JsonPropertyName("scopes")] public List<string> Scopes { get; set; } = new(); // serverIds; "*" = all
    [JsonPropertyName("createdUtc")] public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("expiresUtc")] public DateTime? ExpiresUtc { get; set; }
    [JsonPropertyName("revokedUtc")] public DateTime? RevokedUtc { get; set; }
}

public sealed class IssuedToken
{
    public AuthIdentity Identity { get; init; } = null!;
    public string PlaintextToken { get; init; } = ""; // single-use: caller must persist to a file or discard
}

/// <summary>
/// Credential store with cryptographic generation, hash-only persistence, scope, expiry, revoke,
/// and rotation (rotation is an explicit operation — never automatic, per safety rules).
/// Persisted at config/auth.json. DPAPI encryption is a documented follow-up.
/// </summary>
public static class AuthStore
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, AuthIdentity> ById = new();
    private static string _dir = @"D:\MCP-Bridge\config";
    public static event Action? Changed;

    private static string FilePath => Path.Combine(_dir, "auth.json");
    private static string BackupDir => Path.Combine(_dir, "backups");

    private static readonly JsonSerializerOptions J = new() { WriteIndented = true };

    public static void Initialize(string configDir)
    {
        lock (Gate)
        {
            _dir = configDir;
            Directory.CreateDirectory(_dir);
            if (File.Exists(FilePath))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<AuthIdentity>>(File.ReadAllText(FilePath), J) ?? new();
                    ById.Clear();
                    foreach (var id in list) ById[id.Id] = id;
                }
                catch (Exception ex)
                {
                    LogService.Error("auth", null, "auth.json load failed — starting with empty store", ex.Message);
                }
            }
        }
        LogService.Info("auth", null, $"AuthStore initialized: identities={Count}");
    }

    public static int Count { get { lock (Gate) return ById.Count; } }

    public static IssuedToken Issue(string name, AuthKind kind, IEnumerable<string> scopes, TimeSpan? ttl = null, string? id = null)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = Convert.ToHexString(tokenBytes); // 64 hex chars, cryptographically random
        var identity = new AuthIdentity
        {
            Id = id ?? (kind.ToString().ToLower() + "-" + Guid.NewGuid().ToString("N")[..12]),
            Name = name,
            Kind = kind,
            TokenHash = HashOf(plaintext),
            Scopes = scopes.ToList(),
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null,
        };
        lock (Gate)
        {
            ById[identity.Id] = identity;
            PersistLocked();
        }
        Redaction.RegisterSecret(plaintext);
        Changed?.Invoke();
        LogService.Info("auth", identity.Id, $"Identity issued kind={kind} scopes=[{string.Join(",", identity.Scopes)}] ttl={(ttl?.ToString() ?? "none")}");
        return new IssuedToken { Identity = identity, PlaintextToken = plaintext };
    }

    /// <summary>Constant-shape verification: hash the presented token and compare hashes.
    /// Revoked/expired identities fail. UNKNOWN (no match) is never a match.</summary>
    public static AuthIdentity? Verify(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var h = HashOf(token);
        lock (Gate)
        {
            foreach (var id in ById.Values)
            {
                if (id.RevokedUtc != null) continue;
                if (id.ExpiresUtc != null && id.ExpiresUtc.Value <= DateTime.UtcNow) continue;
                if (string.Equals(id.TokenHash, h, StringComparison.OrdinalIgnoreCase)) return id;
            }
        }
        return null;
    }

    public static bool Revoke(string id)
    {
        lock (Gate)
        {
            if (!ById.TryGetValue(id, out var idn)) return false;
            idn.RevokedUtc = DateTime.UtcNow;
            PersistLocked();
        }
        Changed?.Invoke();
        LogService.Warn("auth", id, "Identity revoked.");
        return true;
    }

    /// <summary>Explicit rotation (never automatic): new token issued, old hash replaced (old
    /// plaintext becomes invalid immediately). Returns the new plaintext once.</summary>
    public static IssuedToken Rotate(string id, TimeSpan? ttl = null)
    {
        AuthIdentity identity;
        lock (Gate)
        {
            if (!ById.TryGetValue(id, out identity!)) throw new KeyNotFoundException(id);
            identity.TokenHash = "";
            identity.RevokedUtc = null;
            identity.CreatedUtc = DateTime.UtcNow;
            identity.ExpiresUtc = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null;
        }
        var issued = Issue(identity.Name, identity.Kind, identity.Scopes, ttl, id: identity.Id);
        LogService.Warn("auth", id, "Identity rotated (previous token invalidated).");
        return issued;
    }

    public static List<AuthIdentity> List()
    {
        lock (Gate) return ById.Values.OrderBy(x => x.CreatedUtc).ToList();
    }

    public static string HashOf(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static void PersistLocked()
    {
        try
        {
            Directory.CreateDirectory(BackupDir);
            if (File.Exists(FilePath))
            {
                var bak = Path.Combine(BackupDir, $"auth-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Copy(FilePath, bak, true);
                var olds = Directory.GetFiles(BackupDir, "auth-*.json").OrderByDescending(f => f).Skip(10);
                foreach (var o in olds) File.Delete(o);
            }
            var list = ById.Values.OrderBy(x => x.CreatedUtc).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, J));
        }
        catch (Exception ex)
        {
            LogService.Error("auth", null, "auth.json persist failed", ex.Message);
        }
    }
}
