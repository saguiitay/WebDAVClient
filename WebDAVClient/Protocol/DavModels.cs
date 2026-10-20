#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml.Linq;
using WebDAVClient.Model;

namespace WebDAVClient.Protocol
{
    /// <summary>A detached, namespace-aware XML element. XML access returns a defensive copy.</summary>
    public sealed class DavXmlValue
    {
        private readonly XElement m_element;

        /// <summary>Copies an element, including inherited namespace and language context.</summary>
        public DavXmlValue(XElement element)
        {
            ArgumentNullException.ThrowIfNull(element);
            m_element = new XElement(element);
            for (var ancestor = element.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                for (var attribute = ancestor.FirstAttribute; attribute != null; attribute = attribute.NextAttribute)
                {
                    if (attribute.IsNamespaceDeclaration && m_element.Attribute(attribute.Name) == null)
                    {
                        m_element.Add(new XAttribute(attribute));
                    }
                }
                var language = ancestor.Attribute(XNamespace.Xml + "lang");
                if (language != null && m_element.Attribute(language.Name) == null)
                {
                    m_element.Add(new XAttribute(language));
                }
                var space = ancestor.Attribute(XNamespace.Xml + "space");
                if (space != null && m_element.Attribute(space.Name) == null)
                {
                    m_element.Add(new XAttribute(space));
                }
            }
        }

        /// <summary>The element's qualified name.</summary>
        public PropertyName Name => new PropertyName(m_element.Name.NamespaceName, m_element.Name.LocalName);

        /// <summary>Returns a mutable copy; mutations never change this value.</summary>
        public XElement ToElement() => new XElement(m_element);

        /// <summary>Creates a text-valued property. Null text means an empty value, not removal.</summary>
        public static DavXmlValue Text(PropertyName name, string? value)
        {
            ArgumentNullException.ThrowIfNull(name);
            return new DavXmlValue(new XElement(XName.Get(name.LocalName, name.Namespace), value ?? ""));
        }

        /// <summary>Gets text only when the element has no child elements; structured XML is never flattened.</summary>
        public bool TryGetText(out string? value)
        {
            value = m_element.HasElements ? null : m_element.Value;
            return value != null;
        }
    }

    /// <summary>One property and its exact propstat outcome; a missing legacy status remains null.</summary>
    public sealed record DavPropertyResult(PropertyName Name, int? StatusCode, DavXmlValue Value);

    /// <summary>A wire propstat group, retaining property order and error XML.</summary>
    public sealed class DavPropertyStatus
    {
        internal DavPropertyStatus(int? status, IReadOnlyList<DavXmlValue> properties, string? description, IEnumerable<DavXmlValue> errors)
        {
            StatusCode = status;
            var results = new DavPropertyResult[properties.Count];
            for (int i = 0; i < results.Length; i++)
            {
                var property = properties[i];
                results[i] = new DavPropertyResult(property.Name, status, property);
            }
            Properties = Array.AsReadOnly(results);
            Description = description;
            Errors = Array.AsReadOnly(errors.ToArray());
        }
        /// <summary>Actual status, or null only in the legacy compatibility reader.</summary>
        public int? StatusCode { get; }
        /// <summary>Properties in wire order, including duplicates.</summary>
        public IReadOnlyList<DavPropertyResult> Properties { get; }
        /// <summary>Server description, when present.</summary>
        public string? Description { get; }
        /// <summary>DAV:error children, including unknown conditions.</summary>
        public IReadOnlyList<DavXmlValue> Errors { get; }
    }

    /// <summary>A resource response. Hrefs are evidence, not authorization to issue another request.</summary>
    public sealed class DavResourceResponse
    {
        internal DavResourceResponse(IEnumerable<string> hrefs, int? status, IEnumerable<DavPropertyStatus> properties,
            string? description, string? location, IEnumerable<DavXmlValue> errors, IEnumerable<DavXmlValue> extensions)
        {
            Hrefs = Array.AsReadOnly(hrefs.ToArray());
            StatusCode = status;
            PropertyStatuses = Array.AsReadOnly(properties.ToArray());
            Description = description;
            Location = location;
            Errors = Array.AsReadOnly(errors.ToArray());
            Extensions = Array.AsReadOnly(extensions.ToArray());
        }
        /// <summary>Unmodified resource identifiers in wire order.</summary>
        public IReadOnlyList<string> Hrefs { get; }
        /// <summary>Response-level status, distinct from propstat statuses.</summary>
        public int? StatusCode { get; }
        /// <summary>Ordered property groups.</summary>
        public IReadOnlyList<DavPropertyStatus> PropertyStatuses { get; }
        /// <summary>Optional response description.</summary>
        public string? Description { get; }
        /// <summary>Optional DAV:location href.</summary>
        public string? Location { get; }
        /// <summary>Response-level error conditions.</summary>
        public IReadOnlyList<DavXmlValue> Errors { get; }
        /// <summary>Uninterpreted response extension elements.</summary>
        public IReadOnlyList<DavXmlValue> Extensions { get; }
        /// <summary>Enumerates matching properties without collapsing repeated wire entries.</summary>
        public IEnumerable<DavPropertyResult> GetProperties(PropertyName name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return EnumerateProperties(name);
        }

