using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Extensions;
using WebDAVClient.Helpers;
using WebDAVClient.Model;
using WebDAVClient.Protocol;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DavClientTests
    {
        internal static readonly Uri Endpoint = new Uri("https://example.com/dav/");

        internal static readonly DavPath File = DavPath.FromSegments("file");

        internal static string Multi(string members) => "<D:multistatus xmlns:D=\"DAV:\" xmlns:X=\"urn:test\">" + members + "</D:multistatus>";

        internal static string Resource(string properties = "<D:displayname>file</D:displayname>", int status = 200, string href = "/dav/file") =>
            "<D:response><D:href>" + href + "</D:href><D:propstat><D:prop>" + properties + "</D:prop><D:status>HTTP/1.1 " + status + " Result</D:status></D:propstat></D:response>";

        internal static string Member(int status, string href = "/dav/file") =>
            "<D:response><D:href>" + href + "</D:href><D:status>HTTP/1.1 " + status + " Result</D:status></D:response>";

        internal static HttpResponseMessage XmlResponse(string xml, int code = 207) => new HttpResponseMessage((HttpStatusCode)code)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        };

        [TestMethod]
        public async Task Direct_requests_do_not_discover_or_mutate_connection_and_keep_transport_caller_owned()
        {
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
            using var http = new System.Net.Http.HttpClient(handler);
            var client = new DavClient(Endpoint, http);
            var head = await client.HeadAsync(File);
            Assert.AreEqual(204, head.StatusCode);
            Assert.AreEqual("HEAD", handler.Requests.Single().Method.Method);
            Assert.AreEqual(new Uri(Endpoint, "file"), head.EffectiveUri);
            Assert.AreEqual(Endpoint, client.Endpoint);
            client.Dispose();
            client.Dispose();
            await http.GetAsync(Endpoint);
            await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => client.HeadAsync(File));
        }

        [DataTestMethod]
        [DataRow(DavPropertySelection.All, DavDepth.Zero, "allprop")]
        [DataRow(DavPropertySelection.Selected, DavDepth.One, "prop")]
        [DataRow(DavPropertySelection.Names, DavDepth.Infinity, "propname")]
        public async Task Propfind_selects_properties_and_retains_root_and_qualified_outcomes(DavPropertySelection selection, DavDepth depth, string element)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource(href: "/dav/") + Resource("<X:a/>", 403))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.PropFindAsync(DavPath.Root, new PropFindOptions
            {
                Selection = selection,
                Depth = depth,
                Properties = selection == DavPropertySelection.Names ? null : new[]
                {
                    new PropertyName("urn:test", "a")
                }
            });
            var request = handler.Requests.Single();
            var body = XElement.Parse(request.BodyAsString());
            Assert.IsNotNull(body.Element(XName.Get(element, "DAV:")));
            Assert.AreEqual(2, result.Resources.Count);
            Assert.AreEqual("/dav/", result.Resources[0].Hrefs[0]);
            Assert.AreEqual(403, result.Resources[1].PropertyStatuses[0].StatusCode);
            Assert.IsNotNull(result.Document);
            var failure = Assert.ThrowsException<WebDAVException>(() => result.EnsureSucceeded());
            Assert.AreEqual(207, failure.GetHttpCode());
            Assert.AreEqual(207, failure.ResponseStatusCode);
            Assert.AreEqual(403, failure.FailedStatusCode);
        }

        [TestMethod]
        public async Task Proppatch_keeps_repeated_ordered_instructions_and_supports_writable_dav_properties()
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource())));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var name = new PropertyName("DAV:", "displayname");
            var result = await client.UpdatePropertiesAsync(File, new[]
                {
                    DavPropertyChange.Remove(name), DavPropertyChange.Set(DavXmlValue.Text(name, "new & name")), DavPropertyChange.Remove(name)
                }, new DavRequestOptions
                {
                    IfMatch = "\"etag\"",
                    Conditions = DavIfConditions.Untagged(new DavConditionList(DavCondition.StateToken("urn:lock:1")))
                });
            result.EnsureSucceeded();
            CollectionAssert.AreEqual(new[]
                {
                    "remove", "set", "remove"
                }, XElement.Parse(handler.Requests.Single().BodyAsString()).Elements().Select(e => e.Name.LocalName).ToArray());
            Assert.AreEqual("\"etag\"", handler.Requests[0].Headers["If-Match"].Single());
            Assert.AreEqual("(<urn:lock:1>)", handler.Requests[0].Headers["If"].Single());
        }

        [DataTestMethod]
        [DataRow("MKCOL")]
        [DataRow("DELETE")]
        [DataRow("MOVE")]
        [DataRow("COPY")]
        [DataRow("UNLOCK")]
        public async Task Namespace_operations_have_no_spurious_body_and_preserve_rejections(string method)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse("<D:error xmlns:D=\"DAV:\"><D:lock-token-submitted/></D:error>", 409));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var destination = DavPath.FromSegments("target");
            var result = method switch
            {
                "MKCOL" => await client.CreateCollectionAsync(File),
                "DELETE" => await client.DeleteAsync(File),
                "MOVE" => await client.MoveAsync(File, destination),
                "COPY" => await client.CopyAsync(File, destination, new CopyOptions
                {
                    Depth = DavDepth.Zero,
                    Overwrite = true
                }),
                _ => await client.UnlockAsync(File, "urn:lock:1")
            };
            var captured = handler.Requests.Single();
            Assert.AreEqual(method, captured.Method.Method);
            Assert.IsNull(captured.Body);
            Assert.AreEqual("lock-token-submitted", result.Errors.Single().Name.LocalName);
            Assert.ThrowsException<WebDAVConflictException>(() => result.EnsureSucceeded());
            if (method == "MOVE" || method == "COPY")
            {
                Assert.AreEqual("https://example.com/dav/target", captured.Headers["Destination"].Single());
                Assert.AreEqual(method == "MOVE" ? "F" : "T", captured.Headers["Overwrite"].Single());
            }
            if (method == "UNLOCK")
            {
                Assert.AreEqual("<urn:lock:1>", captured.Headers["Lock-Token"].Single());
            }
        }

        [TestMethod]
        public async Task Recursive_mutation_failure_retains_successful_and_denied_resources()
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Member(204, "/dav/done") + Member(409))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.DeleteAsync(File);
            var exception = Assert.ThrowsException<WebDAVConflictException>(() => result.EnsureSucceeded());
            Assert.AreEqual(207, exception.GetHttpCode());
            Assert.AreEqual(2, exception.ResourceStatuses.Count);
            Assert.AreEqual(204, exception.ResourceStatuses[0].StatusCode);
            Assert.AreEqual("/dav/file", exception.ResourceStatuses[1].Href);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Upload_ownership_is_explicit_and_nonseekable_streams_work(bool transferOwnership)
        {
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            using var content = new NonSeekableStream();
            var result = await client.UploadAsync(File, content, new UploadOptions
            {
                DisposeContent = transferOwnership,
                IfNoneMatch = "*",
                ContentType = "text/plain"
            });
            result.EnsureSucceeded();
            Assert.AreEqual(transferOwnership, content.Disposed);
            Assert.AreEqual("payload", handler.Requests.Single().BodyAsString());
            Assert.AreEqual("*", handler.Requests.Single().Headers["If-None-Match"].Single());
        }

        [TestMethod]
        public async Task Seekable_upload_uses_remaining_length_and_does_not_replay_after_transport_failure()
        {
            using var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("lost response"));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
            content.Position = 3;
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.UploadAsync(File, content));
            Assert.AreEqual("load", handler.Requests.Single().BodyAsString());
            Assert.IsTrue(content.CanRead);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Options_are_evidence_and_preserve_unknown_tokens()
        {
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.NoContent);
                response.Headers.Add("DAV", new[]
                    {
                        "1, 3", "<urn:unknown>"
                    });
                response.Content.Headers.Add("Allow", "GET, PROPFIND");
                response.Headers.Add("DASL", "<DAV:basicsearch>");
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.GetCapabilitiesAsync(DavPath.Root);
            result.EnsureHttpSuccess();
            CollectionAssert.AreEqual(new[]
                {
                    "1", "3", "<urn:unknown>"
                }, result.DavTokens.ToArray());
            Assert.AreEqual(2, result.AllowedMethods.Count);
            Assert.AreEqual("<DAV:basicsearch>", result.SearchGrammars.Single());
            Assert.IsTrue(result.ObservedAt <= DateTimeOffset.UtcNow);
            Assert.IsNull(result.GetHeader("missing"));
        }

        [DataTestMethod]
        [DataRow(200)]
        [DataRow(304)]
        [DataRow(412)]
        [DataRow(416)]
        public async Task Downloads_keep_metadata_and_conditional_outcomes_without_fake_streams(int status)
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StreamContent(content)
                };
                response.Headers.ETag = new EntityTagHeaderValue("\"tag\"");
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
                response.Content.Headers.LastModified = DateTimeOffset.UnixEpoch;
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.DownloadAsync(File, new DownloadOptions
            {
                IfNoneMatch = "\"tag\""
            });
            Assert.AreEqual(status == 304, result.NotModified);
            Assert.AreEqual(status == 200, result.Content != null);
            Assert.AreEqual("\"tag\"", result.ETag);
            Assert.AreEqual("text/plain", result.ContentType);
            Assert.AreEqual(7L, result.ContentLength);
            Assert.AreEqual(DateTimeOffset.UnixEpoch, result.LastModified);
            Assert.IsNull(result.ContentRange);
            Assert.IsTrue(content.CanRead);
            await result.DisposeAsync();
            Assert.IsFalse(content.CanRead);
        }

        [DataTestMethod]
        [DataRow(0L, 2L)]
        [DataRow(0L, null)]
        [DataRow(null, 3L)]
        public async Task Download_validates_closed_open_and_suffix_ranges(long? from, long? to)
        {
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(new byte[3])
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 2, 3);
                response.Headers.ETag = new EntityTagHeaderValue("\"tag\"");
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            using var result = await client.DownloadAsync(File, new DownloadOptions
            {
                Range = new RangeHeaderValue(from, to),
                IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"tag\""))
            });
            Assert.AreEqual("bytes 0-2/3", result.ContentRange);
            Assert.AreEqual("identity", handler.Requests.Single().Headers["Accept-Encoding"].Single());
            Assert.IsNotNull(result.Content);
        }

        [DataTestMethod]
        [DataRow("ignored")]
        [DataRow("missing")]
        [DataRow("wrong-start")]
        [DataRow("wrong-length")]
        [DataRow("changed")]
        [DataRow("encoded")]
        public async Task Unsafe_range_responses_are_never_returned_as_appendable_content(string problem)
        {
            using var payload = new MemoryStream(new byte[3]);
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(problem == "ignored" ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
                {
                    Content = new StreamContent(payload)
                };
                if (problem != "missing")
                {
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(problem == "wrong-start" ? 1 : 0, 2, 3);
                }
                if (problem == "wrong-length")
                {
                    response.Content.Headers.ContentLength = 4;
                }
                if (problem == "changed")
                {
                    response.Headers.ETag = new EntityTagHeaderValue("\"other\"");
                }
                if (problem == "encoded")
                {
                    response.Content.Headers.ContentEncoding.Add("gzip");
                }
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.DownloadAsync(File,
                new DownloadOptions
                {
                    Range = new RangeHeaderValue(0, 2),
                    IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"tag\""))
                }));
            Assert.IsFalse(payload.CanRead);
        }

        internal static string LockXml(string timeout = "Second-10") => """
            <D:prop xmlns:D="DAV:"><D:lockdiscovery><D:activelock>
            <D:lockscope><D:shared/></D:lockscope><D:locktype><D:write/></D:locktype><D:depth>0</D:depth>
            <D:owner xml:lang="en"><D:href>mailto:owner@example.com</D:href></D:owner>
            <D:lockroot><D:href>/dav/file</D:href></D:lockroot><D:locktoken><D:href>urn:lock:1</D:href></D:locktoken>
            """ + (timeout == null ? "" : "<D:timeout>" + timeout + "</D:timeout>") + "</D:activelock></D:lockdiscovery></D:prop>";

        [DataTestMethod]
        [DataRow("Infinite")]
        [DataRow("Second-10")]
        [DataRow(null)]
        public async Task Lock_observations_preserve_granted_timeout_and_structured_owner(string timeout)
        {
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = XmlResponse(LockXml(timeout), 201);
                response.Headers.Add("Lock-Token", "<urn:lock:1>");
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.AcquireLockAsync(File, new LockOptions
            {
                Scope = DavLockScope.Shared,
                Depth = DavDepth.Infinity,
                Owner = new DavXmlValue(new XElement(XName.Get("owner", "DAV:"), "owner")),
                Timeouts = new[]
                {
                    DavLockTimeout.Infinite, DavLockTimeout.Finite(TimeSpan.FromSeconds(30))
                }
            });
            result.EnsureSucceeded();
            var granted = result.Locks.Single();
            Assert.AreEqual(timeout == "Infinite", granted.Timeout.IsInfinite);
            Assert.AreEqual(timeout == "Second-10" ? TimeSpan.FromSeconds(10) : null, granted.Timeout.Duration);
            Assert.AreEqual("urn:lock:1", granted.Token);
            Assert.AreEqual("/dav/file", granted.Root);
            Assert.AreEqual("shared", granted.Scope.LocalName);
            Assert.IsFalse(granted.Owner.TryGetText(out _));
            Assert.AreEqual("<urn:lock:1>", result.LockToken);
            StringAssert.Contains(handler.Requests.Single().BodyAsString(), "shared");
            Assert.AreEqual("Infinite, Second-30", handler.Requests.Single().Headers["Timeout"].Single());
        }

        [TestMethod]
        public async Task Refresh_has_no_request_body_and_rejects_missing_response_confirmation()
        {
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.RefreshLockAsync(File, "urn:lock:1"));
            Assert.IsNull(handler.Requests.Single().Body);
        }

        [TestMethod]
        public async Task Recursive_lock_failure_is_not_a_granted_tree_lock()
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Member(423))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.AcquireLockAsync(File);
            Assert.AreEqual(0, result.Locks.Count);
            Assert.AreEqual(1, result.Resources.Count);
            Assert.ThrowsException<WebDAVException>(() => result.EnsureSucceeded());
        }

        [TestMethod]
        public async Task Sync_profile_preserves_deletions_and_checkpoint_without_treating_404_as_generic_failure()
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Member(404) + Member(507, "/dav/") + "<D:sync-token>urn:sync:next</D:sync-token>")));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.SyncCollectionAsync(DavPath.Root, "urn:sync:previous", new[]
                {
                    DavProperties.ETag.Name
                }, limit: 10);
            Assert.AreEqual("urn:sync:next", result.SyncToken);
            Assert.IsTrue(result.HasMore);
            Assert.AreEqual(404, result.Response.Resources[0].StatusCode);
            result.Response.EnsureHttpSuccess();
            var body = XElement.Parse(handler.Requests.Single().BodyAsString());
            Assert.AreEqual("1", body.Element(XName.Get("sync-level", "DAV:")).Value);
            Assert.AreEqual("10", body.Descendants(XName.Get("nresults", "DAV:")).Single().Value);
            Assert.AreEqual("0", handler.Requests.Single().Headers["Depth"].Single());
        }

        [TestMethod]
        public async Task Streaming_completion_requires_full_enumeration_and_error_tail_cannot_certify_a_snapshot()
        {
            foreach (var suffix in new[]
            {
                "</D:multistatus>", "<broken>"
            })
            {
                using var handler = new StubHttpMessageHandler(_ => XmlResponse("<D:multistatus xmlns:D=\"DAV:\">" + Resource() + Resource(href: "/dav/other") + suffix));
                using var http = new System.Net.Http.HttpClient(handler);
                using var client = new DavClient(Endpoint, http);
                await using var listing = await client.OpenListingAsync(DavPath.Root);
                var count = 0;
                async Task Read()
                {
                    await foreach (var resource in listing.ReadResourcesAsync())
                    {
                        count++;
                        Assert.AreEqual(count == 1 ? "/dav/file" : "/dav/other", resource.Hrefs[0]);
                        Assert.IsFalse(listing.IsComplete);
                    }
                }
                if (suffix.StartsWith("<broken", StringComparison.Ordinal))
                {
                    await Assert.ThrowsExceptionAsync<WebDAVException>(Read);
                    Assert.IsFalse(listing.IsComplete);
                }
                else
                {
                    await Read();
                    Assert.AreEqual(2, count);
                    Assert.IsTrue(listing.IsComplete);
                }
            }
        }

        [TestMethod]
        public async Task Early_break_releases_listing_but_does_not_certify_completion()
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource() + Resource(href: "/dav/other"))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await using var listing = await client.OpenListingAsync(DavPath.Root);
            await foreach (var resource in listing.ReadResourcesAsync())
            {
                break;
            }
            Assert.IsFalse(listing.IsComplete);
            await Assert.ThrowsExceptionAsync<ObjectDisposedException>(async () =>
            {
                await foreach (var resource in listing.ReadResourcesAsync())
                {
                }
            });
        }

        [TestMethod]
        public async Task Large_streamed_listing_does_not_retain_previous_resources_and_exposes_final_extensions()
        {
            var xml = new StringBuilder("<D:multistatus xmlns:D=\"DAV:\">");
            for (int index = 0; index < 100000; index++)
            {
                xml.Append(Member(200, "/dav/file" + index));
            }
            xml.Append("<D:responsedescription>complete</D:responsedescription></D:multistatus>");
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml.ToString()));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http, new DavClientOptions
            {
                XmlLimits = new DavXmlLimits
                {
                    MaxResponseCharacters = 32 * 1024 * 1024
                }
            });
            await using var listing = await client.OpenListingAsync(DavPath.Root);
            WeakReference first = null;
            int count = 0;
            await foreach (var resource in listing.ReadResourcesAsync())
            {
                first ??= new WeakReference(resource);
                count++;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Assert.IsFalse(first.IsAlive, "Streaming must not retain earlier resource objects.");
            Assert.AreEqual(100000, count);
            Assert.IsTrue(listing.IsComplete);
            Assert.AreEqual("responsedescription", listing.Extensions.Single().Name.LocalName);
        }

        private sealed class NonSeekableStream : MemoryStream
        {
            public NonSeekableStream() : base(Encoding.UTF8.GetBytes("payload"))
            {
            }

            public override bool CanSeek => false;

            public bool Disposed
            {
                get;
                private set;
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
