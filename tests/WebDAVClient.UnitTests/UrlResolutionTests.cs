using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class UrlResolutionTests
    {
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Path_joining_is_consistent_for_path_and_absolute_roots(bool absolute)
        {
            var calls = 0;
            using var harness = new ClientHarness(request => StubHttpMessageHandler.Multistatus(
                ++calls == 1 ? WebDAVResponses.Root(absolute ? "https://example.com/webdav/" : "/webdav/")
                    : WebDAVResponses.SingleFile(request.RequestUri.AbsolutePath)), "https://example.com");
            var cases = new Dictionary<string, string>
            {
                [""] = "/webdav/",
                ["/"] = "/webdav/",
                ["file.txt"] = "/webdav/file.txt",
                ["/file.txt"] = "/webdav/file.txt",
                ["//file.txt"] = "/webdav/file.txt",
                ["/webdav/file.txt"] = "/webdav/file.txt",
                ["/webdav/"] = "/webdav/",
                ["/webdav"] = "/webdav",
                ["/webdavish/file.txt"] = "/webdav/webdavish/file.txt",
                ["webdav/file.txt"] = "/webdav/webdav/file.txt",
                ["nested//file.txt"] = "/webdav/nested//file.txt",
                ["/webdav/nested//file.txt"] = "/webdav/nested//file.txt",
                ["some%20file.txt"] = "/webdav/some%20file.txt"
            };

            foreach (var entry in cases)
            {
                await harness.Client.GetFile(entry.Key);
                Assert.AreEqual(entry.Value, harness.Handler.Requests.Last().RequestUri.AbsolutePath, entry.Key);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Mutations_use_exact_joined_request_and_destination_paths(bool absolute)
        {
            var calls = 0;
            using var harness = new ClientHarness(_ => ++calls == 1
                ? StubHttpMessageHandler.Multistatus(WebDAVResponses.Root(absolute ? "https://example.com/webdav/" : "/webdav/"))
                : StubHttpMessageHandler.StatusOnly(HttpStatusCode.NoContent), "https://example.com");
            using var content = new MemoryStream(new byte[] { 1 });

            await harness.Client.Upload("/", content, "file.txt");
            Assert.AreEqual("/webdav/file.txt", harness.Handler.Requests.Last().RequestUri.AbsolutePath);
            await harness.Client.CreateDir("/", "folder");
            Assert.AreEqual("/webdav/folder", harness.Handler.Requests.Last().RequestUri.AbsolutePath);
            await harness.Client.CopyFile("/webdav/file.txt", "/webdav/copied.txt");
            Assert.AreEqual("/webdav/file.txt", harness.Handler.Requests.Last().RequestUri.AbsolutePath);
            Assert.AreEqual("https://example.com/webdav/copied.txt", harness.Handler.Requests.Last().Headers["Destination"].Single());
            await harness.Client.MoveFolder("/webdav/folder", "/moved");
            Assert.AreEqual("/webdav/folder/", harness.Handler.Requests.Last().RequestUri.AbsolutePath);
            Assert.AreEqual("https://example.com/webdav/moved/", harness.Handler.Requests.Last().Headers["Destination"].Single());
        }
    }
}
