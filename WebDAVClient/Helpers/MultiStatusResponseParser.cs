using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient.Helpers
{
    internal static class MultiStatusResponseParser
    {
        public static async Task<IReadOnlyList<ResourceStatus>> ParseAsync(Stream stream, CancellationToken cancellationToken)
        {
            var results = new List<ResourceStatus>();
            await foreach (var resource in DavXml.ReadMultiStatusAsync(stream, new DavXmlLimits(), cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if (!resource.StatusCode.HasValue)
                {
                    throw new XmlException("A mutation DAV:response requires a resource status.");
                }
                foreach (var href in resource.Hrefs)
                {
                    results.Add(new ResourceStatus(href, resource.StatusCode.Value, resource.Description));
                }
            }
            if (results.Count == 0)
            {
                throw new XmlException("The mutation multi-status response contains no resource outcomes.");
            }
            return results.AsReadOnly();
        }
    }
}
