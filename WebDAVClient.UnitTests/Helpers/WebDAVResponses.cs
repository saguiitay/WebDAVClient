namespace WebDAVClient.UnitTests.Helpers
{
    /// <summary>
    /// Centralized WebDAV multistatus XML payloads used across tests. Keeping them in one
    /// place keeps the actual test cases small and focused on assertions.
    /// </summary>
    internal static class WebDAVResponses
    {
        // Root collection only — used to satisfy the very first PROPFIND that Client issues to
        // resolve m_encodedBasePath via GetServerUrl.
        public static string Root(string href = "/webdav/") =>
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>{href}</D:href>
        <D:propstat>
            <D:prop>
                <D:displayname>root</D:displayname>
                <D:resourcetype><D:collection/></D:resourcetype>
            </D:prop>
            <D:status>HTTP/1.1 200 OK</D:status>
        </D:propstat>
    </D:response>
</D:multistatus>";

        // A list response containing the parent folder + 1 file + 1 sub-collection.
        public static string FolderListing(string parentHref = "/webdav/", string fileHref = "/webdav/file.txt", string subHref = "/webdav/sub/") =>
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>{parentHref}</D:href>
        <D:propstat>
            <D:prop>
                <D:displayname>parent</D:displayname>
                <D:resourcetype><D:collection/></D:resourcetype>
                <D:getlastmodified>Wed, 01 Jan 2025 12:00:00 GMT</D:getlastmodified>
                <D:creationdate>2025-01-01T12:00:00Z</D:creationdate>
            </D:prop>
            <D:status>HTTP/1.1 200 OK</D:status>
        </D:propstat>
    </D:response>
    <D:response>
        <D:href>{fileHref}</D:href>
        <D:propstat>
            <D:prop>
                <D:displayname>file.txt</D:displayname>
                <D:getcontentlength>42</D:getcontentlength>
                <D:getcontenttype>text/plain</D:getcontenttype>
                <D:getetag>""abc123""</D:getetag>
                <D:getlastmodified>Wed, 01 Jan 2025 12:00:00 GMT</D:getlastmodified>
            </D:prop>
            <D:status>HTTP/1.1 200 OK</D:status>
        </D:propstat>
    </D:response>
    <D:response>
        <D:href>{subHref}</D:href>
        <D:propstat>
            <D:prop>
                <D:displayname>sub</D:displayname>
                <D:resourcetype><D:collection/></D:resourcetype>
            </D:prop>
            <D:status>HTTP/1.1 200 OK</D:status>
        </D:propstat>
    </D:response>
</D:multistatus>";

        public static string SingleFile(string href = "/webdav/file.txt") =>
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>{href}</D:href>
        <D:propstat>
            <D:prop>
                <D:displayname>file.txt</D:displayname>
                <D:getcontentlength>10</D:getcontentlength>
                <D:getcontenttype>text/plain</D:getcontenttype>
            </D:prop>
            <D:status>HTTP/1.1 200 OK</D:status>
        </D:propstat>
    </D:response>
</D:multistatus>";

        public static string SingleFolder(string href = "/webdav/folder/") =>
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>{href}</D:href>
        <D:propstat>
            <D:prop>
                <D:displayname>folder</D:displayname>
                <D:resourcetype><D:collection/></D:resourcetype>
            </D:prop>
            <D:status>HTTP/1.1 200 OK</D:status>
        </D:propstat>
    </D:response>
</D:multistatus>";

        // RFC 4918 §9.10.7-style LOCK response body. Includes a single <D:activelock> with all
        // typical fields populated so the parser tests can assert against every one of them.
        public static string LockResponse(
            string token = "opaquelocktoken:e71d4fae-5dec-22d6-fea5-00a0c91e6be4",
            string lockRoot = "/webdav/file.txt",
            string depth = "0",
            string scope = "exclusive",
            string ownerInnerXml = "<D:href>http://example.org/~ejw/contact.html</D:href>",
            string timeout = "Second-604800") =>
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
<D:prop xmlns:D=""DAV:"">
    <D:lockdiscovery>
        <D:activelock>
            <D:locktype><D:write/></D:locktype>
            <D:lockscope><D:{scope}/></D:lockscope>
            <D:depth>{depth}</D:depth>
            <D:owner>{ownerInnerXml}</D:owner>
            <D:timeout>{timeout}</D:timeout>
            <D:locktoken><D:href>{token}</D:href></D:locktoken>
            <D:lockroot><D:href>{lockRoot}</D:href></D:lockroot>
        </D:activelock>
    </D:lockdiscovery>
</D:prop>";

        // RFC 4918 §9.2.1-style PROPPATCH success body. A multistatus with a single response and
        // a single propstat carrying the requested property and a 200 OK status.
        public static string PropPatchSuccess(
            string href = "/webdav/file.txt",
            string propertyXml = @"<Z:author xmlns:Z=""http://example.com/ns""/>",
            string status = "HTTP/1.1 200 OK") =>
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>{href}</D:href>
        <D:propstat>
            <D:prop>{propertyXml}</D:prop>
            <D:status>{status}</D:status>
        </D:propstat>
    </D:response>
</D:multistatus>";

        // PROPPATCH failure with a non-2xx propstat status (e.g. 403, 409, 424 per RFC 4918 §11.4).
        public static string PropPatchFailure(
            string href = "/webdav/file.txt",
            string propertyXml = @"<Z:author xmlns:Z=""http://example.com/ns""/>",
            string status = "HTTP/1.1 403 Forbidden",
            string responseDescription = null) =>
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>{href}</D:href>
        <D:propstat>
            <D:prop>{propertyXml}</D:prop>
            <D:status>{status}</D:status>
            {(responseDescription != null ? $"<D:responsedescription>{responseDescription}</D:responsedescription>" : string.Empty)}
        </D:propstat>
    </D:response>
</D:multistatus>";
    }
}
