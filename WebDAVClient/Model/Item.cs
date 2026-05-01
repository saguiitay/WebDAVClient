using System;

namespace WebDAVClient.Model
{
    /// <summary>
    /// Represents a single WebDAV resource (file or collection) returned by a
    /// <c>PROPFIND</c> response. Instances are produced by
    /// <see cref="WebDAVClient.Helpers.ResponseParser"/> and returned from
    /// <see cref="IClient.List"/>, <see cref="IClient.GetFolder"/>, and
    /// <see cref="IClient.GetFile"/>.
    /// </summary>
    public class Item
    {
        /// <summary>
        /// The <c>&lt;D:href&gt;</c> value reported by the server for this resource.
        /// Per RFC 4918 this may be either an absolute URL (for example
        /// <c>https://server/path/file.txt</c>) or an absolute path on the server
        /// (for example <c>/path/file.txt</c>) — the form is server-controlled and
        /// is preserved as received, with one normalisation: trailing <c>/</c>
        /// characters are stripped for non-collection items so that file hrefs
        /// never end with a slash. Collection (folder) hrefs always end with a
        /// trailing <c>/</c>; this distinction is load-bearing — see
        /// <see cref="IsCollection"/>.
        /// </summary>
        public string Href { get; set; }

        /// <summary>
        /// The <c>&lt;D:creationdate&gt;</c> value reported by the server, parsed
        /// as a <see cref="DateTime"/>. <c>null</c> when the server did not
        /// return the property or when the value could not be parsed.
        /// </summary>
        public DateTime? CreationDate { get; set; }

        /// <summary>
        /// The opaque entity tag (<c>&lt;D:getetag&gt;</c>) reported by the
        /// server. Useful for conditional requests and change detection.
        /// <c>null</c> when the server did not return the property.
        /// </summary>
        public string Etag { get; set; }

        /// <summary>
        /// <c>true</c> when the server marks this resource as hidden (via the
        /// non-standard but widely used <c>&lt;D:ishidden&gt;</c> property).
        /// </summary>
        public bool IsHidden { get; set; }

        /// <summary>
        /// <c>true</c> when this item is a collection (folder), <c>false</c>
        /// when it is a file. Determined from the resource type
        /// (<c>&lt;D:resourcetype&gt;&lt;D:collection/&gt;</c>) or, as a
        /// fallback, the <c>&lt;D:iscollection&gt;</c> property. Collection
        /// items also have a trailing <c>/</c> on <see cref="Href"/>.
        /// </summary>
        public bool IsCollection { get; set; }

        /// <summary>
        /// The MIME type reported by the server in <c>&lt;D:getcontenttype&gt;</c>
        /// (for example <c>text/plain</c>). Typically <c>null</c> for
        /// collections.
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// The <c>&lt;D:getlastmodified&gt;</c> value reported by the server,
        /// parsed as a <see cref="DateTime"/>. <c>null</c> when the server did
        /// not return the property or when the value could not be parsed.
        /// </summary>
        public DateTime? LastModified { get; set; }

        /// <summary>
        /// The human-readable name reported by the server in
        /// <c>&lt;D:displayname&gt;</c>. When the server omits this property
        /// the parser falls back to the URL-decoded final segment of
        /// <see cref="Href"/>, so <see cref="DisplayName"/> is normally a
        /// usable name even when the server is sparse.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// The size of the resource in bytes, as reported by the server in
        /// <c>&lt;D:getcontentlength&gt;</c>. <c>null</c> when the server did
        /// not return the property or when the value could not be parsed —
        /// notably, collections typically do not report a content length.
        /// </summary>
        public long? ContentLength { get; set; }
    }
}
