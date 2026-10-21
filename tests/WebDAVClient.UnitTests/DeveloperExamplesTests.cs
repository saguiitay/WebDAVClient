using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
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
    public class DeveloperExamplesTests
    {
        [TestMethod]
        public async Task Getting_started_lists_then_downloads_exact_bytes_to_a_new_local_file()
        {
            byte[] payload = { 0, 1, 128, 255, 4 };
            using var handler = new StubHttpMessageHandler(request => request.Method.Method == "PROPFIND"
                ? StubHttpMessageHandler.Multistatus(WebDAVResponses.SingleFile("/dav/example.bin"))
                : StubHttpMessageHandler.Stream(HttpStatusCode.OK, payload));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            string destination = TemporaryPath();
            using var output = new StringWriter();
            try
            {
                await GettingStarted.RunAsync(client, DavPath.FromSegments("example.bin"),
                    destination, output, CancellationToken.None);
                CollectionAssert.AreEqual(payload, File.ReadAllBytes(destination));
                CollectionAssert.AreEqual(new[] { "PROPFIND", "GET" },
                    handler.Requests.Select(request => request.Method.Method).ToArray());
                Assert.AreEqual("/dav/example.bin", handler.Requests[1].RequestUri.AbsolutePath);
                StringAssert.Contains(output.ToString(), "Download complete.");
            }
            finally
            {
                File.Delete(destination);
            }
        }

        [DataTestMethod]
        [DataRow(401, false)]
        [DataRow(403, false)]
        [DataRow(404, true)]
        public async Task Getting_started_rejections_do_not_create_a_destination(int status, bool rejectDownload)
        {
            using var handler = new StubHttpMessageHandler(request =>
                request.Method.Method == "PROPFIND" && rejectDownload
                    ? StubHttpMessageHandler.Multistatus(WebDAVResponses.SingleFile("/dav/example.bin"))
                    : StubHttpMessageHandler.StatusOnly((HttpStatusCode)status));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            string destination = TemporaryPath();
            using var output = new StringWriter();
            var exception = await Assert.ThrowsExceptionAsync<WebDAVException>(() =>
                GettingStarted.RunAsync(client, DavPath.FromSegments("example.bin"),
                    destination, output, CancellationToken.None));
            Assert.AreEqual(status, exception.GetHttpCode());
            Assert.IsFalse(File.Exists(destination));
            Assert.AreEqual(rejectDownload ? 2 : 1, handler.Requests.Count);
            Assert.IsFalse(output.ToString().Contains("Download complete."));
        }

        [TestMethod]
        public async Task Getting_started_preserves_existing_output_and_honors_cancellation()
        {
            using var handler = new StubHttpMessageHandler(request => request.Method.Method == "PROPFIND"
                ? StubHttpMessageHandler.Multistatus(WebDAVResponses.SingleFile("/dav/example.bin"))
                : StubHttpMessageHandler.Stream(HttpStatusCode.OK, new byte[] { 9 }));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            string destination = TemporaryPath();
            File.WriteAllText(destination, "preserve me");
            using var output = new StringWriter();
            try
            {
                await Assert.ThrowsExceptionAsync<IOException>(() =>
                    GettingStarted.RunAsync(client, DavPath.FromSegments("example.bin"),
                        destination, output, CancellationToken.None));
                Assert.AreEqual("preserve me", File.ReadAllText(destination));
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
                    GettingStarted.RunAsync(client, DavPath.FromSegments("example.bin"),
                        destination, output, cancelled.Token));
                Assert.AreEqual(2, handler.Requests.Count);
            }
            finally
            {
                File.Delete(destination);
            }
        }

        [DataTestMethod]
        [DataRow("anonymous")]
        [DataRow("credentials")]
        [DataRow("windows")]
        [DataRow("bearer")]
        [DataRow("provider")]
        public async Task Authentication_configuration_can_be_validated_without_a_server(string mode)
        {
            string[] names = { "WEBDAV_ENDPOINT", "WEBDAV_AUTH", "WEBDAV_USERNAME", "WEBDAV_PASSWORD", "WEBDAV_BEARER_TOKEN" };
            string[] previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
            try
            {
                Environment.SetEnvironmentVariable(names[0], "https://example.invalid/dav/");
                Environment.SetEnvironmentVariable(names[1], mode);
                Environment.SetEnvironmentVariable(names[2], "example-user");
                Environment.SetEnvironmentVariable(names[3], "example-not-a-real-password");
                Environment.SetEnvironmentVariable(names[4], "example-not-a-real-token");
                Assert.AreEqual(0, await Examples.Program.Main(new[] { "authentication", "--validate" }));
                if (mode != "anonymous")
                {
                    Environment.SetEnvironmentVariable(names[0], "http://example.invalid/dav/");
                    Assert.AreEqual(2, await Examples.Program.Main(new[] { "authentication", "--validate" }));
                }
            }
            finally
            {
                for (int i = 0; i < names.Length; i++)
                {
                    Environment.SetEnvironmentVariable(names[i], previous[i]);
                }
            }
        }

        [DataTestMethod]
        [DataRow("transfers")]
        [DataRow("file-management")]
        [DataRow("locking")]
        public async Task Mutation_commands_require_an_explicit_write_flag_before_configuration_or_io(string command)
        {
            Assert.AreEqual(2, await Examples.Program.Main(new[] { command }));
        }

        [TestMethod]
        public async Task Unreadable_upload_input_is_reported_before_contacting_a_server()
        {
            string[] names = { "WEBDAV_ENDPOINT", "WEBDAV_AUTH", "WEBDAV_INPUT" };
            string[] previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
            string directory = TemporaryPath();
            Directory.CreateDirectory(directory);
            try
            {
                Environment.SetEnvironmentVariable(names[0], "https://example.invalid/dav/");
                Environment.SetEnvironmentVariable(names[1], "anonymous");
                Environment.SetEnvironmentVariable(names[2], directory);
                Assert.AreEqual(1, await Examples.Program.Main(new[] { "transfers", "--allow-writes" }));
            }
            finally
            {
                Directory.Delete(directory);
                for (int i = 0; i < names.Length; i++)
                {
                    Environment.SetEnvironmentVariable(names[i], previous[i]);
                }
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task File_management_uses_run_owned_paths_and_reports_cleanup_failure(bool failCleanup)
        {
            using var handler = new StubHttpMessageHandler(request => request.Method.Method switch
            {
                "MKCOL" or "PUT" or "COPY" or "MOVE" => StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created),
                "PROPPATCH" => StubHttpMessageHandler.Multistatus(
                    "<d:multistatus xmlns:d=\"DAV:\" xmlns:x=\"https://example.com/ns\"><d:response><d:href>" +
                    request.RequestUri.AbsolutePath + "</d:href><d:propstat><d:prop><x:example/></d:prop>" +
                    "<d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response></d:multistatus>"),
                "DELETE" => StubHttpMessageHandler.StatusOnly(failCleanup && request.RequestUri.AbsolutePath.EndsWith("/")
                    ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent),
                _ => throw new InvalidOperationException("Unexpected request.")
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var output = new StringWriter();
            using var error = new StringWriter();
            int result = await MutationExamples.RunAsync(client, "file-management", null,
                TemporaryPath(), output, error, CancellationToken.None);
            Assert.AreEqual(failCleanup ? 1 : 0, result);
            CollectionAssert.AreEqual(new[] { "MKCOL", "PUT", "COPY", "MOVE", "PROPPATCH", "DELETE", "DELETE" },
                handler.Requests.Select(request => request.Method.Method).ToArray());
            Assert.IsTrue(handler.Requests.All(request => request.RequestUri.AbsolutePath.StartsWith("/dav/webdavclient-example-")));
            Assert.AreEqual("F", handler.Requests.Single(request => request.Method.Method == "COPY").Headers["Overwrite"].Single());
            Assert.AreEqual("F", handler.Requests.Single(request => request.Method.Method == "MOVE").Headers["Overwrite"].Single());
            Assert.AreEqual(failCleanup, error.ToString().Contains("manual reconciliation/cleanup required"));
        }

        [TestMethod]
        public async Task Unconfirmed_collection_creation_is_never_deleted()
        {
            using var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.StatusOnly(HttpStatusCode.Forbidden));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.AreEqual(1, await MutationExamples.RunAsync(client, "file-management", null,
                TemporaryPath(), output, error, CancellationToken.None));
            Assert.AreEqual(1, handler.Requests.Count);
            Assert.AreEqual("MKCOL", handler.Requests[0].Method.Method);
        }

        [DataTestMethod]
        [DataRow(404, false)]
        [DataRow(403, true)]
        public async Task Listing_example_distinguishes_missing_optional_properties_from_denied_data(int propertyStatus, bool fails)
        {
            using var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Multistatus(
                "<d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/dav/example.bin</d:href><d:propstat>" +
                "<d:prop><d:displayname/></d:prop><d:status>HTTP/1.1 " + propertyStatus +
                " Status</d:status></d:propstat></d:response></d:multistatus>"));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var output = new StringWriter();
            if (fails)
            {
                await Assert.ThrowsExceptionAsync<IOException>(() =>
                    ListingAndProperties.RunAsync(client, output, CancellationToken.None));
            }
            else
            {
                await ListingAndProperties.RunAsync(client, output, CancellationToken.None);
                StringAssert.Contains(output.ToString(), "Listing document fully read.");
            }
            StringAssert.Contains(output.ToString(), "HTTP " + propertyStatus);
        }

        [TestMethod]
        public async Task Transfer_example_uploads_downloads_and_reads_a_range_without_appending()
        {
            byte[] payload = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
            using var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method == HttpMethod.Get)
                {
                    bool range = request.Headers.Range != null;
                    var response = StubHttpMessageHandler.Stream(range ? HttpStatusCode.PartialContent : HttpStatusCode.OK,
                        range ? payload.Take(16).ToArray() : payload);
                    if (range)
                    {
                        Assert.AreEqual("bytes=0-15", request.Headers.Range.ToString());
                        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 15, payload.Length);
                    }
                    return response;
                }
                return StubHttpMessageHandler.StatusOnly(request.Method == HttpMethod.Delete
                    ? HttpStatusCode.NoContent : HttpStatusCode.Created);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var input = new MemoryStream(payload);
            using var output = new StringWriter();
            using var error = new StringWriter();
            string destination = TemporaryPath();
            try
            {
                Assert.AreEqual(0, await MutationExamples.RunAsync(client, "transfers", input,
                    destination, output, error, CancellationToken.None));
                CollectionAssert.AreEqual(payload, File.ReadAllBytes(destination));
                CollectionAssert.AreEqual(payload, handler.Requests.Single(request => request.Method == HttpMethod.Put).Body);
                Assert.IsTrue(input.CanRead, "Modern uploads borrow their input.");
                StringAssert.Contains(output.ToString(), "prefix length: 16");
                Assert.AreEqual("", error.ToString());
            }
            finally
            {
                File.Delete(destination);
            }
        }

        [TestMethod]
        public async Task Locking_example_submits_the_token_refreshes_and_unlocks_before_deleting()
        {
            const string token = "urn:uuid:example-lock";
            using var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method.Method == "LOCK")
                {
                    if (request.Content == null)
                    {
                        return StubHttpMessageHandler.StatusOnly(HttpStatusCode.OK);
                    }
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "<d:prop xmlns:d=\"DAV:\"><d:lockdiscovery><d:activelock>" +
                            "<d:lockscope><d:exclusive/></d:lockscope><d:locktype><d:write/></d:locktype>" +
                            "<d:depth>0</d:depth><d:timeout>Second-600</d:timeout><d:locktoken><d:href>" +
                            token + "</d:href></d:locktoken></d:activelock></d:lockdiscovery></d:prop>",
                            Encoding.UTF8, "application/xml")
                    };
                    response.Headers.Add("Lock-Token", "<" + token + ">");
                    return response;
                }
                return StubHttpMessageHandler.StatusOnly(request.Method.Method is "DELETE" or "UNLOCK"
                    ? HttpStatusCode.NoContent : HttpStatusCode.Created);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.AreEqual(0, await MutationExamples.RunAsync(client, "locking", null,
                TemporaryPath(), output, error, CancellationToken.None), error.ToString());
            CollectionAssert.AreEqual(new[] { "MKCOL", "PUT", "LOCK", "PUT", "LOCK", "UNLOCK", "DELETE" },
                handler.Requests.Select(request => request.Method.Method).ToArray());
            StringAssert.Contains(handler.Requests.Where(request => request.Method == HttpMethod.Put).Last().Headers["If"].Single(), token);
            Assert.AreEqual("<" + token + ">", handler.Requests.Single(request => request.Method.Method == "UNLOCK").Headers["Lock-Token"].Single());
            Assert.IsFalse(output.ToString().Contains(token), "Lock tokens are not diagnostics.");
        }

        [DataTestMethod]
        [DataRow(404, false)]
        [DataRow(403, true)]
        public async Task Sync_example_distinguishes_profile_deletions_from_member_failure(int memberStatus, bool fails)
        {
            using var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Multistatus(
                "<d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/dav/changed.bin</d:href>" +
                "<d:status>HTTP/1.1 " + memberStatus + " Status</d:status></d:response>" +
                "<d:response><d:href>/dav/</d:href><d:status>HTTP/1.1 507 Status</d:status></d:response>" +
                "<d:sync-token>urn:uuid:next-token</d:sync-token></d:multistatus>"));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(new Uri("https://example.test/dav/"), http);
            using var output = new StringWriter();
            if (fails)
            {
                await Assert.ThrowsExceptionAsync<IOException>(() =>
                    CollectionSynchronization.RunAsync(client, "urn:uuid:previous", output, CancellationToken.None));
            }
            else
            {
                await CollectionSynchronization.RunAsync(client, "urn:uuid:previous", output, CancellationToken.None);
                StringAssert.Contains(output.ToString(), "Deletion:");
                StringAssert.Contains(output.ToString(), "Has more: True");
                StringAssert.Contains(output.ToString(), "No token was saved.");
            }
            Assert.IsFalse(output.ToString().Contains("urn:uuid:next-token"));
        }

        [TestMethod]
        public void Solution_projects_references_and_package_inputs_resolve_in_the_repository_layout()
        {
            string root = RepositoryRoot();
            string solution = File.ReadAllText(Path.Combine(root, "WebDAVClient.sln"));
            var projects = Regex.Matches(solution, @"""(?<path>[^""]+\.csproj)""")
                .Cast<Match>().Select(match => match.Groups["path"].Value).ToArray();
            Assert.IsTrue(projects.Length > 0);
            foreach (string relative in projects)
            {
                string folder = relative.Split('\\', '/')[0];
                Assert.IsTrue(new[] { "src", "tests", "examples" }.Contains(folder), relative);
                string path = Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(path), relative);
                var project = XDocument.Load(path);
                var inputs = project.Descendants("ProjectReference")
                    .Concat(project.Descendants("None").Where(item => (string)item.Attribute("Pack") == "true"));
                foreach (var item in inputs)
                {
                    string include = ((string)item.Attribute("Include")).Replace('\\', Path.DirectorySeparatorChar);
                    string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), include));
                    Assert.IsTrue(File.Exists(resolved), relative + " -> " + include);
                }
            }
        }

        [TestMethod]
        public void Documentation_links_point_to_existing_repository_files_and_sections()
        {
            string root = RepositoryRoot();
            var documents = new[] { Path.Combine(root, "README.md"), Path.Combine(root, "NOTICE.md") }
                .Concat(Directory.GetFiles(Path.Combine(root, "docs"), "*.md"));
            const string repositoryPrefix = "https://github.com/saguiitay/WebDAVClient/blob/master/";
            foreach (string document in documents)
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(document), @"\[[^\]]*\]\((?<target>[^)\s]+)\)"))
                {
                    string target = match.Groups["target"].Value;
                    bool repositoryLink = target.StartsWith(repositoryPrefix, StringComparison.Ordinal);
                    if (Uri.TryCreate(target, UriKind.Absolute, out _) && !repositoryLink)
                    {
                        continue;
                    }
                    string[] parts = (repositoryLink ? target.Substring(repositoryPrefix.Length) : target).Split('#', 2);
                    string path = parts[0].Length == 0 ? document : Path.GetFullPath(Path.Combine(
                        repositoryLink ? root : Path.GetDirectoryName(document),
                        Uri.UnescapeDataString(parts[0]).Replace('/', Path.DirectorySeparatorChar)));
                    Assert.IsTrue(File.Exists(path) || Directory.Exists(path), document + " -> " + target);
                    if (parts.Length == 2 && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    {
                        var anchors = Regex.Matches(File.ReadAllText(path), @"(?m)^#{1,6} (?<heading>.+)\r?$")
                            .Cast<Match>().Select(heading => Regex.Replace(
                                heading.Groups["heading"].Value.ToLowerInvariant().Trim(), @"[^a-z0-9 _-]", "").Replace(' ', '-'));
                        Assert.IsTrue(anchors.Contains(parts[1]), document + " -> missing section " + target);
                    }
                }
            }
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "WebDAVClient.sln")))
            {
                directory = directory.Parent;
            }
            Assert.IsNotNull(directory, "Locate the checkout for repository validation.");
            return directory.FullName;
        }

        private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), "webdavclient-docs-" + Guid.NewGuid().ToString("N"));
    }
}
