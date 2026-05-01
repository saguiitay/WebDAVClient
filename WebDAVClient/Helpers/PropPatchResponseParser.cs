using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace WebDAVClient.Helpers
{
    /// <summary>
    /// Parses a <c>207 Multi-Status</c> response body produced by a PROPPATCH request
    /// (RFC 4918 §9.2). Walks the <c>D:multistatus / D:response / D:propstat</c>
    /// structure and extracts each property's status code so the caller can confirm
    /// the (atomic) PROPPATCH actually succeeded.
    /// </summary>
    internal static class PropPatchResponseParser
    {
        private const string c_davNamespace = "DAV:";

        /// <summary>
        /// One per <c>D:propstat</c> element found in the response body.
        /// </summary>
        public sealed class PropStatResult
        {
            public int StatusCode { get; set; }
            public string StatusLine { get; set; }
            public string ResponseDescription { get; set; }
        }

        /// <summary>
        /// Returns every <c>D:propstat</c> result across every <c>D:response</c> in the
        /// multistatus body. An empty list signals a malformed / ambiguous response —
        /// callers should treat that as failure rather than as vacuous success.
        /// </summary>
        public static IList<PropStatResult> Parse(Stream stream)
        {
            var results = new List<PropStatResult>();
            using (var reader = XmlReader.Create(stream, ResponseParser.XmlReaderSettings))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element
                        && IsDav(reader, "propstat"))
                    {
                        var result = ReadPropStat(reader);
                        if (result != null)
                            results.Add(result);
                    }
                }
            }
            return results;
        }

        private static PropStatResult ReadPropStat(XmlReader reader)
        {
            if (reader.IsEmptyElement)
                return null;

            var result = new PropStatResult();
            int depth = reader.Depth;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement
                    && reader.Depth == depth
                    && IsDav(reader, "propstat"))
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                // Only consume DAV-namespaced status / responsedescription children of <propstat>.
                // A user-defined property called "status" inside <D:prop> would be a different
                // element (different namespace, different parent) and is correctly ignored.
                if (IsDav(reader, "status"))
                {
                    result.StatusLine = ReadElementText(reader);
                    result.StatusCode = ParseStatusCode(result.StatusLine);
                }
                else if (IsDav(reader, "responsedescription"))
                {
                    result.ResponseDescription = ReadElementText(reader);
                }
            }

            return result;
        }

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

        // RFC 7230: status-line = HTTP-version SP status-code SP reason-phrase
        // We only need the status-code token. Returns 0 if the line is malformed.
        // Span-based scan to avoid the allocation of string.Split.
        private static int ParseStatusCode(string statusLine)
        {
            if (string.IsNullOrEmpty(statusLine))
                return 0;

            var span = statusLine.AsSpan();

            // Skip the HTTP-version token (leading whitespace + non-whitespace run).
            var i = SkipWhitespace(span, 0);
            if (i >= span.Length)
                return 0;
            i = SkipNonWhitespace(span, i);

            // Skip the separating whitespace before the status-code token.
            i = SkipWhitespace(span, i);
            if (i >= span.Length)
                return 0;

            // Find the end of the status-code token.
            var end = SkipNonWhitespace(span, i);

            return int.TryParse(span.Slice(i, end - i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
                ? code
                : 0;
        }

        private static int SkipWhitespace(ReadOnlySpan<char> span, int start)
        {
            var i = start;
            while (i < span.Length && (span[i] == ' ' || span[i] == '\t'))
                i++;
            return i;
        }

        private static int SkipNonWhitespace(ReadOnlySpan<char> span, int start)
        {
            var i = start;
            while (i < span.Length && span[i] != ' ' && span[i] != '\t')
                i++;
            return i;
        }

        private static bool IsDav(XmlReader reader, string localName)
        {
            return string.Equals(reader.LocalName, localName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase);
        }
    }
}
