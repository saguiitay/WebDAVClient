namespace WebDAVClient.Model
{
    /// <summary>
    /// One server-reported resource outcome in a DELETE, MOVE, or COPY multi-status response.
    /// </summary>
    public sealed class ResourceStatus
    {
        internal ResourceStatus(string href, int statusCode, string responseDescription)
        {
            Href = href;
            StatusCode = statusCode;
            ResponseDescription = responseDescription;
        }

        /// <summary>The resource identifier reported by the server. Informational only; not an instruction to follow this URL.</summary>
        public string Href { get; }

        /// <summary>The resource-level HTTP status code, including successful members of a partial result.</summary>
        public int StatusCode { get; }

        /// <summary>The optional server description, or null if absent.</summary>
        public string ResponseDescription { get; }
    }
}
