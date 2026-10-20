using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.Protocol;
using WebDAVClient.UnitTests.Helpers;
using static WebDAVClient.UnitTests.DavClientTests;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DavCoreCompletenessTests
    {
        [DataTestMethod]
        [DataRow(0L)]
        [DataRow(4294967295L)]
        public async Task Lock_timeout_supports_the_entire_unsigned_seconds_range(long seconds)
        {
            var duration = TimeSpan.FromSeconds(seconds);
            var timeout = "Second-" + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(LockXml(timeout), 200));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.AcquireLockAsync(DavClientTests.File,
                new LockOptions { Timeouts = new[] { DavLockTimeout.Finite(duration) } });
            result.EnsureSucceeded();
            Assert.AreEqual(duration, result.Locks.Single().Timeout.Duration);
            Assert.IsFalse(result.Locks.Single().Timeout.IsInfinite);
            Assert.AreEqual(timeout, handler.Requests.Single().Headers["Timeout"].Single());
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Empty_locked_resources_accept_204_without_acquiring_a_body_and_keep_response_owned(bool lengthKnown)
        {
            using var content = new EmptyStream(lengthKnown);
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new StreamContent(content)
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            using var result = await client.DownloadAsync(DavClientTests.File);
            result.EnsureHttpSuccess();
            Assert.AreEqual(204, result.StatusCode);
            Assert.IsNotNull(result.Content);
            Assert.AreEqual(0, await result.Content.ReadAsync(new byte[1]));
            Assert.AreEqual(0, content.ReadCount);
            Assert.IsFalse(content.Disposed);
            Assert.AreEqual(lengthKnown ? 0L : null, result.ContentLength);
            result.Dispose();
            Assert.IsTrue(content.Disposed);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow("range-request")]
        [DataRow("length")]
        [DataRow("range-response")]
        public async Task No_content_cannot_be_used_as_appendable_or_contradictory_content(string problem)
        {
            using var body = new MemoryStream();
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new StreamContent(body) };
                if (problem == "length")
                {
                    response.Content.Headers.ContentLength = 1;
                }
                if (problem == "range-response")
                {
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0);
                }
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.DownloadAsync(DavClientTests.File,
                problem == "range-request" ? new DownloadOptions { Range = new RangeHeaderValue(0, null) } : null));
            Assert.IsFalse(body.CanRead);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow(null, null)]
        [DataRow("no-store", "custom")]
        [DataRow("max-age=60", "no-cache")]
        [DataRow("no-cache", "NO-CACHE")]
        [DataRow("no-cache=\"Content-Type\"", "no-cache=foo")]
        public async Task If_conditions_always_prevent_non_dav_proxy_cache_hits_without_losing_custom_directives(string cacheControl, string pragma)
        {
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var headers = new Dictionary<string, string>();
            if (cacheControl != null)
            {
                headers["Cache-Control"] = cacheControl;
                headers["Pragma"] = pragma;
            }
            await client.DeleteAsync(DavClientTests.File, new DavRequestOptions
            {
                Headers = headers,
                Conditions = DavIfConditions.Untagged(new DavConditionList(DavCondition.StateToken("urn:lock:1")))
            });
            var request = handler.Requests.Single();
            var parsed = CacheControlHeaderValue.Parse(string.Join(", ", request.Headers["Cache-Control"]));
            Assert.IsTrue(parsed.NoCache);
            Assert.AreEqual(0, parsed.NoCacheHeaders.Count);
            Assert.AreEqual(cacheControl == "no-store", parsed.NoStore);
            Assert.AreEqual(cacheControl == "max-age=60" ? TimeSpan.FromSeconds(60) : null, parsed.MaxAge);
            Assert.AreEqual(1, request.Headers["Pragma"].Count(v => v.Equals("no-cache", StringComparison.OrdinalIgnoreCase)));
            if (pragma == "custom")
            {
                CollectionAssert.Contains(request.Headers["Pragma"], "custom");
            }
            await client.DeleteAsync(DavClientTests.File);
            Assert.IsFalse(handler.Requests.Last().Headers.ContainsKey("Cache-Control"));
            Assert.IsFalse(handler.Requests.Last().Headers.ContainsKey("Pragma"));
        }

        [TestMethod]
        public async Task Capabilities_keep_compliance_classes_and_coded_extension_uris_with_embedded_commas()
        {
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.NoContent);
                response.Headers.TryAddWithoutValidation("DAV", new[] { "1, 3, <https://example.com/extensions/a,b>", "2, custom, ," });
                response.Content.Headers.TryAddWithoutValidation("Allow", "OPTIONS, PROPFIND");
                response.Headers.TryAddWithoutValidation("DASL", "<https://example.com/grammars/a,b>, <DAV:basicsearch>");
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.GetCapabilitiesAsync(DavClientTests.File);
            CollectionAssert.AreEqual(new[] { "1", "3", "<https://example.com/extensions/a,b>", "2", "custom" }, result.DavTokens.ToArray());
            CollectionAssert.AreEqual(new[] { "<https://example.com/grammars/a,b>", "<DAV:basicsearch>" }, result.SearchGrammars.ToArray());
            CollectionAssert.AreEqual(new[] { "OPTIONS", "PROPFIND" }, result.AllowedMethods.ToArray());
        }

        [DataTestMethod]
        [DataRow("lockscope", "<D:shared/>", true)]
        [DataRow("locktype", "<D:write/>", true)]
        [DataRow("lockscope", "<D:shared/><D:exclusive/>", false)]
        [DataRow("lockscope", "<X:shared/>", false)]
        [DataRow("locktype", "<X:write/>", false)]
        [DataRow("locktype", "<D:write/><D:write/>", false)]
        public async Task Lock_kind_decoding_ignores_extensions_without_promoting_lookalikes_or_ambiguous_kinds(string kind, string children, bool valid)
        {
            var scope = kind == "lockscope" ? children : "<D:shared/>";
            var type = kind == "locktype" ? children : "<D:write/>";
            var kinds = "<D:lockscope><X:extension><D:exclusive/></X:extension><D:unknown/>" + scope + "</D:lockscope>"
                + "<D:locktype><X:extension/><D:unknown/>" + type + "</D:locktype>";
            var supported = "<D:supportedlock><D:lockentry>" + kinds + "</D:lockentry></D:supportedlock>";
            var active = "<D:lockdiscovery><D:activelock>" + kinds + "<D:depth>0</D:depth></D:activelock></D:lockdiscovery>";
            using var handler = new StubHttpMessageHandler(r => r.Method.Method == "LOCK"
                ? XmlResponse("<D:prop xmlns:D=\"DAV:\" xmlns:X=\"urn:test\">" + active + "</D:prop>", 200)
                : XmlResponse(Multi(Resource(supported + active))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var resource = (await client.PropFindAsync(DavClientTests.File)).Resources.Single();
            var advertised = DavProperties.SupportedLocks.Read(resource).Single();
            var discovered = DavProperties.LockDiscovery.Read(resource).Single();
            Assert.AreEqual(valid, advertised.HasValue);
            Assert.AreEqual(valid, discovered.HasValue);
            if (valid)
            {
                Assert.AreEqual("shared", advertised.Value.Single().Scope.LocalName);
                Assert.AreEqual("write", discovered.Value.Single().Type.LocalName);
                Assert.AreEqual(1, (await client.AcquireLockAsync(DavClientTests.File)).Locks.Count);
            }
            else
            {
                Assert.IsInstanceOfType<XmlException>(advertised.DecodingError);
                Assert.IsInstanceOfType<XmlException>(discovered.DecodingError);
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.AcquireLockAsync(DavClientTests.File));
            }
        }

        [DataTestMethod]
        [DataRow("lock-token-matches-request-uri", 409)]
        [DataRow("lock-token-submitted", 423)]
        [DataRow("no-conflicting-lock", 423)]
        [DataRow("no-external-entities", 403)]
        [DataRow("preserved-live-properties", 409)]
        [DataRow("propfind-finite-depth", 403)]
        [DataRow("cannot-modify-protected-property", 403)]
        public async Task Every_core_condition_preserves_qualified_details_without_following_error_hrefs(string condition, int status)
        {
            var error = "<D:error><D:" + condition + "><D:href>https://other.example/locked</D:href>"
                + "<X:detail xml:lang=\"en\">reason</X:detail></D:" + condition + "></D:error>";
            var rootError = error.Replace("<D:error>", "<D:error xmlns:D=\"DAV:\" xmlns:X=\"urn:test\">");
            var propstat = "<D:response><D:href>/dav/file</D:href><D:propstat><D:prop><D:displayname/></D:prop>"
                + "<D:status>HTTP/1.1 " + status + " Rejected</D:status>" + error + "</D:propstat></D:response>";
            using var handler = new StubHttpMessageHandler(r => r.Method.Method == "PROPFIND"
                ? XmlResponse(Multi(propstat)) : XmlResponse(rootError, status));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var rejected = await client.DeleteAsync(DavClientTests.File);
            var properties = await client.PropFindAsync(DavClientTests.File);
            foreach (var detail in new[] { rejected.Errors.Single(), properties.Resources.Single().PropertyStatuses.Single().Errors.Single() })
            {
                Assert.AreEqual("DAV:", detail.Name.Namespace);
                Assert.AreEqual(condition, detail.Name.LocalName);
                Assert.AreEqual("https://other.example/locked", detail.ToElement().Element(System.Xml.Linq.XName.Get("href", "DAV:")).Value);
                Assert.AreEqual("reason", detail.ToElement().Element(System.Xml.Linq.XName.Get("detail", "urn:test")).Value);
            }
            Assert.AreEqual(status, rejected.StatusCode);
            Assert.AreEqual(status, properties.Resources.Single().PropertyStatuses.Single().StatusCode);
            Assert.AreEqual(2, handler.Requests.Count);
            Assert.IsTrue(handler.Requests.All(r => r.RequestUri.Host == Endpoint.Host));
        }

        private sealed class EmptyStream : MemoryStream
        {
            private readonly bool m_lengthKnown;

            public EmptyStream(bool lengthKnown)
            {
                m_lengthKnown = lengthKnown;
            }

            public override bool CanSeek => m_lengthKnown;

            public bool Disposed
            {
                get; private set;
            }

            public int ReadCount
            {
                get; private set;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ReadCount++;
                throw new AssertFailedException("A 204 must not acquire a response stream.");
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
