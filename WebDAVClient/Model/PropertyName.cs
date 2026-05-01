using System;
using System.Xml;

namespace WebDAVClient.Model
{
    /// <summary>
    /// Identifies a single WebDAV property by its XML namespace URI and local name —
    /// the (qualified) name a server uses inside <c>&lt;D:prop&gt;</c>. Used to build
    /// targeted <c>PROPFIND &lt;prop&gt;</c> requests (RFC 4918 §9.1) and to key the
    /// values returned in <see cref="Item.FoundProperties"/>,
    /// <see cref="Item.NotFoundProperties"/>, and
    /// <see cref="Item.AvailablePropertyNames"/>.
    /// </summary>
    public sealed class PropertyName : IEquatable<PropertyName>
    {
        /// <summary>
        /// XML namespace URI for the property (for example <c>DAV:</c> for a live
        /// property like <c>getetag</c>, or a custom URI such as
        /// <c>http://example.com/ns</c> for application metadata).
        /// </summary>
        public string Namespace { get; }

        /// <summary>
        /// Local XML name of the property — the element local name without any prefix
        /// (for example <c>getetag</c>, <c>displayname</c>, <c>author</c>).
        /// </summary>
        public string LocalName { get; }

        /// <summary>
        /// Constructs a qualified property name. <paramref name="namespace"/> may be
        /// any non-null string (the empty string is allowed for the no-namespace case
        /// some servers use). <paramref name="localName"/> must be a valid XML
        /// <c>NCName</c> so the request body is well-formed.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="namespace"/> or <paramref name="localName"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="localName"/> is not a valid XML <c>NCName</c>.</exception>
        public PropertyName(string @namespace, string localName)
        {
            if (@namespace == null)
                throw new ArgumentNullException(nameof(@namespace));
            if (localName == null)
                throw new ArgumentNullException(nameof(localName));
            if (localName.Length == 0)
                throw new ArgumentException("Property local name must be a non-empty XML NCName.", nameof(localName));

            try
            {
                XmlConvert.VerifyNCName(localName);
            }
            catch (XmlException ex)
            {
                throw new ArgumentException(
                    "Property local name '" + localName + "' is not a valid XML NCName (no colons, no spaces, must start with a letter or underscore).",
                    nameof(localName), ex);
            }

            Namespace = @namespace;
            LocalName = localName;
        }

        /// <summary>
        /// Two property names are equal when both <see cref="Namespace"/> and
        /// <see cref="LocalName"/> match using <see cref="StringComparison.Ordinal"/>.
        /// XML namespace comparison is case-sensitive per the XML Namespaces spec.
        /// </summary>
        public bool Equals(PropertyName other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
                && string.Equals(LocalName, other.LocalName, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as PropertyName);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(Namespace) * 397)
                    ^ StringComparer.Ordinal.GetHashCode(LocalName);
            }
        }

        /// <summary>
        /// Returns the property name in James-Clark notation
        /// (<c>{namespace}localname</c>) — convenient for diagnostics and logging.
        /// </summary>
        public override string ToString() => "{" + Namespace + "}" + LocalName;

        public static bool operator ==(PropertyName left, PropertyName right)
        {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null);
            return left.Equals(right);
        }

        public static bool operator !=(PropertyName left, PropertyName right) => !(left == right);
    }
}
