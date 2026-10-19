using System;
using System.Collections.Generic;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Model;

namespace WebDAVClient.UnitTests.Model
{
    [TestClass]
    public class PropertyNameTests
    {
        [TestMethod]
        public void Null_arguments_are_named_and_an_empty_namespace_is_supported()
        {
            Assert.AreEqual("namespace", Assert.ThrowsException<ArgumentNullException>(() => new PropertyName(null, "author")).ParamName);
            Assert.AreEqual("localName", Assert.ThrowsException<ArgumentNullException>(() => new PropertyName("urn:test", null)).ParamName);
            Assert.AreEqual("localName", Assert.ThrowsException<ArgumentException>(() => new PropertyName("urn:test", "")).ParamName);
            var emptyNamespace = new PropertyName("", "_author");
            Assert.AreEqual("", emptyNamespace.Namespace);
            Assert.AreEqual("_author", emptyNamespace.LocalName);
        }

        [DataTestMethod]
        [DataRow("prefix:name")]
        [DataRow("has space")]
        [DataRow("1name")]
        [DataRow("<name>")]
        public void Invalid_local_names_retain_the_xml_validation_cause(string localName)
        {
            var error = Assert.ThrowsException<ArgumentException>(() => new PropertyName("urn:test", localName));
            Assert.AreEqual("localName", error.ParamName);
            Assert.IsInstanceOfType<XmlException>(error.InnerException);
        }

        [TestMethod]
        public void Equality_operators_hashing_and_diagnostics_use_case_sensitive_qualified_names()
        {
            var name = new PropertyName("urn:test", "author");
            var equal = new PropertyName("urn:test", "author");
            var otherNamespace = new PropertyName("urn:TEST", "author");
            var otherLocalName = new PropertyName("urn:test", "Author");
            PropertyName missing = null;
            Assert.IsTrue(name.Equals(name));
            Assert.IsTrue(name.Equals((object)equal));
            Assert.IsFalse(name.Equals(new object()));
            Assert.IsFalse(name.Equals(missing));
            Assert.IsTrue(name == equal);
            Assert.IsFalse(name != equal);
            Assert.IsTrue(name != otherNamespace);
            Assert.IsTrue(name != otherLocalName);
            Assert.IsFalse(missing == name);
            Assert.IsFalse(name == missing);
            Assert.IsTrue(missing == (PropertyName)null);
            Assert.AreEqual(name.GetHashCode(), equal.GetHashCode());
            var values = new Dictionary<PropertyName, string> { [name] = "value", [otherNamespace] = "separate" };
            Assert.AreEqual("value", values[equal]);
            Assert.AreEqual(2, values.Count);
            Assert.AreEqual("{urn:test}author", name.ToString());
            Assert.AreEqual("\u00E9l\u00E9ment", new PropertyName("urn:test", "\u00E9l\u00E9ment").LocalName);
        }
    }
}
