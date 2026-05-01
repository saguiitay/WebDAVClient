using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.Model;

namespace WebDAVClient.UnitTests.Helpers
{
    /// <summary>
    /// Unit tests for the three RFC 4918 §9.1 PROPFIND body forms produced by
    /// <see cref="PropFindRequestBuilder"/>: cached <c>&lt;allprop/&gt;</c> and
    /// <c>&lt;propname/&gt;</c> bytes plus on-demand <c>&lt;prop&gt;</c> bodies
    /// with arbitrary namespaces.
    /// </summary>
    [TestClass]
    public class PropFindRequestBuilderTests
    {
        // -------------------- AllProp --------------------

        [TestMethod]
        public void BuildAllPropBody_returns_allprop_payload_in_DAV_namespace()
        {
            var bytes = PropFindRequestBuilder.BuildAllPropBody();
            var xml = Encoding.UTF8.GetString(bytes);

            var doc = XDocument.Parse(xml);
            XNamespace dav = "DAV:";
            Assert.AreEqual(dav + "propfind", doc.Root.Name);
            Assert.IsNotNull(doc.Root.Element(dav + "allprop"));
            Assert.IsNull(doc.Root.Element(dav + "prop"));
            Assert.IsNull(doc.Root.Element(dav + "propname"));
        }

        [TestMethod]
        public void BuildAllPropBody_returns_cached_byte_array_so_repeated_calls_share_buffer()
        {
            // The cached buffer is part of the documented allocation contract that the
            // legacy s_propFindRequestContentBytes used to provide; protect that.
            var a = PropFindRequestBuilder.BuildAllPropBody();
            var b = PropFindRequestBuilder.BuildAllPropBody();
            Assert.AreSame(a, b);
        }

        // -------------------- PropName --------------------

        [TestMethod]
        public void BuildPropNameBody_returns_propname_payload_in_DAV_namespace()
        {
            var bytes = PropFindRequestBuilder.BuildPropNameBody();
            var xml = Encoding.UTF8.GetString(bytes);

            var doc = XDocument.Parse(xml);
            XNamespace dav = "DAV:";
            Assert.AreEqual(dav + "propfind", doc.Root.Name);
            Assert.IsNotNull(doc.Root.Element(dav + "propname"));
            Assert.IsNull(doc.Root.Element(dav + "allprop"));
            Assert.IsNull(doc.Root.Element(dav + "prop"));
        }

        [TestMethod]
        public void BuildPropNameBody_returns_cached_byte_array()
        {
            var a = PropFindRequestBuilder.BuildPropNameBody();
            var b = PropFindRequestBuilder.BuildPropNameBody();
            Assert.AreSame(a, b);
        }

        // -------------------- Prop --------------------

        [TestMethod]
        public void BuildPropBody_throws_on_null_collection()
        {
            Assert.ThrowsException<ArgumentNullException>(() => PropFindRequestBuilder.BuildPropBody(null));
        }

        [TestMethod]
        public void BuildPropBody_throws_on_empty_collection()
        {
            // A <prop> body with zero properties is rejected by some servers and is
            // pointless for the caller; fail fast with a clear message.
            Assert.ThrowsException<ArgumentException>(() => PropFindRequestBuilder.BuildPropBody(new PropertyName[0]));
        }

        [TestMethod]
        public void BuildPropBody_throws_on_null_entry()
        {
            var props = new PropertyName[] { new PropertyName("DAV:", "displayname"), null };
            Assert.ThrowsException<ArgumentException>(() => PropFindRequestBuilder.BuildPropBody(props));
        }

        [TestMethod]
        public void BuildPropBody_emits_each_property_under_prop_in_its_namespace()
        {
            var props = new[]
            {
                new PropertyName("DAV:", "getetag"),
                new PropertyName("http://example.com/ns", "author"),
                new PropertyName("http://example.com/other", "tag"),
            };

            var bytes = PropFindRequestBuilder.BuildPropBody(props);
            var xml = Encoding.UTF8.GetString(bytes);

            var doc = XDocument.Parse(xml);
            XNamespace dav = "DAV:";
            var prop = doc.Root.Element(dav + "prop");
            Assert.IsNotNull(prop, "Body must contain <D:prop>");

            var children = prop.Elements().ToList();
            Assert.AreEqual(3, children.Count);
            Assert.AreEqual(XName.Get("getetag", "DAV:"), children[0].Name);
            Assert.AreEqual(XName.Get("author", "http://example.com/ns"), children[1].Name);
            Assert.AreEqual(XName.Get("tag", "http://example.com/other"), children[2].Name);
        }

        [TestMethod]
        public void BuildPropBody_reuses_prefix_for_repeated_namespace()
        {
            // The same namespace appearing twice should not lead to a separate
            // xmlns binding per element — the writer must reuse the existing prefix.
            var props = new[]
            {
                new PropertyName("http://example.com/ns", "author"),
                new PropertyName("http://example.com/ns", "tag"),
            };

            var bytes = PropFindRequestBuilder.BuildPropBody(props);
            var xml = Encoding.UTF8.GetString(bytes);

            // Count xmlns declarations for the custom namespace; one is enough.
            var occurrences = 0;
            var idx = 0;
            while ((idx = xml.IndexOf("\"http://example.com/ns\"", idx, StringComparison.Ordinal)) >= 0)
            {
                occurrences++;
                idx++;
            }
            Assert.AreEqual(1, occurrences, "Custom namespace URI should appear exactly once as an xmlns binding.");
        }

        [TestMethod]
        public void BuildPropBody_produces_well_formed_utf8_xml_without_BOM()
        {
            var props = new[] { new PropertyName("DAV:", "getetag") };
            var bytes = PropFindRequestBuilder.BuildPropBody(props);

            // No UTF-8 BOM (matches the existing PropPatch builder convention).
            Assert.AreNotEqual(0xEF, bytes[0]);

            // Round-trips through XmlReader without throwing.
            using (var ms = new System.IO.MemoryStream(bytes))
            using (var reader = XmlReader.Create(ms))
            {
                while (reader.Read()) { }
            }
        }
    }
}
