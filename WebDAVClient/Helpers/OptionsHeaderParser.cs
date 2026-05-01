using System;
using System.Collections.Generic;

namespace WebDAVClient.Helpers
{
    /// <summary>
    /// Parses the comma-separated tokens carried by HTTP <c>OPTIONS</c>
    /// response headers — <c>DAV</c> (RFC 4918 §10.1) and <c>Allow</c>
    /// (RFC 9110 §10.2.1). Whitespace around tokens is trimmed and empty
    /// tokens are skipped, but token casing is preserved so callers can
    /// surface the server's exact tokens (which matters for non-numeric
    /// extensions like <c>access-control</c>).
    /// </summary>
    internal static class OptionsHeaderParser
    {
        /// <summary>
        /// Splits a comma-separated header value into its constituent tokens.
        /// Returns an empty list (never <c>null</c>) when <paramref name="headerValue"/>
        /// is <c>null</c>, empty, or contains only whitespace / commas.
        /// </summary>
        public static List<string> Split(string headerValue)
        {
            if (string.IsNullOrWhiteSpace(headerValue))
                return new List<string>(0);

            var result = new List<string>(4);
            var span = headerValue.AsSpan();
            int start = 0;
            for (int i = 0; i <= span.Length; i++)
            {
                if (i == span.Length || span[i] == ',')
                {
                    var slice = span.Slice(start, i - start).Trim();
                    if (slice.Length > 0)
                        result.Add(slice.ToString());
                    start = i + 1;
                }
            }
            return result;
        }

        /// <summary>
        /// Joins multiple raw header values (e.g. when a header was repeated)
        /// into a single comma-separated string for the <see cref="Model.ServerOptions.RawDavHeader"/>
        /// / <see cref="Model.ServerOptions.RawAllowHeader"/> surface. Returns
        /// <c>null</c> when <paramref name="values"/> is <c>null</c> or empty.
        /// </summary>
        public static string Join(IEnumerable<string> values)
        {
            if (values == null) return null;
            string joined = string.Join(", ", values);
            return string.IsNullOrEmpty(joined) ? null : joined;
        }
    }
}
