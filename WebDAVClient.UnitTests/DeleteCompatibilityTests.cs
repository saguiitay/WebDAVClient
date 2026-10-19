using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DeleteCompatibilityTests
    {
        [DataTestMethod]
        [DataRow("DeleteFile")]
        [DataRow("DeleteFolder")]
        public void Shipped_parameter_names_and_defaults_are_preserved(string method)
        {
            var concrete = typeof(Client).GetMethod(method).GetParameters()[0];
            var contract = typeof(IClient).GetMethod(method).GetParameters()[0];
            Assert.AreEqual("href", concrete.Name);
            Assert.IsFalse(concrete.HasDefaultValue);
            Assert.AreEqual("path", contract.Name);
            Assert.IsTrue(contract.HasDefaultValue);
            Assert.AreEqual("/", contract.DefaultValue);
        }

        [TestMethod]
        public async Task Named_and_default_calls_compile_and_target_the_documented_resources()
        {
            using var harness = new ClientHarness(request => request.Method.Method == "PROPFIND"
                ? StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"))
                : new HttpResponseMessage(HttpStatusCode.NoContent));
            Client concrete = harness.Client;
            IClient contract = concrete;
            await concrete.DeleteFile(href: "file");
            await contract.DeleteFile(path: "file");
            await contract.DeleteFile();
            await concrete.DeleteFolder(href: "folder");
            await contract.DeleteFolder(path: "folder");
            await contract.DeleteFolder();
            Assert.AreEqual("/webdav/", harness.Handler.Requests[3].RequestUri.AbsolutePath);
            Assert.AreEqual("/webdav/", harness.Handler.Requests[6].RequestUri.AbsolutePath);
        }
    }
}
