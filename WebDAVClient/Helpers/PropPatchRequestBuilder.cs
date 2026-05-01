using System;
using System.IO;
using System.Text;
using System.Xml;

namespace WebDAVClient.Helpers
{
    /// <summary>
    /// Builds and validates inputs for WebDAV PROPPATCH requests (RFC 4918 §9.2).
    /// Mirrors <see cref="PropPatchResponseParser"/> on the response side.
    /// </summary>
    internal static class PropPatchRequestBuilder
    {
        /// <summary>
        /// Validates that <paramref name="propertyName"/> is a non-empty XML NCName so the
        /// emitted body is well-formed. PROPPATCH would otherwise be rejected by the
        /// server with a 400 from the wire; failing here gives a clearer error.
        /// </summary>
        public static void ValidatePropertyName(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                throw new ArgumentException("Property name must be a non-empty string.", nameof(propertyName));
            try
            {
                XmlConvert.VerifyNCName(propertyName);
            }
            catch (XmlException ex)
            {
                throw new ArgumentException(
                    "Property name '" + propertyName + "' is not a valid XML NCName (no colons, no spaces, must start with a letter or underscore).",
                    nameof(propertyName), ex);
            }
        }

        /// <summary>
        /// Validates <paramref name="propertyNamespace"/> and rejects the reserved DAV:
        /// namespace, which holds protected (live) properties per RFC 4918 §15. PROPPATCH
        /// would be rejected by the server; fail fast with a clearer error than a 403/409.
        /// </summary>
        public static void ValidatePropertyNamespace(string propertyNamespace)
        {
            if (string.IsNullOrWhiteSpace(propertyNamespace))
                throw new ArgumentException("Property namespace must be a non-empty string.", nameof(propertyNamespace));
            if (string.Equals(propertyNamespace, "DAV:", StringComparison.Ordinal))
                throw new ArgumentException(
                    "The DAV: namespace is reserved for protected (live) properties; SetProperty/RemoveProperty only support custom (dead) properties.",
                    nameof(propertyNamespace));
        }

        /// <summary>
        /// Builds a PROPPATCH request body for either <c>set</c> (when <paramref name="isRemove"/> is <c>false</c>)
        /// or <c>remove</c> (when <c>true</c>). The XML writer handles all character escaping
        /// for the value, so callers don't need to pre-escape.
        /// </summary>
        public static byte[] BuildPropPatchBody(string propertyName, string propertyNamespace, string value, bool isRemove)
        {
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
                    writer.WriteStartElement("D", "propertyupdate", "DAV:");
                    writer.WriteStartElement("D", isRemove ? "remove" : "set", "DAV:");
                    writer.WriteStartElement("D", "prop", "DAV:");
                    writer.WriteStartElement("X", propertyName, propertyNamespace);
                    if (!isRemove)
                    {
                        writer.WriteString(value ?? string.Empty);
                    }
                    writer.WriteEndElement(); // property
                    writer.WriteEndElement(); // prop
                    writer.WriteEndElement(); // set/remove
                    writer.WriteEndElement(); // propertyupdate
                    writer.WriteEndDocument();
                }
                return ms.ToArray();
            }
        }
    }
}
