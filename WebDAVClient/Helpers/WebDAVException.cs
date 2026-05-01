using System;

namespace WebDAVClient.Helpers
{
    /// <summary>
    /// Exception thrown by <see cref="IClient"/> operations when a WebDAV server
    /// returns a non-success HTTP status (or when an underlying HTTP / parsing
    /// error occurs). The HTTP status code is available via
    /// <see cref="GetHttpCode"/>; 409 (Conflict) responses are surfaced as the
    /// more specific <see cref="WebDAVConflictException"/>.
    /// </summary>
    public class WebDAVException : Exception
    {
        private readonly int m_httpCode;

        /// <summary>
        /// Optional implementation-defined error code (HRESULT-style).
        /// <c>0</c> when not set.
        /// </summary>
        public int ErrorCode { get; }

        /// <summary>
        /// Initialises a new <see cref="WebDAVException"/> with no message.
        /// </summary>
        public WebDAVException()
        {
        }

        /// <summary>
        /// Initialises a new <see cref="WebDAVException"/> with the specified
        /// error message.
        /// </summary>
        /// <param name="message">A description of the error.</param>
        public WebDAVException(string message)
            : base(message)
        {}

        /// <summary>
        /// Initialises a new <see cref="WebDAVException"/> with the specified
        /// error message and an implementation-defined error code.
        /// </summary>
        /// <param name="message">A description of the error.</param>
        /// <param name="hr">An implementation-defined error code, exposed via <see cref="ErrorCode"/>.</param>
        public WebDAVException(string message, int hr)
            : base(message)
        {
            ErrorCode = hr;
        }

        /// <summary>
        /// Initialises a new <see cref="WebDAVException"/> with the specified
        /// error message and inner exception.
        /// </summary>
        /// <param name="message">A description of the error.</param>
        /// <param name="innerException">The exception that caused this error, or <c>null</c>.</param>
        public WebDAVException(string message, Exception innerException)
            : base(message, innerException)
        {}

        /// <summary>
        /// Initialises a new <see cref="WebDAVException"/> with the HTTP status
        /// code returned by the server, an error message, and an inner
        /// exception.
        /// </summary>
        /// <param name="httpCode">The HTTP status code returned by the server, exposed via <see cref="GetHttpCode"/>.</param>
        /// <param name="message">A description of the error.</param>
        /// <param name="innerException">The exception that caused this error, or <c>null</c>.</param>
        public WebDAVException(int httpCode, string message, Exception innerException)
            : base(message, innerException)
        {
            m_httpCode = httpCode;
        }

        /// <summary>
        /// Initialises a new <see cref="WebDAVException"/> with the HTTP status
        /// code returned by the server and an error message.
        /// </summary>
        /// <param name="httpCode">The HTTP status code returned by the server, exposed via <see cref="GetHttpCode"/>.</param>
        /// <param name="message">A description of the error.</param>
        public WebDAVException(int httpCode, string message)
            : base(message)
        {
            m_httpCode = httpCode;
        }

        /// <summary>
        /// Initialises a new <see cref="WebDAVException"/> with the HTTP status
        /// code returned by the server, an error message, and an
        /// implementation-defined error code.
        /// </summary>
        /// <param name="httpCode">The HTTP status code returned by the server, exposed via <see cref="GetHttpCode"/>.</param>
        /// <param name="message">A description of the error.</param>
        /// <param name="hr">An implementation-defined error code, exposed via <see cref="ErrorCode"/>.</param>
        public WebDAVException(int httpCode, string message, int hr)
            : base(message)
        {
            m_httpCode = httpCode;
            ErrorCode = hr;
        }

        /// <summary>
        /// Returns the HTTP status code returned by the server, or <c>0</c>
        /// when the exception was not constructed with one.
        /// </summary>
        public int GetHttpCode()
        {
            return m_httpCode;
        }

        /// <summary>
        /// Returns a string that combines <see cref="GetHttpCode"/>,
        /// <see cref="ErrorCode"/>, the message, and the base
        /// <see cref="Exception.ToString"/> output.
        /// </summary>
        public override string ToString()
        {
            var s = string.Format("HttpStatusCode: {0}", GetHttpCode());
            s += Environment.NewLine + string.Format("ErrorCode: {0}", ErrorCode);
            s += Environment.NewLine + string.Format("Message: {0}", Message);
            s += Environment.NewLine + base.ToString();

            return s;
        }
    }
}