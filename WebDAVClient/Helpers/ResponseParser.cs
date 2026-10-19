using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using WebDAVClient.Model;

namespace WebDAVClient.Helpers
{
    /// <summary>
    /// Selects which RFC 4918 §9.1 PROPFIND request flavour the response was
    /// produced by — controls whether <see cref="ResponseParser"/> populates the
    /// <see cref="Item.FoundProperties"/> /
    /// <see cref="Item.NotFoundProperties"/> /
    /// <see cref="Item.AvailablePropertyNames"/> collections in addition to the
    /// well-known typed fields on <see cref="Item"/>.
    /// </summary>
    internal enum PropFindMode
    {
        /// <summary>
        /// <c>&lt;allprop/&gt;</c> — historical default. Only the typed fields
        /// on <see cref="Item"/> are populated; the property-bag collections
        /// stay <c>null</c> so existing callers see no behavioural change.
        /// </summary>
        AllProp = 0,

        /// <summary>
        /// <c>&lt;prop&gt;</c> — caller asked for specific properties. Typed
        /// fields are still populated when the response contains the matching
        /// DAV: properties, and every property element under <c>D:propstat</c>
        /// is also captured into <see cref="Item.FoundProperties"/> (status 200)
        /// or <see cref="Item.NotFoundProperties"/> (status 404+).
        /// </summary>
        NamedProperties = 1,

        /// <summary>
        /// <c>&lt;propname/&gt;</c> — server returns property names only with no
        /// values. Each empty element under <c>D:prop</c> is captured into
        /// <see cref="Item.AvailablePropertyNames"/>.
        /// </summary>
        PropName = 2,
    }

    /// <summary>
    /// Represents the parser for response's results.
    /// </summary>
    internal static class ResponseParser
    {
        private const string c_davNamespace = "DAV:";

        /// <summary>
        /// Parses the disk item.
        /// </summary>
        /// <param name="stream">The response text.</param>
        /// <returns>The  parsed item.</returns>
        public static Item ParseItem(Stream stream)
        {
            return ParseItem(stream, PropFindMode.AllProp);
        }

        /// <summary>
        /// Parses the disk item, optionally populating the property-bag collections on
        /// <see cref="Item"/> for targeted PROPFIND request flavours.
        /// </summary>
        public static Item ParseItem(Stream stream, PropFindMode mode)
        {
            return ParseItemAsync(stream, mode).GetAwaiter().GetResult();
        }

        internal static XmlReaderSettings XmlReaderSettings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true
        };

        /// <summary>
        /// Parses the disk items.
        /// </summary>
        /// <param name="stream">The response text.</param>
        /// <returns>The list of parsed items.</returns>
        public static List<Item> ParseItems(Stream stream)
        {
            return ParseItems(stream, PropFindMode.AllProp);
        }

        /// <summary>
        /// Parses the disk items, optionally populating the property-bag collections on
        /// <see cref="Item"/> for targeted PROPFIND request flavours.
        /// </summary>
        public static List<Item> ParseItems(Stream stream, PropFindMode mode)
        {
            return ParseItemsAsync(stream, mode).GetAwaiter().GetResult();
        }

