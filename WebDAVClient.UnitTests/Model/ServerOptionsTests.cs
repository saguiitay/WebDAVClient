using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Model;

namespace WebDAVClient.UnitTests.Model
{
    [TestClass]
    public class ServerOptionsTests
    {
        [TestMethod]
        public void Missing_headers_and_lists_are_empty_capabilities_not_guessed_support()
        {
            var options = new ServerOptions(null, null, null, null);
            Assert.AreEqual(0, options.DavComplianceClasses.Count);
            Assert.AreEqual(0, options.AllowedMethods.Count);
            Assert.IsNull(options.RawDavHeader);
            Assert.IsNull(options.RawAllowHeader);
            Assert.IsFalse(options.IsWebDavServer || options.IsClass1 || options.IsClass2 || options.IsClass3);
            Assert.IsFalse(options.SupportsMethod(null));
            Assert.IsFalse(options.SupportsMethod(""));
            Assert.IsFalse(options.SupportsMethod("LOCK"));
            Assert.IsFalse(options.HasComplianceToken(null));
            Assert.IsFalse(options.HasComplianceToken(""));
            Assert.IsFalse(options.HasComplianceToken("1"));
        }

        [TestMethod]
        public void Parsed_capabilities_are_case_insensitive_and_raw_headers_are_preserved()
        {
            var options = new ServerOptions(new[] { "1", "2", "3", "calendar-access" },
                new[] { "PROPFIND", "LOCK" }, "1, 2, 3, calendar-access", "PROPFIND, LOCK");
            Assert.IsTrue(options.IsWebDavServer && options.IsClass1 && options.IsClass2 && options.IsClass3);
            Assert.IsTrue(options.SupportsMethod("lock"));
            Assert.IsTrue(options.HasComplianceToken("CALENDAR-ACCESS"));
            Assert.IsFalse(options.SupportsMethod("DELETE"));
            Assert.IsFalse(options.HasComplianceToken("unknown"));
            Assert.AreEqual("1, 2, 3, calendar-access", options.RawDavHeader);
            Assert.AreEqual("PROPFIND, LOCK", options.RawAllowHeader);
        }
    }
}
