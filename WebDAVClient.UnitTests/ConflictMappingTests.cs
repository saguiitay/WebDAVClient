using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class ConflictMappingTests
    {
        [DataTestMethod]
        [DataRow(409)]
        [DataRow(403)]
        public async Task Every_operation_preserves_status_and_conflict_specialization(int status)
        {
            Func<Client, Task>[] operations =
            {
                c => c.GetServerOptions(),
                c => c.List(),
                c => c.ListPropertyNames(),
                c => c.GetFile("file"),
                c => c.GetFolder("folder"),
                c => c.Upload("/", new MemoryStream(new byte[1]), "file"),
                c => c.UploadPartial("/", new MemoryStream(new byte[1]), "file", 0, 1),
                c => c.CreateDir("/", "folder"),
                c => c.DeleteFile("file"),
                c => c.DeleteFolder("folder"),
                c => c.MoveFile("source", "destination"),
                c => c.MoveFolder("source", "destination"),
                c => c.CopyFile("source", "destination"),
                c => c.CopyFolder("source", "destination"),
                c => c.LockFile("file"),
                c => c.LockFolder("folder"),
                c => c.UnlockFile("file", "opaquelocktoken:test"),
                c => c.UnlockFolder("folder", "opaquelocktoken:test"),
                c => c.RefreshLock("file", "opaquelocktoken:test"),
                c => c.SetProperty("file", "name", "urn:test", "value"),
                c => c.RemoveProperty("file", "name", "urn:test"),
                c => c.Download("file"),
                c => c.DownloadPartial("file", 0, 0)
            };

            foreach (var operation in operations)
            {
                using var harness = new ClientHarness(request =>
                    request.Method.Method == "PROPFIND" && request.RequestUri.AbsolutePath == "/webdav/"
                        && request.Headers.GetValues("Depth").Contains("0")
                        ? StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"))
                        : StubHttpMessageHandler.StatusOnly((HttpStatusCode)status));
                try
                {
                    await operation(harness.Client);
                    Assert.Fail("Expected a status-bearing exception.");
                }
                catch (WebDAVException ex)
                {
                    Assert.AreEqual(status, ex.GetHttpCode());
                    Assert.AreEqual(status == 409, ex is WebDAVConflictException);
                }
            }
        }

        [TestMethod]
        public async Task Discovery_conflict_uses_specialized_exception()
        {
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.StatusOnly(HttpStatusCode.Conflict));
            var ex = await Assert.ThrowsExceptionAsync<WebDAVConflictException>(() => harness.Client.GetFile("file"));
            Assert.AreEqual(409, ex.GetHttpCode());
        }
    }
}
