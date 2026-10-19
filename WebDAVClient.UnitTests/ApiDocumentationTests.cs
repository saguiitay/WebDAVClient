using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class ApiDocumentationTests
    {
        [TestMethod]
        public void Xml_documentation_is_available_beside_each_target_assembly_with_ownership_and_parameter_contracts()
        {
            var path = Path.ChangeExtension(typeof(Client).Assembly.Location, ".xml");
            Assert.IsTrue(File.Exists(path), $"Missing API documentation: {path}");
            var members = XDocument.Load(path).Descendants("member").ToArray();
            var upload = members.Single(m => ((string)m.Attribute("name")).StartsWith("M:WebDAVClient.IClient.Upload("));
            StringAssert.Contains(upload.Element("remarks").Value, "owns and disposes");
            StringAssert.Contains(upload.Elements("param").Single(p => (string)p.Attribute("name") == "content").Value, "readable");
            var download = members.Single(m => ((string)m.Attribute("name")).StartsWith("M:WebDAVClient.IClient.Download("));
            StringAssert.Contains(download.Element("returns").Value, "caller owns");
            var concreteDownload = members.Single(m => ((string)m.Attribute("name")).StartsWith("M:WebDAVClient.Client.Download("));
            Assert.IsNotNull(concreteDownload.Element("inheritdoc"));
            var constructor = members.Single(m => ((string)m.Attribute("name")).StartsWith("M:WebDAVClient.Client.#ctor(System.Net.ICredentials,"));
            StringAssert.Contains(constructor.Elements("param").Single(p => (string)p.Attribute("name") == "credential").Value, "Windows-default");
        }
    }
}
