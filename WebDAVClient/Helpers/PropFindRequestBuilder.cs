using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using WebDAVClient.Model;

namespace WebDAVClient.Helpers
{
    /// <summary>
    /// Builds the three PROPFIND request body forms defined by RFC 4918 §9.1:
    /// <c>&lt;allprop/&gt;</c> (default — every dead and live property the server
    /// chooses to return), <c>&lt;propname/&gt;</c> (names only — property
    /// discovery), and <c>&lt;prop&gt;</c> (a caller-supplied list of qualified
    /// property names — bandwidth-efficient targeted retrieval).
    /// </summary>
    /// <remarks>
    /// The two static body forms (<c>allprop</c> / <c>propname</c>) are cached
    /// once at type initialisation as pre-encoded UTF-8 byte arrays so every
    /// PROPFIND of those flavours reuses the same buffer (matching the prior
    /// allocation profile of <c>s_propFindRequestContentBytes</c> in
    /// <see cref="Client"/>).
    /// </remarks>
    internal static class PropFindRequestBuilder
    {
        // http://webdav.org/specs/rfc4918.html#METHOD_PROPFIND
        private const string c_allPropBody =
            "<?xml version=\"1.0\" encoding=\"utf-8\" ?>" +
            "<propfind xmlns=\"DAV:\">" +
            "<allprop/>" +
            "</propfind>";

        private const string c_propNameBody =
            "<?xml version=\"1.0\" encoding=\"utf-8\" ?>" +
            "<propfind xmlns=\"DAV:\">" +
            "<propname/>" +
            "</propfind>";

        private static readonly byte[] s_allPropBytes = Encoding.UTF8.GetBytes(c_allPropBody);
        private static readonly byte[] s_propNameBytes = Encoding.UTF8.GetBytes(c_propNameBody);

        /// <summary>
        /// Returns the cached UTF-8 bytes for the <c>&lt;allprop/&gt;</c> request
        /// body — the historical default for every PROPFIND issued by this client.
        /// </summary>
        public static byte[] BuildAllPropBody() => s_allPropBytes;

        /// <summary>
        /// Returns the cached UTF-8 bytes for the <c>&lt;propname/&gt;</c> request
        /// body, used for property-name discovery (RFC 4918 §9.1).
        /// </summary>
        public static byte[] BuildPropNameBody() => s_propNameBytes;

        /// <summary>
        /// Builds a <c>&lt;propfind&gt;&lt;prop&gt;...&lt;/prop&gt;&lt;/propfind&gt;</c>
        /// body containing one element per supplied <see cref="PropertyName"/>.
        /// Each property is emitted with its declared namespace bound to a unique
        /// generated prefix so callers can mix DAV: with arbitrary custom
        /// namespaces freely. <see cref="XmlWriter"/> handles all escaping.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="properties"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when the sequence is empty or contains a <c>null</c> entry.
        /// PROPFIND <c>&lt;prop&gt;</c> bodies with no properties are pointless
        /// (and rejected by some servers); fail fast with a clear message.
        /// </exception>
        public static byte[] BuildPropBody(IEnumerable<PropertyName> properties)
        {
            if (properties == null)
                throw new ArgumentNullException(nameof(properties));

            // Materialise once so we can validate non-emptiness up-front and
            // assign a stable prefix per distinct namespace.
            var list = new List<PropertyName>();
            foreach (var p in properties)
            {
                if (p == null)
                    throw new ArgumentException("Property list must not contain null entries.", nameof(properties));
                list.Add(p);
            }

            if (list.Count == 0)
                throw new ArgumentException("PROPFIND <prop> requires at least one property.", nameof(properties));

            using (var ms = new MemoryStream())
            {
                var settings = new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    OmitXmlDeclaration = false,
                    Indent = false
                };
                using (var writer = XmlWriter.Create(ms, settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("D", "propfind", "DAV:");

                    // Pre-declare every distinct non-DAV namespace as an xmlns attribute on
                    // the root element so each property element below reuses the prefix
                    // instead of triggering a per-element xmlns binding.
                    var nsToPrefix = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "DAV:", "D" }
                    };
                    foreach (var prop in list)
                    {
                        if (nsToPrefix.ContainsKey(prop.Namespace))
                            continue;
                        var prefix = "n" + (nsToPrefix.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        nsToPrefix[prop.Namespace] = prefix;
                        writer.WriteAttributeString("xmlns", prefix, null, prop.Namespace);
                    }

                    writer.WriteStartElement("D", "prop", "DAV:");

                    foreach (var prop in list)
                    {
                        var prefix = nsToPrefix[prop.Namespace];
                        writer.WriteStartElement(prefix, prop.LocalName, prop.Namespace);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement(); // prop
                    writer.WriteEndElement(); // propfind
                    writer.WriteEndDocument();
                }
                return ms.ToArray();
            }
        }
    }
}
