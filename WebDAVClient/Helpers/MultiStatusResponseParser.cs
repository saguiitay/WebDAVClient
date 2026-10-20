using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using WebDAVClient.Model;

namespace WebDAVClient.Helpers
{
    internal static class MultiStatusResponseParser
    {
        public static async Task<IReadOnlyList<ResourceStatus>> ParseAsync(Stream stream, CancellationToken cancellationToken)
        {
            var results = new List<ResourceStatus>();
            using var reader = XmlReader.Create(stream, ResponseParser.XmlReaderSettings);
            bool isMultistatus = false;
            bool inResponse = false;
            bool advance = true;
            string href = null;
            string description = null;
            int status = 0;

            while (!advance || await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                advance = true;
                if (reader.NamespaceURI != "DAV:")
                {
                    continue;
                }

                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.Depth == 0 && reader.LocalName == "multistatus")
                    {
                        isMultistatus = true;
                    }
                    else if (isMultistatus && reader.Depth == 1 && reader.LocalName == "response")
                    {
                        if (reader.IsEmptyElement)
                        {
                            throw new XmlException("A mutation DAV:response must contain a href and status.");
                        }

                        inResponse = true;
                        href = null;
                        description = null;
                        status = 0;
                    }
                    else if (inResponse && reader.Depth == 2)
                    {
                        if (reader.LocalName == "href")
                        {
                            href = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                        }
                        else if (reader.LocalName == "status")
                        {
                            status = ResponseParser.ParseStatusCode(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        }
                        else if (reader.LocalName == "responsedescription")
                        {
                            description = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            continue;
                        }

                        advance = false;
                    }
                }
                else if (inResponse && reader.NodeType == XmlNodeType.EndElement
                    && reader.Depth == 1 && reader.LocalName == "response")
                {
                    if (string.IsNullOrWhiteSpace(href) || status == 0)
                    {
                        throw new XmlException("A mutation DAV:response must contain a href and HTTP status.");
                    }

                    results.Add(new ResourceStatus(href, status, description));
                    inResponse = false;
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
