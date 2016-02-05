using System;

namespace WebDAVClient.Helpers
{
    public class HttpException : Exception
    {
        private int _httpCode;

        public HttpException()
        {
        }

        public virtual int ErrorCode
        {
            get
            {
                return HResult;
            }
        }

        public int GetHttpCode()
        {
            return _httpCode;
        }

        public HttpException(string message)
            : base(message)
        { }

        public HttpException(string message, int hr)
            : base(message)
        {
            this.HResult = hr;
        }

        public HttpException(string message, Exception innerException)
            : base(message, innerException)
        { }

        public HttpException(int httpCode, string message, Exception innerException)
            : base(message, innerException)
        {
            this._httpCode = httpCode;
        }

        public HttpException(int httpCode, string message)
            : base(message)
        {
            this._httpCode = httpCode;
        }

        public HttpException(int httpCode, string message, int hr)
            : this(message, hr)
        {
            this._httpCode = httpCode;
        }
    }
}
