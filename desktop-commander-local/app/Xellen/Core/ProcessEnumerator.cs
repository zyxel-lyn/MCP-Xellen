using System.Management;

namespace McpXellen.Core;

public sealed record ProcInfo(
    uint Pid,
    uint Ppid,
    string Name,
    string? CmdLine,
    DateTime? StartedUtc,
    long WsBytes);

public enum ProcessSnapshotStatus { Success, Failed }

/// <summary>Explicit result of a process-table enumeration.
/// Success=true with an EMPTY list is a VALID result. Success=false means the inventory itself
/// could not be read; the process list MUST then be ignored by every caller.</summary>
public sealed class ProcessSnapshotResult
{
    public ProcessSnapshotStatus Status { get; init; }
    public bool Success => Status == ProcessSnapshotStatus.Success;
    public List<ProcInfo> Processes { get; init; } = new();
    public string? Error { get; init; }
    public DateTime CapturedUtc { get; init; } = DateTime.UtcNow;

    public static ProcessSnapshotResult Ok(List<ProcInfo> processes) =>
        new() { Status = ProcessSnapshotStatus.Success, Processes = processes };
    public static ProcessSnapshotResult Fail(string error) =>
        new() { Status = ProcessSnapshotStatus.Failed, Error = error };
}

/// <summary>Explicit outcome of a per-PID query. NotFound means the query SUCCEEDED and the PID
/// genuinely does not exist. Failed/Timeout mean the process state is UNKNOWN.</summary>
public enum ProcQueryStatus { Success, NotFound, Failed, Timeout }

public sealed class ProcQueryResult
{
    public ProcQueryStatus Status { get; init; }
    public ProcInfo? Info { get; init; }
    public string? Error { get; init; }
    public static ProcQueryResult Found(ProcInfo i) => new() { Status = ProcQueryStatus.Success, Info = i };
    public static ProcQueryResult NotFoundResult() => new() { Status = ProcQueryStatus.NotFound };
    public static ProcQueryResult FailedResult(string e) => new() { Status = ProcQueryStatus.Failed, Error = e };
    public static ProcQueryResult TimeoutResult(string e) => new() { Status = ProcQueryStatus.Timeout, Error = e };
}

/// <summary>Cancel handle passed into raw query implementations. The pipeline calls Cancel() on
/// timeout; the implementation MUST register its real cancellation callback (for WMI this is
/// ManagementOperationObserver.Cancel → IWbemServices::CancelAsyncCall).</summary>
public sealed class CancellationHandle
{
    private volatile Action? _onCancel;
    public bool CancelRequested { get; private set; }
    public void Register(Action onCancel) => _onCancel = onCancel;
    public void Cancel()
    {
        CancelRequested = true;
        var a = _onCancel;
        if (a == null) return;
        try { a(); } catch { }
    }
}

/// <summary>Honest accounting of WMI operations. Cancellation is only counted as such when the
/// operation is OBSERVED to stop (Completed callback / task completion within the grace window).
/// AbandonedOperations counts timeouts where the work did NOT stop — we never claim those cancelled;
/// OutstandingOperations stays elevated until the work actually ends (tracked by a continuation).</summary>
public static class WmiInstrumentation
{
    public static long OutstandingOperations;
    public static long CompletedOperations;
    public static long CancelledOperations;   // cancel OBSERVED (work provably stopped)
    public static long AbandonedOperations;   // timeout, work NOT observed to stop (honest: may still run)
    public static void Reset()
    {
        OutstandingOperations = CompletedOperations = CancelledOperations = AbandonedOperations = 0;
    }
}

