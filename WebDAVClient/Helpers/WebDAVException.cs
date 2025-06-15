using System;

namespace WebDAVClient.Helpers
{
    public class WebDAVException : Exception
    {
        private readonly int m_httpCode;

        public int ErrorCode { get; }

        public WebDAVException()
        {
        }

        public WebDAVException(string message)
            : base(message)
        {}

        public WebDAVException(string message, int hr)
            : base(message)
        {
            ErrorCode = hr;
        }

        public WebDAVException(string message, Exception innerException)
            : base(message, innerException)
        {}

        public WebDAVException(int httpCode, string message, Exception innerException)
            : base(message, innerException)
        {
            m_httpCode = httpCode;
        }

        public WebDAVException(int httpCode, string message)
            : base(message)
        {
            m_httpCode = httpCode;
        }

        public WebDAVException(int httpCode, string message, int hr)
            : base(message)
        {
            m_httpCode = httpCode;
            ErrorCode = hr;
        }

        public int GetHttpCode()
        {
            return m_httpCode;
        }

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