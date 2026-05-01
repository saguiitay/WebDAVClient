using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;

namespace WebDAVClient.UnitTests.Helpers
{
    [TestClass]
    public class PropPatchRequestBuilderTests
    {
        // -------------------- ValidatePropertyName --------------------

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void ValidatePropertyName_throws_on_null_or_whitespace(string input)
        {
            Assert.ThrowsException<ArgumentException>(() => PropPatchRequestBuilder.ValidatePropertyName(input));
        }

        [DataTestMethod]
        [DataRow("bad name")]      // space
        [DataRow("bad:name")]      // colon (would conflict with namespace prefix)
        [DataRow("1starts-with-digit")]
        [DataRow("-starts-with-dash")]
        public void ValidatePropertyName_throws_on_invalid_NCName(string input)
        {
            Assert.ThrowsException<ArgumentException>(() => PropPatchRequestBuilder.ValidatePropertyName(input));
        }

        [DataTestMethod]
        [DataRow("author")]
        [DataRow("_underscore")]
        [DataRow("with-dashes-and-123")]
        public void ValidatePropertyName_accepts_valid_NCNames(string input)
        {
            PropPatchRequestBuilder.ValidatePropertyName(input);
        }

        // -------------------- ValidatePropertyNamespace --------------------

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void ValidatePropertyNamespace_throws_on_null_or_whitespace(string input)
        {
            Assert.ThrowsException<ArgumentException>(() => PropPatchRequestBuilder.ValidatePropertyNamespace(input));
        }

        [TestMethod]
        public void ValidatePropertyNamespace_rejects_DAV_namespace()
        {
            // RFC 4918 §15: DAV: holds protected (live) properties; PROPPATCH would be rejected
            // by the server. Failing here gives a clearer error than a 403/409 from the wire.
            var ex = Assert.ThrowsException<ArgumentException>(
                () => PropPatchRequestBuilder.ValidatePropertyNamespace("DAV:"));
            StringAssert.Contains(ex.Message, "DAV:");
        }

        [TestMethod]
        public void ValidatePropertyNamespace_accepts_custom_namespace()
        {
            PropPatchRequestBuilder.ValidatePropertyNamespace("http://example.com/ns/");
        }

        // -------------------- BuildPropPatchBody --------------------

        [TestMethod]
        public void BuildPropPatchBody_emits_set_with_value_when_not_remove()
        {
            var bytes = PropPatchRequestBuilder.BuildPropPatchBody(
                "author", "http://example.com/ns/", "Alice", isRemove: false);
            var xml = Encoding.UTF8.GetString(bytes);
            StringAssert.Contains(xml, "<D:propertyupdate");
            StringAssert.Contains(xml, "<D:set");
            StringAssert.Contains(xml, "<D:prop");
            // Property is emitted with prefix X bound to the custom namespace.
            StringAssert.Contains(xml, "xmlns:X=\"http://example.com/ns/\"");
            StringAssert.Contains(xml, "<X:author");
            StringAssert.Contains(xml, ">Alice</X:author>");
        }

        [TestMethod]
        public void BuildPropPatchBody_omits_value_text_when_remove()
        {
            var bytes = PropPatchRequestBuilder.BuildPropPatchBody(
                "author", "http://example.com/ns/", value: "ignored", isRemove: true);
            var xml = Encoding.UTF8.GetString(bytes);
            StringAssert.Contains(xml, "<D:remove");
            // Even when the caller passes a value, remove must not include any text content.
            Assert.IsFalse(xml.Contains("ignored"), "remove must not include any value text");
        }

        [TestMethod]
        public void BuildPropPatchBody_escapes_special_characters_in_value()
        {
            // XmlWriter.WriteString handles entity escaping, so callers can pass raw text.
            var bytes = PropPatchRequestBuilder.BuildPropPatchBody(
                "tag", "http://example.com/ns/", "<a> & \"b\"", isRemove: false);
            var xml = Encoding.UTF8.GetString(bytes);
            StringAssert.Contains(xml, "&lt;a&gt; &amp;");
        }

        [TestMethod]
        public void BuildPropPatchBody_emits_utf8_without_bom()
        {
            // BOM in the request body would corrupt the XML declaration on strict parsers.
            var bytes = PropPatchRequestBuilder.BuildPropPatchBody(
                "x", "http://example.com/ns/", "v", isRemove: false);
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "PROPPATCH body should not start with a UTF-8 BOM.");
        }

        [TestMethod]
        public void BuildPropPatchBody_treats_null_value_as_empty_string_for_set()
        {
            var bytes = PropPatchRequestBuilder.BuildPropPatchBody(
                "x", "http://example.com/ns/", value: null, isRemove: false);
            var xml = Encoding.UTF8.GetString(bytes);
            // Self-closing element or open/close pair with no text content.
            Assert.IsTrue(xml.Contains("<X:x") && (xml.Contains("/>") || xml.Contains("></X:x>")));
        }
    }
}
