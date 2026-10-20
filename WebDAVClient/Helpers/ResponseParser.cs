using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient.Helpers
{
    internal enum PropFindMode { AllProp, NamedProperties, PropName }

    // Legacy projection only. Structural parsing is shared with the modern protocol surface.
    internal static class ResponseParser
    {
        internal static XmlReaderSettings XmlReaderSettings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true
        };

        public static Item ParseItem(Stream stream) => ParseItem(stream, PropFindMode.AllProp);
        public static Item ParseItem(Stream stream, PropFindMode mode) => ParseItemAsync(stream, mode).GetAwaiter().GetResult();
        public static List<Item> ParseItems(Stream stream) => ParseItems(stream, PropFindMode.AllProp);
        public static List<Item> ParseItems(Stream stream, PropFindMode mode) => ParseItemsAsync(stream, mode).GetAwaiter().GetResult();
        public static async Task<Item> ParseItemAsync(Stream stream, PropFindMode mode, CancellationToken cancellationToken = default) =>
            (await ParseCoreAsync(stream, mode, true, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        public static Task<List<Item>> ParseItemsAsync(Stream stream, PropFindMode mode, CancellationToken cancellationToken = default) =>
            ParseCoreAsync(stream, mode, false, cancellationToken);

        private static async Task<List<Item>> ParseCoreAsync(Stream stream, PropFindMode mode, bool firstOnly, CancellationToken token)
        {
            var items = new List<Item>();
            // Existing callers have no configurable size budget. Do not impose modern defaults on large legacy listings.
            var limits = new DavXmlLimits { MaxResponseCharacters = long.MaxValue, MaxResourceCharacters = int.MaxValue, MaxDepth = 256 };
            await foreach (var resource in DavXml.ReadMultiStatusAsync(stream, limits, legacy: true, cancellationToken: token,
                namesOnly: mode == PropFindMode.PropName, davPropertiesOnly: mode == PropFindMode.AllProp).ConfigureAwait(false))
            {
                if (resource.StatusCode.HasValue && (resource.StatusCode < 200 || resource.StatusCode >= 300))
                {
                    if (resource.StatusCode == 409)
                    {
                        throw new WebDAVConflictException(409, "PROPFIND reported a failed resource.");
                    }
                    throw new WebDAVException(resource.StatusCode.Value, "PROPFIND reported a failed resource.");
                }
                var item = new Item { Href = resource.Hrefs[0].Replace("#", "%23") };
                foreach (var group in resource.PropertyStatuses)
                {
                    foreach (var property in group.Properties)
                    {
                        var success = property.StatusCode >= 200 && property.StatusCode < 300;
                        if (mode != PropFindMode.AllProp)
                        {
                            item.PropertyStatuses ??= new Dictionary<PropertyName, int?>();
                            item.PropertyStatuses[property.Name] = property.StatusCode;
                        }
                        if (mode == PropFindMode.PropName)
                        {
                            if (success || !property.StatusCode.HasValue)
                            {
                                item.AvailablePropertyNames ??= new List<PropertyName>();
                                item.AvailablePropertyNames.Add(property.Name);
                            }
                        }
                        else if (success || mode == PropFindMode.AllProp && !property.StatusCode.HasValue)
                        {
                            var value = LegacyValue(property.Value);
                            if (mode == PropFindMode.NamedProperties)
                            {
                                item.FoundProperties ??= new Dictionary<PropertyName, string>();
                                item.FoundProperties[property.Name] = value;
                            }
                            ApplyTypedField(item, property.Name, value);
                        }
                        else if (mode == PropFindMode.NamedProperties)
                        {
                            item.NotFoundProperties ??= new List<PropertyName>();
                            item.NotFoundProperties.Add(property.Name);
                        }
                    }
                }
                if (mode != PropFindMode.AllProp
                    && (item.FoundProperties == null || !item.FoundProperties.ContainsKey(new PropertyName("DAV:", "resourcetype"))
                        && !item.FoundProperties.ContainsKey(new PropertyName("DAV:", "iscollection")))
                    && item.Href.EndsWith("/", StringComparison.Ordinal))
                {
                    item.IsCollection = true;
                }
                var href = item.Href.TrimEnd('/');
                if (!item.IsCollection)
                {
                    item.Href = href;
                }
                if (string.IsNullOrEmpty(item.DisplayName))
                {
                    item.DisplayName = WebUtility.UrlDecode(href.Substring(href.LastIndexOf('/') + 1));
                }
                if (!firstOnly || items.Count == 0)
                {
                    items.Add(item);
                }
            }
            return items;
        }

        private static string LegacyValue(DavXmlValue value)
        {
            if (value.TryGetText(out var text))
            {
                return text;
            }
            using var reader = value.ToElement().CreateReader();
            reader.MoveToContent();
            return reader.ReadInnerXml();
        }

        private static void ApplyTypedField(Item item, PropertyName name, string value)
        {
            if (name.Namespace != "DAV:")
            {
                return;
            }
            switch (name.LocalName.ToLowerInvariant())
            {
                case "creationdate":
                    if (DateTime.TryParse(value, out var created)) { item.CreationDate = created; }
                    break;
                case "getlastmodified":
                    if (DateTime.TryParse(value, out var modified)) { item.LastModified = modified; }
                    break;
                case "displayname":
                    item.DisplayName = value;
                    break;
                case "getcontentlength":
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)) { item.ContentLength = size; }
                    break;
                case "getcontenttype":
                    item.ContentType = value;
                    break;
                case "getetag":
                    item.Etag = value;
                    break;
                case "iscollection":
                    item.IsCollection = ParseBoolean(value);
                    break;
                case "hidden":
                case "ishidden":
                    item.IsHidden = ParseBoolean(value);
                    break;
                case "resourcetype":
                    var settings = XmlReaderSettings.Clone();
                    settings.ConformanceLevel = ConformanceLevel.Fragment;
                    using (var reader = XmlReader.Create(new StringReader(value), settings))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element && reader.Depth == 0 && reader.NamespaceURI == "DAV:"
                                && string.Equals(reader.LocalName, "collection", StringComparison.OrdinalIgnoreCase))
                            {
                                item.IsCollection = true;
                            }
                        }
                    }
                    break;
            }
        }

        private static bool ParseBoolean(string value) => bool.TryParse(value, out var boolean) ? boolean
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number == 1;

        internal static int ParseStatusCode(string statusLine) => DavXml.ParseStatus(statusLine);
    }
}
