using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
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
            return ParseItems(stream).FirstOrDefault();
        }

        /// <summary>
        /// Parses the disk item, optionally populating the property-bag collections on
        /// <see cref="Item"/> for targeted PROPFIND request flavours.
        /// </summary>
        public static Item ParseItem(Stream stream, PropFindMode mode)
        {
            return ParseItems(stream, mode).FirstOrDefault();
        }

        internal static XmlReaderSettings XmlReaderSettings = new XmlReaderSettings
        {
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
            return ParseItemsCore(stream, PropFindMode.AllProp);
        }

        /// <summary>
        /// Parses the disk items, optionally populating the property-bag collections on
        /// <see cref="Item"/> for targeted PROPFIND request flavours.
        /// </summary>
        public static List<Item> ParseItems(Stream stream, PropFindMode mode)
        {
            return ParseItemsCore(stream, mode);
        }

        private static List<Item> ParseItemsCore(Stream stream, PropFindMode mode)
        {
            var items = new List<Item>();
            using (var reader = XmlReader.Create(stream, XmlReaderSettings))
            {
                Item itemInfo = null;
                // Status of the <D:propstat> currently being parsed; 0 when not inside one
                // or when no <D:status> has been seen yet.
                int currentPropStatStatus = 0;
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
                    if (advance)
                    {
                        if (!reader.Read()) break;
                    }
                    advance = true;

                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        var localName = reader.LocalName;

                        // ----- Structural propstat tracking (only used in new modes) -----
                        if (mode != PropFindMode.AllProp)
                        {
                            if (string.Equals(localName, "propstat", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase))
                            {
                                currentPropStatStatus = 0;
                                propStatBuffer = new List<(PropertyName, string)>();
                                continue;
                            }
                            if (string.Equals(localName, "status", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase))
                            {
                                if (!reader.IsEmptyElement)
                                {
                                    reader.Read();
                                    currentPropStatStatus = ParseStatusCode(reader.Value);
                                }
                                continue;
                            }
                            if (string.Equals(localName, "prop", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase))
                            {
                                propDepth = reader.IsEmptyElement ? -1 : reader.Depth;
                                continue;
                            }

                            // Property element: any child of <D:prop>. Buffer for later
                            // dispatch on </propstat> once the status is known.
                            if (propDepth >= 0 && reader.Depth == propDepth + 1)
                            {
                                bool consumedEndTag = BufferProperty(reader, propStatBuffer, mode);
                                // ReadInnerXml-based capture left the reader positioned on the
                                // next node. Don't advance again or we'd skip a sibling.
                                advance = !consumedEndTag;
                                continue;
                            }
                        }

                        if (string.Equals(localName, "response", StringComparison.OrdinalIgnoreCase))
                        {
                            itemInfo = new Item();
                            currentPropStatStatus = 0;
                            propDepth = -1;
                            propStatBuffer = null;
                        }
                        else if (string.Equals(localName, "href", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                var value = reader.Value;
                                value = value.Replace("#", "%23");
                                itemInfo.Href = value;
                            }
                        }
                        else if (string.Equals(localName, "creationdate", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                if (DateTime.TryParse(reader.Value, out var creationDate))
                                {
                                    itemInfo.CreationDate = creationDate;
                                }
                            }
                        }
                        else if (string.Equals(localName, "getlastmodified", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                if (DateTime.TryParse(reader.Value, out var lastModified))
                                {
                                    itemInfo.LastModified = lastModified;
                                }
                            }
                        }
                        else if (string.Equals(localName, "displayname", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                itemInfo.DisplayName = reader.Value;
                            }
                        }
                        else if (string.Equals(localName, "getcontentlength", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                if (long.TryParse(reader.Value, out long contentLength))
                                {
                                    itemInfo.ContentLength = contentLength;
                                }
                            }
                        }
                        else if (string.Equals(localName, "getcontenttype", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                itemInfo.ContentType = reader.Value;
                            }
                        }
                        else if (string.Equals(localName, "getetag", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                itemInfo.Etag = reader.Value;
                            }
                        }
                        else if (string.Equals(localName, "iscollection", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                if (bool.TryParse(reader.Value, out bool isCollection))
                                {
                                    itemInfo.IsCollection = isCollection;
                                }
                                if (int.TryParse(reader.Value, out int isCollectionInt))
                                {
                                    itemInfo.IsCollection = isCollectionInt == 1;
                                }
                            }
                        }
                        else if (string.Equals(localName, "resourcetype", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                if (string.Equals(reader.LocalName, "collection", StringComparison.OrdinalIgnoreCase))
                                {
                                    itemInfo.IsCollection = true;
                                }
                            }
                        }
                        else if (string.Equals(localName, "hidden", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(localName, "ishidden", StringComparison.OrdinalIgnoreCase))
                        {
                            itemInfo.IsHidden = true;
                        }
                        else if (string.Equals(localName, "checked-in", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(localName, "version-controlled-configuration", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Skip();
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (mode != PropFindMode.AllProp
                            && propDepth >= 0
                            && reader.Depth == propDepth
                            && string.Equals(reader.LocalName, "prop", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase))
                        {
                            propDepth = -1;
                        }
                        else if (mode != PropFindMode.AllProp
                            && propStatBuffer != null
                            && string.Equals(reader.LocalName, "propstat", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(reader.NamespaceURI, c_davNamespace, StringComparison.OrdinalIgnoreCase))
                        {
                            // Status is now known; dispatch the buffered properties.
                            DispatchPropStat(itemInfo, propStatBuffer, currentPropStatStatus, mode);
                            propStatBuffer = null;
                        }
                        else if (string.Equals(reader.LocalName, "response", StringComparison.OrdinalIgnoreCase))
                        {
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
                            items.Add(itemInfo);
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
        // instead of falling through to the typed-field branches.
        // Returns true when the reader was advanced past the element's end tag (the caller
        // must NOT call Read() again for this iteration); false when the reader is still on
        // the original empty element.
        private static bool BufferProperty(XmlReader reader, List<(PropertyName Name, string Value)> buffer, PropFindMode mode)
        {
            var localName = reader.LocalName;
            var ns = reader.NamespaceURI ?? string.Empty;
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
                reader.ReadInnerXml();
                text = string.Empty;
                advanced = true;
            }
            else
            {
                text = reader.ReadInnerXml();
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
                // <propname/> responses are expected to carry status 200; record names
                // regardless so callers don't lose data when servers omit the status.
                foreach (var entry in buffer)
                {
                    AppendAvailableName(itemInfo, entry.Name);
                }
                return;
            }

            // NamedProperties mode.
            if (statusCode == 200)
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

        // Strips simple wrapping markup so <D:resourcetype><D:collection/></D:resourcetype>
        // surfaces as the inner XML rather than throwing in callers that just want text.
        // For pure-text elements, the input is already the value.
        private static string ExtractElementText(string innerXml)
        {
            if (string.IsNullOrEmpty(innerXml))
                return string.Empty;
            // Fast path: no markup.
            if (innerXml.IndexOf('<') < 0)
                return innerXml;
            // For elements with structured content (e.g. resourcetype), keep the inner
            // XML as-is — callers that need it can re-parse. We don't try to strip tags
            // because that would be lossy for legitimate values containing '<'.
            return innerXml;
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

        // Mirror the typed-field updates the AllProp branch performs, so callers that
        // queried via <prop> still see Item.Etag / IsCollection / etc. populated for
        // the standard live properties.
        private static void ApplyTypedFieldFromBag(Item itemInfo, string ns, string localName, string value)
        {
            if (!string.Equals(ns, c_davNamespace, StringComparison.OrdinalIgnoreCase))
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
                // <resourcetype> may contain an empty <D:collection/> child; detect via the
                // captured inner XML.
                if (!string.IsNullOrEmpty(value)
                    && value.IndexOf("collection", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    itemInfo.IsCollection = true;
                }
            }
            else if (string.Equals(localName, "hidden", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(localName, "ishidden", StringComparison.OrdinalIgnoreCase))
            {
                itemInfo.IsHidden = true;
            }
        }

        // RFC 7230 status-line parser. Returns 0 when the line is malformed.
        // Span-based to avoid a string.Split allocation per propstat block.
        private static int ParseStatusCode(string statusLine)
        {
            if (string.IsNullOrEmpty(statusLine))
                return 0;
            var span = statusLine.AsSpan();
            int i = 0;
            while (i < span.Length && (span[i] == ' ' || span[i] == '\t')) i++;
            while (i < span.Length && span[i] != ' ' && span[i] != '\t') i++;
            while (i < span.Length && (span[i] == ' ' || span[i] == '\t')) i++;
            int start = i;
            while (i < span.Length && span[i] != ' ' && span[i] != '\t') i++;
            return int.TryParse(span.Slice(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
                ? code
                : 0;
        }
    }
}
