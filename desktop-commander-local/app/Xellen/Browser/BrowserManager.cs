using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using McpXellen.Core;
using ActivityEvent = McpXellen.Core.ActivityEvent;

namespace McpXellen.Browser;

public enum BrowserSessionStatus { Creating, Starting, Ready, IdleExpired, Stopping, Stopped, Failed }

/// <summary>Record sesi browser: identitas, ownership, port, profil, fingerprint proses.</summary>
public sealed class BrowserSessionRecord
{
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("clientId")] public string ClientId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("agentPort")] public int AgentPort { get; set; }
    [JsonPropertyName("wrapperPort")] public int WrapperPort { get; set; }
    [JsonPropertyName("profileDir")] public string ProfileDir { get; set; } = "";
    [JsonPropertyName("tokenFile")] public string TokenFile { get; set; } = "";
    /// <summary>Fingerprint cmdline proses agent milik record ini (dipakai sweep + stop).</summary>
    [JsonPropertyName("agentMarker")] public string AgentMarker { get; set; } = "";
    /// <summary>Fingerprint cmdline wrapper milik record ini.</summary>
    [JsonPropertyName("wrapperMarker")] public string WrapperMarker { get; set; } = "";
    [JsonPropertyName("headless")] public bool Headless { get; set; } = true;
    [JsonPropertyName("createdUtc")] public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("lastActivityUtc")] public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("status")] public string Status { get; set; } = "Creating";
    [JsonPropertyName("ownedPids")] public List<int> OwnedPids { get; set; } = new();
    [JsonPropertyName("error")] public string? Error { get; set; }
}

/// <summary>State file persist (ownership fingerprint) — dasar startup sweep yang tidak mengklaim proses asing.</summary>
public sealed class BrowserStateFile
{
    [JsonPropertyName("records")] public List<BrowserSessionRecord> Records { get; set; } = new();
}

/// <summary>
/// Browser Manager (Opsi 3 Hybrid, disetujui owner 2026-11-13): satu agent-infra terisolasi per client
/// (proses + user-data-dir + token sendiri), lazy start, idle stop (default 15 menit, parameter terkunci owner),
/// maks 3 client aktif, ownership wajib, event ke Activity Monitor.
/// TIDAK menyentuh: daemon browsermcp 9010, Chrome pribadi, Hermes, port production, Funnel, token production.
/// </summary>
public sealed class BrowserManager
{
    private static BrowserManager? _instance;
    public static BrowserManager Instance => _instance ??= new BrowserManager();

    private const string Root = @"D:\MCP-Bridge";
    private const string SupergatewayEntry = @"D:\MCP-Bridge\mcp-guard-stack\node_modules\supergateway\dist\index.js";

    private readonly object _gate = new();
    private readonly Dictionary<string, BrowserSessionRecord> _sessions = new();
    private readonly Dictionary<string, Process> _agentProcs = new();     // sessionId -> top process (cmd/npx)
    private readonly Dictionary<string, Process> _wrapperProcs = new();   // sessionId -> wrapper (node supergateway)
    private System.Threading.Timer? _timer;
    private BrowserManagerConfig _cfg = new();
    private string _secretsRoot = @"D:\MCP-Bridge\secrets\browser-sessions";
    private string _statePath = @"D:\MCP-Bridge\config\browser-sessions-state.json";
    private string _profileRoot = @"D:\MCP-Bridge\browser-profiles";
    private int _nextAgentPort;
    private int _nextWrapperPort;

    /// <summary>Live feed untuk Activity Monitor / UI; semua event juga masuk ActivityStore.</summary>
    public event Action<ActivityEvent>? SessionEvent;

    public bool Enabled => _cfg.Enabled;
    public int MaxActiveSessions => Math.Max(1, _cfg.MaxActiveSessions);
    public int IdleTimeoutMinutes => Math.Max(1, _cfg.IdleTimeoutMinutes);

    // ---------------------------------------------------------------- lifecycle

