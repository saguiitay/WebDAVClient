using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using WebDAVClient.Protocol;

namespace WebDAVClient.Helpers
{
    internal static class PropPatchResponseParser
    {
        public sealed class PropStatResult
        {
            public int StatusCode
            {
                get;
                set;
            }

            public string StatusLine
            {
                get;
                set;
            }

            public string ResponseDescription
            {
                get;
                set;
            }
        }

        public static IList<PropStatResult> Parse(Stream stream) => ParseAsync(stream).GetAwaiter().GetResult();

        public static async Task<IList<PropStatResult>> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            var root = await DavXml.ReadDocumentAsync(stream, new DavXmlLimits(), cancellationToken).ConfigureAwait(false);
            var results = new List<PropStatResult>();
            if (root.Name != DavXml.Dav + "multistatus")
            {
                return results;
            }
            foreach (var group in root.Elements(DavXml.Dav + "response").SelectMany(r => r.Elements(DavXml.Dav + "propstat")))
            {
                if (group.IsEmpty)
                {
                    continue;
                }
                var status = group.Element(DavXml.Dav + "status") is XElement element ? DavXml.Text(element) : null;
                // Legacy callers use zero to represent an omitted/unparseable property status.
                var parts = status?.Split(new[]
                    {
                        ' ', '\t'
                    }, StringSplitOptions.RemoveEmptyEntries);
                var code = parts != null && parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
                results.Add(new PropStatResult
                {
                    StatusCode = code,
                    StatusLine = status,
                    ResponseDescription = group.Element(DavXml.Dav + "responsedescription") is XElement description ? DavXml.Text(description) : null
                });
            }
            return results;
        }
    }
}
