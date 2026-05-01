using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;

namespace WebDAVClient.UnitTests.Helpers
{
    [TestClass]
    public class XmlEscapeTests
    {
        [DataTestMethod]
        [DataRow(null, null)]
        [DataRow("", "")]
        [DataRow("plain text", "plain text")]
        [DataRow("&", "&amp;")]
        [DataRow("<", "&lt;")]
        [DataRow(">", "&gt;")]
        [DataRow("\"", "&quot;")]
        [DataRow("'", "&apos;")]
        public void Escape_handles_individual_characters(string input, string expected)
        {
            Assert.AreEqual(expected, XmlEscape.Escape(input));
        }

        [TestMethod]
        public void Escape_handles_all_five_predefined_entities_together()
        {
            Assert.AreEqual("&amp;&lt;&gt;&quot;&apos;", XmlEscape.Escape("&<>\"'"));
        }

        [TestMethod]
        public void Escape_replaces_ampersand_first_so_existing_entities_are_double_escaped()
        {
            // Important behaviour: &amp; in input becomes &amp;amp; — callers must pass
            // raw, unescaped text. Verifying this so the contract isn't accidentally inverted.
            Assert.AreEqual("&amp;amp;", XmlEscape.Escape("&amp;"));
        }
    }
}
