using System;
using System.IO;
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
            try
            {
                using (var reader = XmlReader.Create(stream, ResponseParser.XmlReaderSettings))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element
                            && string.Equals(reader.LocalName, "activelock", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase))
                        {
                            return ReadActiveLock(reader);
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

        private static LockInfo ReadActiveLock(XmlReader reader)
        {
            var info = new LockInfo();
            int depth = reader.Depth;

            // All Read* helpers below use ReadSubtree internally, which leaves the parent
            // reader positioned on the activelock-child's EndElement. That keeps the loop's
            // reader.Read() correct (advances to the next sibling) without needing a
            // "previous call already advanced" flag.
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement
                    && reader.Depth == depth
                    && string.Equals(reader.LocalName, "activelock", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                if (!string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase))
                    continue;

                var localName = reader.LocalName;
                if (string.Equals(localName, "locktype", StringComparison.OrdinalIgnoreCase))
                {
                    info.LockType = ReadFirstChildElementName(reader);
                }
                else if (string.Equals(localName, "lockscope", StringComparison.OrdinalIgnoreCase))
                {
                    info.LockScope = ReadFirstChildElementName(reader);
                }
                else if (string.Equals(localName, "depth", StringComparison.OrdinalIgnoreCase))
                {
                    info.Depth = ReadElementText(reader);
                }
                else if (string.Equals(localName, "timeout", StringComparison.OrdinalIgnoreCase))
                {
                    info.TimeoutSeconds = ParseTimeout(ReadElementText(reader));
                }
                else if (string.Equals(localName, "owner", StringComparison.OrdinalIgnoreCase))
                {
                    info.Owner = ReadOwnerInnerXml(reader);
                }
                else if (string.Equals(localName, "locktoken", StringComparison.OrdinalIgnoreCase))
                {
                    info.Token = ReadHrefChild(reader);
                }
                else if (string.Equals(localName, "lockroot", StringComparison.OrdinalIgnoreCase))
                {
                    info.LockRoot = ReadHrefChild(reader);
                }
            }

            return info;
        }

        // For elements like <D:locktype><D:write/></D:locktype> — return "write".
        private static string ReadFirstChildElementName(XmlReader reader)
        {
            if (reader.IsEmptyElement)
                return null;

            using (var sub = reader.ReadSubtree())
            {
                sub.MoveToContent();
                while (sub.Read())
                {
                    if (sub.NodeType == XmlNodeType.Element)
                        return sub.LocalName;
                }
            }
            return null;
        }

        // For elements like <D:locktoken><D:href>opaquelocktoken:...</D:href></D:locktoken>
        // — return the inner href text.
        private static string ReadHrefChild(XmlReader reader)
        {
            if (reader.IsEmptyElement)
                return null;

            using (var sub = reader.ReadSubtree())
            {
                sub.MoveToContent();
                while (sub.Read())
                {
                    if (sub.NodeType == XmlNodeType.Element
                        && string.Equals(sub.LocalName, "href", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(sub.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase))
                    {
                        return sub.ReadElementContentAsString().Trim();
                    }
                }
            }
            return null;
        }

        // Owner can be free-form XML (e.g. <D:href>...</D:href>) or plain text.
        // Capture the inner XML so callers can inspect whatever the server returned.
        private static string ReadOwnerInnerXml(XmlReader reader)
        {
            if (reader.IsEmptyElement)
                return null;

            using (var sub = reader.ReadSubtree())
            {
                sub.MoveToContent();
                return sub.ReadInnerXml().Trim();
            }
        }

        // Reads an element's text content. Uses a subtree reader so the parent reader is left
        // on the element's EndElement (so the caller's outer loop advances normally).
        private static string ReadElementText(XmlReader reader)
        {
            if (reader.IsEmptyElement)
                return null;

            using (var sub = reader.ReadSubtree())
            {
                sub.MoveToContent();
                return sub.ReadElementContentAsString();
            }
        }

        // RFC 4918 §10.7: TimeType = "Second-" 1*DIGIT | "Infinite"
        // We accept a comma-separated list (the server picks one) and take the first.
        internal static int? ParseTimeout(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            foreach (var part in raw.Split(','))
            {
                var token = part.Trim();
                if (token.Length == 0)
                    continue;
                if (string.Equals(token, "Infinite", StringComparison.OrdinalIgnoreCase))
                    return null;
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
