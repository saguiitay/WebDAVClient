namespace WebDAVClient.Model
{
    /// <summary>
    /// Information about a WebDAV lock returned by <see cref="IClient.LockFile"/>,
    /// <see cref="IClient.LockFolder"/>, and <see cref="IClient.RefreshLock"/>.
    /// </summary>
    /// <remarks>
    /// Mirrors the contents of the <c>&lt;D:activelock&gt;</c> element described in
    /// RFC 4918 §14.1 (with a copy of the canonical lock token from the
    /// <c>Lock-Token</c> response header per §10.5).
    /// </remarks>
    public class LockInfo
    {
        /// <summary>
        /// The opaque lock token URI returned by the server, in bare form
        /// (no surrounding <c>&lt;&gt;</c>). This is the value to pass back to
        /// <see cref="IClient.UnlockFile"/> / <see cref="IClient.UnlockFolder"/> /
        /// <see cref="IClient.RefreshLock"/>; the client wraps it in the correct
        /// header syntax automatically.
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// Free-form text content of the <c>&lt;D:owner&gt;</c> element as the
        /// server returned it. May be <c>null</c> when the server did not
        /// echo an owner.
        /// </summary>
        public string Owner { get; set; }

        /// <summary>
        /// Lock type — typically <c>"write"</c> (the only value defined by RFC 4918).
        /// </summary>
        public string LockType { get; set; }

        /// <summary>
        /// Lock scope — <c>"exclusive"</c> or <c>"shared"</c>. This client always
        /// requests <c>exclusive</c>.
        /// </summary>
        public string LockScope { get; set; }

        /// <summary>
        /// Depth at which the lock applies — <c>"0"</c> for a single resource
        /// or <c>"infinity"</c> for a collection lock.
        /// </summary>
        public string Depth { get; set; }

        /// <summary>
        /// Lock timeout in seconds, or <c>null</c> when the server returned
        /// <c>Infinite</c>. Note that the server may grant a different timeout
        /// than was requested.
        /// </summary>
        public int? TimeoutSeconds { get; set; }

        /// <summary>
        /// The href to which the lock actually applies — i.e. the value of
        /// <c>&lt;D:lockroot&gt;&lt;D:href&gt;</c> in the response. Useful when
        /// locking a member of a depth-infinity collection lock.
        /// </summary>
        public string LockRoot { get; set; }
    }
}
