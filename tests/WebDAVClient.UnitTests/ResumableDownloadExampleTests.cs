using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Examples;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    [DoNotParallelize]
    public class ResumableDownloadExampleTests
    {
        [TestMethod]
        public async Task Example_publishes_exact_bytes_and_never_replaces_an_existing_destination()
        {
            byte[] payload = { 0, 1, 128, 255 };
            using var handler = new StubHttpMessageHandler(_ => Response(payload));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var output = new StringWriter();
            string destination = TemporaryPath();
            try
            {
                await ResumableDownloads.RunAsync(client, DavPath.FromSegments("file.bin"),
                    destination, output, CancellationToken.None);
                CollectionAssert.AreEqual(payload, File.ReadAllBytes(destination));
                Assert.IsTrue(File.Exists(destination + ".davlock"));
                Assert.IsFalse(File.Exists(destination + ".davpart"));
                Assert.IsFalse(File.Exists(destination + ".davcheckpoint"));
                StringAssert.Contains(output.ToString(), "Download complete.");
                StringAssert.Contains(output.ToString(), ".davlock sentinel is retained");
                Assert.AreEqual("identity", handler.Requests.Single().Headers["Accept-Encoding"].Single());
                await Assert.ThrowsExceptionAsync<IOException>(() => ResumableDownloads.RunAsync(client,
                    DavPath.FromSegments("file.bin"), destination, output, CancellationToken.None));
                Assert.AreEqual(1, handler.Requests.Count);
                CollectionAssert.AreEqual(payload, File.ReadAllBytes(destination));
            }
            finally
            {
                DeleteArtifacts(destination);
            }
        }

        [TestMethod]
        public async Task Example_preserves_failed_transfer_state_and_resumes_only_on_an_explicit_second_call()
        {
            byte[] payload = { 0, 1, 2, 3, 4, 5, 128, 255 };
            int requests = 0;
            using var handler = new StubHttpMessageHandler(request =>
            {
                requests++;
                if (requests == 1)
                {
                    return Response(payload.Take(4).ToArray(), payload.Length);
                }
                Assert.AreEqual("\"stable\"", request.Headers.IfMatch.Single().ToString());
                Assert.IsNull(request.Headers.Range, "The initial durable checkpoint is at offset zero.");
                return Response(payload);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var output = new StringWriter();
            string destination = TemporaryPath();
            try
            {
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => ResumableDownloads.RunAsync(client,
                    DavPath.FromSegments("file.bin"), destination, output, CancellationToken.None));
                Assert.AreEqual(1, requests, "No implicit retry.");
                Assert.IsFalse(File.Exists(destination));
                Assert.IsTrue(File.Exists(destination + ".davpart"));
                Assert.IsTrue(File.Exists(destination + ".davcheckpoint"));
                Assert.AreEqual("", output.ToString(), "Do not report a failed attempt as complete.");
                await ResumableDownloads.RunAsync(client, DavPath.FromSegments("file.bin"),
                    destination, output, CancellationToken.None);
                Assert.AreEqual(2, requests);
                CollectionAssert.AreEqual(payload, File.ReadAllBytes(destination));
                Assert.IsFalse(File.Exists(destination + ".davcheckpoint"));
            }
            finally
            {
                DeleteArtifacts(destination);
            }
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task Command_reports_invalid_checkpoints_and_supports_offline_validation(bool invalidCheckpoint)
        {
            string destination = TemporaryPath();
            string[] names = { "WEBDAV_ENDPOINT", "WEBDAV_AUTH", "WEBDAV_REMOTE_FILE", "WEBDAV_OUTPUT" };
            string[] previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
            var previousError = Console.Error;
            using var error = new StringWriter();
            try
            {
                Environment.SetEnvironmentVariable(names[0], "https://example.invalid/dav/");
                Environment.SetEnvironmentVariable(names[1], "anonymous");
                Environment.SetEnvironmentVariable(names[2], "file.bin");
                Environment.SetEnvironmentVariable(names[3], destination);
                Console.SetError(error);
                if (invalidCheckpoint)
                {
                    File.WriteAllText(destination + ".davpart", "preserve staged data");
                    File.WriteAllText(destination + ".davcheckpoint", "invalid");
                }
                int result = await Examples.Program.Main(invalidCheckpoint
                    ? new[] { "resumable-downloads" } : new[] { "resumable-downloads", "--validate" });
                Assert.AreEqual(invalidCheckpoint ? 1 : 0, result);
                Assert.IsFalse(File.Exists(destination));
                if (invalidCheckpoint)
                {
                    StringAssert.Contains(error.ToString(), "Checkpoint checksum is invalid.");
                    Assert.AreEqual("preserve staged data", File.ReadAllText(destination + ".davpart"));
                    Assert.AreEqual("invalid", File.ReadAllText(destination + ".davcheckpoint"));
                }
                else
                {
                    Assert.IsFalse(File.Exists(destination + ".davlock"));
                }
            }
            finally
            {
                Console.SetError(previousError);
                for (int i = 0; i < names.Length; i++)
                {
                    Environment.SetEnvironmentVariable(names[i], previous[i]);
                }
                DeleteArtifacts(destination);
            }
        }

        private static HttpResponseMessage Response(byte[] payload, long? length = null)
        {
            var response = StubHttpMessageHandler.Stream(HttpStatusCode.OK, payload);
            response.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
            response.Content.Headers.ContentLength = length ?? payload.Length;
            return response;
        }

        private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), "webdavclient-resume-example-" + Guid.NewGuid().ToString("N"));

        private static void DeleteArtifacts(string destination)
        {
            foreach (string suffix in new[] { "", ".davpart", ".davcheckpoint", ".davcheckpoint.new", ".davlock" })
            {
                File.Delete(destination + suffix);
            }
        }
    }
}
