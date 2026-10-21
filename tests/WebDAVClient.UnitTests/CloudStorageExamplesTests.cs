using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Examples;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    [DoNotParallelize]
    public class CloudStorageExamplesTests
    {
        [DataTestMethod]
        [DataRow(true, "/nextcloud/remote.php/dav/files/alice%40example.test/")]
        [DataRow(false, "/owncloud/remote.php/webdav/")]
        public async Task Cloud_examples_request_qualified_vendor_properties_and_download_exact_bytes(bool nextcloud, string prefix)
        {
            byte[] payload = { 0, 1, 128, 255 };
            using var handler = new StubHttpMessageHandler(request => request.Method.Method == "PROPFIND"
                ? StubHttpMessageHandler.Multistatus(Listing(prefix, nextcloud, 200))
                : StubHttpMessageHandler.Stream(HttpStatusCode.OK, payload));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test" + prefix), http);
            using var output = new StringWriter();
            string destination = TemporaryPath();
            try
            {
                await CloudStorage.RunAsync(client, nextcloud, DavPath.FromSegments("report + r\u00e9sum\u00e9%1.txt"),
                    destination, output, CancellationToken.None);
                CollectionAssert.AreEqual(payload, File.ReadAllBytes(destination));
                CollectionAssert.AreEqual(new[] { "PROPFIND", "GET" },
                    handler.Requests.Select(request => request.Method.Method).ToArray());
                Assert.AreEqual(prefix, handler.Requests[0].RequestUri.AbsolutePath);
                Assert.AreEqual("1", handler.Requests[0].Headers["Depth"].Single());
                Assert.AreEqual(prefix + "report%20%2B%20r%C3%A9sum%C3%A9%251.txt",
                    handler.Requests[1].RequestUri.AbsolutePath);
                XNamespace dav = "DAV:";
                XNamespace oc = "http://owncloud.org/ns";
                var selected = XDocument.Parse(handler.Requests[0].BodyAsString())
                    .Root.Element(dav + "prop").Elements().Select(element => element.Name).ToArray();
                CollectionAssert.AreEquivalent(new[]
                {
                    dav + "displayname", dav + "resourcetype", dav + "getetag",
                    oc + "fileid", oc + "permissions", oc + "size",
                    nextcloud ? XName.Get("has-preview", "http://nextcloud.org/ns") : oc + "id"
                }, selected);
                StringAssert.Contains(output.ToString(), "Value: 0000000042");
                StringAssert.Contains(output.ToString(), "{http://owncloud.org/ns}fileid: HTTP 200");
                StringAssert.Contains(output.ToString(), "Value: RDNVW");
                StringAssert.Contains(output.ToString(), nextcloud ? "Value: true" : "Value: instance-file-id");
                StringAssert.Contains(output.ToString(), "Download complete.");
            }
            finally
            {
                File.Delete(destination);
            }
        }

        [DataTestMethod]
        [DataRow(true, 404, false)]
        [DataRow(false, 404, false)]
        [DataRow(true, 403, true)]
        [DataRow(false, 500, true)]
        public async Task Cloud_examples_allow_unavailable_metadata_but_stop_on_denied_or_failed_properties(
            bool nextcloud, int status, bool fails)
        {
            using var handler = new StubHttpMessageHandler(request => request.Method.Method == "PROPFIND"
                ? StubHttpMessageHandler.Multistatus(Listing("/dav/", nextcloud, status))
                : StubHttpMessageHandler.Stream(HttpStatusCode.OK, new byte[] { 42 }));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var output = new StringWriter();
            string destination = TemporaryPath();
            try
            {
                if (fails)
                {
                    await Assert.ThrowsExceptionAsync<IOException>(() => CloudStorage.RunAsync(client, nextcloud,
                        DavPath.FromSegments("example.bin"), destination, output, CancellationToken.None));
                }
                else
                {
                    await CloudStorage.RunAsync(client, nextcloud, DavPath.FromSegments("example.bin"),
                        destination, output, CancellationToken.None);
                }
                Assert.AreEqual(!fails, File.Exists(destination));
                Assert.AreEqual(fails ? 1 : 2, handler.Requests.Count);
                StringAssert.Contains(output.ToString(), "HTTP " + status);
                Assert.IsFalse(output.ToString().Contains(nextcloud ? "Value: true" : "Value: instance-file-id"));
            }
            finally
            {
                File.Delete(destination);
            }
        }

        [DataTestMethod]
        [DataRow(true, true)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(false, false)]
        public async Task Cloud_examples_do_not_download_after_truncated_xml_or_resource_failure(bool nextcloud, bool truncated)
        {
            string xml = truncated
                ? Listing("/dav/", nextcloud, 200).Replace("</d:multistatus>", "")
                : "<d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/dav/example.bin</d:href>" +
                    "<d:status>HTTP/1.1 403 Forbidden</d:status></d:response></d:multistatus>";
            using var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Multistatus(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var output = new StringWriter();
            string destination = TemporaryPath();
            if (truncated)
            {
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => CloudStorage.RunAsync(client, nextcloud,
                    DavPath.FromSegments("example.bin"), destination, output, CancellationToken.None));
            }
            else
            {
                await Assert.ThrowsExceptionAsync<IOException>(() => CloudStorage.RunAsync(client, nextcloud,
                    DavPath.FromSegments("example.bin"), destination, output, CancellationToken.None));
            }
            Assert.AreEqual(1, handler.Requests.Count);
            Assert.IsFalse(File.Exists(destination));
        }

        [DataTestMethod]
        [DataRow(true, 401, false)]
        [DataRow(false, 403, false)]
        [DataRow(true, 403, true)]
        [DataRow(false, 404, true)]
        public async Task Cloud_examples_report_http_rejections_without_creating_output(
            bool nextcloud, int status, bool rejectDownload)
        {
            using var handler = new StubHttpMessageHandler(request => rejectDownload && request.Method.Method == "PROPFIND"
                ? StubHttpMessageHandler.Multistatus(Listing("/dav/", nextcloud, 200))
                : StubHttpMessageHandler.StatusOnly((HttpStatusCode)status));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var output = new StringWriter();
            string destination = TemporaryPath();
            var exception = await Assert.ThrowsExceptionAsync<WebDAVException>(() => CloudStorage.RunAsync(
                client, nextcloud, DavPath.FromSegments("example.bin"), destination, output, CancellationToken.None));
            Assert.AreEqual(status, exception.GetHttpCode());
            Assert.IsFalse(File.Exists(destination));
            Assert.AreEqual(rejectDownload ? 2 : 1, handler.Requests.Count);
            Assert.IsFalse(output.ToString().Contains("Download complete."));
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task Cloud_examples_preserve_existing_files_and_honor_cancellation(bool nextcloud)
        {
            using var handler = new StubHttpMessageHandler(request => request.Method.Method == "PROPFIND"
                ? StubHttpMessageHandler.Multistatus(Listing("/dav/", nextcloud, 200))
                : StubHttpMessageHandler.Stream(HttpStatusCode.OK, new byte[] { 42 }));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var output = new StringWriter();
            string destination = TemporaryPath();
            File.WriteAllText(destination, "preserve me");
            try
            {
                await Assert.ThrowsExceptionAsync<IOException>(() => CloudStorage.RunAsync(client, nextcloud,
                    DavPath.FromSegments("example.bin"), destination, output, CancellationToken.None));
                Assert.AreEqual("preserve me", File.ReadAllText(destination));
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => CloudStorage.RunAsync(client,
                    nextcloud, DavPath.FromSegments("example.bin"), destination, output, cancelled.Token));
                Assert.AreEqual(2, handler.Requests.Count);
            }
            finally
            {
                File.Delete(destination);
            }
        }

        [DataTestMethod]
        [DataRow("nextcloud")]
        [DataRow("owncloud")]
        public async Task Cloud_commands_validate_app_password_configuration_offline(string command)
        {
            string[] names = { "WEBDAV_ENDPOINT", "WEBDAV_AUTH", "WEBDAV_USERNAME", "WEBDAV_PASSWORD" };
            string[] previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
            try
            {
                Environment.SetEnvironmentVariable(names[0], "https://example.invalid/cloud/remote.php/webdav/");
                Environment.SetEnvironmentVariable(names[1], "credentials");
                Environment.SetEnvironmentVariable(names[2], "example-user");
                Environment.SetEnvironmentVariable(names[3], "not-a-real-app-password");
                Assert.AreEqual(0, await Examples.Program.Main(new[] { command, "--validate" }));
                Environment.SetEnvironmentVariable(names[0], "http://example.invalid/dav/");
                Assert.AreEqual(2, await Examples.Program.Main(new[] { command, "--validate" }));
            }
            finally
            {
                for (int i = 0; i < names.Length; i++)
                {
                    Environment.SetEnvironmentVariable(names[i], previous[i]);
                }
            }
        }

        private static string Listing(string prefix, bool nextcloud, int propertyStatus)
        {
            string extension = nextcloud ? "<n:has-preview>true</n:has-preview>" : "<x:id>instance-file-id</x:id>";
            if (propertyStatus != 200)
            {
                extension = nextcloud ? "<n:has-preview/>" : "<x:id/>";
            }
            return "<d:multistatus xmlns:d=\"DAV:\" xmlns:x=\"http://owncloud.org/ns\" xmlns:n=\"http://nextcloud.org/ns\">" +
                "<d:response><d:href>" + prefix + "example.bin</d:href><d:propstat><d:prop>" +
                "<d:displayname>example.bin</d:displayname><d:resourcetype/><d:getetag>&quot;opaque&quot;</d:getetag>" +
                "<x:fileid>0000000042</x:fileid><x:permissions>RDNVW</x:permissions><x:size>4</x:size>" +
                "</d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat><d:propstat><d:prop>" + extension +
                "</d:prop><d:status>HTTP/1.1 " + propertyStatus + " Status</d:status></d:propstat></d:response></d:multistatus>";
        }

        private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), "webdavclient-cloud-" + Guid.NewGuid().ToString("N"));
    }
}
