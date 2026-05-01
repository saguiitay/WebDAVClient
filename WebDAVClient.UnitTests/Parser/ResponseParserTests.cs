using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;

namespace WebDAVClient.UnitTests.Parser
{
    [TestClass]
    public class ResponseParserTests
    {
        private static Stream Xml(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

        [TestMethod]
        public void ParseItem_returns_first_item()
        {
            const string xml = @"<?xml version=""1.0""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response><D:href>/a.txt</D:href><D:propstat><D:prop><D:displayname>a</D:displayname></D:prop></D:propstat></D:response>
    <D:response><D:href>/b.txt</D:href><D:propstat><D:prop><D:displayname>b</D:displayname></D:prop></D:propstat></D:response>
</D:multistatus>";

            var item = ResponseParser.ParseItem(Xml(xml));

            Assert.IsNotNull(item);
            Assert.AreEqual("a", item.DisplayName);
        }

        [TestMethod]
        public void ParseItems_collection_via_resourcetype()
        {
            const string xml = @"<?xml version=""1.0""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>/folder/</D:href>
        <D:propstat><D:prop>
            <D:displayname>folder</D:displayname>
            <D:resourcetype><D:collection/></D:resourcetype>
        </D:prop></D:propstat>
    </D:response>
</D:multistatus>";

            var item = ResponseParser.ParseItems(Xml(xml)).Single();

            Assert.IsTrue(item.IsCollection);
            Assert.AreEqual("/folder/", item.Href, "Trailing slash should be preserved on collections");
            Assert.AreEqual("folder", item.DisplayName);
        }

        [TestMethod]
        public void ParseItems_strips_trailing_slash_for_files_only()
        {
            const string xml = @"<?xml version=""1.0""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>/file.txt/</D:href>
        <D:propstat><D:prop><D:displayname>file.txt</D:displayname></D:prop></D:propstat>
    </D:response>
</D:multistatus>";

            var item = ResponseParser.ParseItems(Xml(xml)).Single();

            Assert.IsFalse(item.IsCollection);
            Assert.AreEqual("/file.txt", item.Href);
        }

        [TestMethod]
        public void ParseItems_populates_all_known_properties()
        {
            const string xml = @"<?xml version=""1.0""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>/file.txt</D:href>
        <D:propstat><D:prop>
            <D:displayname>display</D:displayname>
            <D:creationdate>2025-01-02T03:04:05Z</D:creationdate>
            <D:getlastmodified>Wed, 01 Jan 2025 12:00:00 GMT</D:getlastmodified>
            <D:getcontentlength>1024</D:getcontentlength>
            <D:getcontenttype>application/json</D:getcontenttype>
            <D:getetag>""etag-1""</D:getetag>
            <D:ishidden>1</D:ishidden>
        </D:prop></D:propstat>
    </D:response>
</D:multistatus>";

            var item = ResponseParser.ParseItems(Xml(xml)).Single();

            Assert.AreEqual("display", item.DisplayName);
            Assert.AreEqual(1024, item.ContentLength);
            Assert.AreEqual("application/json", item.ContentType);
            Assert.AreEqual("\"etag-1\"", item.Etag);
            Assert.IsTrue(item.IsHidden);
            Assert.IsNotNull(item.CreationDate);
            Assert.IsNotNull(item.LastModified);
        }

        [TestMethod]
        public void ParseItems_iscollection_via_int_value_one()
        {
            const string xml = @"<?xml version=""1.0""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>/folder/</D:href>
        <D:propstat><D:prop>
            <D:displayname>folder</D:displayname>
            <D:iscollection>1</D:iscollection>
        </D:prop></D:propstat>
    </D:response>
</D:multistatus>";

            var item = ResponseParser.ParseItems(Xml(xml)).Single();

            Assert.IsTrue(item.IsCollection);
        }

        [TestMethod]
        public void ParseItems_iscollection_via_bool_value()
        {
            const string xml = @"<?xml version=""1.0""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>/folder/</D:href>
        <D:propstat><D:prop>
            <D:displayname>folder</D:displayname>
            <D:iscollection>true</D:iscollection>
        </D:prop></D:propstat>
    </D:response>
</D:multistatus>";

            var item = ResponseParser.ParseItems(Xml(xml)).Single();

            Assert.IsTrue(item.IsCollection);
        }

        [TestMethod]
        public void ParseItems_default_displayname_derived_from_href_url_decoded()
        {
            const string xml = @"<?xml version=""1.0""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>/folder/some%20file.txt</D:href>
        <D:propstat><D:prop><D:getcontentlength>1</D:getcontentlength></D:prop></D:propstat>
    </D:response>
</D:multistatus>";

            var item = ResponseParser.ParseItems(Xml(xml)).Single();

            Assert.AreEqual("some file.txt", item.DisplayName);
        }

        [TestMethod]
        public void ParseItems_replaces_hash_in_href_with_percent23()
        {
            const string xml = @"<?xml version=""1.0""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>/path/file#1.txt</D:href>
        <D:propstat><D:prop><D:displayname>x</D:displayname></D:prop></D:propstat>
    </D:response>
</D:multistatus>";

            var item = ResponseParser.ParseItems(Xml(xml)).Single();

            Assert.AreEqual("/path/file%231.txt", item.Href);
        }

        [TestMethod]
        public void ParseItems_does_not_crash_on_special_props_that_must_be_skipped()
        {
            // The parser explicitly Skip()s checked-in / version-controlled-configuration
            // because their inner <href> elements would otherwise be (mis)read as the
            // response href. We only assert that parsing completes and we get one item back —
            // the precise siblings the parser may consume after Skip() are an implementation
            // detail of XmlReader semantics we don't want to over-specify.
            const string xml = @"<?xml version=""1.0""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>/f.txt</D:href>
        <D:propstat><D:prop>
            <D:checked-in><D:href>/old</D:href></D:checked-in>
            <D:version-controlled-configuration><D:href>/cfg</D:href></D:version-controlled-configuration>
        </D:prop></D:propstat>
    </D:response>
</D:multistatus>";

            var items = ResponseParser.ParseItems(Xml(xml));

            Assert.AreEqual(1, items.Count);
        }

        [TestMethod]
        public void ParseItems_invalid_dates_and_lengths_remain_null()
        {
            const string xml = @"<?xml version=""1.0""?>
<D:multistatus xmlns:D=""DAV:"">
    <D:response>
        <D:href>/f.txt</D:href>
        <D:propstat><D:prop>
            <D:displayname>f</D:displayname>
            <D:creationdate>not-a-date</D:creationdate>
            <D:getlastmodified>also-not-a-date</D:getlastmodified>
            <D:getcontentlength>NaN</D:getcontentlength>
        </D:prop></D:propstat>
    </D:response>
</D:multistatus>";

            var item = ResponseParser.ParseItems(Xml(xml)).Single();

            Assert.IsNull(item.CreationDate);
            Assert.IsNull(item.LastModified);
            Assert.IsNull(item.ContentLength);
        }

        [TestMethod]
        public void ParseItems_empty_multistatus_returns_empty_list()
        {
            const string xml = @"<?xml version=""1.0""?><D:multistatus xmlns:D=""DAV:""></D:multistatus>";

            var items = ResponseParser.ParseItems(Xml(xml));

            Assert.IsNotNull(items);
            Assert.AreEqual(0, items.Count);
        }
    }
}
