using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;

namespace WebDAVClient.UnitTests.Helpers
{
    [TestClass]
    public class WebDAVConflictExceptionTests
    {
        [TestMethod]
        public void Is_a_WebDAVException()
        {
            var ex = new WebDAVConflictException();
            Assert.IsInstanceOfType(ex, typeof(WebDAVException));
        }

        [TestMethod]
        public void Constructors_propagate_their_fields()
        {
            var inner = new InvalidOperationException();

            Assert.AreEqual("m", new WebDAVConflictException("m").Message);

            var withHr = new WebDAVConflictException("m", 5);
            Assert.AreEqual("m", withHr.Message);
            Assert.AreEqual(5, withHr.ErrorCode);

            var withInner = new WebDAVConflictException("m", inner);
            Assert.AreEqual("m", withInner.Message);
            Assert.AreSame(inner, withInner.InnerException);

            var withHttpAndInner = new WebDAVConflictException(409, "m", inner);
            Assert.AreEqual(409, withHttpAndInner.GetHttpCode());
            Assert.AreSame(inner, withHttpAndInner.InnerException);

            var withHttp = new WebDAVConflictException(409, "m");
            Assert.AreEqual(409, withHttp.GetHttpCode());

            var full = new WebDAVConflictException(409, "m", 7);
            Assert.AreEqual(409, full.GetHttpCode());
            Assert.AreEqual(7, full.ErrorCode);
        }
    }
}
