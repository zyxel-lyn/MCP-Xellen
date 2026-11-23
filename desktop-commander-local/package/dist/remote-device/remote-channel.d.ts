import { Session, User } from '@supabase/supabase-js';
/**
 * Strip NUL characters (U+0000) from strings and object keys — Postgres rejects
 * them in jsonb and text (22P05). Walks the structure rather than
 * round-tripping JSON, which would also match escape text in legitimate content.
 */
export declare function stripNullBytes<T>(value: T): T;
export interface AuthSession {
    access_token: string;
    refresh_token: string | null;
    device_id?: string;
}
/**
 * The device registered, but its realtime channel is not usable — the join
 * failed, or the device row could not be marked ready. Distinct from every other
 * registration failure on purpose: this one the socket watchdog can repair, so
 * the caller keeps the process alive, while a failed lookup or a missing row
 * happens before the recreation parameters are stored and nothing can repair it.
 */
export declare class ChannelUnreachableError extends Error {
    constructor(message: string);
}
interface DeviceData {
    user_id: string;
    device_name: string;
    capabilities: any;
    status: string;
    last_seen: string;
}
export declare const HEARTBEAT_INTERVAL_MS: number;
/** Exponential-ish SDK backoff. Attempts 1-4 preserve short recovery latency. */
export declare function realtimeReconnectDelayMs(tries: number, random?: () => number): number;
/** Watchdog must not defeat the SDK's slower persistent-failure backoff. */
export declare function watchdogRecreateDelayMs(tries: number, random?: () => number): number;
/**
 * Wait before the next track() after failed ones, across SDK rejoins and
 * recreates. Never shorter than the step; up to 50% extra spreads the fleet.
 */
