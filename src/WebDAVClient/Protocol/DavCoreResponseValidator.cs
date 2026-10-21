#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace WebDAVClient.Protocol
{
    internal sealed class DavCoreResponseValidator
    {
        private readonly Uri m_requestUri;

        private readonly HashSet<string> m_hrefs = new HashSet<string>(StringComparer.Ordinal);

        private bool? m_absolute;

        private bool m_description;

        internal DavCoreResponseValidator(Uri requestUri)
        {
            m_requestUri = requestUri;
        }

        internal void ValidateRoot(XElement root)
        {
            ElementContent(root);
            foreach (var child in root.Elements())
            {
                ObserveRootChild(child);
            }
        }

        internal void ObserveRootChild(XElement element)
        {
            if (element.Name == DavXml.Dav + "responsedescription")
            {
                if (m_description)
                {
                    throw new XmlException("A DAV:multistatus permits only one response description.");
                }
                m_description = true;
                DavXml.Text(element);
            }
        }

        internal void ValidateResponse(XElement response)
        {
            ElementContent(response);
            Single(response, "status");
            Single(response, "responsedescription");
            Single(response, "error");
            var location = Single(response, "location");
            if (location != null)
            {
                ElementContent(location);
                var href = Single(location, "href", required: true)!;
                SimpleReference(DavXml.Text(href), resource: false);
            }
            foreach (var href in response.Elements(DavXml.Dav + "href"))
            {
                var value = DavXml.Text(href);
                bool absolute = SimpleReference(value, resource: true);
                if (absolute)
                {
                    var uri = new Uri(value, UriKind.Absolute);
                    if (!string.Equals(uri.Scheme, m_requestUri.Scheme, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(uri.IdnHost, m_requestUri.IdnHost, StringComparison.OrdinalIgnoreCase)
                        || uri.Port != m_requestUri.Port)
                    {
                        throw new XmlException("Core resource href origins must match the request URI.");
                    }
                }
                if (m_absolute.HasValue && m_absolute != absolute)
                {
                    throw new XmlException("Core resource hrefs must use one absolute-URI or absolute-path format.");
                }
                m_absolute = absolute;
                if (!m_hrefs.Add(value))
                {
                    throw new XmlException("A resource href must not occur more than once in a core multistatus.");
                }
            }
            foreach (var propstat in response.Elements(DavXml.Dav + "propstat"))
            {
                ElementContent(propstat);
                var prop = Single(propstat, "prop", required: true)!;
                ElementContent(prop);
                Single(propstat, "status", required: true);
                Single(propstat, "responsedescription");
                var error = Single(propstat, "error");
                if (error != null)
                {
                    Error(error);
                }
            }
            var responseError = response.Element(DavXml.Dav + "error");
            if (responseError != null)
            {
                Error(responseError);
            }
        }

        internal static XElement? Single(XElement element, string name, bool required = false)
        {
            var children = element.Elements(DavXml.Dav + name).Take(2).ToArray();
            if (children.Length > 1 || required && children.Length == 0)
            {
                throw new XmlException("Invalid cardinality for DAV:" + name + " in " + element.Name + ".");
            }
            return children.Length == 0 ? null : children[0];
        }

        internal static void ElementContent(XElement element)
        {
            if (element.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
            {
                throw new XmlException("Unexpected text in " + element.Name + ".");
            }
        }

        internal static void Error(XElement element)
        {
            ElementContent(element);
            if (!element.HasElements)
            {
                throw new XmlException("DAV:error requires at least one condition element.");
            }
        }

        internal static bool SameEscapedPathAndQuery(Uri left, Uri right, bool allowPathPlusEncodingAlias)
        {
            var first = left.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);
            var second = right.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);
            if (allowPathPlusEncodingAlias)
            {
                first = PathPlusAlias(first);
                second = PathPlusAlias(second);
            }
            if (first.Length != second.Length)
            {
                return false;
            }
            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] == '%' && second[i] == '%')
                {
                    if (char.ToUpperInvariant(first[i + 1]) != char.ToUpperInvariant(second[i + 1])
                        || char.ToUpperInvariant(first[i + 2]) != char.ToUpperInvariant(second[i + 2]))
                    {
                        return false;
                    }
                    i += 2;
                }
                else if (first[i] != second[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static string PathPlusAlias(string pathAndQuery)
        {
            int query = pathAndQuery.IndexOf('?');
            if (query < 0)
            {
                return pathAndQuery.Replace("%2B", "+", StringComparison.OrdinalIgnoreCase);
            }
            return pathAndQuery.Substring(0, query).Replace("%2B", "+", StringComparison.OrdinalIgnoreCase)
                + pathAndQuery.Substring(query);
        }

        internal static bool SimpleReference(string value, bool resource)
        {
            if (value.Length == 0 || value.Any(c => c > 127 || char.IsWhiteSpace(c) || char.IsControl(c))
                || value.IndexOfAny(new[] { '\\', '#', '<', '>', '"', '{', '}', '|', '^', '`' }) >= 0)
            {
                throw new XmlException("DAV hrefs require an escaped URI without a fragment.");
            }
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '%' && (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2])))
                {
                    throw new XmlException("Invalid DAV href percent escape.");
                }
            }
            bool absolutePath = value[0] == '/' && !value.StartsWith("//", StringComparison.Ordinal);
            int colon = value.IndexOf(':');
            if (!absolutePath && (colon <= 0 || !Uri.CheckSchemeName(value.Substring(0, colon))))
            {
                throw new XmlException("A DAV href requires an absolute URI or an absolute path.");
            }
            if (!Uri.TryCreate(absolutePath ? "http://validation.invalid" + value : value, UriKind.Absolute, out var uri)
                || resource && (uri.Scheme != "http" && uri.Scheme != "https"
                    || uri.UserInfo.Length != 0 || uri.Host.Length == 0))
            {
                throw new XmlException("A DAV resource href requires an HTTP(S) URI or an absolute path.");
            }
            int contentStart = absolutePath ? 0 : colon + 1;
            if (!absolutePath && value.AsSpan(contentStart).StartsWith("//", StringComparison.Ordinal))
            {
                int authorityEnd = value.IndexOfAny(new[] { '/', '?' }, contentStart + 2);
                contentStart = authorityEnd < 0 ? value.Length : authorityEnd;
            }
            // Uri's IRI normalization can misclassify valid escaped Unicode combined with %25 or %23.
            // Validate the original path/query octets instead of the normalized URI's well-formedness.
            if (value.AsSpan(contentStart).ContainsAnyExcept(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~!$&'()*+,;=:@/?%"))
            {
                throw new XmlException("Invalid character in DAV href path or query.");
            }
            if (resource)
            {
                var path = absolutePath ? value.Split('?')[0] : value.Substring(value.IndexOf("://", StringComparison.Ordinal) + 3).Split('?')[0];
                if (path.Split('/').Select(Uri.UnescapeDataString).Any(segment => segment == "." || segment == ".."))
                {
                    throw new XmlException("Core resource hrefs must not contain dot segments.");
                }
            }
            return !absolutePath;
        }
    }
}
