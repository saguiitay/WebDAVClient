using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient.Helpers
{
    internal static class LockResponseParser
    {
        public static LockInfo Parse(Stream stream) => ParseAsync(stream).GetAwaiter().GetResult();

        public static async Task<LockInfo> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            try
            {
                var root = await DavXml.ReadDocumentAsync(stream, new DavXmlLimits(), cancellationToken).ConfigureAwait(false);
                var active = root.Element(DavXml.Dav + "lockdiscovery")?.Elements(DavXml.Dav + "activelock").FirstOrDefault();
                if (active == null)
                {
                    return null;
                }
                var owner = active.Element(DavXml.Dav + "owner");
                string ownerXml = null;
                if (owner != null && !owner.IsEmpty)
                {
                    using var reader = owner.CreateReader();
                    reader.MoveToContent();
                    ownerXml = reader.ReadInnerXml().Trim();
                }
                return new LockInfo
                {
                    LockType = active.Element(DavXml.Dav + "locktype")?.Elements().FirstOrDefault()?.Name.LocalName,
                    LockScope = active.Element(DavXml.Dav + "lockscope")?.Elements().FirstOrDefault()?.Name.LocalName,
                    Depth = active.Element(DavXml.Dav + "depth")?.Value,
                    TimeoutSeconds = ParseTimeout(active.Element(DavXml.Dav + "timeout")?.Value),
                    Owner = ownerXml,
                    Token = active.Element(DavXml.Dav + "locktoken")?.Element(DavXml.Dav + "href")?.Value.Trim(),
                    LockRoot = active.Element(DavXml.Dav + "lockroot")?.Element(DavXml.Dav + "href")?.Value.Trim()
                };
            }
            catch (XmlException)
            {
                // Compatibility only: legacy LOCK callers decide whether missing information is fatal.
                // Modern LOCK surfaces malformed XML as an explicit protocol error.
                return null;
            }
        }

        internal static int? ParseTimeout(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }
            foreach (var part in raw.Split(','))
            {
                var token = part.Trim();
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
