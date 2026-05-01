using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;

namespace WebDAVClient.UnitTests.Helpers
{
    [TestClass]
    public class WebDAVExceptionTests
    {
        [TestMethod]
        public void Default_constructor_yields_zero_codes_and_no_message()
        {
            var ex = new WebDAVException();
            Assert.AreEqual(0, ex.GetHttpCode());
            Assert.AreEqual(0, ex.ErrorCode);
            // Default Exception.Message is non-null but unspecified text — we only check it's not empty XML.
            Assert.IsNotNull(ex.Message);
        }

        [TestMethod]
        public void Message_only_constructor_preserves_message()
        {
            var ex = new WebDAVException("boom");
            Assert.AreEqual("boom", ex.Message);
            Assert.AreEqual(0, ex.GetHttpCode());
            Assert.AreEqual(0, ex.ErrorCode);
        }

        [TestMethod]
        public void Message_and_hr_populates_ErrorCode()
        {
            var ex = new WebDAVException("boom", 42);
            Assert.AreEqual("boom", ex.Message);
            Assert.AreEqual(42, ex.ErrorCode);
            Assert.AreEqual(0, ex.GetHttpCode());
        }

        [TestMethod]
        public void Message_with_inner_exception_preserves_inner()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new WebDAVException("outer", inner);
            Assert.AreEqual("outer", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        [TestMethod]
        public void HttpCode_constructor_populates_code()
        {
            var ex = new WebDAVException(404, "missing");
            Assert.AreEqual(404, ex.GetHttpCode());
            Assert.AreEqual("missing", ex.Message);
            Assert.AreEqual(0, ex.ErrorCode);
        }

        [TestMethod]
        public void HttpCode_with_inner_exception_preserves_both()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new WebDAVException(500, "boom", inner);
            Assert.AreEqual(500, ex.GetHttpCode());
            Assert.AreEqual("boom", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        [TestMethod]
        public void HttpCode_message_and_hr_populates_all()
        {
            var ex = new WebDAVException(409, "conflict", 7);
            Assert.AreEqual(409, ex.GetHttpCode());
            Assert.AreEqual("conflict", ex.Message);
            Assert.AreEqual(7, ex.ErrorCode);
        }

        [TestMethod]
        public void ToString_includes_status_error_and_message()
        {
            var ex = new WebDAVException(404, "missing");
            var text = ex.ToString();
            StringAssert.Contains(text, "HttpStatusCode: 404");
            StringAssert.Contains(text, "ErrorCode: 0");
            StringAssert.Contains(text, "Message: missing");
        }
    }
}
