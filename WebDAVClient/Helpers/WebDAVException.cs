using System;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Web;

namespace WebDAVClient.Helpers
{
    public class WebDAVException : Exception
    {

        public string Message { get; private set; }
        public int HR { get; set; }
        public int HttpCode { get; set; }

        public WebDAVException()
        {
        }

        public WebDAVException(string message)
        : base(message)
        {
        }

        public WebDAVException(string message, int hr)
        : base(message)
        {
            HR = hr;
        }

        public WebDAVException(string message, Exception innerException) 
        : base (message, innerException)
        {}

        public WebDAVException(int httpCode, string message, Exception innerException)
            : base(message, innerException)
        {
            HttpCode = httpCode;
        }

        public WebDAVException(int httpCode, string message)
            : base(message)
        {
            HttpCode = httpCode;
        }

        public WebDAVException(int httpCode, string message, int hr)
            : base(message)
        {
            HttpCode = httpCode;
            HR = hr;
        }

        protected WebDAVException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {}

        public override string ToString()
        {
            var s = string.Format("HttpStatusCode: {0}", HttpCode);
            s += Environment.NewLine + string.Format("ErrorCode: {0}", HR);
            s += Environment.NewLine + string.Format("Message: {0}", Message);
            s += Environment.NewLine + base.ToString();

            return s;
        }
    }
}