/// <summary>
/// Process-table enumeration via WMI. Cancellation model (honest):
///  - Real WMI queries run asynchronously through ManagementOperationObserver; on timeout we call
///    observer.Cancel() (COM CancelAsyncCall) and only report "cancellation observed" when the
///    Completed callback actually fires within the grace window.
///  - If the work does not stop within the grace window, the result says so explicitly
///    ("abandoned … may still be running") — we never claim a cancel that was not observed.
///  - OutstandingOperations tracks work until it truly ends (continuation decrements it).
/// Failure contract: an exception/timeout is NEVER converted into a successful empty list.
/// </summary>
public static class ProcessEnumerator
{
    public static TimeSpan EnumerationTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public static TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>How long we wait AFTER issuing Cancel() for the work to actually stop.</summary>
    public static TimeSpan PostCancelGrace { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Upper bound an abandoned query thread may block before giving up its waiter.</summary>
    private static readonly TimeSpan AbandonedWaiterCap = TimeSpan.FromMinutes(10);

    /// <summary>Test injection (enumeration). Receives the cancel handle; MUST honor Register()
    /// by stopping when Cancel fires. Null in production.</summary>
    public static Func<CancellationHandle, List<ProcInfo>>? RawEnumerationOverride { get; set; }
    /// <summary>Test injection (per-PID query). Returns null = NotFound; may throw = Failed.
    /// Null in production.</summary>
    public static Func<uint, CancellationHandle, ProcInfo?>? RawSingleQueryOverride { get; set; }

    // ---------------------------------------------------------------- enumeration

    public static ProcessSnapshotResult Snapshot()
    {
        var handle = new CancellationHandle();
        Interlocked.Increment(ref WmiInstrumentation.OutstandingOperations);
        var task = Task.Run(() => RunRawEnumeration(handle));
        // Work is tracked until it truly ends — even when Snapshot() has already given up on it.
        _ = task.ContinueWith(_ => Interlocked.Decrement(ref WmiInstrumentation.OutstandingOperations));

        try
        {
            if (task.Wait(EnumerationTimeout))
            {
                Interlocked.Increment(ref WmiInstrumentation.CompletedOperations);
                return ProcessSnapshotResult.Ok(task.Result);
            }
        }
        catch (AggregateException agg)
        {
            var msg = agg.InnerException?.Message ?? agg.Message;
            LogService.Debug("enumerator", null, $"Snapshot failed: {msg}");
            return ProcessSnapshotResult.Fail(msg);
        }

        // Timeout: issue REAL cancellation, then VERIFY it within the grace window.
        handle.Cancel();
        try
        {
            if (task.Wait(PostCancelGrace))
            {
                Interlocked.Increment(ref WmiInstrumentation.CancelledOperations);
                return ProcessSnapshotResult.Fail(
                    $"WMI enumeration timed out after {EnumerationTimeout.TotalMilliseconds:F0}ms; cancellation observed (operation stopped)");
            }
        }
        catch (AggregateException)
        {
            Interlocked.Increment(ref WmiInstrumentation.CancelledOperations);
            return ProcessSnapshotResult.Fail(
                $"WMI enumeration timed out after {EnumerationTimeout.TotalMilliseconds:F0}ms; cancellation observed (operation ended with an error after cancel)");
        }

        // Honest branch: the work did NOT stop. Say so; never claim cancellation.
        Interlocked.Increment(ref WmiInstrumentation.AbandonedOperations);
        LogService.Warn("enumerator", null,
            $"WMI enumeration ABANDONED: did not stop within {PostCancelGrace.TotalMilliseconds:F0}ms after Cancel(). Background work may still be running — tracked in OutstandingOperations until it ends. NOT cancelled.");
        return ProcessSnapshotResult.Fail(
            $"WMI enumeration timed out after {EnumerationTimeout.TotalMilliseconds:F0}ms and did not respond to cancellation within {PostCancelGrace.TotalMilliseconds:F0}ms — background work may still be running (abandoned, tracked)");
    }

    private static List<ProcInfo> RunRawEnumeration(CancellationHandle handle)
    {
        if (RawEnumerationOverride != null) return RawEnumerationOverride(handle);
        return EnumerateViaWmi(handle);
    }

    /// <summary>Asynchronous WMI enumeration with real cancellation: ManagementOperationObserver
    /// maps to COM async query; observer.Cancel() calls IWbemServices::CancelAsyncCall. Completion
    /// is OBSERVED via the Completed event — that is our only proof the work stopped.</summary>
    private static List<ProcInfo> EnumerateViaWmi(CancellationHandle handle)
    {
        var done = new ManualResetEventSlim(false);
        var results = new List<ProcInfo>(512);
        string? error = null;

        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, ParentProcessId, Name, CommandLine, CreationDate, WorkingSetSize FROM Win32_Process");
        var observer = new ManagementOperationObserver();
        observer.ObjectReady += (_, e) =>
        {
            try { using var mo = e.NewObject; results.Add(ToProcInfo(mo)); }
            catch (Exception ex) { error ??= ex.Message; }
        };
        observer.Completed += (_, e) =>
        {
            // OperationCanceled is the expected status when WE cancelled — completion observed.
            if (e.Status != ManagementStatus.NoError && e.Status != ManagementStatus.OperationCanceled && error == null)
                error = $"WMI status {e.Status}";
            done.Set();
        };
        handle.Register(observer.Cancel);

        try { searcher.Get(observer); }
        catch (Exception ex) { error = ex.Message; done.Set(); }

        done.Wait(AbandonedWaiterCap);
        if (error != null) throw new InvalidOperationException(error);
        return results;
    }

    // ---------------------------------------------------------------- per-PID query

    /// <summary>Per-PID query with timeout and explicit 4-status outcome. Failed/Timeout mean
    /// UNKNOWN — kill paths must refuse on those, never treat them as "process gone".</summary>
    public static ProcQueryResult QueryPid(uint pid)
    {
        var handle = new CancellationHandle();
        Interlocked.Increment(ref WmiInstrumentation.OutstandingOperations);
        var task = Task.Run(() => RunRawSingleQuery(pid, handle));
        _ = task.ContinueWith(_ => Interlocked.Decrement(ref WmiInstrumentation.OutstandingOperations));

        try
        {
            if (task.Wait(QueryTimeout))
            {
                var info = task.Result; // throws AggregateException if faulted → Failed
                Interlocked.Increment(ref WmiInstrumentation.CompletedOperations);
                return info == null ? ProcQueryResult.NotFoundResult() : ProcQueryResult.Found(info);
            }
        }
        catch (AggregateException agg)
        {
            var msg = agg.InnerException?.Message ?? agg.Message;
            LogService.Debug("enumerator", null, $"QueryPid({pid}) failed: {msg}");
            return ProcQueryResult.FailedResult(msg);
        }

        // Timeout: real cancel, then verify within grace. Data from a cancelled op is NOT used.
        handle.Cancel();
        try
        {
            if (task.Wait(PostCancelGrace))
            {
                Interlocked.Increment(ref WmiInstrumentation.CancelledOperations);
                return ProcQueryResult.TimeoutResult(
                    $"PID {pid} query timed out after {QueryTimeout.TotalMilliseconds:F0}ms; cancellation observed (operation stopped)");
            }
        }
        catch (AggregateException)
        {
            Interlocked.Increment(ref WmiInstrumentation.CancelledOperations);
            return ProcQueryResult.TimeoutResult(
                $"PID {pid} query timed out after {QueryTimeout.TotalMilliseconds:F0}ms; cancellation observed (operation ended with an error after cancel)");
        }

        Interlocked.Increment(ref WmiInstrumentation.AbandonedOperations);
        LogService.Warn("enumerator", null,
            $"QueryPid({pid}) ABANDONED: did not stop within {PostCancelGrace.TotalMilliseconds:F0}ms after Cancel(). Background work may still be running — tracked. NOT cancelled.");
        return ProcQueryResult.TimeoutResult(
            $"PID {pid} query timed out after {QueryTimeout.TotalMilliseconds:F0}ms and did not respond to cancellation within {PostCancelGrace.TotalMilliseconds:F0}ms — background work may still be running (abandoned, tracked)");
    }

    private static ProcInfo? RunRawSingleQuery(uint pid, CancellationHandle handle)
    {
        if (RawSingleQueryOverride != null) return RawSingleQueryOverride(pid, handle);
        return QueryViaWmi(pid, handle);
    }

    private static ProcInfo? QueryViaWmi(uint pid, CancellationHandle handle)
    {
        var done = new ManualResetEventSlim(false);
        ProcInfo? found = null;
        string? error = null;

        using var searcher = new ManagementObjectSearcher(
            $"SELECT ProcessId, ParentProcessId, Name, CommandLine, CreationDate, WorkingSetSize FROM Win32_Process WHERE ProcessId = {pid}");
        var observer = new ManagementOperationObserver();
        observer.ObjectReady += (_, e) =>
        {
            try { using var mo = e.NewObject; found = ToProcInfo(mo); }
            catch (Exception ex) { error ??= ex.Message; }
        };
        observer.Completed += (_, e) =>
        {
            if (e.Status != ManagementStatus.NoError && e.Status != ManagementStatus.OperationCanceled && error == null)
                error = $"WMI status {e.Status}";
            done.Set();
        };
        handle.Register(observer.Cancel);

        try { searcher.Get(observer); }
        catch (Exception ex) { error = ex.Message; done.Set(); }

        done.Wait(AbandonedWaiterCap);
        if (error != null) throw new InvalidOperationException(error);
        return found;
    }

    // ---------------------------------------------------------------- shared mapping

    private static ProcInfo ToProcInfo(ManagementBaseObject mo)
    {
        uint pid = Convert.ToUInt32(mo["ProcessId"]);
        uint ppid = Convert.ToUInt32(mo["ParentProcessId"]);
        string name = mo["Name"]?.ToString() ?? "";
        string? cmd = mo["CommandLine"]?.ToString();
        DateTime? started = null;
        try
        {
            var cd = mo["CreationDate"]?.ToString();
            if (!string.IsNullOrEmpty(cd)) started = ManagementDateTimeConverter.ToDateTime(cd);
        }
        catch { }
        long ws = 0;
        try { ws = Convert.ToInt64(mo["WorkingSetSize"]); } catch { }
        return new ProcInfo(pid, ppid, name, cmd, started, ws);
    }
}