        private IEnumerable<DavPropertyResult> EnumerateProperties(PropertyName name)
        {
            for (int groupIndex = 0; groupIndex < PropertyStatuses.Count; groupIndex++)
            {
                var properties = PropertyStatuses[groupIndex].Properties;
                for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                {
                    var property = properties[propertyIndex];
                    if (property.Name == name)
                    {
                        yield return property;
                    }
                }
            }
        }
    }

    /// <summary>An ordered PROPPATCH instruction. Repeated names and set/remove order are significant.</summary>
    public sealed class DavPropertyChange
    {
        private DavPropertyChange(PropertyName name, DavXmlValue? value)
        {
            Name = name;
            Value = value;
        }
        /// <summary>Qualified property identity.</summary>
        public PropertyName Name { get; }
        /// <summary>New value, or null for removal.</summary>
        public DavXmlValue? Value { get; }
        /// <summary>Creates a set instruction; writable DAV properties are permitted.</summary>
        public static DavPropertyChange Set(DavXmlValue value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return new DavPropertyChange(value.Name, value);
        }
        /// <summary>Creates a remove instruction.</summary>
        public static DavPropertyChange Remove(PropertyName name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return new DavPropertyChange(name, null);
        }
    }

    /// <summary>Server-granted timeout. Missing, infinite and finite are distinct.</summary>
    public sealed record DavLockTimeout
    {
        private DavLockTimeout(TimeSpan? duration, bool infinite) { Duration = duration; IsInfinite = infinite; }
        /// <summary>Finite duration; null for unknown or infinite.</summary>
        public TimeSpan? Duration { get; }
        /// <summary>True only for an explicit Infinite value.</summary>
        public bool IsInfinite { get; }
        /// <summary>No granted timeout was reported.</summary>
        public static DavLockTimeout Unspecified { get; } = new DavLockTimeout(null, false);
        /// <summary>An explicit Infinite timeout.</summary>
        public static DavLockTimeout Infinite { get; } = new DavLockTimeout(null, true);
        /// <summary>A positive, whole-second timeout representable on the wire.</summary>
        public static DavLockTimeout Finite(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero || duration.TotalSeconds > uint.MaxValue || duration.Ticks % TimeSpan.TicksPerSecond != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }
            return new DavLockTimeout(duration, false);
        }
        internal string ToHeader() => IsInfinite ? "Infinite" : Duration.HasValue
            ? "Second-" + ((uint)Duration.Value.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : throw new InvalidOperationException("An unspecified timeout cannot be requested.");
    }

    /// <summary>A lock observation, not a self-renewing lease or a guarantee of current exclusivity.</summary>
    public sealed record DavLock(string? Token, PropertyName Scope, PropertyName Type, string Depth,
        DavXmlValue? Owner, string? Root, DavLockTimeout Timeout, DateTimeOffset ObservedAt);

    /// <summary>Immutable XML resource limits; total limits apply to an entire streamed response.</summary>
    public sealed record DavXmlLimits
    {
        /// <summary>Maximum XML characters per response. Default: 16 Mi characters.</summary>
        public long MaxResponseCharacters { get; init; } = 16 * 1024 * 1024;
        /// <summary>Maximum characters retained for any one resource/XML element.</summary>
        public int MaxResourceCharacters { get; init; } = 1024 * 1024;
        /// <summary>Maximum XML nesting depth.</summary>
        public int MaxDepth { get; init; } = 64;
        internal void Validate()
        {
            if (MaxResponseCharacters <= 0 || MaxResourceCharacters <= 0 || MaxDepth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(DavXmlLimits), "XML limits must be positive.");
            }
        }
    }
}
