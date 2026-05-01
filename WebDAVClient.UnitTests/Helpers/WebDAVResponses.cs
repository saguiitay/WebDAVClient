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
    }
}
