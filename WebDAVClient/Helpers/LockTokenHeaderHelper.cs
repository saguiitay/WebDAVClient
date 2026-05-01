using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace WebDAVClient.Helpers
{
    /// <summary>
    /// Helpers for emitting and parsing the WebDAV lock-token-related HTTP headers
    /// defined by RFC 4918:
    /// <list type="bullet">
    ///   <item><description><c>If</c> request header (§10.4) — submits active lock tokens on PUT / DELETE / MOVE / COPY.</description></item>
    ///   <item><description><c>Lock-Token</c> response header (§10.5) — returned by a successful LOCK.</description></item>
    /// </list>
    /// <para>
    /// Lock tokens flow through this client in <em>bare</em> form (no surrounding angle
    /// brackets); the <c>Lock-Token</c> and <c>If</c> headers add the brackets when
    /// emitting. Callers may submit either form because real-world tokens are commonly
    /// copy-pasted from response headers including the brackets.
    /// </para>
    /// </summary>
    internal static class LockTokenHeaderHelper
    {
        // Characters that must not appear inside a normalized lock token: angle brackets
        // would corrupt the If/Lock-Token header framing, and CR/LF would enable HTTP
        // header injection. Cached so IndexOfAny doesn't reallocate on every call.
        private static readonly char[] s_lockTokenForbiddenChars = { '<', '>', '\r', '\n' };

        /// <summary>
        /// Returns the lock token in bare (no angle brackets) form, throwing
        /// <see cref="ArgumentException"/> if the token is null/empty/whitespace or contains
        /// characters that would corrupt an emitted header (<c>&lt;</c>, <c>&gt;</c>, CR, LF).
        /// </summary>
        public static string NormalizeLockToken(string lockToken)
        {
            if (string.IsNullOrWhiteSpace(lockToken))
                throw new ArgumentException("Lock token must be a non-empty string.", nameof(lockToken));

            var trimmed = lockToken.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '<' && trimmed[trimmed.Length - 1] == '>')
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
            }

            if (trimmed.Length == 0 || trimmed.IndexOfAny(s_lockTokenForbiddenChars) >= 0)
            {
                throw new ArgumentException("Lock token is malformed.", nameof(lockToken));
            }

            return trimmed;
        }

        /// <summary>
        /// Convenience for the unlocked-fast-path: returns <c>null</c> when no lock token is
        /// supplied (so the caller skips header allocation entirely), otherwise a fresh
        /// header dictionary containing a single <c>If</c> header tagged to <paramref name="requestUri"/>.
        /// </summary>
        public static IDictionary<string, string> BuildLockTokenHeaders(Uri requestUri, string lockToken)
        {
            if (lockToken == null)
                return null;

            var headers = new Dictionary<string, string>(1);
            AddIfHeader(headers, requestUri, lockToken, destinationUri: null, destinationLockToken: null);
            return headers;
        }

        /// <summary>
        /// Adds an <c>If</c> header to <paramref name="headers"/> if either token is supplied.
        /// No-op when both tokens are <c>null</c>.
        /// </summary>
        public static void AddIfHeader(IDictionary<string, string> headers, Uri requestUri, string sourceLockToken, Uri destinationUri, string destinationLockToken)
        {
            var value = BuildIfHeader(requestUri, sourceLockToken, destinationUri, destinationLockToken);
            if (value != null)
            {
                headers["If"] = value;
            }
        }

        /// <summary>
        /// Builds the value of the <c>If</c> header per RFC 4918 §10.4. Forms emitted:
        /// <list type="bullet">
        ///   <item><description>request-URI lock only → <c>If: (&lt;token&gt;)</c> (no-tag-list)</description></item>
        ///   <item><description>destination lock only → <c>If: &lt;dest-uri&gt; (&lt;token&gt;)</c> (tagged-list)</description></item>
        ///   <item><description>both → <c>If: &lt;src-uri&gt; (&lt;src-token&gt;) &lt;dest-uri&gt; (&lt;dest-token&gt;)</c></description></item>
        /// </list>
        /// Returns <c>null</c> when both tokens are <c>null</c>.
        /// </summary>
        /// <remarks>
        /// The "both" form is two tagged productions; per §10.4.3 they OR at If-evaluation
        /// time, but server-side WebDAV lock-token-submitted rules require only that each
        /// needed token <em>appear</em> in the header, which both productions guarantee.
        /// The two productions cannot be mixed with a no-tag-list inside the same header,
        /// so as soon as a destination token is in play both sides switch to tagged form.
        /// </remarks>
        public static string BuildIfHeader(Uri requestUri, string sourceLockToken, Uri destinationUri, string destinationLockToken)
        {
            if (sourceLockToken == null && destinationLockToken == null)
                return null;

            var srcToken = sourceLockToken == null ? null : NormalizeLockToken(sourceLockToken);
            var dstToken = destinationLockToken == null ? null : NormalizeLockToken(destinationLockToken);

            // Only the request-URI is locked → no-tag-list.
            if (srcToken != null && dstToken == null)
            {
                return "(<" + srcToken + ">)";
            }

            // Destination is locked → tagged-list (cannot mix no-tag and tagged in one header).
            var sb = new StringBuilder();
            if (srcToken != null)
            {
                if (requestUri == null)
                    throw new InvalidOperationException("requestUri is required when emitting a tagged If-header for the source lock token.");
                sb.Append('<').Append(requestUri.AbsoluteUri).Append("> (<").Append(srcToken).Append(">)");
            }
            if (dstToken != null)
            {
                if (destinationUri == null)
                    throw new InvalidOperationException("destinationUri is required when a destinationLockToken is supplied.");
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('<').Append(destinationUri.AbsoluteUri).Append("> (<").Append(dstToken).Append(">)");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Reads the <c>Lock-Token</c> response header (RFC 4918 §10.5) and returns the
        /// token in bare form (angle brackets stripped), or <c>null</c> if the header is
        /// absent or empty. <c>Lock-Token = "Lock-Token" ":" Coded-URL</c> where
        /// <c>Coded-URL = "&lt;" absolute-URI "&gt;"</c>.
        /// </summary>
        public static string ExtractLockTokenHeader(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Lock-Token", out var values))
            {
                foreach (var v in values)
                {
                    if (string.IsNullOrWhiteSpace(v))
                        continue;
                    var trimmed = v.Trim();
                    if (trimmed.Length >= 2 && trimmed[0] == '<' && trimmed[trimmed.Length - 1] == '>')
                    {
                        return trimmed.Substring(1, trimmed.Length - 2).Trim();
                    }
                    return trimmed;
                }
            }
            return null;
        }
    }
}
