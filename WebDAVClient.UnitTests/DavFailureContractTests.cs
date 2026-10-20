using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
using static WebDAVClient.UnitTests.DavClientTests;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DavFailureContractTests
    {
        private static DavPath Target => DavClientTests.File;

        [DataTestMethod]
        [DataRow("MKCOL", 201, true)]
        [DataRow("MKCOL", 202, false)]
        [DataRow("MOVE", 201, true)]
        [DataRow("MOVE", 204, true)]
        [DataRow("MOVE", 207, true)]
        [DataRow("MOVE", 202, false)]
        [DataRow("COPY", 201, true)]
        [DataRow("COPY", 204, true)]
        [DataRow("COPY", 207, true)]
        [DataRow("COPY", 202, false)]
        [DataRow("DELETE", 200, true)]
        [DataRow("DELETE", 204, true)]
        [DataRow("DELETE", 207, true)]
        [DataRow("DELETE", 202, false)]
        [DataRow("UNLOCK", 204, true)]
        [DataRow("UNLOCK", 202, false)]
        [DataRow("PUT", 200, true)]
        [DataRow("PUT", 201, true)]
        [DataRow("PUT", 204, true)]
        [DataRow("PUT", 202, false)]
        public async Task Confirmed_completion_is_operation_specific_not_any_success_class(string method, int status, bool confirmed)
        {
            using var handler = new StubHttpMessageHandler(_ => status == 207 ? XmlResponse(Multi(Member(200))) : new HttpResponseMessage((HttpStatusCode)status));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            using var content = new MemoryStream(new byte[] { 1 });
            var result = method switch
            {
                "MKCOL" => await client.CreateCollectionAsync(Target),
                "MOVE" => await client.MoveAsync(Target, DavPath.Parse("other"), new MoveOptions { Overwrite = true }),
                "COPY" => await client.CopyAsync(Target, DavPath.Parse("other")),
                "DELETE" => await client.DeleteAsync(Target),
                "UNLOCK" => await client.UnlockAsync(Target, "urn:lock"),
                _ => await client.UploadAsync(Target, content)
            };
            if (confirmed)
            {
                result.EnsureSucceeded();
            }
            else
            {
                Assert.ThrowsException<WebDAVException>(result.EnsureSucceeded);
            }
        }

        [DataTestMethod]
        [DataRow("urn:sync:valid", true)]
        [DataRow("not a uri", false)]
        [DataRow("relative", false)]
        public async Task Sync_checkpoint_is_an_opaque_uri_and_initial_sync_cannot_report_deletions(string token, bool valid)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi("<D:sync-token>" + token + "</D:sync-token>")));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            if (valid)
            {
                var result = await client.SyncCollectionAsync(DavPath.Root, null, Array.Empty<PropertyName>(), options: new ReportOptions());
                Assert.AreEqual(token, result.SyncToken);
                Assert.IsFalse(result.HasMore);
            }
            else
            {
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.SyncCollectionAsync(DavPath.Root, null, Array.Empty<PropertyName>()));
            }
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.SyncCollectionAsync(DavPath.Root, null, Array.Empty<PropertyName>(),
                options: new ReportOptions { Depth = DavDepth.One }));
        }

        [TestMethod]
        public async Task Initial_sync_rejects_deleted_member_and_member_storage_failure_is_not_paging()
        {
            int status = 404;
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Member(status) + "<D:sync-token>urn:next</D:sync-token>")));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.SyncCollectionAsync(DavPath.Root, null, Array.Empty<PropertyName>()));
            status = 507;
            var result = await client.SyncCollectionAsync(DavPath.Root, "urn:previous", Array.Empty<PropertyName>());
            Assert.IsFalse(result.HasMore);
            Assert.AreEqual(507, result.Response.Resources.Single().StatusCode);
        }

        [TestMethod]
        public void Endpoint_and_resource_limits_are_validated_before_transport_allocation()
        {
            foreach (var endpoint in new[]
            {
                new Uri("relative", UriKind.Relative), new Uri("ftp://example.com/"), new Uri("https://example.com/dav"),
                new Uri("https://example.com/?q=x"), new Uri("https://example.com/#f"), new Uri("https://name@example.com/")
            })
            {
                Assert.ThrowsException<ArgumentException>(() => new DavClient(endpoint));
            }
            Assert.ThrowsException<ArgumentNullException>(() => new DavClient(null));
            Assert.ThrowsException<ArgumentNullException>(() => new DavClient(Endpoint, (System.Net.Http.HttpClient)null));
            foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-2), TimeSpan.FromDays(100) })
            {
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => new DavClient(Endpoint, new DavClientOptions { MetadataTimeout = timeout }));
            }
            foreach (var limits in new[]
            {
                new DavXmlLimits { MaxDepth = 0 }, new DavXmlLimits { MaxResourceCharacters = 0 }, new DavXmlLimits { MaxResponseCharacters = 0 }
            })
            {
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => new DavClient(Endpoint, new DavClientOptions { XmlLimits = limits }));
            }
            using var valid = new DavClient(Endpoint, new DavClientOptions { MetadataTimeout = Timeout.InfiniteTimeSpan });
        }

        [TestMethod]
        public void Invalid_condition_grammar_and_timeouts_cannot_be_constructed()
        {
            foreach (var token in new[] { "relative", "urn:a b", "urn:a>", "<urn:a>", "" })
            {
                Assert.ThrowsException<ArgumentException>(() => DavCondition.StateToken(token));
            }
            Assert.ThrowsException<ArgumentException>(() => DavCondition.EntityTag("*"));
            Assert.ThrowsException<ArgumentNullException>(() => DavCondition.StateToken(null));
            Assert.ThrowsException<ArgumentException>(() => new DavConditionList());
            Assert.ThrowsException<ArgumentException>(() => new DavConditionList(new DavCondition[] { null }));
            Assert.ThrowsException<ArgumentNullException>(() => new DavConditionList(null));
            Assert.ThrowsException<ArgumentException>(() => DavIfConditions.Untagged());
            Assert.ThrowsException<ArgumentException>(() => DavIfConditions.Untagged(new DavConditionList[] { null }));
            Assert.ThrowsException<ArgumentNullException>(() => DavIfConditions.Untagged(null));
            Assert.ThrowsException<ArgumentException>(() => DavIfConditions.Tagged());
            Assert.ThrowsException<ArgumentException>(() => DavIfConditions.Tagged(new DavTaggedConditions[] { null }));
            Assert.ThrowsException<ArgumentNullException>(() => DavIfConditions.Tagged(null));
            Assert.ThrowsException<ArgumentNullException>(() => new DavTaggedConditions(null, new DavConditionList(DavCondition.EntityTag("\"e\""))));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => DavLockTimeout.Finite(TimeSpan.Zero));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => DavLockTimeout.Finite(TimeSpan.FromMilliseconds(100)));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => DavLockTimeout.Finite(TimeSpan.FromDays(100000)));
            Assert.ThrowsException<InvalidOperationException>(() => DavLockTimeout.Unspecified.ToHeader());
            Assert.AreEqual("([\"tag\"])", DavIfConditions.Untagged(new DavConditionList(DavCondition.EntityTag("\"tag\""))).ToHeader(_ => Endpoint));
            var original = new XElement(XName.Get("x", "urn:x"), "original");
            var value = new DavXmlValue(original);
            original.Value = "changed";
            Assert.IsTrue(value.TryGetText(out var text));
            Assert.AreEqual("original", text);
            Assert.AreEqual("", DavXmlValue.Text(new PropertyName("", "x"), null).ToElement().Value);
            var removal = DavPropertyChange.Remove(new PropertyName("", "x"));
            Assert.IsNull(removal.Value);
        }

        [TestMethod]
        public async Task Modern_validation_rejects_null_members_and_invalid_ranges_before_sending()
        {
            using var handler = new StubHttpMessageHandler(_ => throw new AssertFailedException("Unexpected I/O"));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.PropFindAsync(Target, new PropFindOptions { Properties = new PropertyName[] { null } }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.UpdatePropertiesAsync(Target, new DavPropertyChange[] { null }));
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => client.UploadAsync(Target, null));
            using var unreadable = new MemoryStream();
            unreadable.Dispose();
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.UploadAsync(Target, unreadable));
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => client.AcquireLockAsync(Target, new LockOptions { Timeouts = null }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.AcquireLockAsync(Target, new LockOptions { Timeouts = new DavLockTimeout[] { null } }));
            foreach (var range in new[] { new RangeHeaderValue(null, 0), RangeHeaderValue.Parse("items=0-2"), RangeHeaderValue.Parse("bytes=0-1,3-4") })
            {
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.DownloadAsync(Target, new DownloadOptions { Range = range }));
            }
            foreach (var headers in new[]
            {
                new Dictionary<string, string> { ["X-Test"] = null }, new Dictionary<string, string> { ["X-\rTest"] = "value" },
                new Dictionary<string, string> { ["Host"] = "evil.example" }
            })
            {
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.DeleteAsync(Target, new DavRequestOptions { Headers = headers }));
            }
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Well_formed_http_rejections_are_results_while_missing_confirmation_is_a_protocol_error()
        {
            foreach (var status in new[] { 200, 204, 403 })
            {
                using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)status));
                using var http = new System.Net.Http.HttpClient(handler);
                using var client = new DavClient(Endpoint, http);
                if (status == 403)
                {
                    var result = await client.PropFindAsync(Target);
                    Assert.ThrowsException<WebDAVException>(result.EnsureSucceeded);
                    var report = await client.ReportAsync(Target, DavXmlValue.Text(new PropertyName("urn:test", "report"), ""));
                    Assert.IsNull(report.Document);
                    Assert.AreEqual(0, report.Resources.Count);
                    var sync = await client.SyncCollectionAsync(Target, null, Array.Empty<PropertyName>());
                    Assert.IsNull(sync.SyncToken);
                    Assert.IsFalse(sync.HasMore);
                }
                else
                {
                    await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.PropFindAsync(Target));
                }
                var caps = await client.GetCapabilitiesAsync(Target);
                Assert.AreEqual(0, caps.DavTokens.Count);
                Assert.AreEqual(0, caps.AllowedMethods.Count);
                Assert.AreEqual(0, caps.SearchGrammars.Count);
            }
        }

        [DataTestMethod]
        [DataRow("<D:response><D:href>/dav/file</D:href><D:status>HTTP/1.1 200 OK</D:status><D:status>HTTP/1.1 403 Forbidden</D:status></D:response>")]
        [DataRow("<D:response><D:href>/dav/file</D:href><D:propstat><D:prop/></D:propstat></D:response>")]
        [DataRow("<D:response><D:href>/dav/file</D:href><D:propstat><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>")]
        [DataRow("<D:response><D:href>/dav/file</D:href><D:propstat><D:prop/><D:status>HTTP/1.1 200 OK</D:status><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>")]
        [DataRow("<D:response><D:href>/dav/file</D:href><D:status>HTTP/1.1 200 OK</D:status><D:propstat><D:prop/><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>")]
        [DataRow("<D:response><D:href>/a</D:href><D:href>/b</D:href><D:propstat><D:prop/><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>")]
        [DataRow("<D:response><D:href><X:child/></D:href><D:status>HTTP/1.1 200 OK</D:status></D:response>")]
        [DataRow("<D:response><D:href>/dav/file</D:href><D:status>WRONG 200 OK</D:status></D:response>")]
        [DataRow("<D:response><D:href>/dav/file</D:href><D:status>HTTP/1.1 99 BAD</D:status></D:response>")]
        [DataRow("<D:response><D:href>/dav/file</D:href><D:status>HTTP/1.1 999 BAD</D:status></D:response>")]
        public async Task Ambiguous_structural_outcomes_fail_in_buffered_and_streaming_paths(string member)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(member)));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.PropFindAsync(Target));
            await using var listing = await client.OpenListingAsync(Target);
            await Assert.ThrowsExceptionAsync<WebDAVException>(async () =>
            {
                await foreach (var resource in listing.ReadResourcesAsync()) { }
            });
            Assert.IsFalse(listing.IsComplete);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("<wrong/>")]
        [DataRow("<D:multistatus xmlns:D=\"DAV:\">unexpected text</D:multistatus>")]
        public async Task Streaming_rejects_missing_root_and_unexpected_text(string xml)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await using var listing = await client.OpenListingAsync(Target);
            await Assert.ThrowsExceptionAsync<WebDAVException>(async () =>
            {
                await foreach (var resource in listing.ReadResourcesAsync()) { }
            });
        }

        [TestMethod]
        public async Task Empty_listing_can_complete_but_empty_mutation_cannot_confirm_a_write()
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse("<D:multistatus xmlns:D=\"DAV:\"/>"));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await using var listing = await client.OpenListingAsync(Target);
            await foreach (var resource in listing.ReadResourcesAsync()) { Assert.Fail("Expected no members."); }
            Assert.IsTrue(listing.IsComplete);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.DeleteAsync(Target));
        }

        [TestMethod]
        public async Task Streaming_disallows_concurrent_enumeration_and_non_multistatus_success()
        {
            foreach (var status in new[] { 200, 207, 403 })
            {
                using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource() + Resource()), status));
                using var http = new System.Net.Http.HttpClient(handler);
                using var client = new DavClient(Endpoint, http);
                await using var listing = await client.OpenListingAsync(Target);
                await using var first = listing.ReadResourcesAsync().GetAsyncEnumerator();
                if (status == 207)
                {
                    Assert.IsTrue(await first.MoveNextAsync());
                    await using var second = listing.ReadResourcesAsync().GetAsyncEnumerator();
                    await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => second.MoveNextAsync().AsTask());
                }
                else
                {
                    await Assert.ThrowsExceptionAsync<WebDAVException>(() => first.MoveNextAsync().AsTask());
                }
                Assert.IsFalse(listing.IsComplete);
            }
        }

        [TestMethod]
        public async Task Lock_malformed_structure_and_timeouts_are_protocol_errors_not_missing_locks()
        {
            var valid = LockXml();
            foreach (var xml in new[]
            {
                "<wrong/>", "<D:prop xmlns:D=\"DAV:\"/>", "<D:prop xmlns:D=\"DAV:\"><D:lockdiscovery/></D:prop>",
                valid.Replace("<D:lockscope><D:shared/></D:lockscope>", ""),
                valid.Replace("<D:locktype><D:write/></D:locktype>", ""),
                valid.Replace("<D:depth>0</D:depth>", ""),
                valid.Replace("<D:depth>0</D:depth>", "<D:depth>1</D:depth>"),
                valid.Replace("<D:shared/>", "<D:shared/><D:exclusive/>"),
                LockXml("garbage"), LockXml("Second-0"), LockXml("Second-bad"),
                LockXml("Second-4294967296")
            })
            {
                using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml, 200));
                using var http = new System.Net.Http.HttpClient(handler);
                using var client = new DavClient(Endpoint, http);
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.AcquireLockAsync(Target));
            }
        }

        [TestMethod]
        public async Task Multiple_active_locks_and_optional_lock_fields_survive()
        {
            XNamespace dav = "DAV:";
            var document = XElement.Parse(LockXml(null));
            var active = document.Descendants(dav + "activelock").Single();
            active.Element(dav + "locktoken").Remove();
            active.Element(dav + "lockroot").Remove();
            active.Element(dav + "owner").Remove();
            document.Element(dav + "lockdiscovery").Add(new XElement(active));
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(document.ToString(), 200));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.RefreshLockAsync(Target, "urn:token", new[] { DavLockTimeout.Infinite });
            Assert.AreEqual(2, result.Locks.Count);
            Assert.IsNull(result.Locks[0].Token);
            Assert.IsNull(result.Locks[0].Root);
            Assert.IsNull(result.Locks[0].Owner);
            Assert.AreEqual("write", result.Locks[0].Type.LocalName);
        }

        [TestMethod]
        public async Task Report_profile_interprets_only_its_own_valid_response_shape()
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse("<X:report xmlns:X=\"urn:custom\"/>", 200));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var report = await client.ReportAsync(Target, DavXmlValue.Text(new PropertyName("urn:custom", "request"), null));
            Assert.AreEqual("report", report.Document.Name.LocalName);
            Assert.AreEqual(0, report.Resources.Count);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.SyncCollectionAsync(Target, null, Array.Empty<PropertyName>()));
        }

        [TestMethod]
        public async Task Injected_redirect_result_is_rejected_and_response_disposed()
        {
            using var stream = new MemoryStream(new byte[3]);
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/file")
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.HeadAsync(Target));
            Assert.IsFalse(stream.CanRead);
        }

        [TestMethod]
        public async Task Range_validation_covers_unknown_total_open_ranges_and_unrequested_partial_content()
        {
            foreach (var requested in new[] { (RangeHeaderValue)null, new RangeHeaderValue(0, null), new RangeHeaderValue(null, 2), new RangeHeaderValue(0, 2) })
            {
                using var handler = new StubHttpMessageHandler(_ =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[3]) };
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 2);
                    return response;
                });
                using var http = new System.Net.Http.HttpClient(handler);
                using var client = new DavClient(Endpoint, http);
                if (requested?.Ranges.Single().From == 0 && requested.Ranges.Single().To == 2)
                {
                    using var result = await client.DownloadAsync(Target, new DownloadOptions { Range = requested });
                    Assert.IsNotNull(result.Content);
                    result.Dispose();
                    await result.DisposeAsync();
                }
                else
                {
                    await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.DownloadAsync(Target, new DownloadOptions { Range = requested }));
                }
            }
        }

        [TestMethod]
        public async Task Download_preserves_dav_errors_and_rejects_success_without_content()
        {
            foreach (var status in new[] { 204, 412 })
            {
                using var handler = new StubHttpMessageHandler(_ => XmlResponse("<D:error xmlns:D=\"DAV:\"><D:condition/></D:error>", status));
                using var http = new System.Net.Http.HttpClient(handler);
                using var client = new DavClient(Endpoint, http);
                if (status == 204)
                {
                    await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.DownloadAsync(Target));
                }
                else
                {
                    var result = await client.DownloadAsync(Target);
                    Assert.AreEqual("condition", result.Errors.Single().Name.LocalName);
                    await result.DisposeAsync();
                    await result.DisposeAsync();
                }
            }
        }

        [TestMethod]
        public async Task Invalid_typed_supported_lock_entries_retain_decoding_error()
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource("<D:supportedlock><D:lockentry/></D:supportedlock>"))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var value = DavProperties.SupportedLocks.Read((await client.PropFindAsync(Target)).Resources.Single()).Single();
            Assert.IsFalse(value.HasValue);
            Assert.IsInstanceOfType<XmlException>(value.DecodingError);
        }
    }
}