    public void Initialize(BrowserManagerConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        lock (_gate)
        {
            _cfg = cfg;
            _profileRoot = string.IsNullOrWhiteSpace(cfg.ProfileRoot) ? Path.Combine(Root, "browser-profiles") : cfg.ProfileRoot;
            _secretsRoot = string.IsNullOrWhiteSpace(cfg.SecretsRoot) ? Path.Combine(Root, @"secrets\browser-sessions") : cfg.SecretsRoot;
            _statePath = string.IsNullOrWhiteSpace(cfg.StatePath) ? Path.Combine(Root, @"config\browser-sessions-state.json") : cfg.StatePath;
            _nextAgentPort = cfg.AgentPortsBase;
            _nextWrapperPort = cfg.WrapperPortsBase;
            Directory.CreateDirectory(_profileRoot);
            Directory.CreateDirectory(_secretsRoot);
            Directory.CreateDirectory(Root + @"\logs");

            // Startup orphan sweep: HANYA record milik sendiri, dan HANYA bila fingerprint cmdline cocok.
            // PID hidup tapi cmdline tidak mengandung marker record → SKIP + log (tidak pernah mengklaim proses asing).
            var state = LoadState();
            var swept = 0;
            foreach (var rec in state.Records)
            {
                if (rec.Status is not ("Ready" or "Creating" or "Starting")) continue;
                foreach (var pid in rec.OwnedPids.ToList())
                {
                    if (!TryGetProcess(pid, out var proc)) continue;
                    var cmd = GetCommandLine(pid) ?? "";
                    if ((rec.AgentMarker.Length > 0 && cmd.Contains(rec.AgentMarker, StringComparison.Ordinal))
                     || (rec.WrapperMarker.Length > 0 && cmd.Contains(rec.WrapperMarker, StringComparison.Ordinal)))
                    {
                        try { proc.Kill(entireProcessTree: true); swept++; LogService.Warn("browser", rec.SessionId, $"startup orphan sweep: owned orphan PID={pid} dihentikan (fingerprint cocok)"); }
                        catch (Exception ex) { LogService.Warn("browser", rec.SessionId, $"startup sweep: kill PID={pid} gagal", ex.Message); }
                    }
                    else
                    {
                        LogService.Warn("browser", rec.SessionId, $"startup sweep: PID={pid} hidup tapi fingerprint TIDAK cocok — dibiarkan hidup (bukan proses milik record ini)");
                    }
                }
                // Pass-2: leaf yang di-spawn npx bisa re-parent dan lolos tree-kill. Cmdline yang memuat
                // sessionId profil/token ini provably milik record → stop via jalur ownership teruji.
                swept += SweepResidualsBySessionId(rec);
                rec.Status = "Stopped";
                rec.Error = "orphaned; dibersihkan startup sweep (profil tetap tersimpan)";
                LogService.Info("browser", rec.SessionId, "startup sweep selesai untuk record ini");
            }
            SaveState(state);
            LogService.Info("browser", null, $"Browser Manager initialized: enabled={cfg.Enabled} maxActive={cfg.MaxActiveSessions} idle={cfg.IdleTimeoutMinutes}m headless={cfg.Headless} swept={swept} state={_statePath}");

            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => TimerTick(), null, 15000, 30000);
        }
    }

    // ---------------------------------------------------------------- queries

    public List<BrowserSessionRecord> ListSessions(string? clientId = null)
    {
        lock (_gate)
        {
            IEnumerable<BrowserSessionRecord> q = _sessions.Values;
            if (!string.IsNullOrEmpty(clientId)) q = q.Where(s => s.ClientId == clientId);
            return q.OrderByDescending(s => s.CreatedUtc).Select(CloneRecord).ToList();
        }
    }

    /// <summary>Ownership wajib: hanya pemilik session yang boleh melihat record.</summary>
    public BrowserSessionRecord? GetSession(string sessionId, string clientId)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var s)) return null;
            if (s.ClientId != clientId) { EmitSessionAuthFailure(s, clientId); return null; }
            return CloneRecord(s);
        }
    }

    // ---------------------------------------------------------------- commands

    /// <summary>Buat sesi (lazy — proses belum jalan). Token per sesi ditulis ke file, TIDAK dikembalikan/dilog.</summary>
    public BrowserSessionRecord CreateSession(string clientId, string name)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("clientId wajib", nameof(clientId));
        if (!_cfg.Enabled) throw new InvalidOperationException("Browser Manager disabled (config.browserManager.enabled=false). Aktifkan lewat konfigurasi dengan persetujuan owner.");

        lock (_gate)
        {
            var active = _sessions.Values.Count(s => s.Status is "Ready" or "Starting" or "Creating");
            if (active >= Math.Max(1, _cfg.MaxActiveSessions))
            {
                Emit(null, "browser_session_limit_reached", ActivityStatus.Failed,
                    $"client={clientId} ditolak: batas {Math.Max(1, _cfg.MaxActiveSessions)} client browser aktif tercapai (sesi aktif tidak dihentikan paksa)");
                throw new InvalidOperationException($"Batas {Math.Max(1, _cfg.MaxActiveSessions)} client browser aktif tercapai. Tutup session yang tidak terpakai terlebih dahulu.");
            }
        }

        var rec = new BrowserSessionRecord
        {
            ClientId = clientId,
            Name = string.IsNullOrWhiteSpace(name) ? clientId : name,
            Headless = _cfg.Headless,
            ProfileDir = Path.Combine(_profileRoot, Guid.NewGuid().ToString("N")), // final dir = sessionId; diisi setelah id jadi
        };
        rec.ProfileDir = Path.Combine(_profileRoot, rec.SessionId);
        lock (_gate) _sessions[rec.SessionId] = rec;
        WriteSessionToken(rec);                       // token hanya hidup di file secrets, terdaftar di Redaction
        PersistSession(rec);
        EmitSession("browser_session_created", rec, ActivityStatus.Queued, $"client={clientId} session={rec.SessionId} profile={rec.ProfileDir}");
        LogService.Info("browser", rec.SessionId, $"session created untuk client {clientId} (lazy; proses belum jalan)");
        return CloneRecord(rec);
    }

    /// <summary>Lazy start: spawn agent-infra (isolated user-data-dir) lalu wrapper auth. Tidak memblokir pemanggil UI.</summary>
    public async Task<bool> StartSessionAsync(string sessionId, string clientId)
    {
        var rec = RequireOwned(sessionId, clientId);
        if (rec == null) return false;
        return await Task.Run(() => StartSessionCore(rec)).ConfigureAwait(false);
    }

    private bool StartSessionCore(BrowserSessionRecord rec)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(rec.SessionId, out var cur)) cur.Status = "Starting";
            rec.Status = "Starting";
        }
        EmitSession("browser_session_starting", rec, ActivityStatus.Running, $"lazy start: agent-infra {_cfg.AgentInfraVersion} headless={rec.Headless}");
        try
        {
            Directory.CreateDirectory(rec.ProfileDir);
            rec.AgentPort = FindFreePort(ref _nextAgentPort, 50);
            rec.WrapperPort = FindFreePort(ref _nextWrapperPort, 50);
            if (rec.AgentPort == 0 || rec.WrapperPort == 0) throw new InvalidOperationException("tidak ada port bebas di rentang konfigurasi");
            rec.AgentMarker = "@agent-infra/mcp-server-browser@" + _cfg.AgentInfraVersion + " --port " + rec.AgentPort;
            rec.WrapperMarker = "--streamableHttp http://127.0.0.1:" + rec.AgentPort + "/mcp --port " + rec.WrapperPort;
            PersistSession(rec);

            // ---- Child 1: agent-infra browser server. Isolasi: user-data-dir sendiri, 127.0.0.1, headless default ON.
            var agentArgs = "/d /s /c \"npx -y @agent-infra/mcp-server-browser@" + _cfg.AgentInfraVersion +
                " --port " + rec.AgentPort + " --host 127.0.0.1 --user-data-dir \"" + rec.ProfileDir + "\"" +
                (rec.Headless ? " --headless" : "") +
                (string.IsNullOrWhiteSpace(_cfg.BrowserExecutablePath) ? "" : " --executable-path \"" + _cfg.BrowserExecutablePath + "\"") +
                "\"";
            var agent = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = agentArgs,
                UseShellExecute = false,
                CreateNoWindow = true,          // tidak pernah ada window console terlihat
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Root,
            };
            var agentProc = Process.Start(agent) ?? throw new InvalidOperationException("spawn agent-infra gagal");
            lock (_gate) { rec.OwnedPids = new List<int> { agentProc.Id }; _agentProcs[rec.SessionId] = agentProc; }
            agentProc.OutputDataReceived += (_, e) => OnAgentLine(rec, e.Data);
            agentProc.ErrorDataReceived += (_, e) => OnAgentErr(rec, e.Data);
            agentProc.BeginOutputReadLine();
            agentProc.BeginErrorReadLine();
            EmitSession("browser_process_started", rec, ActivityStatus.Running, $"agent-infra PID={agentProc.Id} port={rec.AgentPort} headless={rec.Headless} profil terisolasi");
            LogService.Info("browser", rec.SessionId, $"agent-infra spawned PID={agentProc.Id} port={rec.AgentPort}");

            // npx -y bisa mengunduh paket saat cache dingin → timeout 90s.
            if (!WaitPort(rec.AgentPort, 90_000))
                throw new TimeoutException("agent-infra tidak listen dalam 90s (lihat log browser-agent)");

            // ---- Child 2: wrapper auth per sesi (Supergateway upstream streamableHttp; Bearer dari file token sesi).
            var node = ResolveNode();
            var wrapperArgs = "\"" + SupergatewayEntry + "\"" +
                " --streamableHttp http://127.0.0.1:" + rec.AgentPort + "/mcp" +
                " --port " + rec.WrapperPort + " --host 127.0.0.1" +
                " --apiKeyFile \"" + rec.TokenFile + "\"" +
                " --outputTransport streamableHttp --healthEndpoint /healthz";
            var wrapper = new ProcessStartInfo
            {
                FileName = node,
                Arguments = wrapperArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Root,
            };
            var wProc = Process.Start(wrapper) ?? throw new InvalidOperationException("spawn wrapper auth gagal");
            lock (_gate) { rec.OwnedPids.Add(wProc.Id); _wrapperProcs[rec.SessionId] = wProc; }
            wProc.OutputDataReceived += (_, e) => OnWrapperLine(rec, e.Data);
            wProc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogService.Warn("browser-wrapper", rec.SessionId, Redaction.Sanitize(e.Data, 240)); };
            wProc.BeginOutputReadLine();
            wProc.BeginErrorReadLine();
            EmitSession("browser_session_attached", rec, ActivityStatus.Running, $"wrapper PID={wProc.Id} port={rec.WrapperPort} (Bearer per session; token tidak pernah di URL)");
            LogService.Info("browser", rec.SessionId, $"wrapper auth spawned PID={wProc.Id} port={rec.WrapperPort}");

            if (!WaitPort(rec.WrapperPort, 20_000))
                throw new TimeoutException("wrapper auth tidak listen dalam 20s");

            lock (_gate) { rec.Status = "Ready"; rec.LastActivityUtc = DateTime.UtcNow; if (_sessions.TryGetValue(rec.SessionId, out var cur)) { cur.Status = "Ready"; cur.AgentPort = rec.AgentPort; cur.WrapperPort = rec.WrapperPort; cur.OwnedPids = new List<int>(rec.OwnedPids); cur.AgentMarker = rec.AgentMarker; cur.WrapperMarker = rec.WrapperMarker; cur.LastActivityUtc = rec.LastActivityUtc; } }
            PersistSession(rec);
            EmitSession("browser_session_ready", rec, ActivityStatus.Success, $"ready: MCP endpoint http://127.0.0.1:{rec.WrapperPort}/mcp (auth per client)");
            return true;
        }
        catch (Exception ex)
        {
            lock (_gate) { rec.Status = "Failed"; rec.Error = ex.Message; if (_sessions.TryGetValue(rec.SessionId, out var cur)) { cur.Status = "Failed"; cur.Error = ex.Message; } }
            EmitSession("browser_session_failed", rec, ActivityStatus.Failed, Redaction.Sanitize(ex.Message, 240));
            LogService.Error("browser", rec.SessionId, "session start gagal", ex.Message);
            StopTree(rec);
            PersistSession(rec);
            return false;
        }
    }

    /// <summary>Tutup sesi. Ownership mismatch → browser_auth_failure, proses TIDAK disentuh.</summary>
    public bool CloseSession(string sessionId, string clientId, string reason = "owner")
    {
        var rec = RequireOwned(sessionId, clientId);
        if (rec == null) return false;
        EmitSession("browser_session_cleanup_started", rec, ActivityStatus.Running, "reason=" + reason);
        LogService.Info("browser", sessionId, $"session closing (reason={reason})");
        StopTree(rec);
        lock (_gate) { rec.Status = "Stopped"; if (_sessions.TryGetValue(sessionId, out var cur)) { cur.Status = "Stopped"; cur.OwnedPids.Clear(); } }
        PersistSession(rec);
        EmitSession("browser_session_cleanup_completed", rec, ActivityStatus.Success, "sesi dihentikan; profil tetap tersimpan (tidak pernah dihapus otomatis)");
        return true;
    }

    public void ShutdownAll()
    {
        List<BrowserSessionRecord> list;
        lock (_gate) list = _sessions.Values.Select(CloneRecord).ToList();
        foreach (var s in list.Where(x => x.Status is "Ready" or "Starting" or "Creating"))
        {
            EmitSession("browser_session_cleanup_started", s, ActivityStatus.Running, "app shutdown");
            StopTree(s);
            lock (_gate) { s.Status = "Stopped"; if (_sessions.TryGetValue(s.SessionId, out var cur)) { cur.Status = "Stopped"; cur.OwnedPids.Clear(); } }
            PersistSession(s);
        }
        _timer?.Dispose();
        _timer = null;
        LogService.Info("browser", null, "Browser Manager shutdown: semua owned session dihentikan (profil persisten, tanpa penghapusan).");
    }

    // ---------------------------------------------------------------- timer: health + idle stop

    private void TimerTick()
    {
        List<BrowserSessionRecord> list;
        lock (_gate) list = _sessions.Values.Select(CloneRecord).ToList();
        var now = DateTime.UtcNow;
        foreach (var s in list)
        {
            if (s.Status != "Ready") continue;
            // Health: agent port harus hidup; mati → Failed + cleanup proses milik record ini saja.
            if (!PortAlive(s.AgentPort))
            {
                lock (_gate) { s.Status = "Failed"; s.Error = "agent process tidak merespons"; if (_sessions.TryGetValue(s.SessionId, out var cur)) { cur.Status = "Failed"; cur.Error = s.Error; } }
                EmitSession("browser_process_exited", s, ActivityStatus.Failed, "agent port tidak merespons — crash isolation: hanya proses sesi ini yang dibersihkan");
                LogService.Warn("browser", s.SessionId, "health check gagal: agent port mati");
                StopTree(s);
                PersistSession(s);
                continue;
            }
            // Idle stop (parameter terkunci owner: default 15 menit). Aktivitas = baris stdout wrapper/agent.
            var idleMin = (now - s.LastActivityUtc).TotalMinutes;
            if (idleMin >= Math.Max(1, _cfg.IdleTimeoutMinutes))
            {
                EmitSession("browser_session_expired", s, ActivityStatus.Timeout, $"idle {idleMin:F0} menit >= batas {Math.Max(1, _cfg.IdleTimeoutMinutes)} menit");
                LogService.Info("browser", s.SessionId, $"idle stop setelah {idleMin:F0} menit tanpa aktivitas");
                StopTree(s);
                lock (_gate) { s.Status = "Stopped"; if (_sessions.TryGetValue(s.SessionId, out var cur)) cur.Status = "Stopped"; }
                PersistSession(s);
                EmitSession("browser_session_cleanup_completed", s, ActivityStatus.Success, "idle stop selesai; profil tetap tersimpan");
            }
        }
    }

    // ---------------------------------------------------------------- stdout handlers (activity + idle feed)

    private void OnAgentLine(BrowserSessionRecord rec, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_gate) { rec.LastActivityUtc = DateTime.UtcNow; if (_sessions.TryGetValue(rec.SessionId, out var cur)) cur.LastActivityUtc = rec.LastActivityUtc; }
        LogService.Debug("browser-agent", rec.SessionId, Redaction.Sanitize(line, 240));
        if (line.Contains("navigat", StringComparison.OrdinalIgnoreCase))
        {
            var url = ExtractUrl(line);
            if (url.Length > 0) EmitSession("browser_navigation_requested", rec, ActivityStatus.Running, Redaction.Sanitize(url, 200));
        }
    }

    private void OnAgentErr(BrowserSessionRecord rec, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        LogService.Warn("browser-agent", rec.SessionId, Redaction.Sanitize(line, 240));
    }

    private void OnWrapperLine(BrowserSessionRecord rec, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_gate) { rec.LastActivityUtc = DateTime.UtcNow; if (_sessions.TryGetValue(rec.SessionId, out var cur)) cur.LastActivityUtc = rec.LastActivityUtc; }
        LogService.Debug("browser-wrapper", rec.SessionId, Redaction.Sanitize(line, 200));
    }

    private static string ExtractUrl(string line)
    {
        var m = Regex.Match(line, "https?://[^\\s\"']+");
        return m.Success ? m.Value : "";
    }

    // ---------------------------------------------------------------- ownership

    private BrowserSessionRecord? RequireOwned(string sessionId, string clientId)
    {
        BrowserSessionRecord? rec;
        lock (_gate) _sessions.TryGetValue(sessionId, out rec);
        if (rec == null)
        {
            LogService.Warn("browser", sessionId, $"operasi ditolak: session tidak dikenal (peminta={clientId})");
            return null;
        }
        if (rec.ClientId != clientId)
        {
            EmitSessionAuthFailure(rec, clientId);
            return null;
        }
        return rec;
    }

    private void EmitSessionAuthFailure(BrowserSessionRecord rec, string clientId)
    {
        EmitSession("browser_auth_failure", rec, ActivityStatus.Failed,
            $"ownership mismatch: peminta='{clientId}' pemilik='{rec.ClientId}' — proses sesi TIDAK disentuh");
        LogService.Warn("browser", rec.SessionId, $"auth failure: ownership mismatch untuk peminta {clientId}");
    }

    private void StopTree(BrowserSessionRecord rec)
    {
        foreach (var pid in rec.OwnedPids)
        {
            if (!TryGetProcess(pid, out var proc)) continue;
            // Belt and suspenders: verifikasi ulang fingerprint SEBELUM kill (anti PID-reuse).
            var cmd = GetCommandLine(pid) ?? "";
            if (!(rec.AgentMarker.Length > 0 && cmd.Contains(rec.AgentMarker, StringComparison.Ordinal))
             && !(rec.WrapperMarker.Length > 0 && cmd.Contains(rec.WrapperMarker, StringComparison.Ordinal)))
            {
                LogService.Warn("browser", rec.SessionId, $"stop: PID={pid} fingerprint tidak cocok lagi — dilewati (anti PID-reuse)");
                continue;
            }
            try { proc.Kill(entireProcessTree: true); LogService.Info("browser", rec.SessionId, $"stopped PID={pid} (tree)"); }
            catch (Exception ex) { LogService.Warn("browser", rec.SessionId, $"stop PID={pid} gagal", ex.Message); }
        }
        rec.OwnedPids.Clear();
        lock (_gate) { _agentProcs.Remove(rec.SessionId); _wrapperProcs.Remove(rec.SessionId); }
        // Pass-2 anti-residual: tree-kill bisa melewatkan leaf yang re-parent (bukti empiris T46 round-1:
        // node agent-infra hasil npx selamat sebagai orphan). Cmdline yang memuat sessionId profil/token
        // sesi ini provably milik record → dihentikan lewat jalur ownership teruji (TryKillOwned).
        try { SweepResidualsBySessionId(rec); }
        catch (Exception ex) { LogService.Warn("browser", rec.SessionId, "residual sweep gagal", ex.Message); }
    }

    /// <summary>Stop proses residual milik SESI INI saja: cmdline memuat sessionId (profil/token unik per sesi)
    /// DAN identitas stack browser. Produksi/asing tidak pernah memuat sessionId kita → tidak pernah tersentuh.</summary>
    private int SweepResidualsBySessionId(BrowserSessionRecord rec)
    {
        var killed = 0;
        var snap = ProcessEnumerator.Snapshot();
        if (!snap.Success)
        {
            LogService.Warn("browser", rec.SessionId, "residual sweep: enumerasi proses gagal — leaf mungkin tertinggal (dicatat, tidak dihentikan buta)");
            return 0;
        }
        foreach (var p in snap.Processes)
        {
            var cmd = p.CmdLine ?? "";
            if (!cmd.Contains(rec.SessionId, StringComparison.Ordinal)) continue;
            if (!(cmd.Contains("mcp-server-browser", StringComparison.OrdinalIgnoreCase)
               || cmd.Contains("supergateway", StringComparison.OrdinalIgnoreCase))) continue;
            if (ProcessHost.TryKillOwned(p.Pid, rec.SessionId, "browser residual cleanup (sessionId fingerprint)", "browser-manager")) killed++;
        }
        return killed;
    }

    // ---------------------------------------------------------------- token (file-only, never returned/logged)

    private void WriteSessionToken(BrowserSessionRecord rec)
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToHexString(bytes);
        var dir = Path.Combine(_secretsRoot, rec.SessionId);
        Directory.CreateDirectory(dir);
        rec.TokenFile = Path.Combine(dir, "token.txt");
        File.WriteAllText(rec.TokenFile, token);
        Redaction.RegisterSecret(token);   // bila token pernah nyaris masuk log/event, otomatis dimask
    }

    // ---------------------------------------------------------------- events

    private void Emit(BrowserSessionRecord? s, string type, ActivityStatus st, string details)
    {
        var ev = new ActivityEvent
        {
            EventType = type,
            ClientId = s?.ClientId,
            ServerId = "browser-manager",
            ToolName = s?.SessionId,
            Status = st,
            InputSummary = Redaction.Sanitize(details, 300),
        };
        SessionEvent?.Invoke(ev);
        ActivityStore.Add(ev);
    }

    private void EmitSession(string type, BrowserSessionRecord s, ActivityStatus st, string details) => Emit(s, type, st, details);

    // ---------------------------------------------------------------- test-only hooks (same assembly; tidak pernah dipanggil jalur production)

    /// <summary>TEST-ONLY (SelfTest T43–T47): reset state in-memory antar kasus uji sandbox.</summary>
    internal void TestOnlyReset()
    {
        lock (_gate)
        {
            _sessions.Clear();
            _agentProcs.Clear();
            _wrapperProcs.Clear();
        }
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>TEST-ONLY (SelfTest T47): mundurkan LastActivityUtc agar idle expiry teruji deterministik
    /// tanpa menunggu 15 menit (produksi tetap 15 menit). Tidak dipanggil production.</summary>
    internal void TestOnlyBackdateActivity(string sessionId, string clientId, TimeSpan age)
    {
        var rec = RequireOwned(sessionId, clientId);
        if (rec == null) return;
        lock (_gate)
        {
            rec.LastActivityUtc = DateTime.UtcNow - age;
            if (_sessions.TryGetValue(sessionId, out var cur)) cur.LastActivityUtc = rec.LastActivityUtc;
        }
    }

    // ---------------------------------------------------------------- helpers

    private static bool TryGetProcess(int pid, out Process proc)
    {
        try { proc = Process.GetProcessById(pid); return true; }
        catch { proc = null!; return false; }
    }

    /// <summary>Cmdline via WMI (System.Management sudah direferensikan csproj). Null = tidak terbaca → selalu dianggap BUKAN milik kita.</summary>
    private static string? GetCommandLine(int pid)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var o in searcher.Get())
                return o["CommandLine"] as string;
        }
        catch { }
        return null;
    }

    private static bool PortAlive(int port)
    {
        try
        {
            using var c = new TcpClient();
            var ar = c.BeginConnect("127.0.0.1", port, null, null);
            if (ar.AsyncWaitHandle.WaitOne(800)) { c.EndConnect(ar); return true; }
            return false;
        }
        catch { return false; }
    }

    private static bool WaitPort(int port, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var c = new TcpClient();
                var ar = c.BeginConnect("127.0.0.1", port, null, null);
                if (ar.AsyncWaitHandle.WaitOne(1000)) { c.EndConnect(ar); return true; }
            }
            catch { }
            Thread.Sleep(500);
        }
        return false;
    }

    private static int FindFreePort(ref int cursor, int attempts)
    {
        for (int i = 0; i < attempts; i++)
        {
            int p = cursor + i;
            try
            {
                var l = new TcpListener(IPAddress.Loopback, p);
                l.Start();
                l.Stop();
                cursor = p + 1;
                return p;
            }
            catch { }
        }
        return 0;
    }

    private static string ResolveNode()
    {
        var def = @"C:\Program Files\nodejs\node.exe";
        if (File.Exists(def)) return def;
        return "node.exe"; // fallback PATH
    }

    // ---------------------------------------------------------------- state persist

    private BrowserStateFile LoadState()
    {
        try
        {
            if (File.Exists(_statePath))
                return JsonSerializer.Deserialize<BrowserStateFile>(File.ReadAllText(_statePath)) ?? new BrowserStateFile();
        }
        catch (Exception ex) { LogService.Warn("browser", null, "state load gagal", ex.Message); }
        return new BrowserStateFile();
    }

    private void SaveState(BrowserStateFile state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { LogService.Warn("browser", null, "state save gagal", ex.Message); }
    }

    private void PersistSession(BrowserSessionRecord rec)
    {
        lock (_gate)
        {
            var state = LoadState();
            var existing = state.Records.FirstOrDefault(x => x.SessionId == rec.SessionId);
            if (existing != null) state.Records.Remove(existing);
            state.Records.Add(CloneRecord(rec));
            SaveState(state);
        }
    }

    private static BrowserSessionRecord CloneRecord(BrowserSessionRecord r) => new()
    {
        SessionId = r.SessionId, ClientId = r.ClientId, Name = r.Name,
        AgentPort = r.AgentPort, WrapperPort = r.WrapperPort,
        ProfileDir = r.ProfileDir, TokenFile = r.TokenFile,
        AgentMarker = r.AgentMarker, WrapperMarker = r.WrapperMarker,
        Headless = r.Headless, CreatedUtc = r.CreatedUtc,
        LastActivityUtc = r.LastActivityUtc, Status = r.Status,
        OwnedPids = new List<int>(r.OwnedPids), Error = r.Error,
    };
}
