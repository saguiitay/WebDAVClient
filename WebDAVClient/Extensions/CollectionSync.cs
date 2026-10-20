#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient.Extensions
{
    /// <summary>
    /// One RFC 6578 sync-collection response. This does not implement durable synchronization or checkpoint storage.
    /// </summary>
    public sealed class DavSyncResult
    {
        internal DavSyncResult(DavReportResult response, string? token)
        {
            Response = response;
            SyncToken = token;
        }

        /// <summary>
        /// Full outcomes. A resource-level 404 represents a deletion in this profile.
        /// </summary>
        public DavReportResult Response
        {
            get;
        }

        /// <summary>
        /// Next opaque token; commit only after all applicable changes have been durably applied.
        /// </summary>
        public string? SyncToken
        {
            get;
        }

        /// <summary>
        /// True only for a 507 on the requested collection, indicating a truncated result set.
        /// </summary>
        public bool HasMore => Response.Resources.Any(r => r.StatusCode == 507 &&
            r.Hrefs.Any(h => Uri.TryCreate(Response.EffectiveUri, h, out var uri) && uri == Response.EffectiveUri));
    }

    /// <summary>
    /// Explicit collection synchronization REPORT profile using the public client execution seam.
    /// </summary>
    public static class CollectionSync
    {
        /// <summary>
        /// Requests one sync-level 1 change batch. Null token requests initial synchronization.
        /// Does not advance stored tokens, infer stable resource identity, retry, or recover an invalid token automatically.
        /// </summary>
        public static async Task<DavSyncResult> SyncCollectionAsync(this DavClient client, DavPath collection, string? syncToken,
            IReadOnlyList<PropertyName> properties, int? limit = null, ReportOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(properties);
            var names = properties.ToArray();
            if (names.Any(p => p == null) || limit <= 0 || options != null && options.Depth != DavDepth.Zero)
            {
                throw new ArgumentException("Use non-null property names and a positive limit.");
            }
            XNamespace dav = "DAV:";
            var report = new XElement(dav + "sync-collection",
                new XElement(dav + "sync-token", syncToken ?? ""),
                new XElement(dav + "sync-level", "1"),
                new XElement(dav + "prop", names.Select(p => new XElement(XName.Get(p.LocalName, p.Namespace)))));
            if (limit.HasValue)
            {
                report.Add(new XElement(dav + "limit", new XElement(dav + "nresults", limit.Value.ToString(CultureInfo.InvariantCulture))));
            }
            var response = await client.ReportAsync(collection, new DavXmlValue(report), options, cancellationToken).ConfigureAwait(false);
            string? token = null;
            if (response.StatusCode == 207)
            {
                var tokens = response.Document!.ToElement().Elements(dav + "sync-token").ToArray();
                if (tokens.Length != 1 || tokens[0].HasElements || string.IsNullOrWhiteSpace(tokens[0].Value))
                {
                    throw response.CreateException("A sync-collection response requires one nonempty sync token.");
                }
                token = tokens[0].Value;
                if (!Uri.TryCreate(token, UriKind.Absolute, out _) || token.Any(char.IsWhiteSpace))
                {
                    throw response.CreateException("A sync token must be an opaque URI.");
                }
                if (string.IsNullOrEmpty(syncToken) && response.Resources.Any(r => r.StatusCode == 404))
                {
                    throw response.CreateException("Initial synchronization must not report removed resources.");
                }
            }
            else if (response.StatusCode >= 200 && response.StatusCode < 300)
            {
                throw response.CreateException("sync-collection requires a 207 response.");
            }
            return new DavSyncResult(response, token);
        }
    }
}