export declare function presenceRecoveryDelayMs(failures: number, random?: () => number): number;
export declare function observeServerDate(dateHeader: string | null): void;
export declare class RemoteChannel {
    private client;
    private channel;
    private heartbeatInterval;
    private connectionCheckInterval;
    private statusWriteChain;
    /**
     * Answers whether the local execution child is alive. Default yes, so a
     * RemoteChannel used without a device (tests, other callers) behaves as
     * before; MCPDevice installs the real probe.
     */
    private localExecutorProbe;
    /** Tokens from the last setSession / TOKEN_REFRESHED, for setOffline(). */
    private lastKnownSession;
    /** Notified when auth-js rotates the session; the device persists it. */
    private sessionRefreshedHandler;
    /** Set by unsubscribe(): suppresses status/heartbeat writes so they can't
     * land after setOffline()'s durable write. */
    private shuttingDown;
    /** Auth session gone for good: stops rejoins and caps the notice at one line. */
    private sessionLost;
    private handlingSignedOut;
    private deviceId;
    private deviceName;
    private onToolCall;
    private authListenerRegistered;
    /** The current join's status write landed: dispatchable. Gates the
     * heartbeat, so only that write first marks the row online. */
    private channelReady;
    /** Readiness write in flight. Cleared on every join, so each join writes its own. */
    private readinessInFlight;
    private consecutiveReadinessFailures;
    private nextReadinessAttemptAt;
    /** Presence published on the current join. Dashboard only; never gates dispatch. */
    private presenceTracked;
    /** Re-entrancy guard: on a wedged socket each track() buffers for the full
     * 10s push timeout, so 10s health ticks would stack pushes. */
    private isTrackingPresence;
    private consecutiveUnreadyJoins;
    private consecutivePresenceFailures;
    private nextPresenceAttemptAt;
    /** performance.now() of the latest SUBSCRIBED; null while not joined. */
    private joinedAt;
    /** SDK channel and socket timers share reconnectAfterMs. The persistent
     * unready-join budget prevents a series of partial successes restarting at 1s. */
    private reconnectDelayMs;
    private readonly realtimeHealth;
    private lastDeviceStatus;
    private lastChannelState;
    private reconnectAttempt;
    private isRecreatingChannel;
    private joiningSince;
    /** Last confirmed proof of life (SUBSCRIBED or heartbeat 'ok'); null until the
     * first one lands. On performance.now(), not Date.now(): the clock-skew
     * correction above can (un)patch Date.now mid-run, jumping wall-clock math by
     * the whole offset — a backward jump would suppress stale detection for as
     * long as the offset. Same for joiningSince. */
    private lastHeartbeatOkAt;
    private heartbeatListenerRegistered;
    /** Our own fixed-cadence auth refresh timer — see TOKEN_REFRESH_INTERVAL_MS. */
    private tokenRefreshInterval;
    private _user;
    get user(): User | null;
    initialize(url: string, key: string): void;
    /**
     * Teach the channel how to ask whether the local executor is alive.
     * `status` is a claim that this device will run a tool call right now, and
     * a joined channel alone cannot support that claim - issue #4.
     */
    setLocalExecutorProbe(probe: () => boolean): void;
    /**
     * Register a callback fired when auth-js rotates the session. auth-js
     * rotates the refresh token on every refresh and the previous one is spent,
     * so a config written once at startup replays a dead token on the next
     * restart and the device demands browser authorization again.
     */
    onSessionRefreshed(handler: (session: AuthSession) => void): void;
    setSession(session: AuthSession): Promise<{
        error: any;
    }>;
    /**
     * Session gone: one restore attempt, then go offline and stop retrying.
     *
     * The attempt matters because auth-js only treats network errors and 502/503/504
     * as retryable — a 429 or 500 kills the session while the refresh token is fine.
     * We don't re-authenticate: DeviceAuthenticator opens a browser and waits.
     */
    private handleSignedOut;
    getSession(): Promise<{
        data: {
            session: Session | null;
        };
        error: any;
    }>;
    /**
     * The session's current access token, for the realtime socket and the
     * device's own requests. getSession() may refresh on-demand when the token
     * reads expired — that check is clock-skew-safe now (see clockAwareFetch
     * above). Without a session it falls back to the last token known, and
     * without that it is null, never the anon key.
     */
    getAccessToken(): Promise<string | null>;
    findDevice(deviceId: string): Promise<{
        id: any;
        device_name: any;
    } | null>;
    updateDevice(deviceId: string, updates: any): Promise<{
        data: any[] | null;
        error: import("@supabase/postgrest-js").PostgrestError | null;
    }>;
    createDevice(deviceData: DeviceData): Promise<{
        data: any;
        error: null;
    }>;
    registerDevice(capabilities: any, currentDeviceId: string | undefined, deviceName: string, onToolCall: (payload: any) => void): Promise<void>;
    /**
     * Mark this join dispatchable by writing `status`. The write has to land —
     * Supabase reports a refused write in the result rather than by throwing.
     * The health check retries on its own cooldown.
     */
    private markChannelReady;
    private markChannelReadyInner;
    /**
     * Publish presence for the dashboard. Best effort: one track() per call,
     * no effect on readiness, and a growing cooldown after each failure — a
     * track() timeout on the server can end the whole channel, and every
     * rejoin would otherwise publish again.
     */
    private trackPresence;
    /** Presence is withheld by its cooldown right now. Monotonic, so a Date.now
     * skew correction cannot shorten or extend it. */
    private presenceInCooldown;
    /**
     * The complete `capabilities` JSONB value. The write replaces the whole
     * column, so a key left out here is dropped from the row.
     */
    private capabilitiesPayload;
    /** Aborts one write to this device's row after DEVICE_ROW_WRITE_TIMEOUT_MS (a method, so tests can shorten it). */
    private deviceRowWriteSignal;
    /** Create and subscribe the private channel (initial join and recreation). */
    private createChannel;
    /**
     * Recover calls whose Realtime doorbell was missed while this device was
     * disconnected. Broadcast provides the fast path; the DB row is the source
     * of truth. Only still-live pending rows for this device are considered.
     *
     * The follow-up onDoorbell() call performs the existing conditional claim,
     * which makes this safe against a doorbell arriving at the same time.
     */
    private recoverPendingCalls;
    /** Hand a call to device.ts, observing the rejection — the handler is async
     * and an unhandled rejection terminates the process. */
    private dispatchToolCall;
    /**
     * Handle a 'new_call' doorbell. It carries ids only; one conditional update
     * claims the row (pending -> executing) and returns it, and it is handed to
     * device.ts marked as claimed.
     */
    private onDoorbell;
    /**
     * Compact connection state for logs — e.g. "socket=open(1) ch=errored attempt=3".
     * readyState 1=OPEN (a 1 while joins keep failing = a half-open socket being reused),
     * 3=CLOSED, '-'=no socket. Reads realtime-js internals defensively; never throws.
     */
    private connState;
    /**
     * Check if channel is connected, recreate if not.
     */
    private checkConnectionHealth;
    /**
     * Run an async op but reject if it doesn't settle within `ms`, so a hung await
     * can't leave isRecreatingChannel stuck true and disable the watchdog. Mirrors
     * closeWithTimeout() in desktop-commander-integration.ts.
     */
    private sleep;
    /**
     * Block until realtime-js has left the 'disconnecting' state it enters on
     * disconnect(), so the next subscribe() actually dials a socket instead of
     * hitting connect()'s early return. Bounded either way — worst case we cost
     * a recreate SOCKET_SETTLE_MAX_MS.
     */
    private waitForSocketSettled;
    private withTimeout;
    /**
     * Recreate the channel by destroying old one and creating fresh instance.
     */
    private recreateChannel;
    /**
     * Claim a call. True only when THIS update flipped the row pending ->
     * executing, which is what makes dual delivery safe across processes.
     * .eq('status','pending') makes it conditional; .select('id') makes the
     * result observable. Expiry is checked using Postgres time, not the
     * device clock. On a transient DB error it returns true (execute anyway),
     * matching prior behaviour — so device.ts's in-memory guard is what
     * actually guarantees exactly-once within a process.
     */
    markCallExecuting(callId: string): Promise<boolean>;
    updateCallResult(callId: string, status: string, result?: any, errorMessage?: string | null): Promise<void>;
    /**
     * Reachable means all of it is true at once: the private channel is
     * joined, the join's status write landed, and the local executor answers.
     * The server needs the first two before it will dispatch, and a healthy
     * channel on a device whose executor is dead is the false-online state
     * issue #4 was opened for. Presence is not part of it. Gates the heartbeat
     * and `status`.
     */
    private isReachable;
    /**
     * Set `status` from actual reachability. `status` is transport-agnostic (the
     * server filters on it), so it must not follow one channel's health — the
     * private channel's error path re-fires on every rejoin and would oscillate
     * the row against the heartbeat. Same predicate as the heartbeat gate.
     *
     * Every transition belongs here rather than calling setOnlineStatus(), which
     * is the write and not the decision. Public so the device can route its
     * recovery transition through the predicate too.
     */
    /** Resolves with whether the row actually took the write. */
    syncReachabilityStatus(): Promise<boolean>;
    /**
     * Serialize the status writes, the heartbeat's included. They fire from
     * un-awaited callbacks, and inside recreateChannel() a teardown's 'offline'
     * and the fresh join's 'online' land ~100-300ms apart — unordered, 'offline'
     * can win and leave a healthy device undispatchable until the next heartbeat.
     *
     * Not the single writer: registerDevice and setOffline's subprocess write
     * status directly, so this is not total ordering.
     */
    private queueStatusWrite;
    updateHeartbeat(): Promise<void>;
    startHeartbeat(deviceId: string): void;
    private refreshTokenNow;
    private startTokenRefresh;
    private stopTokenRefresh;
    stopHeartbeat(): void;
    /** @returns whether the row actually took the write. */
    setOnlineStatus(deviceId: string, status: 'online' | 'offline'): Promise<boolean>;
    setOffline(deviceId: string | undefined): Promise<void>;
    unsubscribe(): Promise<void>;
}
export {};
