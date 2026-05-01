using System;

namespace WebDAVClient.Helpers
{
    /// <summary>
    /// Specialised <see cref="WebDAVException"/> raised when a WebDAV server
    /// returns HTTP <c>409 Conflict</c>. Typically signals that a parent
    /// resource is missing (for example creating a directory whose parent
    /// does not exist) or that the operation conflicts with the current
    /// state of the resource.
    /// </summary>
    public class WebDAVConflictException : WebDAVException
    {
        /// <summary>
        /// Initialises a new <see cref="WebDAVConflictException"/> with no message.
        /// </summary>
        public WebDAVConflictException()
        {
        }

        /// <summary>
        /// Initialises a new <see cref="WebDAVConflictException"/> with the specified error message.
        /// </summary>
        /// <param name="message">A description of the error.</param>
        public WebDAVConflictException(string message) 
            : base(message)
        {}

        /// <summary>
        /// Initialises a new <see cref="WebDAVConflictException"/> with the specified error message and an implementation-defined error code.
        /// </summary>
        /// <param name="message">A description of the error.</param>
        /// <param name="hr">An implementation-defined error code, exposed via <see cref="WebDAVException.ErrorCode"/>.</param>
        public WebDAVConflictException(string message, int hr) 
            : base(message, hr)
        {}

        /// <summary>
        /// Initialises a new <see cref="WebDAVConflictException"/> with the specified error message and inner exception.
        /// </summary>
        /// <param name="message">A description of the error.</param>
        /// <param name="innerException">The exception that caused this error, or <c>null</c>.</param>
        public WebDAVConflictException(string message, Exception innerException) 
            : base(message, innerException)
        {}

        /// <summary>
        /// Initialises a new <see cref="WebDAVConflictException"/> with the HTTP status code returned by the server, an error message, and an inner exception.
        /// </summary>
        /// <param name="httpCode">The HTTP status code returned by the server (typically 409), exposed via <see cref="WebDAVException.GetHttpCode"/>.</param>
        /// <param name="message">A description of the error.</param>
        /// <param name="innerException">The exception that caused this error, or <c>null</c>.</param>
        public WebDAVConflictException(int httpCode, string message, Exception innerException) 
            : base(httpCode, message, innerException)
        {}

        /// <summary>
        /// Initialises a new <see cref="WebDAVConflictException"/> with the HTTP status code returned by the server and an error message.
        /// </summary>
        /// <param name="httpCode">The HTTP status code returned by the server (typically 409), exposed via <see cref="WebDAVException.GetHttpCode"/>.</param>
        /// <param name="message">A description of the error.</param>
        public WebDAVConflictException(int httpCode, string message) 
            : base(httpCode, message)
        {}

        /// <summary>
        /// Initialises a new <see cref="WebDAVConflictException"/> with the HTTP status code returned by the server, an error message, and an implementation-defined error code.
        /// </summary>
        /// <param name="httpCode">The HTTP status code returned by the server (typically 409), exposed via <see cref="WebDAVException.GetHttpCode"/>.</param>
        /// <param name="message">A description of the error.</param>
        /// <param name="hr">An implementation-defined error code, exposed via <see cref="WebDAVException.ErrorCode"/>.</param>
        public WebDAVConflictException(int httpCode, string message, int hr) 
            : base(httpCode, message, hr)
        {}
    }
}