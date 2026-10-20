#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using WebDAVClient.Model;

namespace WebDAVClient.Protocol
{
    /// <summary>
    /// A property outcome with an optional decoded value and explicit decoding failure.
    /// </summary>
    public sealed record DavDecodedProperty<T>(DavPropertyResult Source, T? Value, Exception? DecodingError)
    {
        /// <summary>
        /// True only when the server returned success and decoding completed.
        /// </summary>
        public bool HasValue => Source.StatusCode >= 200 && Source.StatusCode < 300 && DecodingError == null;
    }

    /// <summary>
    /// A reusable typed property codec. Wire XML remains authoritative and available on every result.
    /// </summary>
    public sealed class DavProperty<T>
    {
        private readonly Func<DavXmlValue, T> m_decode;

        /// <summary>
        /// Defines a qualified property and its decoder. Expected malformed values should throw FormatException or XmlException.
        /// </summary>
        public DavProperty(PropertyName name, Func<DavXmlValue, T> decode)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(decode);
            Name = name;
            m_decode = decode;
        }

        /// <summary>
        /// Property identity used in PROPFIND and typed lookups.
        /// </summary>
        public PropertyName Name
        {
            get;
        }

        /// <summary>
        /// Decodes every matching occurrence. Missing properties produce no entries; denied properties retain their status.
        /// Invalid values carry a decoding error, never a fabricated default presented as success.
        /// </summary>
        public IEnumerable<DavDecodedProperty<T>> Read(DavResourceResponse resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return resource.GetProperties(Name).Select(Decode);
        }

        private DavDecodedProperty<T> Decode(DavPropertyResult property)
        {
            if (!(property.StatusCode >= 200 && property.StatusCode < 300))
            {
                return new DavDecodedProperty<T>(property, default, null);
            }
            try
            {
                return new DavDecodedProperty<T>(property, m_decode(property.Value), null);
            }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException || exception is XmlException)
            {
                return new DavDecodedProperty<T>(property, default, exception);
            }
        }
    }

    /// <summary>
    /// A server-advertised lock scope/type pair.
    /// </summary>
    public sealed record DavSupportedLock(PropertyName Scope, PropertyName Type);

    /// <summary>
    /// Core DAV property codecs; unknown properties can use application-defined codecs without changing the client.
    /// </summary>
    public static class DavProperties
    {
        /// <summary>
        /// DAV:displayname.
        /// </summary>
        public static DavProperty<string> DisplayName
        {
            get;
        } = Text("displayname");

        /// <summary>
        /// DAV:getetag; opacity and weak/strong distinctions are retained.
        /// </summary>
        public static DavProperty<string> ETag
        {
            get;
        } = Text("getetag");

        /// <summary>
        /// DAV:getcontenttype.
        /// </summary>
        public static DavProperty<string> ContentType
        {
            get;
        } = Text("getcontenttype");

        /// <summary>
        /// DAV:getcontentlanguage.
        /// </summary>
        public static DavProperty<string> ContentLanguage
        {
            get;
        } = Text("getcontentlanguage");

        /// <summary>
        /// DAV:creationdate, including its offset.
        /// </summary>
        public static DavProperty<DateTimeOffset> CreationDate
        {
            get;
        } = new DavProperty<DateTimeOffset>(new PropertyName("DAV:", "creationdate"),
            v => XmlConvert.ToDateTimeOffset(TextValue(v)));

        /// <summary>
        /// DAV:getlastmodified, including its offset.
        /// </summary>
        public static DavProperty<DateTimeOffset> LastModified
        {
            get;
        } = new DavProperty<DateTimeOffset>(new PropertyName("DAV:", "getlastmodified"),
            v => DateTimeOffset.Parse(TextValue(v), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));

        /// <summary>
        /// DAV:getcontentlength; negative values are invalid, not an unknown size.
        /// </summary>
        public static DavProperty<long> ContentLength
        {
            get;
        } = new DavProperty<long>(new PropertyName("DAV:", "getcontentlength"), v =>
        {
            var length = long.Parse(TextValue(v), NumberStyles.None, CultureInfo.InvariantCulture);
            return length;
        });

        /// <summary>
        /// DAV:resourcetype. No property result means unknown type; an empty successful value is distinct.
        /// </summary>
        public static DavProperty<IReadOnlyList<PropertyName>> ResourceTypes
        {
            get;
        } =
            new DavProperty<IReadOnlyList<PropertyName>>(new PropertyName("DAV:", "resourcetype"),
                v =>
                {
                    var element = v.ToElement();
                    DavCoreResponseValidator.ElementContent(element);
                    return Array.AsReadOnly(element.Elements().Select(e => new PropertyName(e.Name.NamespaceName, e.Name.LocalName)).ToArray());
                });

        /// <summary>
        /// Every active lock in DAV:lockdiscovery.
        /// </summary>
        public static DavProperty<IReadOnlyList<DavLock>> LockDiscovery
        {
            get;
        } =
            new DavProperty<IReadOnlyList<DavLock>>(new PropertyName("DAV:", "lockdiscovery"), v => DavXml.ParseLocks(v.ToElement(), DateTimeOffset.UtcNow));

        /// <summary>
        /// Advertised core write-lock entries. Unknown XML extensions are ignored, not interpreted as lock kinds.
        /// </summary>
        public static DavProperty<IReadOnlyList<DavSupportedLock>> SupportedLocks
        {
            get;
        } =
            new DavProperty<IReadOnlyList<DavSupportedLock>>(new PropertyName("DAV:", "supportedlock"), v =>
            {
                var element = v.ToElement();
                DavCoreResponseValidator.ElementContent(element);
                return Array.AsReadOnly(element.Elements(DavXml.Dav + "lockentry").Select(e =>
                {
                    DavCoreResponseValidator.ElementContent(e);
                    return new DavSupportedLock(ChildName(e, "lockscope"), ChildName(e, "locktype"));
                }).ToArray());
            });

        private static PropertyName ChildName(XElement element, string name)
        {
            var child = DavXml.LockKind(element, name);
            if (child == null)
            {
                throw new XmlException("A supported lock requires one scope and one type.");
            }
            return new PropertyName(child.Name.NamespaceName, child.Name.LocalName);
        }

        private static DavProperty<string> Text(string name) => new DavProperty<string>(new PropertyName("DAV:", name), TextValue);

        private static string TextValue(DavXmlValue value) => value.TryGetText(out var text) ? text! : throw new FormatException("Expected a text-only DAV property.");
    }
}
