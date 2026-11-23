using System.Text.RegularExpressions;

namespace McpXellen.Core;

/// <summary>
/// Central secret redaction. Every log line, activity summary, and diagnostic string that may
/// carry user/tool data MUST pass through Sanitize before storage or UI display.
/// Registered runtime secrets (issued tokens) are held in memory ONLY — never persisted —
/// and are scrubbed from any sanitized output. Patterns are deliberately targeted to avoid
/// masking ordinary content; truncation bounds payload size.
/// </summary>
public static class Redaction
{
    private static readonly object Gate = new();
    private static readonly List<string> RuntimeSecrets = new();
    private static readonly List<Regex> Patterns = new()
    {
        new Regex(@"(?i)authorization\s*:\s*bearer\s+[A-Za-z0-9\-._~+/=]{8,}", RegexOptions.Compiled),
        new Regex(@"(?i)(""?token""?\s*[:=]\s*"")[^""]{8,}(""?)", RegexOptions.Compiled),
        new Regex(@"(?i)(""?api[_-]?key""?\s*[:=]\s*"")[^""]{8,}(""?)", RegexOptions.Compiled),
        new Regex(@"(?i)(""?password""?\s*[:=]\s*"")[^""]{4,}(""?)", RegexOptions.Compiled),
        new Regex(@"(?i)(""?cookie""?\s*[:=]\s*"")[^""]{8,}(""?)", RegexOptions.Compiled),
        new Regex(@"sk-[A-Za-z0-9]{16,}", RegexOptions.Compiled),
        new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled),
        new Regex(@"(?i)(password|passwd|pwd)\s*[:=]\s*[^\s;&]{4,}", RegexOptions.Compiled),
        new Regex(@"(?i)token=[A-Za-z0-9\-._~+/=]{8,}", RegexOptions.Compiled),
    };

    public static void RegisterSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 8) return;
        lock (Gate)
        {
            if (!RuntimeSecrets.Contains(secret)) RuntimeSecrets.Add(secret);
        }
    }

    public static void UnregisterSecret(string secret)
    {
        lock (Gate) RuntimeSecrets.Remove(secret);
    }

    public static int RegisteredSecretCount { get { lock (Gate) return RuntimeSecrets.Count; } }

    /// <summary>Sanitize a string: replace known secret shapes with placeholders, then truncate.</summary>
    public static string Sanitize(string? input, int maxLen = 200)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var s = input;
        lock (Gate)
        {
            foreach (var sec in RuntimeSecrets)
                if (s.Contains(sec, StringComparison.Ordinal))
                    s = s.Replace(sec, "[REDACTED]", StringComparison.Ordinal);
        }
        foreach (var rx in Patterns)
        {
            try { s = rx.Replace(s, m => m.Value.Length <= 0 ? m.Value : MaskValue(m.Value)); }
            catch { }
        }
        if (s.Length > maxLen) s = s[..maxLen] + "…[truncated]";
        return s;
    }

    /// <summary>Mask only the secret-looking part of a matched span (keep the label, hide the value).</summary>
    private static string MaskValue(string match)
    {
        // Keep the leading label (up to ~24 chars or first separator), mask the tail.
        var keep = Math.Min(match.Length, 24);
        var cut = match.LastIndexOfAny(new[] { ':', '=', ' ' }, Math.Min(match.Length - 1, 23));
        if (cut > 0) keep = Math.Min(cut + 1, match.Length);
        return match[..keep] + "[REDACTED]";
    }

    /// <summary>Sanitize an object by JSON-serializing then sanitizing (bounded).</summary>
    public static string SanitizeObject(object? o, int maxLen = 200)
    {
        if (o == null) return "";
        try { return Sanitize(System.Text.Json.JsonSerializer.Serialize(o), maxLen); }
        catch { return Sanitize(o.ToString(), maxLen); }
    }
}
