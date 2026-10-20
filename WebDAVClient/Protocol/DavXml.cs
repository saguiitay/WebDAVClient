#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace WebDAVClient.Protocol
{
    internal static class DavXml
    {
        internal static readonly XNamespace Dav = "DAV:";

        internal static XmlReader CreateReader(Stream stream, DavXmlLimits limits)
        {
            limits.Validate();
            return XmlReader.Create(stream, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = limits.MaxResponseCharacters,
                IgnoreWhitespace = false,
                CloseInput = false
            });
        }

        internal static async IAsyncEnumerable<DavResourceResponse> ReadMultiStatusAsync(Stream stream, DavXmlLimits limits,
            bool legacy = false, [EnumeratorCancellation] CancellationToken cancellationToken = default,
            bool namesOnly = false, bool davPropertiesOnly = false, Action<DavXmlValue>? onExtension = null)
        {
            using var reader = CreateReader(stream, limits);
            await reader.MoveToContentAsync().ConfigureAwait(false);
            if (!Is(reader, "multistatus", legacy))
            {
                throw new XmlException("Expected a DAV:multistatus document.");
            }
            if (reader.IsEmptyElement)
            {
                await reader.ReadAsync().ConfigureAwait(false);
            }
            else
            {
                await reader.ReadAsync().ConfigureAwait(false);
                while (!(reader.NodeType == XmlNodeType.EndElement && reader.Depth == 0))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.EOF)
                    {
                        throw new XmlException("Incomplete DAV:multistatus document.");
                    }
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        var isResponse = reader.Depth == 1 && Is(reader, "response", legacy);
                        var element = await ReadElementAsync(reader, limits, cancellationToken, namesOnly, davPropertiesOnly).ConfigureAwait(false);
                        if (isResponse)
                        {
                            yield return ParseResponse(element, legacy);
                        }
                        else
                        {
                            onExtension?.Invoke(new DavXmlValue(element));
                        }
                    }
                    else
                    {
                        if (!legacy && (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA))
                        {
                            throw new XmlException("Unexpected text in DAV:multistatus.");
                        }
                        await reader.ReadAsync().ConfigureAwait(false);
                    }
                }
                await reader.ReadAsync().ConfigureAwait(false);
            }
            // Consume the entire document. A valid prefix must never certify a truncated listing.
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        internal static async Task<XElement> ReadDocumentAsync(Stream stream, DavXmlLimits limits, CancellationToken token)
        {
            using var reader = CreateReader(stream, limits);
            await reader.MoveToContentAsync().ConfigureAwait(false);
            var result = await ReadElementAsync(reader, limits, token).ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
            }
            return result;
        }

        private static async Task<XElement> ReadElementAsync(XmlReader reader, DavXmlLimits limits, CancellationToken token,
            bool namesOnly = false, bool davPropertiesOnly = false)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                throw new XmlException("Expected an XML element.");
            }
            var stack = new Stack<XElement>();
            XElement? root = null;
            int size = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (reader.Depth > limits.MaxDepth)
                {
                    throw new XmlException("DAV XML nesting limit exceeded.");
                }
                if (reader.NodeType == XmlNodeType.Element)
                {
                    var element = new XElement(XName.Get(reader.LocalName, reader.NamespaceURI));
                    if (stack.Count == 3 && stack.Peek().Name == Dav + "prop"
                        && stack.Peek().Parent!.Name == Dav + "propstat" && root!.Name == Dav + "response"
                        && (namesOnly || davPropertiesOnly && reader.NamespaceURI != "DAV:"))
                    {
                        if (namesOnly)
                        {
                            stack.Peek().Add(element);
                        }
                        await reader.SkipAsync().ConfigureAwait(false);
                        continue;
                    }
                    size = checked(size + reader.Name.Length);
                    if (reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            var name = reader.Prefix == "xmlns" ? XNamespace.Xmlns + reader.LocalName
                                : reader.Name == "xmlns" ? XName.Get("xmlns") : XName.Get(reader.LocalName, reader.NamespaceURI);
                            element.Add(new XAttribute(name, reader.Value));
                            size = checked(size + reader.Name.Length + reader.Value.Length);
                        } while (reader.MoveToNextAttribute());
                        reader.MoveToElement();
                    }
                    if (root == null)
                    {
                        root = element;
                        if (reader is IXmlNamespaceResolver resolver)
                        {
                            foreach (var ns in resolver.GetNamespacesInScope(XmlNamespaceScope.All))
                            {
                                if (ns.Key == "xml")
                                {
                                    continue;
                                }
                                var name = ns.Key.Length == 0 ? XName.Get("xmlns") : XNamespace.Xmlns + ns.Key;
                                if (element.Attribute(name) == null)
                                {
                                    element.Add(new XAttribute(name, ns.Value));
                                    size = checked(size + ns.Key.Length + ns.Value.Length);
                                }
                            }
                        }
                        if (reader.XmlLang.Length > 0 && element.Attribute(XNamespace.Xml + "lang") == null)
                        {
                            element.Add(new XAttribute(XNamespace.Xml + "lang", reader.XmlLang));
                            size = checked(size + "xml:lang".Length + reader.XmlLang.Length);
                        }
                        if (reader.XmlSpace != XmlSpace.None && element.Attribute(XNamespace.Xml + "space") == null)
                        {
                            var space = reader.XmlSpace == XmlSpace.Preserve ? "preserve" : "default";
                            element.Add(new XAttribute(XNamespace.Xml + "space", space));
                            size = checked(size + "xml:space".Length + space.Length);
                        }
                    }
                    else
                    {
                        stack.Peek().Add(element);
                    }
                    if (!reader.IsEmptyElement)
                    {
                        stack.Push(element);
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    stack.Pop();
                }
                else if (stack.Count > 0)
                {
                    var value = await reader.GetValueAsync().ConfigureAwait(false);
                    size = checked(size + value.Length);
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Text:
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            stack.Peek().Add(new XText(value));
                            break;
                        case XmlNodeType.CDATA:
                            stack.Peek().Add(new XCData(value));
                            break;
                        case XmlNodeType.Comment:
                            stack.Peek().Add(new XComment(value));
                            break;
                        case XmlNodeType.ProcessingInstruction:
                            stack.Peek().Add(new XProcessingInstruction(reader.Name, value));
                            break;
                    }
                }
                if (size > limits.MaxResourceCharacters)
                {
                    throw new XmlException("DAV resource XML size limit exceeded.");
                }
                var complete = stack.Count == 0;
                if (!await reader.ReadAsync().ConfigureAwait(false) && !complete)
                {
                    throw new XmlException("Incomplete DAV XML element.");
                }
                if (complete)
                {
                    return root!;
                }
            }
        }

        private static bool Is(XmlReader reader, string name, bool legacy) =>
            reader.NamespaceURI == "DAV:" && string.Equals(reader.LocalName, name, legacy ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        internal static DavResourceResponse ParseResponse(XElement element, bool legacy)
        {
            var hrefs = new List<string>();
            var propstats = new List<DavPropertyStatus>();
            var errors = new List<DavXmlValue>();
            var extensions = new List<DavXmlValue>();
            int? status = null;
            string? description = null;
            string? location = null;
            foreach (var child in element.Elements())
            {
                if (child.Name.Namespace != Dav)
                {
                    extensions.Add(new DavXmlValue(child));
                    continue;
                }
                var name = legacy ? child.Name.LocalName.ToLowerInvariant() : child.Name.LocalName;
                switch (name)
                {
                    case "href":
                        var href = Text(child);
                        if (string.IsNullOrWhiteSpace(href))
                        {
                            throw new XmlException("A DAV:response requires a direct, nonempty DAV:href.");
                        }
                        hrefs.Add(href);
                        break;
                    case "status":
                        if (status.HasValue)
                        {
                            throw new XmlException("Duplicate DAV response status.");
                        }
                        status = ParseStatus(Text(child));
                        break;
                    case "propstat":
                        propstats.Add(ParsePropertyStatus(child, legacy));
                        break;
                    case "responsedescription":
                        description ??= Text(child);
                        break;
                    case "location":
                        if (location == null)
                        {
                            foreach (var candidate in child.Elements())
                            {
                                if (candidate.Name.Namespace == Dav && string.Equals(candidate.Name.LocalName, "href",
                                    legacy ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                                {
                                    location = Text(candidate);
                                    break;
                                }
                            }
                        }
                        break;
                    case "error":
                        foreach (var error in child.Elements())
                        {
                            errors.Add(new DavXmlValue(error));
                        }
                        break;
                }
            }
            if (hrefs.Count == 0)
            {
                throw new XmlException("A DAV:response requires a direct, nonempty DAV:href.");
            }
            if (!legacy && ((status.HasValue && propstats.Count != 0) || (!status.HasValue && propstats.Count == 0)
                || (propstats.Count != 0 && hrefs.Count != 1)))
            {
                throw new XmlException("A DAV:response requires either a resource status or property statuses.");
            }
            return new DavResourceResponse(hrefs, status, propstats, description, location, errors, extensions);
        }

        private static DavPropertyStatus ParsePropertyStatus(XElement element, bool legacy)
        {
            int? status = null;
            int propCount = 0;
            string? description = null;
            var properties = new List<DavXmlValue>();
            var errors = new List<DavXmlValue>();
            foreach (var child in element.Elements())
            {
                if (child.Name.Namespace != Dav)
                {
                    continue;
                }
                var name = legacy ? child.Name.LocalName.ToLowerInvariant() : child.Name.LocalName;
                switch (name)
                {
                    case "status":
                        if (status.HasValue)
                        {
                            throw new XmlException("A DAV:propstat requires one prop and one status.");
                        }
                        status = ParseStatus(Text(child));
                        break;
                    case "prop":
                        propCount++;
                        foreach (var property in child.Elements())
                        {
                            properties.Add(new DavXmlValue(property));
                        }
                        break;
                    case "responsedescription":
                        description ??= Text(child);
                        break;
                    case "error":
                        foreach (var error in child.Elements())
                        {
                            errors.Add(new DavXmlValue(error));
                        }
                        break;
                }
            }
            if (!legacy && (!status.HasValue || propCount != 1))
            {
                throw new XmlException("A DAV:propstat requires one prop and one status.");
            }
            return new DavPropertyStatus(status, properties, description, errors);
        }

        internal static string Text(XElement element)
        {
            if (element.HasElements)
            {
                throw new XmlException("Expected text in " + element.Name + ".");
            }
            return element.Value;
        }

        internal static int ParseStatus(string value)
        {
            var parts = value.Split(new[]
                {
                    ' ', '\t'
                }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0].Length != 8 || !parts[0].StartsWith("HTTP/", StringComparison.Ordinal)
                || parts[0][5] < '0' || parts[0][5] > '9' || parts[0][6] != '.' || parts[0][7] < '0' || parts[0][7] > '9'
                || parts[1].Length != 3 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var status)
                || status < 100 || status > 599)
            {
                throw new XmlException("Invalid DAV HTTP status line.");
            }
            return status;
        }

        internal static IReadOnlyList<DavLock> ParseLocks(XElement root, DateTimeOffset observed)
        {
            var discovery = root.Name == Dav + "lockdiscovery" ? root : root.Element(Dav + "lockdiscovery");
            if (discovery == null)
            {
                throw new XmlException("LOCK requires DAV:lockdiscovery.");
            }
            return Array.AsReadOnly(discovery.Elements(Dav + "activelock").Select(e =>
            {
                var scopes = e.Element(Dav + "lockscope")?.Elements().ToArray();
                var types = e.Element(Dav + "locktype")?.Elements().ToArray();
                var scope = scopes?.Length == 1 ? scopes[0] : null;
                var type = types?.Length == 1 ? types[0] : null;
                var depth = e.Element(Dav + "depth");
                if (scope == null || type == null || depth == null || (depth.Value != "0" && depth.Value != "infinity"))
                {
                    throw new XmlException("Invalid active lock scope, type or depth.");
                }
                var timeout = e.Element(Dav + "timeout")?.Value;
                var granted = DavLockTimeout.Unspecified;
                if (timeout == "Infinite")
                {
                    granted = DavLockTimeout.Infinite;
                }
                else if (timeout != null)
                {
                    if (!timeout.StartsWith("Second-", StringComparison.Ordinal) || !uint.TryParse(timeout.AsSpan(7), NumberStyles.None,
                        CultureInfo.InvariantCulture, out var seconds) || seconds == 0)
                    {
                        throw new XmlException("Invalid granted lock timeout.");
                    }
                    granted = DavLockTimeout.Finite(TimeSpan.FromSeconds(seconds));
                }
                return new DavLock(e.Element(Dav + "locktoken")?.Element(Dav + "href")?.Value,
                    new Model.PropertyName(scope.Name.NamespaceName, scope.Name.LocalName),
                    new Model.PropertyName(type.Name.NamespaceName, type.Name.LocalName), depth.Value,
                    e.Element(Dav + "owner") is XElement owner ? new DavXmlValue(owner) : null,
                    e.Element(Dav + "lockroot")?.Element(Dav + "href")?.Value, granted, observed);
            }).ToArray());
        }
    }
}
