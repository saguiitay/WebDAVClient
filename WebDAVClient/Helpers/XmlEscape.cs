namespace WebDAVClient.Helpers
{
    /// <summary>
    /// Minimal XML text-content escaping for the five predefined entities. Used when
    /// callers' free-form text (e.g. the LOCK request <c>&lt;owner&gt;</c> body) is interpolated
    /// into a hand-written XML template rather than emitted via <see cref="System.Xml.XmlWriter"/>.
    /// </summary>
    internal static class XmlEscape
    {
        /// <summary>
        /// Returns <paramref name="value"/> with the five predefined XML entities escaped.
        /// <c>null</c> and empty strings are returned unchanged.
        /// </summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
