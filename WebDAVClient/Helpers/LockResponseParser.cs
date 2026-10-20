using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using WebDAVClient.Model;

namespace WebDAVClient.Helpers
{
    /// <summary>
    /// Parses the <c>&lt;D:lockdiscovery&gt;&lt;D:activelock&gt;</c> subtree
    /// returned by a successful LOCK or refresh-LOCK response (RFC 4918 §9.10).
    /// Only the first <c>activelock</c> element is read — this client always
    /// requests an exclusive write lock, so a successful response carries
    /// exactly one.
    /// </summary>
    internal static class LockResponseParser
    {
        private const string c_davNamespace = "DAV:";

        /// <summary>
        /// Parses a LOCK response body. Returns <c>null</c> if no
        /// <c>activelock</c> element is present (e.g. an empty body from a
        /// non-compliant server) — the caller can decide whether to treat
        /// that as an error.
        /// </summary>
        public static LockInfo Parse(Stream stream)
        {
            return ParseAsync(stream).GetAwaiter().GetResult();
        }

        public static async Task<LockInfo> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            try
            {
                using (var reader = XmlReader.Create(stream, ResponseParser.XmlReaderSettings))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reader.NodeType == XmlNodeType.Element
                            && string.Equals(reader.LocalName, "activelock", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase))
                        {
                            return await ReadActiveLockAsync(reader, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (XmlException)
            {
                // Empty / malformed bodies are treated the same as a missing activelock —
                // callers (LockFile/RefreshLock) decide whether absence is an error.
                return null;
            }
            return null;
        }

        private static async Task<LockInfo> ReadActiveLockAsync(XmlReader reader, CancellationToken cancellationToken)
        {
            var info = new LockInfo();
            int depth = reader.Depth;

            bool advance = true;
            while (!advance || await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                advance = true;
                if (reader.NodeType == XmlNodeType.EndElement
                    && reader.Depth == depth
                    && string.Equals(reader.LocalName, "activelock", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (!string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var localName = reader.LocalName;
                if (string.Equals(localName, "locktype", StringComparison.OrdinalIgnoreCase))
                {
                    info.LockType = await ReadFirstChildElementNameAsync(reader).ConfigureAwait(false);
                }
                else if (string.Equals(localName, "lockscope", StringComparison.OrdinalIgnoreCase))
                {
                    info.LockScope = await ReadFirstChildElementNameAsync(reader).ConfigureAwait(false);
                }
                else if (string.Equals(localName, "depth", StringComparison.OrdinalIgnoreCase))
                {
                    if (!reader.IsEmptyElement)
                    {
                        info.Depth = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                        advance = false;
                    }
                }
                else if (string.Equals(localName, "timeout", StringComparison.OrdinalIgnoreCase))
                {
                    if (!reader.IsEmptyElement)
                    {
                        info.TimeoutSeconds = ParseTimeout(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        advance = false;
                    }
                }
                else if (string.Equals(localName, "owner", StringComparison.OrdinalIgnoreCase))
                {
                    if (!reader.IsEmptyElement)
                    {
                        info.Owner = (await reader.ReadInnerXmlAsync().ConfigureAwait(false)).Trim();
                        advance = false;
                    }
                }
                else if (string.Equals(localName, "locktoken", StringComparison.OrdinalIgnoreCase))
                {
                    info.Token = await ReadHrefChildAsync(reader).ConfigureAwait(false);
                }
                else if (string.Equals(localName, "lockroot", StringComparison.OrdinalIgnoreCase))
                {
                    info.LockRoot = await ReadHrefChildAsync(reader).ConfigureAwait(false);
                }
            }

            return info;
        }

        // For elements like <D:locktype><D:write/></D:locktype> — return "write".
        private static async Task<string> ReadFirstChildElementNameAsync(XmlReader reader)
        {
            if (reader.IsEmptyElement)
            {
                return null;
            }

            int depth = reader.Depth;
            string name = null;
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }

                if (name == null && reader.NodeType == XmlNodeType.Element)
                {
                    name = reader.LocalName;
                }
            }
            return name;
        }

        // For elements like <D:locktoken><D:href>opaquelocktoken:...</D:href></D:locktoken>
        // — return the inner href text.
        private static async Task<string> ReadHrefChildAsync(XmlReader reader)
        {
            if (reader.IsEmptyElement)
            {
                return null;
            }

            int depth = reader.Depth;
            string href = null;
            bool advance = true;
            while (!advance || await reader.ReadAsync().ConfigureAwait(false))
            {
                advance = true;
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }

                if (href == null && reader.NodeType == XmlNodeType.Element
                    && string.Equals(reader.LocalName, "href", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase))
                {
                    href = (await reader.ReadElementContentAsStringAsync().ConfigureAwait(false)).Trim();
                    advance = false;
                }
            }
            return href;
        }

        // RFC 4918 §10.7: TimeType = "Second-" 1*DIGIT | "Infinite"
        // We accept a comma-separated list (the server picks one) and take the first.
        internal static int? ParseTimeout(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            foreach (var part in raw.Split(','))
            {
                var token = part.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                if (string.Equals(token, "Infinite", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (token.StartsWith("Second-", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(token.Substring("Second-".Length), out var seconds))
                {
                    return seconds;
                }
            }
            return null;
        }
    }
}
