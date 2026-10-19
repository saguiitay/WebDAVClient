using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;

namespace WebDAVClient.UnitTests.Helpers
{
    [TestClass]
    public class OptionsHeaderParserTests
    {
        [TestMethod]
        public void Absent_header_values_stay_absent_and_repeated_values_preserve_tokens_and_case()
        {
            Assert.IsNull(OptionsHeaderParser.Join(null));
            Assert.IsNull(OptionsHeaderParser.Join(Array.Empty<string>()));
            Assert.IsNull(OptionsHeaderParser.Join(new[] { "" }));
            Assert.AreEqual("1, 2, calendar-access", OptionsHeaderParser.Join(new[] { "1", "2, calendar-access" }));
            CollectionAssert.AreEqual(new[] { "1", "2", "calendar-access" },
                OptionsHeaderParser.Split(" 1, , 2,\tcalendar-access, "));
        }
    }
}
