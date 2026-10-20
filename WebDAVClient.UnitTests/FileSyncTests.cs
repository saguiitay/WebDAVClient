using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.Sync;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class FileSyncTests
    {
        private static string FileMember(string name = "file", string etag = "&quot;one&quot;", string type = "") =>
            DavClientTests.Resource("<D:getetag>" + etag + "</D:getetag><D:resourcetype>" + type + "</D:resourcetype>", href: "/dav/" + name);

        private static HttpResponseMessage Batch(string members = "", string token = "urn:sync:one") =>
            DavClientTests.XmlResponse(DavClientTests.Multi(members + "<D:sync-token>" + token + "</D:sync-token>"));

        private static HttpResponseMessage Download(string text = "one", string etag = "\"one\"", int code = 200)
        {
            var response = new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(text) };
            if (etag != null)
            {
                response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
            }
            return response;
        }

        [TestMethod]
        public async Task Pull_stages_whole_batch_before_files_and_token_and_restart_uses_checkpoint()
        {
            using var fixture = new Fixture();
            var call = 0;
            fixture.Respond = request =>
            {
                call++;
                Assert.IsFalse(System.IO.File.Exists(fixture.Checkpoint));
                Assert.IsFalse(System.IO.File.Exists(fixture.Data("file")));
                if (request.Method.Method == "REPORT")
                {
                    return Batch(FileMember() + FileMember("other"));
                }
                Assert.AreEqual("\"one\"", request.Headers.IfMatch.Single().ToString());
                return Download();
            };
            Assert.AreEqual("urn:sync:one", await fixture.Session.PullAsync());
            Assert.AreEqual(3, call);
            Assert.AreEqual("one", System.IO.File.ReadAllText(fixture.Data("file")));
            Assert.AreEqual("one", System.IO.File.ReadAllText(fixture.Data("other")));
            Assert.IsFalse(System.IO.File.Exists(fixture.Pending));
            Assert.AreEqual(0, Directory.GetFiles(fixture.State, "*.stage").Length);
            fixture.Respond = _ => Batch(token: "urn:sync:two");
            var restarted = fixture.NewSession();
            Assert.AreEqual("\"one\"", await restarted.GetSynchronizedETagAsync("file"));
            Assert.IsNull(await restarted.GetSynchronizedETagAsync("new"));
            Assert.AreEqual("urn:sync:two", await restarted.PullAsync());
            StringAssert.Contains(fixture.Handler.Requests.Last().BodyAsString(), "urn:sync:one");
            Assert.AreEqual("one", System.IO.File.ReadAllText(fixture.Data("file")));
        }

        [TestMethod]
        public async Task Explicit_outgoing_changes_are_conditional_and_do_not_advance_checkpoint()
        {
            using var fixture = new Fixture();
            fixture.Respond = _ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
            using var input = new MemoryStream(new byte[] { 1, 2, 3 });
            var create = await fixture.Session.UploadAsync("é %25.txt", input);
            Assert.AreEqual(412, create.StatusCode);
            Assert.AreEqual("*", fixture.Handler.Requests.Last().Headers["If-None-Match"].Single());
            Assert.AreEqual("https://example.com/dav/%C3%A9%20%2525.txt", fixture.Handler.Requests.Last().RequestUri.AbsoluteUri);
            input.Position = 0;
            await fixture.Session.UploadAsync("file", input, "\"old\"");
            Assert.AreEqual("\"old\"", fixture.Handler.Requests.Last().Headers["If-Match"].Single());
            Assert.IsFalse(fixture.Handler.Requests.Last().Headers.ContainsKey("If-None-Match"));
            var delete = await fixture.Session.DeleteRemoteAsync("file", "\"old\"");
            Assert.AreEqual(412, delete.StatusCode);
            Assert.AreEqual("DELETE", fixture.Handler.Requests.Last().Method.Method);
            Assert.AreEqual("\"old\"", fixture.Handler.Requests.Last().Headers["If-Match"].Single());
            Assert.IsTrue(input.CanRead);
            Assert.IsFalse(System.IO.File.Exists(fixture.Checkpoint));
        }

        [DataTestMethod]
        [DataRow("é%20%252F.txt", "é %2F.txt")]
        [DataRow("%E6%96%87%E4%BB%B6.txt", "文件.txt")]
        [DataRow("%23%3B%26.txt", "#;&.txt")]
        public async Task Escaped_resource_names_round_trip_without_reinterpretation(string href, string local)
        {
            using var fixture = new Fixture();
            fixture.Respond = r => r.Method.Method == "REPORT" ? Batch(FileMember(href)) : Download();
            await fixture.Session.PullAsync();
            Assert.AreEqual("one", System.IO.File.ReadAllText(fixture.Data(local)));
            Assert.AreEqual("\"one\"", await fixture.Session.GetSynchronizedETagAsync(local));
        }

        [DataTestMethod]
        [DataRow("edited", false)]
        [DataRow(null, false)]
        [DataRow("edited", true)]
        public async Task Local_edits_or_deletions_conflict_without_advancing_token(string content, bool remoteDelete)
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync();
            if (content == null)
            {
                System.IO.File.Delete(fixture.Data("file"));
            }
            else
            {
                System.IO.File.WriteAllText(fixture.Data("file"), content);
            }
            var before = System.IO.File.ReadAllText(fixture.Checkpoint);
            fixture.Respond = r => r.Method.Method == "REPORT"
                ? Batch(remoteDelete ? DavClientTests.Member(404) : FileMember(etag: "&quot;two&quot;"), "urn:sync:two")
                : Download("two", "\"two\"");
            await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => fixture.Session.PullAsync());
            Assert.AreEqual(before, System.IO.File.ReadAllText(fixture.Checkpoint));
            Assert.IsFalse(System.IO.File.Exists(fixture.Pending));
            Assert.AreEqual(0, Directory.GetFiles(fixture.State, "*.stage").Length);
            Assert.AreEqual(content, System.IO.File.Exists(fixture.Data("file")) ? System.IO.File.ReadAllText(fixture.Data("file")) : null);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Remote_deletion_applies_only_explicit_delta_and_is_idempotent_when_locally_removed(bool localDelete)
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync();
            if (localDelete)
            {
                System.IO.File.Delete(fixture.Data("file"));
            }
            fixture.Respond = _ => Batch(DavClientTests.Member(404), "urn:sync:two");
            await fixture.Session.PullAsync();
            Assert.IsFalse(System.IO.File.Exists(fixture.Data("file")));
            Assert.IsNull(await fixture.Session.GetSynchronizedETagAsync("file"));
        }

        [TestMethod]
        public async Task Identical_local_and_downloaded_content_reconciles_explicit_upload()
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync();
            System.IO.File.WriteAllText(fixture.Data("file"), "two");
            fixture.Respond = r => r.Method.Method == "REPORT" ? Batch(FileMember(etag: "&quot;two&quot;"), "urn:sync:two") : Download("two", "\"two\"");
            await fixture.Session.PullAsync();
            Assert.AreEqual("\"two\"", await fixture.Session.GetSynchronizedETagAsync("file"));
        }

        [TestMethod]
        public async Task Initial_missing_files_never_mean_deletion_and_unknown_deletion_preserves_local_content()
        {
            using var fixture = new Fixture();
            System.IO.File.WriteAllText(fixture.Data("untracked"), "mine");
            fixture.Respond = _ => Batch();
            await fixture.Session.PullAsync();
            Assert.AreEqual("mine", System.IO.File.ReadAllText(fixture.Data("untracked")));
            fixture.Respond = _ => Batch(DavClientTests.Member(404, "/dav/untracked"), "urn:sync:two");
            await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => fixture.Session.PullAsync());
            Assert.AreEqual("mine", System.IO.File.ReadAllText(fixture.Data("untracked")));
        }

        [DataTestMethod]
        [DataRow("denied")]
        [DataRow("truncated")]
        [DataRow("member507")]
        [DataRow("missingetag")]
        [DataRow("duplicateetag")]
        [DataRow("weak")]
        [DataRow("wildcard")]
        [DataRow("collection")]
        [DataRow("texttype")]
        [DataRow("duplicate")]
        [DataRow("alias")]
        [DataRow("multiplehref")]
        [DataRow("resource403")]
        [DataRow("root404")]
        [DataRow("error")]
        [DataRow("propertyerror")]
        [DataRow("missingtype")]
        public async Task Unsupported_or_incomplete_report_never_downloads_or_mutates(string scenario)
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync();
            var member = scenario switch
            {
                "denied" => DavClientTests.Resource("<D:getetag/>", 403),
                "truncated" => DavClientTests.Member(507, "/dav/"),
                "member507" => DavClientTests.Member(507),
                "missingetag" => DavClientTests.Resource("<D:resourcetype/>"),
                "duplicateetag" => DavClientTests.Resource("<D:resourcetype/><D:getetag>\"a\"</D:getetag><D:getetag>\"b\"</D:getetag>"),
                "weak" => FileMember(etag: "W/&quot;one&quot;"),
                "wildcard" => FileMember(etag: "*"),
                "collection" => FileMember(type: "<D:collection/>"),
                "texttype" => FileMember(type: "not-a-file"),
                "duplicate" => FileMember() + FileMember(),
                "alias" => FileMember("FILE"),
                "multiplehref" => FileMember().Replace("<D:href>", "<D:href>/dav/other</D:href><D:href>"),
                "resource403" => DavClientTests.Member(403),
                "root404" => DavClientTests.Member(404, "/dav/"),
                "error" => FileMember().Replace("</D:response>", "<D:error><D:denied/></D:error></D:response>"),
                "propertyerror" => FileMember().Replace("</D:propstat>", "<D:error><D:denied/></D:error></D:propstat>"),
                _ => DavClientTests.Resource("<D:getetag>\"one\"</D:getetag>")
            };
            var before = System.IO.File.ReadAllText(fixture.Checkpoint);
            fixture.Handler.Requests.Clear();
            fixture.Respond = _ => Batch(member, "urn:sync:two");
            if (scenario == "multiplehref")
            {
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => fixture.Session.PullAsync());
            }
            else
            {
                await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => fixture.Session.PullAsync());
            }
            Assert.AreEqual(1, fixture.Handler.Requests.Count);
            Assert.AreEqual(before, System.IO.File.ReadAllText(fixture.Checkpoint));
            Assert.AreEqual("one", System.IO.File.ReadAllText(fixture.Data("file")));
        }

        [DataTestMethod]
        [DataRow("../file")]
        [DataRow("%2e%2e/file")]
        [DataRow("a%2Fb")]
        [DataRow("a%5Cb")]
        [DataRow("CON.txt")]
        [DataRow("LPT1")]
        [DataRow("COM9.txt")]
        [DataRow("name.")]
        [DataRow("name%20")]
        [DataRow("name%3Aads")]
        [DataRow("name%00")]
        [DataRow("sub/file")]
        public async Task Unsafe_report_paths_are_rejected_before_download(string name)
        {
            using var fixture = new Fixture();
            fixture.Respond = _ => Batch(FileMember(name));
            await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => fixture.Session.PullAsync());
            Assert.AreEqual(1, fixture.Handler.Requests.Count);
            Assert.IsFalse(System.IO.File.Exists(fixture.Checkpoint));
        }

        [DataTestMethod]
        [DataRow("https://evil.example/file")]
        [DataRow("/outside/file")]
        [DataRow("/dav/file?query")]
        [DataRow("/dav/file#fragment")]
        public async Task Hrefs_cannot_escape_collection_or_origin(string href)
        {
            using var fixture = new Fixture();
            fixture.Respond = _ => Batch(FileMember().Replace("/dav/file", href));
            try
            {
                await fixture.Session.PullAsync();
                Assert.Fail("Unsafe href accepted.");
            }
            catch (Exception error) when (error is FileSyncConflictException || error is ArgumentException)
            {
            }
            Assert.AreEqual(1, fixture.Handler.Requests.Count);
            Assert.IsFalse(System.IO.File.Exists(fixture.Checkpoint));
        }

        [TestMethod]
        public async Task Root_success_is_not_treated_as_file()
        {
            using var fixture = new Fixture();
            fixture.Respond = _ => Batch(DavClientTests.Resource("<D:resourcetype><D:collection/></D:resourcetype>", href: "/dav/"));
            await fixture.Session.PullAsync();
            Assert.AreEqual(0, Directory.GetFiles(fixture.Directory).Length);
        }

        [DataTestMethod]
        [DataRow("expired")]
        [DataRow("malformed")]
        [DataRow("missingtoken")]
        [DataRow("initialdelete")]
        public async Task Invalid_tokens_and_malformed_reports_never_reset_checkpoint(string scenario)
        {
            using var fixture = new Fixture();
            if (scenario != "initialdelete")
            {
                await fixture.SeedAsync();
            }
            var before = System.IO.File.Exists(fixture.Checkpoint) ? System.IO.File.ReadAllText(fixture.Checkpoint) : null;
            fixture.Respond = _ => scenario switch
            {
                "expired" => DavClientTests.XmlResponse("<D:error xmlns:D=\"DAV:\"><D:valid-sync-token/></D:error>", 403),
                "malformed" => DavClientTests.XmlResponse("<D:multistatus xmlns:D=\"DAV:\">"),
                "missingtoken" => DavClientTests.XmlResponse(DavClientTests.Multi("")),
                _ => Batch(DavClientTests.Member(404))
            };
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => fixture.Session.PullAsync());
            Assert.AreEqual(before, System.IO.File.Exists(fixture.Checkpoint) ? System.IO.File.ReadAllText(fixture.Checkpoint) : null);
        }

        [DataTestMethod]
        [DataRow(200, null)]
        [DataRow(200, "\"changed\"")]
        [DataRow(206, "\"one\"")]
        [DataRow(304, "\"one\"")]
        public async Task Download_must_confirm_complete_reported_revision(int code, string etag)
        {
            using var fixture = new Fixture();
            fixture.Respond = r => r.Method.Method == "REPORT" ? Batch(FileMember()) : Download(etag: etag, code: code);
            if (code == 200)
            {
                await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => fixture.Session.PullAsync());
            }
            else
            {
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => fixture.Session.PullAsync());
            }
            Assert.IsFalse(System.IO.File.Exists(fixture.Checkpoint));
            Assert.IsFalse(System.IO.File.Exists(fixture.Data("file")));
        }

        [TestMethod]
        public async Task Failed_or_canceled_transfer_leaves_no_data_or_checkpoint_and_cleans_staging()
        {
            using var fixture = new Fixture();
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => fixture.Session.PullAsync(canceled.Token));
            Assert.AreEqual(0, fixture.Handler.Requests.Count);
            fixture.Respond = r => r.Method.Method == "REPORT" ? Batch(FileMember() + FileMember("second")) :
                r.RequestUri.AbsolutePath.EndsWith("second", StringComparison.Ordinal) ? Download(code: 412) : Download();
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => fixture.Session.PullAsync());
            Assert.IsFalse(System.IO.File.Exists(fixture.Checkpoint));
            Assert.AreEqual(0, Directory.GetFiles(fixture.Directory).Length);
            Assert.AreEqual(0, Directory.GetFiles(fixture.State, "*.stage").Length);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Interrupted_body_never_publishes_partial_content(bool cancel)
        {
            using var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            fixture.Respond = r =>
            {
                if (r.Method.Method == "REPORT")
                {
                    return Batch(FileMember());
                }
                var response = Download();
                response.Content = new StreamContent(new InterruptedStream(cancellation, cancel));
                return response;
            };
            if (cancel)
            {
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => fixture.Session.PullAsync(cancellation.Token));
            }
            else
            {
                await Assert.ThrowsExceptionAsync<IOException>(() => fixture.Session.PullAsync(cancellation.Token));
            }
            Assert.IsFalse(System.IO.File.Exists(fixture.Checkpoint));
            Assert.IsFalse(System.IO.File.Exists(fixture.Pending));
            Assert.AreEqual(0, Directory.GetFiles(fixture.Directory).Length);
            Assert.AreEqual(0, Directory.GetFiles(fixture.State, "*.stage").Length);
        }

        [TestMethod]
        public async Task Restart_reclaims_only_session_owned_orphan_stages()
        {
            using var fixture = new Fixture();
            var stage = Path.Combine(fixture.State, Guid.NewGuid().ToString("N") + ".stage");
            System.IO.File.WriteAllText(stage, "abandoned");
            var unrelated = Path.Combine(fixture.State, "not-a-stage.stage");
            System.IO.File.WriteAllText(unrelated, "keep");
            Assert.IsNull(await fixture.NewSession().GetSynchronizedETagAsync("untracked"));
            Assert.IsFalse(System.IO.File.Exists(stage));
            Assert.IsTrue(System.IO.File.Exists(unrelated));
            await fixture.NewSession().PullAsync();
        }

        [TestMethod]
        public async Task Checkpoint_failure_retains_recoverable_journal_after_applying_content()
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync();
            fixture.PendingBatch(false, false);
            System.IO.File.Delete(fixture.Checkpoint);
            Directory.CreateDirectory(fixture.Checkpoint);
            fixture.Handler.Requests.Clear();
            await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => fixture.NewSession().GetSynchronizedETagAsync("file"));
            Assert.AreEqual("two", System.IO.File.ReadAllText(fixture.Data("file")));
            Assert.IsTrue(System.IO.File.Exists(fixture.Pending));
            Assert.AreEqual(0, fixture.Handler.Requests.Count);
            Directory.Delete(fixture.Checkpoint);
            Assert.AreEqual("\"two\"", await fixture.NewSession().GetSynchronizedETagAsync("file"));
            Assert.IsFalse(System.IO.File.Exists(fixture.Pending));
        }

        [TestMethod]
        public async Task Journal_publication_failure_does_not_change_local_files()
        {
            using var fixture = new Fixture();
            fixture.Respond = r => r.Method.Method == "REPORT" ? Batch(FileMember()) : Download();
            Directory.CreateDirectory(Path.Combine(fixture.State, "writing.json"));
            await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => fixture.Session.PullAsync());
            Assert.AreEqual(0, Directory.GetFiles(fixture.Directory).Length);
            Assert.IsFalse(System.IO.File.Exists(fixture.Pending));
            Assert.IsFalse(System.IO.File.Exists(fixture.Checkpoint));
        }

        [DataTestMethod]
        [DataRow(false, false, false)]
        [DataRow(true, false, false)]
        [DataRow(true, true, false)]
        [DataRow(true, false, true)]
        public async Task Restart_replays_pending_journal_before_network_and_commits_only_after_all_files(bool alreadyApplied, bool delete, bool checkpointCommitted)
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync();
            fixture.PendingBatch(alreadyApplied, delete);
            if (checkpointCommitted)
            {
                System.IO.File.WriteAllText(fixture.Checkpoint, JsonNode.Parse(System.IO.File.ReadAllText(fixture.Pending))["State"].ToJsonString());
            }
            fixture.Respond = _ =>
            {
                Assert.IsFalse(System.IO.File.Exists(fixture.Pending));
                Assert.AreEqual(delete ? null : "two", System.IO.File.Exists(fixture.Data("file")) ? System.IO.File.ReadAllText(fixture.Data("file")) : null);
                StringAssert.Contains(System.IO.File.ReadAllText(fixture.Checkpoint), "urn:sync:two");
                return Batch(token: "urn:sync:three");
            };
            await fixture.NewSession().PullAsync();
            Assert.IsFalse(System.IO.File.Exists(fixture.Pending));
            Assert.AreEqual(0, Directory.GetFiles(fixture.State, "*.stage").Length);
        }

        [TestMethod]
        public async Task Partial_replay_conflict_keeps_old_token_and_restart_completes_remaining_work()
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync();
            fixture.PendingBatch(false, false);
            var pending = JsonNode.Parse(System.IO.File.ReadAllText(fixture.Pending));
            var next = pending["Changes"][0].DeepClone();
            next["Name"] = "other";
            next["OldHash"] = null;
            var stage = Guid.NewGuid().ToString("N") + ".stage";
            next["Stage"] = stage;
            System.IO.File.WriteAllText(Path.Combine(fixture.State, stage), "two");
            pending["Changes"].AsArray().Add(next);
            pending["State"]["Entries"]["other"] = pending["State"]["Entries"]["file"].DeepClone();
            System.IO.File.WriteAllText(fixture.Pending, pending.ToJsonString());
            System.IO.File.WriteAllText(fixture.Data("other"), "edit");
            fixture.Handler.Requests.Clear();
            await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => fixture.NewSession().GetSynchronizedETagAsync("file"));
            Assert.AreEqual("two", System.IO.File.ReadAllText(fixture.Data("file")));
            Assert.AreEqual("edit", System.IO.File.ReadAllText(fixture.Data("other")));
            StringAssert.Contains(System.IO.File.ReadAllText(fixture.Checkpoint), "urn:sync:one");
            Assert.AreEqual(0, fixture.Handler.Requests.Count);
            System.IO.File.Delete(fixture.Data("other"));
            Assert.AreEqual("\"two\"", await fixture.NewSession().GetSynchronizedETagAsync("other"));
            Assert.AreEqual("two", System.IO.File.ReadAllText(fixture.Data("other")));
            StringAssert.Contains(System.IO.File.ReadAllText(fixture.Checkpoint), "urn:sync:two");
            Assert.IsFalse(System.IO.File.Exists(fixture.Pending));
        }

        [DataTestMethod]
        [DataRow("local")]
        [DataRow("stage")]
        [DataRow("missing")]
        [DataRow("name")]
        [DataRow("identity")]
        [DataRow("version")]
        [DataRow("token")]
        public async Task Recovery_conflicts_preserve_pending_journal_and_old_token(string scenario)
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync();
            fixture.PendingBatch(false, false);
            var pending = JsonNode.Parse(System.IO.File.ReadAllText(fixture.Pending));
            if (scenario == "local")
            {
                System.IO.File.WriteAllText(fixture.Data("file"), "edited");
            }
            else if (scenario is "stage" or "missing")
            {
                var stage = Path.Combine(fixture.State, pending["Changes"][0]["Stage"].GetValue<string>());
                if (scenario == "missing")
                {
                    System.IO.File.Delete(stage);
                }
                else
                {
                    System.IO.File.WriteAllText(stage, "corrupt");
                }
            }
            else if (scenario == "name")
            {
                pending["Changes"][0]["Stage"] = "../outside";
            }
            else
            {
                pending["State"][scenario switch { "identity" => "Identity", "version" => "Version", _ => "Token" }] =
                    scenario == "version" ? JsonValue.Create(99) : JsonValue.Create(scenario == "token" ? "invalid token" : "other root");
            }
            System.IO.File.WriteAllText(fixture.Pending, pending.ToJsonString());
            fixture.Handler.Requests.Clear();
            var before = System.IO.File.ReadAllText(fixture.Checkpoint);
            await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => fixture.NewSession().PullAsync());
            Assert.AreEqual(before, System.IO.File.ReadAllText(fixture.Checkpoint));
            Assert.AreEqual(0, fixture.Handler.Requests.Count);
            Assert.IsTrue(System.IO.File.Exists(fixture.Pending));
        }

        [TestMethod]
        public async Task Corrupt_checkpoint_is_not_silently_reinitialized()
        {
            using var fixture = new Fixture();
            System.IO.File.WriteAllText(fixture.Checkpoint, "null");
            await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => fixture.Session.PullAsync());
            System.IO.File.WriteAllText(fixture.Checkpoint, "{broken");
            await Assert.ThrowsExceptionAsync<System.Text.Json.JsonException>(() => fixture.Session.PullAsync());
            Assert.AreEqual(0, fixture.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task State_binding_and_exclusive_ownership_are_enforced()
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync();
            var other = Path.Combine(fixture.Root, "other");
            Directory.CreateDirectory(other);
            var session = new FileSyncSession(fixture.Client, DavPath.Root, other, fixture.State);
            await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => session.GetSynchronizedETagAsync("file"));
            using var lease = new FileStream(Path.Combine(fixture.State, "session.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await Assert.ThrowsExceptionAsync<IOException>(() => fixture.NewSession().PullAsync());
        }

        [TestMethod]
        public async Task Local_directory_at_file_path_is_not_overwritten()
        {
            using var fixture = new Fixture();
            Directory.CreateDirectory(fixture.Data("file"));
            fixture.Respond = r => r.Method.Method == "REPORT" ? Batch(FileMember()) : Download();
            await Assert.ThrowsExceptionAsync<FileSyncConflictException>(() => fixture.Session.PullAsync());
            Assert.IsTrue(Directory.Exists(fixture.Data("file")));
        }

        [TestMethod]
        public void Constructor_and_outgoing_argument_validation_are_explicit()
        {
            using var fixture = new Fixture();
            Assert.ThrowsException<ArgumentNullException>(() => new FileSyncSession(null, DavPath.Root, fixture.Directory, fixture.State));
            Assert.ThrowsException<ArgumentException>(() => new FileSyncSession(fixture.Client, DavPath.FromSegments("file"), fixture.Directory, fixture.State));
            Assert.ThrowsException<ArgumentException>(() => new FileSyncSession(fixture.Client, DavPath.Parse("?query"), fixture.Directory, fixture.State));
            Assert.ThrowsException<ArgumentException>(() => new FileSyncSession(fixture.Client, DavPath.Root, "relative", fixture.State));
            Assert.ThrowsException<ArgumentException>(() => new FileSyncSession(fixture.Client, DavPath.Root, Path.Combine(fixture.Root, "missing"), fixture.State));
            Assert.ThrowsException<ArgumentException>(() => new FileSyncSession(fixture.Client, DavPath.Root, fixture.Directory, fixture.Directory));
            Assert.ThrowsException<ArgumentException>(() => new FileSyncSession(fixture.Client, DavPath.Root, fixture.Root, fixture.State));
            Assert.ThrowsException<ArgumentException>(() => new FileSyncSession(fixture.Client, DavPath.Root, fixture.State, fixture.Root));
            Assert.ThrowsException<ArgumentException>(() => new FileSyncSession(fixture.Client, DavPath.Root, Path.GetPathRoot(fixture.Root), fixture.State));
            using var stream = new MemoryStream();
            foreach (var name in new[] { "", ".", "..", "file/", "file\\a", "a*", "a?", "a|", "a<", "a>", "a\"", "AUX", "PRN", "NUL", "COM¹.txt", "CON .txt", "CONIN$", "CONOUT$" })
            {
                Assert.ThrowsException<FileSyncConflictException>(() => fixture.Session.UploadAsync(name, stream));
            }
            Assert.ThrowsException<ArgumentNullException>(() => fixture.Session.DeleteRemoteAsync(null, "\"one\""));
            Assert.ThrowsException<FileSyncConflictException>(() => fixture.Session.UploadAsync("file", stream, "invalid"));
            Assert.ThrowsException<FileSyncConflictException>(() => fixture.Session.DeleteRemoteAsync("file", "W/\"one\""));
            Assert.ThrowsException<FileSyncConflictException>(() => fixture.Session.DeleteRemoteAsync("file", "*"));
        }

        private sealed class InterruptedStream : MemoryStream
        {
            private readonly CancellationTokenSource m_cancellation;

            private readonly bool m_cancel;

            private bool m_started;

            public InterruptedStream(CancellationTokenSource cancellation, bool cancel) : base(new byte[10])
            {
                m_cancellation = cancellation;
                m_cancel = cancel;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (!m_started)
                {
                    m_started = true;
                    return base.ReadAsync(buffer.Slice(0, 1), cancellationToken);
                }
                if (m_cancel)
                {
                    m_cancellation.Cancel();
                    throw new OperationCanceledException(cancellationToken);
                }
                throw new IOException("Interrupted body.");
            }

            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
                CopyWithReadsAsync(destination, cancellationToken);

            private async Task CopyWithReadsAsync(Stream destination, CancellationToken cancellationToken)
            {
                var buffer = new byte[1];
                while (await ReadAsync(buffer.AsMemory(), cancellationToken) != 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(), cancellationToken);
                }
            }
        }

        private sealed class Fixture : IDisposable
        {
            public Fixture()
            {
                Root = Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".sync-tests", Guid.NewGuid().ToString("N"));
                Directory = Path.Combine(Root, "data");
                State = Path.Combine(Root, "state");
                System.IO.Directory.CreateDirectory(Directory);
                System.IO.Directory.CreateDirectory(State);
                Handler = new StubHttpMessageHandler(r => Respond(r));
                Transport = new System.Net.Http.HttpClient(Handler);
                Client = new DavClient(DavClientTests.Endpoint, Transport);
                Session = NewSession();
            }

            public string Root
            {
                get;
            }

            public string Directory
            {
                get;
            }

            public string State
            {
                get;
            }

            public string Checkpoint => Path.Combine(State, "checkpoint.json");

            public string Pending => Path.Combine(State, "pending.json");

            public StubHttpMessageHandler Handler
            {
                get;
            }

            public System.Net.Http.HttpClient Transport
            {
                get;
            }

            public DavClient Client
            {
                get;
            }

            public FileSyncSession Session
            {
                get;
            }

            public Func<HttpRequestMessage, HttpResponseMessage> Respond
            {
                get;
                set;
            } = _ => Batch();

            public string Data(string name) => Path.Combine(Directory, name);

            public FileSyncSession NewSession() => new FileSyncSession(Client, DavPath.Root, Directory, State);

            public async Task SeedAsync()
            {
                Respond = r => r.Method.Method == "REPORT" ? Batch(FileMember()) : Download();
                await Session.PullAsync();
            }

            public void PendingBatch(bool applied, bool delete)
            {
                var state = JsonNode.Parse(System.IO.File.ReadAllText(Checkpoint));
                var oldHash = state["Entries"]["file"]["Hash"].GetValue<string>();
                var stage = Guid.NewGuid().ToString("N") + ".stage";
                var hash = delete ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("two")));
                state["Token"] = "urn:sync:two";
                if (delete)
                {
                    state["Entries"].AsObject().Remove("file");
                }
                else
                {
                    state["Entries"]["file"]["Hash"] = hash;
                    state["Entries"]["file"]["ETag"] = "\"two\"";
                }
                var journal = new JsonObject
                {
                    ["State"] = state,
                    ["Changes"] = new JsonArray(new JsonObject
                    {
                        ["Name"] = "file",
                        ["OldHash"] = oldHash,
                        ["Hash"] = hash,
                        ["ETag"] = delete ? null : "\"two\"",
                        ["Stage"] = delete ? null : stage
                    })
                };
                System.IO.File.WriteAllText(Pending, journal.ToJsonString());
                if (!delete)
                {
                    System.IO.File.WriteAllText(Path.Combine(State, stage), "two");
                }
                if (applied)
                {
                    if (delete)
                    {
                        System.IO.File.Delete(Data("file"));
                    }
                    else
                    {
                        System.IO.File.Move(Path.Combine(State, stage), Data("file"), true);
                    }
                }
            }

            public void Dispose()
            {
                Client.Dispose();
                Transport.Dispose();
                System.IO.Directory.Delete(Root, true);
            }
        }
    }
}
