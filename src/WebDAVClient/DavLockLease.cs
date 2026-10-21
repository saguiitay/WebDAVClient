#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebDAVClient.Protocol;

namespace WebDAVClient
{
    /// <summary>
    /// Local knowledge of a managed lock; Active is an observation, not a guarantee of server exclusivity.
    /// </summary>
    public enum DavLockLeaseState
    {
        /// <summary>
        /// A matching lock was confirmed and its conservatively measured timeout has not elapsed.
        /// </summary>
        Active,

        /// <summary>
        /// The last confirmed finite timeout elapsed locally.
        /// </summary>
        Expired,

        /// <summary>
        /// The granted timeout is unknown, or a renewal/release has no confirmed successful outcome.
        /// </summary>
        Uncertain,

        /// <summary>
        /// UNLOCK completed with HTTP 204.
        /// </summary>
        Released
    }

    /// <summary>
    /// Explicitly renewed, serialized lock lifecycle. There is no timer, background I/O, reacquisition or retry.
    /// DisposeAsync makes at most one release attempt; inspect State after a failed attempt and reconcile on the server.
    /// The client remains caller-owned and must outlive the lease.
    /// </summary>
    public sealed class DavLockLease : IAsyncDisposable
    {
        private readonly DavClient m_client;

        private readonly TimeProvider m_timeProvider;

        private readonly SemaphoreSlim m_gate = new SemaphoreSlim(1, 1);

        private readonly object m_stateLock = new object();

        private DavLock m_observation;

        private long m_started;

        private DavLockLeaseState m_state;

        private bool m_releaseAttempted;

        private DavLockLease(DavClient client, DavPath path, string token, DavLock observation, TimeProvider timeProvider, long started)
        {
            m_client = client;
            Path = path;
            Token = token;
            m_timeProvider = timeProvider;
            m_observation = observation;
            m_started = started;
            m_state = KnownState(observation);
        }

        /// <summary>
        /// Original lock request path. Lockroot values received from a server are never followed.
        /// </summary>
        public DavPath Path
        {
            get;
        }

        /// <summary>
        /// Bare opaque token confirmed in both the acquisition header and lockdiscovery.
        /// </summary>
        public string Token
        {
            get;
        }

        /// <summary>
        /// Last confirmed matching lock, never fabricated from a requested timeout.
        /// </summary>
        public DavLock Observation
        {
            get
            {
                lock (m_stateLock)
                {
                    return m_observation;
                }
            }
        }

        /// <summary>
        /// Conservative current state. Finite timeouts run from before the request, using monotonic elapsed time.
        /// Unknown timeouts cannot establish an Active lease.
        /// </summary>
        public DavLockLeaseState State
        {
            get
            {
                lock (m_stateLock)
                {
                    return m_state == DavLockLeaseState.Active && m_observation.Timeout.Duration is TimeSpan duration
                        && m_timeProvider.GetElapsedTime(m_started) >= duration ? DavLockLeaseState.Expired : m_state;
                }
            }
        }

        /// <summary>
        /// Acquires a lock and validates its token identity. Rejections and unconfirmed locks throw.
        /// A timeout or malformed acquisition may leave a server lock behind; no automatic recovery mutation occurs.
        /// </summary>
        /// <param name="client">
        /// Caller-owned client.
        /// </param>
        /// <param name="path">
        /// Same-origin lock target.
        /// </param>
        /// <param name="options">
        /// Acquisition scope, depth, owner, timeout preferences and conditions.
        /// </param>
        /// <param name="timeProvider">
        /// Optional monotonic clock; defaults to TimeProvider.System.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels acquisition, not the later lease lifetime.
        /// </param>
        /// <exception cref="Helpers.WebDAVException">
        /// The server rejects acquisition or does not confirm exactly one matching token, scope and depth.
        /// </exception>
        /// <exception cref="System.Net.Http.HttpRequestException">
        /// Transport acquisition fails; the server outcome may be ambiguous.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Acquisition is canceled; the server outcome may be ambiguous.
        /// </exception>
        public static async Task<DavLockLease> AcquireAsync(DavClient client, DavPath path, LockOptions? options = null,
            TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(path);
            var clock = timeProvider ?? TimeProvider.System;
            var started = clock.GetTimestamp();
            var result = await client.AcquireLockAsync(path, options, cancellationToken).ConfigureAwait(false);
            result.EnsureSucceeded();
            var header = result.LockToken!.Trim(' ', '\t');
            var token = header.Substring(1, header.Length - 2);
            var observation = MatchingLock(result, token);
            var scope = options?.Scope == DavLockScope.Shared ? "shared" : "exclusive";
            var depth = options?.Depth == DavDepth.Infinity ? "infinity" : "0";
            if (observation.Scope != new Model.PropertyName("DAV:", scope) || observation.Depth != depth)
            {
                throw result.CreateException("The granted lock did not confirm the requested scope and depth.");
            }
            return new DavLockLease(client, path, token, observation, clock, started);
        }

