using System;
using System.Collections.Generic;

namespace WebDAVClient.Model
{
    /// <summary>
    /// Result of an HTTP <c>OPTIONS</c> request against a WebDAV resource
    /// (RFC 4918 §9.1, RFC 9110 §9.3.7). Surfaces the two pieces of metadata
    /// callers typically want from <c>OPTIONS</c>:
    /// <list type="bullet">
    ///   <item><description>the WebDAV compliance classes the server reports
    ///   in the <c>DAV</c> response header (e.g. <c>"1, 2, 3"</c>); and</description></item>
    ///   <item><description>the HTTP / WebDAV methods the server reports in
    ///   the <c>Allow</c> response header.</description></item>
    /// </list>
    /// </summary>
    public sealed class ServerOptions
    {
        /// <summary>
        /// Compliance classes parsed from the <c>DAV</c> response header
        /// (e.g. <c>"1"</c>, <c>"2"</c>, <c>"3"</c>, <c>"access-control"</c>,
        /// <c>"calendar-access"</c>). Empty when the server didn't send a
        /// <c>DAV</c> header — that is the canonical signal the resource is
        /// not WebDAV-enabled.
        /// </summary>
        public IReadOnlyList<string> DavComplianceClasses { get; }

        /// <summary>
        /// Methods parsed from the <c>Allow</c> response header
        /// (e.g. <c>"OPTIONS"</c>, <c>"PROPFIND"</c>, <c>"PUT"</c>, <c>"LOCK"</c>).
        /// Empty when the server didn't send an <c>Allow</c> header.
        /// </summary>
        public IReadOnlyList<string> AllowedMethods { get; }

        /// <summary>
        /// The verbatim <c>DAV</c> response header value, or <c>null</c> when
        /// no <c>DAV</c> header was returned.
        /// </summary>
        public string RawDavHeader { get; }

        /// <summary>
        /// The verbatim <c>Allow</c> response header value, or <c>null</c>
        /// when no <c>Allow</c> header was returned.
        /// </summary>
        public string RawAllowHeader { get; }

        public ServerOptions(
            IReadOnlyList<string> davComplianceClasses,
            IReadOnlyList<string> allowedMethods,
            string rawDavHeader,
            string rawAllowHeader)
        {
            DavComplianceClasses = davComplianceClasses ?? Array.Empty<string>();
            AllowedMethods = allowedMethods ?? Array.Empty<string>();
            RawDavHeader = rawDavHeader;
            RawAllowHeader = rawAllowHeader;
        }

        /// <summary>
        /// True when the server is a WebDAV server at all — i.e. the response
        /// included a <c>DAV</c> header. Without this header, the resource is
        /// just a plain HTTP endpoint (RFC 4918 §10.1 / §18).
        /// </summary>
        public bool IsWebDavServer => DavComplianceClasses.Count > 0;

        /// <summary>
        /// Class 1 compliance — the baseline RFC 4918 PROPFIND / PROPPATCH /
        /// MKCOL / COPY / MOVE / DELETE / PUT / GET surface (§3.1).
        /// </summary>
        public bool IsClass1 => HasComplianceToken("1");

        /// <summary>
        /// Class 2 compliance — adds <c>LOCK</c> / <c>UNLOCK</c> support
        /// (RFC 4918 §3.2).
        /// </summary>
        public bool IsClass2 => HasComplianceToken("2");

        /// <summary>
        /// Class 3 compliance — revised RFC 4918 over RFC 2518 (§3.3).
        /// </summary>
        public bool IsClass3 => HasComplianceToken("3");

        /// <summary>
        /// Returns true when the server's <c>Allow</c> header lists the given
        /// HTTP method (case-insensitive).
        /// </summary>
        public bool SupportsMethod(string method)
        {
            if (string.IsNullOrEmpty(method)) return false;
            for (int i = 0; i < AllowedMethods.Count; i++)
            {
                if (string.Equals(AllowedMethods[i], method, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true when the server's <c>DAV</c> header lists the given
        /// compliance token (case-insensitive). Useful for non-numeric
        /// extensions like <c>access-control</c> or <c>calendar-access</c>.
        /// </summary>
        public bool HasComplianceToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            for (int i = 0; i < DavComplianceClasses.Count; i++)
            {
                if (string.Equals(DavComplianceClasses[i], token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