        public static async Task<Item> ParseItemAsync(Stream stream, PropFindMode mode, CancellationToken cancellationToken = default)
        {
            return (await ParseItemsCoreAsync(stream, mode, firstItemOnly: true, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        }

        public static Task<List<Item>> ParseItemsAsync(Stream stream, PropFindMode mode, CancellationToken cancellationToken = default)
        {
            return ParseItemsCoreAsync(stream, mode, firstItemOnly: false, cancellationToken);
        }

        private static async Task<List<Item>> ParseItemsCoreAsync(Stream stream, PropFindMode mode, bool firstItemOnly, CancellationToken cancellationToken)
        {
            var items = new List<Item>();
            using (var reader = XmlReader.Create(stream, XmlReaderSettings))
            {
                Item itemInfo = null;
                int multistatusDepth = -1;
                int responseDepth = -1;
                int propStatDepth = -1;
                // Status of the <D:propstat> currently being parsed; 0 when not inside one
                // or when no <D:status> has been seen yet.
                int currentPropStatStatus = 0;
                int resourceStatus = 0;
                // Depth of the enclosing <D:prop> element, or -1 when the cursor is not
                // inside one. Used to identify property elements at exactly propDepth + 1.
                int propDepth = -1;
                // Buffer of properties captured under the current <D:propstat>. Real-world
                // WebDAV responses place <D:prop> BEFORE <D:status>, so the status isn't
                // known until the propstat closes — buffer here, then dispatch on </propstat>.
                List<(PropertyName Name, string Value)> propStatBuffer = null;
                // BufferProperty consumes the current element via ReadInnerXml, which leaves
                // the reader positioned on the *next* node. Skip the next Read() so we don't
                // step over a sibling property element.
                bool advance = true;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (advance)
                    {
                        if (!await reader.ReadAsync().ConfigureAwait(false)) break;
                    }
                    advance = true;

                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        var localName = reader.LocalName;

                        bool isDav = string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.Ordinal);
                        if (reader.Depth == 0 && isDav
                            && string.Equals(localName, "multistatus", StringComparison.OrdinalIgnoreCase))
                        {
                            multistatusDepth = reader.Depth;
                            continue;
                        }
                        if (multistatusDepth >= 0 && reader.Depth == multistatusDepth + 1 && isDav
                            && string.Equals(localName, "response", StringComparison.OrdinalIgnoreCase))
                        {
                            itemInfo = new Item();
                            responseDepth = reader.Depth;
                            currentPropStatStatus = 0;
                            resourceStatus = 0;
                            propStatDepth = -1;
                            propDepth = -1;
                            propStatBuffer = null;
                            continue;
                        }
                        if (itemInfo == null)
                            continue;

                        if (reader.Depth == responseDepth + 1 && isDav
                            && string.Equals(localName, "propstat", StringComparison.OrdinalIgnoreCase))
                        {
                            propStatDepth = reader.IsEmptyElement ? -1 : reader.Depth;
                            currentPropStatStatus = 0;
                            propStatBuffer = reader.IsEmptyElement ? null : new List<(PropertyName, string)>();
                            continue;
                        }
                        if (propStatDepth >= 0 && reader.Depth == propStatDepth + 1 && isDav
                            && string.Equals(localName, "prop", StringComparison.OrdinalIgnoreCase))
                        {
                            propDepth = reader.IsEmptyElement ? -1 : reader.Depth;
                            continue;
                        }
                        if (isDav && string.Equals(localName, "status", StringComparison.OrdinalIgnoreCase)
                            && (reader.Depth == responseDepth + 1
                                || (propStatDepth >= 0 && reader.Depth == propStatDepth + 1)))
                        {
                            var isResourceStatus = reader.Depth == responseDepth + 1;
                            var statusCode = ParseStatusCode(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                            if (isResourceStatus)
                                resourceStatus = statusCode;
                            else
                                currentPropStatStatus = statusCode;
                            advance = false;
                            continue;
                        }
                        if (propDepth >= 0 && reader.Depth == propDepth + 1)
                        {
                            advance = !await BufferPropertyAsync(reader, propStatBuffer, mode).ConfigureAwait(false);
                            continue;
                        }

                        if (reader.Depth == responseDepth + 1 && isDav
                            && string.Equals(localName, "href", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                await reader.ReadAsync().ConfigureAwait(false);
                                var value = await reader.GetValueAsync().ConfigureAwait(false);
                                value = value.Replace("#", "%23");
                                itemInfo.Href = value;
                            }
                            continue;
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (itemInfo == null)
                            continue;

                        if (propDepth >= 0
                            && reader.Depth == propDepth
                            && string.Equals(reader.LocalName, "prop", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.Ordinal))
                        {
                            propDepth = -1;
                        }
                        else if (propStatDepth >= 0 && reader.Depth == propStatDepth
                            && string.Equals(reader.LocalName, "propstat", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.Ordinal))
                        {
                            // Status is now known; dispatch the buffered properties.
                            DispatchPropStat(itemInfo, propStatBuffer, currentPropStatStatus, mode);
                            propStatBuffer = null;
                            propStatDepth = -1;
                            propDepth = -1;
                        }
                        else if (reader.Depth == responseDepth
                            && string.Equals(reader.LocalName, "response", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.Ordinal))
                        {
                            if (string.IsNullOrEmpty(itemInfo.Href))
                                throw new XmlException("A DAV:response must contain a direct DAV:href resource identifier.");
                            if (resourceStatus != 0 && (resourceStatus < 200 || resourceStatus >= 300))
                            {
                                if (resourceStatus == (int)HttpStatusCode.Conflict)
                                    throw new WebDAVConflictException(resourceStatus, "PROPFIND reported a failed resource.");
                                throw new WebDAVException(resourceStatus, "PROPFIND reported a failed resource.");
                            }
                            if (mode != PropFindMode.AllProp)
                            {
                                var hasTypeValue = itemInfo.FoundProperties != null
                                    && (itemInfo.FoundProperties.ContainsKey(new PropertyName(c_davNamespace, "resourcetype"))
                                        || itemInfo.FoundProperties.ContainsKey(new PropertyName(c_davNamespace, "iscollection")));
                                if (!hasTypeValue && itemInfo.Href.EndsWith("/", StringComparison.Ordinal))
                                    itemInfo.IsCollection = true;
                            }
                            // Remove trailing / if the item is not a collection
                            var href = itemInfo.Href.TrimEnd('/');
                            if (!itemInfo.IsCollection)
                            {
                                itemInfo.Href = href;
                            }
                            if (string.IsNullOrEmpty(itemInfo.DisplayName))
                            {
                                var name = href.Substring(href.LastIndexOf('/') + 1);
                                itemInfo.DisplayName = WebUtility.UrlDecode(name);
                            }
                            // Validate the complete body even for a single result so
                            // later resource failures cannot masquerade as success.
                            if (!firstItemOnly || items.Count == 0)
                                items.Add(itemInfo);
                            itemInfo = null;
                            responseDepth = -1;
                        }
                    }
                }
            }

            return items;
        }

        // Reads a single property element (currently positioned on its start tag) and
        // appends it to <paramref name="buffer"/> for later dispatch on </propstat>.
        // After this call returns, the reader's cursor is positioned past the property
        // element's end tag, so the caller must <c>continue</c> the outer Read loop
        // Returns true when the reader was advanced past the element's end tag (the caller
        // must NOT call Read() again for this iteration); false when the reader is still on
        // the original empty element.
        private static async Task<bool> BufferPropertyAsync(XmlReader reader, List<(PropertyName Name, string Value)> buffer, PropFindMode mode)
        {
            var localName = reader.LocalName;
            var ns = reader.NamespaceURI ?? string.Empty;
            if (mode == PropFindMode.AllProp && !string.Equals(ns, c_davNamespace, StringComparison.Ordinal))
            {
                await reader.SkipAsync().ConfigureAwait(false);
                return true;
            }
            var isEmpty = reader.IsEmptyElement;

            string text;
            bool advanced;
            if (isEmpty)
            {
                text = string.Empty;
                advanced = false;
            }
            else if (mode == PropFindMode.PropName)
            {
                // Be liberal: some servers emit non-empty elements anyway in propname mode; consume.
                await reader.SkipAsync().ConfigureAwait(false);
                text = string.Empty;
                advanced = true;
            }
            else
            {
                text = await reader.ReadInnerXmlAsync().ConfigureAwait(false);
                advanced = true;
            }

            buffer?.Add((new PropertyName(ns, localName), text));
            return advanced;
        }

        // Once the <D:status> for a <D:propstat> is known, route the buffered properties
        // into the appropriate bag on the Item.
        private static void DispatchPropStat(Item itemInfo, List<(PropertyName Name, string Value)> buffer, int statusCode, PropFindMode mode)
        {
            if (itemInfo == null || buffer == null || buffer.Count == 0)
                return;

            if (mode == PropFindMode.PropName)
            {
                if (statusCode == 0 || (statusCode >= 200 && statusCode < 300))
                    foreach (var entry in buffer)
                        AppendAvailableName(itemInfo, entry.Name);
                return;
            }

            var success = statusCode >= 200 && statusCode < 300;
            if (mode == PropFindMode.AllProp)
            {
                // Preserve legacy support for omitted propstat status, but never
                // apply values explicitly marked as failed.
                if (success || statusCode == 0)
                    foreach (var entry in buffer)
                        ApplyTypedFieldFromBag(itemInfo, entry.Name.Namespace, entry.Name.LocalName, ExtractElementText(entry.Value));
                return;
            }
            if (success)
            {
                foreach (var entry in buffer)
                {
                    var value = ExtractElementText(entry.Value);
                    AppendFoundProperty(itemInfo, entry.Name, value);
                    ApplyTypedFieldFromBag(itemInfo, entry.Name.Namespace, entry.Name.LocalName, value);
                }
            }
            else
            {
                // RFC 4918 §9.1: 404 Not Found is the canonical "absent" status. Other
                // statuses (401/403/424) also indicate the property isn't usable on this
                // resource; bucket them as not-found so callers don't see ghost values.
                foreach (var entry in buffer)
                {
                    AppendNotFoundProperty(itemInfo, entry.Name);
                }
            }
        }

        private static string ExtractElementText(string innerXml)
        {
            if (string.IsNullOrEmpty(innerXml))
                return string.Empty;
            if (innerXml.IndexOf('<') < 0 && innerXml.IndexOf('&') < 0)
                return innerXml;

            using (var reader = CreatePropertyReader(innerXml))
            {
                var text = new StringBuilder();
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                        return innerXml;
                    if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA
                        || reader.NodeType == XmlNodeType.Whitespace || reader.NodeType == XmlNodeType.SignificantWhitespace)
                        text.Append(reader.Value);
                }
                return text.ToString();
            }
        }

        private static XmlReader CreatePropertyReader(string innerXml)
        {
            var settings = XmlReaderSettings.Clone();
            settings.ConformanceLevel = ConformanceLevel.Fragment;
            settings.IgnoreWhitespace = false;
            settings.CloseInput = true;
            return XmlReader.Create(new StringReader(innerXml), settings);
        }

        private static void AppendFoundProperty(Item itemInfo, PropertyName name, string value)
        {
            if (itemInfo.FoundProperties == null)
                itemInfo.FoundProperties = new Dictionary<PropertyName, string>();
            itemInfo.FoundProperties[name] = value;
        }

        private static void AppendNotFoundProperty(Item itemInfo, PropertyName name)
        {
            if (itemInfo.NotFoundProperties == null)
                itemInfo.NotFoundProperties = new List<PropertyName>();
            itemInfo.NotFoundProperties.Add(name);
        }

        private static void AppendAvailableName(Item itemInfo, PropertyName name)
        {
            if (itemInfo.AvailablePropertyNames == null)
                itemInfo.AvailablePropertyNames = new List<PropertyName>();
            itemInfo.AvailablePropertyNames.Add(name);
        }

        // All response modes share the same namespace-aware typed-property mapping.
        private static void ApplyTypedFieldFromBag(Item itemInfo, string ns, string localName, string value)
        {
            if (!string.Equals(ns, c_davNamespace, StringComparison.Ordinal))
                return;

            if (string.Equals(localName, "creationdate", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.TryParse(value, out var d))
                    itemInfo.CreationDate = d;
            }
            else if (string.Equals(localName, "getlastmodified", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.TryParse(value, out var d))
                    itemInfo.LastModified = d;
            }
            else if (string.Equals(localName, "displayname", StringComparison.OrdinalIgnoreCase))
            {
                itemInfo.DisplayName = value;
            }
            else if (string.Equals(localName, "getcontentlength", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    itemInfo.ContentLength = n;
            }
            else if (string.Equals(localName, "getcontenttype", StringComparison.OrdinalIgnoreCase))
            {
                itemInfo.ContentType = value;
            }
            else if (string.Equals(localName, "getetag", StringComparison.OrdinalIgnoreCase))
            {
                itemInfo.Etag = value;
            }
            else if (string.Equals(localName, "iscollection", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(value, out var b))
                    itemInfo.IsCollection = b;
                else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    itemInfo.IsCollection = i == 1;
            }
            else if (string.Equals(localName, "resourcetype", StringComparison.OrdinalIgnoreCase))
            {
                using (var reader = CreatePropertyReader(value))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.Depth == 0
                            && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.Ordinal)
                            && string.Equals(reader.LocalName, "collection", StringComparison.OrdinalIgnoreCase))
                            itemInfo.IsCollection = true;
                    }
                }
            }
            else if (string.Equals(localName, "hidden", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(localName, "ishidden", StringComparison.OrdinalIgnoreCase))
            {
                itemInfo.IsHidden = ParseBooleanValue(value);
            }
        }

        private static bool ParseBooleanValue(string value)
        {
            if (bool.TryParse(value, out var result))
                return result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number == 1;
        }

        // RFC 7230 status-line parser. Zero is reserved for an omitted status.
        // Span-based to avoid a string.Split allocation per propstat block.
        internal static int ParseStatusCode(string statusLine)
        {
            if (string.IsNullOrEmpty(statusLine))
                throw new XmlException("A DAV:status must contain an HTTP status line.");
            var span = statusLine.AsSpan();
            int i = 0;
            while (i < span.Length && (span[i] == ' ' || span[i] == '\t')) i++;
            while (i < span.Length && span[i] != ' ' && span[i] != '\t') i++;
            while (i < span.Length && (span[i] == ' ' || span[i] == '\t')) i++;
            int start = i;
            while (i < span.Length && span[i] != ' ' && span[i] != '\t') i++;
            if (int.TryParse(span.Slice(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
                && code >= 100 && code <= 599)
                return code;
            throw new XmlException("A DAV:status contains an invalid HTTP status code.");
        }
    }
}