        /// <summary>
        /// Produces a root-tagged token assertion, optionally ANDed with an entity tag.
        /// Refuses locally expired, uncertain or released leases. This local check cannot prevent server-side expiry;
        /// always submit the returned conditions with the mutation and inspect its outcome.
        /// </summary>
        /// <param name="entityTag">
        /// Optional quoted entity tag for the lock target, not '*'. This is a DAV If comparison, not HTTP If-Match.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// The observed lease is not active.
        /// </exception>
        public DavIfConditions CreateConditions(string? entityTag = null)
        {
            if (State != DavLockLeaseState.Active)
            {
                throw new InvalidOperationException("Only an active observed lease can provide mutation conditions.");
            }
            var token = DavCondition.StateToken(Token);
            var list = entityTag == null ? new DavConditionList(token)
                : new DavConditionList(token, DavCondition.EntityTag(entityTag));
            return DavIfConditions.Tagged(new DavTaggedConditions(Path, list));
        }

        /// <summary>
        /// Explicitly refreshes this token without a body or depth header. Unknown or expired leases may be reconciled
        /// by an explicit refresh; failures leave State uncertain. No reacquisition or replay is performed.
        /// </summary>
        /// <param name="timeouts">
        /// Ordered timeout preferences, or the low-level client's default preferences when null.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels waiting and this refresh attempt; cancellation after it starts leaves the outcome uncertain.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Release was already attempted.
        /// </exception>
        /// <exception cref="Helpers.WebDAVException">
        /// The server rejects renewal or does not confirm the same lock identity, scope and depth.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Waiting or renewal was canceled.
        /// </exception>
        public async Task<DavLock> RenewAsync(System.Collections.Generic.IReadOnlyList<DavLockTimeout>? timeouts = null,
            CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (m_stateLock)
                {
                    if (m_releaseAttempted)
                    {
                        throw new InvalidOperationException("A release was already attempted.");
                    }
                    m_state = DavLockLeaseState.Uncertain;
                }
                var started = m_timeProvider.GetTimestamp();
                var result = await m_client.RefreshLockAsync(Path, Token, timeouts, cancellationToken).ConfigureAwait(false);
                result.EnsureSucceeded();
                var observation = MatchingLock(result, Token);
                lock (m_stateLock)
                {
                    if (observation.Scope != m_observation.Scope || observation.Depth != m_observation.Depth)
                    {
                        throw result.CreateException("A refresh cannot change the lock scope or depth.");
                    }
                    m_observation = observation;
                    m_started = started;
                    m_state = KnownState(observation);
                }
                return observation;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Makes at most one UNLOCK attempt, including across repeated disposal and concurrent callers.
        /// Cancellation before the attempt leaves the lease unchanged. Any unconfirmed attempt leaves it uncertain;
        /// subsequent calls do not retry, and manual server reconciliation is required.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels waiting or the one release attempt.
        /// </param>
        /// <exception cref="Helpers.WebDAVException">
        /// The server does not confirm release with HTTP 204.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Waiting or release was canceled.
        /// </exception>
        public async Task ReleaseAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (m_stateLock)
                {
                    if (m_releaseAttempted)
                    {
                        return;
                    }
                    m_releaseAttempted = true;
                    m_state = DavLockLeaseState.Uncertain;
                }
                var result = await m_client.UnlockAsync(Path, Token, cancellationToken).ConfigureAwait(false);
                result.EnsureSucceeded();
                lock (m_stateLock)
                {
                    m_state = DavLockLeaseState.Released;
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases once without disposing the client. Server rejection or transport failure propagates.
        /// </summary>
        public ValueTask DisposeAsync() => new ValueTask(ReleaseAsync());

        private static DavLockLeaseState KnownState(DavLock observation) =>
            observation.Timeout.IsInfinite || observation.Timeout.Duration.HasValue ? DavLockLeaseState.Active : DavLockLeaseState.Uncertain;

        private static DavLock MatchingLock(DavLockResult result, string token)
        {
            var matches = result.Locks.Where(l => string.Equals(l.Token, token, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
            {
                throw result.CreateException("The response did not confirm exactly one matching write lock.");
            }
            return matches[0];
        }
    }
}
