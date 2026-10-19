using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.HttpClient;
using WebDAVClient.Model;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class ArgumentValidationTests
    {
        [TestMethod]
        public void Null_transports_and_invalid_configuration_have_named_argument_errors()
        {
            Assert.AreEqual("httpClient", Assert.ThrowsException<ArgumentNullException>(() => new Client((System.Net.Http.HttpClient)null)).ParamName);
            Assert.AreEqual("httpClientWrapper", Assert.ThrowsException<ArgumentNullException>(() => new Client((IHttpClientWrapper)null)).ParamName);
            Assert.AreEqual("httpClient", Assert.ThrowsException<ArgumentNullException>(() => new HttpClientWrapper(null)).ParamName);
            using var harness = new ClientHarness(_ => throw new AssertFailedException("Unexpected network request."));
            Assert.AreEqual("value", Assert.ThrowsException<ArgumentNullException>(() => harness.Client.Server = null).ParamName);
            Assert.AreEqual("value", Assert.ThrowsException<ArgumentNullException>(() => harness.Client.BasePath = null).ParamName);
            foreach (var server in new[] { "", " ", "http://", "not a valid host", "ftp://example.com" })
                Assert.AreEqual("value", Assert.ThrowsException<ArgumentException>(() => harness.Client.Server = server).ParamName);
            Assert.AreEqual("value", Assert.ThrowsException<ArgumentException>(() => harness.Client.BasePath = " ").ParamName);
            Assert.AreEqual("value", Assert.ThrowsException<ArgumentOutOfRangeException>(() => harness.Client.Port = 65536).ParamName);
            Assert.AreEqual(0, harness.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task Invalid_operation_arguments_fail_before_discovery_without_disposing_caller_content()
        {
            using var content = new MemoryStream(new byte[] { 1 });
            using var unreadable = new MemoryStream();
            unreadable.Dispose();
            var cases = new (string Parameter, Func<Client, Task> Operation)[]
            {
                ("remoteFilePath", c => c.Download(null)),
                ("remoteFilePath", c => c.DownloadPartial(null, 0, 0)),
                ("remoteFilePath", c => c.Upload(null, content, "file")),
                ("content", c => c.Upload("/", null, "file")),
                ("content", c => c.Upload("/", unreadable, "file")),
                ("name", c => c.Upload("/", content, null)),
                ("name", c => c.Upload("/", content, "")),
                ("name", c => c.UploadPartial("/", content, "/", 0, 1)),
                ("remotePath", c => c.CreateDir(null, "folder")),
                ("name", c => c.CreateDir("/", " ")),
                ("href", c => c.DeleteFile(null)),
                ("href", c => c.DeleteFolder(null)),
                ("srcFilePath", c => c.MoveFile(null, "dest")),
                ("dstFilePath", c => c.MoveFile("source", null)),
                ("srcFolderPath", c => c.CopyFolder(null, "dest")),
                ("dstFolderPath", c => c.CopyFolder("source", null)),
                ("filePath", c => c.LockFile(null)),
                ("folderPath", c => c.UnlockFolder(null, "opaquelocktoken:test")),
                ("timeoutSeconds", c => c.LockFile("file", 0)),
                ("path", c => c.RefreshLock(null, "opaquelocktoken:test")),
                ("path", c => c.SetProperty(null, "author", "urn:test", "value")),
                ("path", c => c.List(" ")),
                ("properties", c => c.GetFile("/", Array.Empty<PropertyName>())),
                ("properties", c => c.GetFolder("/", new PropertyName[] { null })),
                ("properties", c => c.List("/", 1, Array.Empty<PropertyName>()))
            };
            foreach (var test in cases)
            {
                using var harness = new ClientHarness(_ => throw new AssertFailedException("Unexpected network request."));
                try
                {
                    await test.Operation(harness.Client);
                    Assert.Fail($"Expected argument error for {test.Parameter}.");
                }
                catch (ArgumentException exception)
                {
                    Assert.AreEqual(test.Parameter, exception.ParamName);
                }
                Assert.AreEqual(0, harness.Handler.Requests.Count, test.Parameter);
                Assert.IsTrue(content.CanRead);
            }
        }

        [TestMethod]
        public async Task Read_metadata_operations_retain_null_and_empty_root_path_semantics()
        {
            foreach (var path in new[] { null, "", "/" })
            {
                using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/")));
                await harness.Client.List(path);
                await harness.Client.GetFolder(path);
                await harness.Client.GetFile(path);
                Assert.AreEqual(4, harness.Handler.Requests.Count);
            }

        }

        [DataTestMethod]
        [DataRow("example.com", "http://example.com/webdav/")]
        [DataRow("localhost", "http://localhost/webdav/")]
        public async Task Legacy_hostnames_retain_uri_builder_http_defaults(string server, string expected)
        {
            using var harness = new ClientHarness(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            harness.Client.Server = server;
            await harness.Client.GetServerOptions();
            Assert.AreEqual(expected, harness.Handler.Requests[0].RequestUri.AbsoluteUri);
        }
    }
}
