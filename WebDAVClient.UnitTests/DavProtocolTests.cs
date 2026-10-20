using System;
using System.Collections.Generic;
using Stream = System.IO.Stream;
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
using static WebDAVClient.UnitTests.DavClientTests;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DavProtocolTests
    {
        [TestMethod]
        public async Task Xml_values_retain_mixed_content_namespace_language_attributes_and_defensive_copies()
        {
            const string xml = """
                <D:multistatus xmlns:D="DAV:" xmlns:X="urn:test" xmlns:T="urn:type" xml:lang="en" xml:space="preserve">
                  <D:response><D:href>/dav/file</D:href><D:propstat><D:prop>
                    <X:note T:kind="T:example"> a <![CDATA[b]]><T:child/> c <!--keep--><?sample keep?></X:note>
                    <X:empty/><X:note>second</X:note>
                  </D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
                  <D:responsedescription>observed</D:responsedescription>
                  <D:location><D:href>/dav/other</D:href></D:location><X:extra/>
                  </D:response>
                </D:multistatus>
                """;
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.PropFindAsync(File);
            result.EnsureSucceeded();
            var resource = result.Resources.Single();
            Assert.AreEqual("observed", resource.Description);
            Assert.AreEqual("/dav/other", resource.Location);
            Assert.AreEqual(1, resource.Extensions.Count);
            var notes = resource.GetProperties(new PropertyName("urn:test", "note")).ToArray();
            Assert.AreEqual(2, notes.Length);
            var first = notes[0].Value;
            Assert.IsFalse(first.TryGetText(out _));
            var element = first.ToElement();
            Assert.AreEqual("en", (string)element.Attribute(XNamespace.Xml + "lang"));
            Assert.AreEqual("preserve", (string)element.Attribute(XNamespace.Xml + "space"));
            Assert.AreEqual("urn:type", element.GetNamespaceOfPrefix("T").NamespaceName);
            Assert.AreEqual("T:example", (string)element.Attribute(XName.Get("kind", "urn:type")));
            Assert.AreEqual(1, element.Nodes().OfType<XCData>().Count());
            Assert.AreEqual(1, element.Nodes().OfType<XComment>().Count());
            Assert.AreEqual(1, element.Nodes().OfType<XProcessingInstruction>().Count());
            element.RemoveNodes();
            Assert.IsTrue(first.ToElement().HasElements);
            Assert.IsTrue(notes[1].Value.TryGetText(out var text));
            Assert.AreEqual("second", text);
        }

        [DataTestMethod]
        [DataRow(200)]
        [DataRow(403)]
        [DataRow(404)]
        [DataRow(409)]
        [DataRow(423)]
        [DataRow(424)]
        [DataRow(507)]
        public async Task Property_statuses_are_exact_regardless_of_wire_order(int status)
        {
            var xml = Multi("<D:response><D:href>/dav/file</D:href><D:propstat><D:status>HTTP/1.1 " + status + " Result</D:status>"
                + "<D:prop><D:getcontentlength>4294967297</D:getcontentlength></D:prop><D:responsedescription>detail</D:responsedescription>"
                + "<D:error><D:cannot-modify-protected-property/></D:error></D:propstat></D:response>");
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.PropFindAsync(File);
            var property = DavProperties.ContentLength.Read(result.Resources.Single()).Single();
            Assert.AreEqual(status, property.Source.StatusCode);
            Assert.AreEqual(status == 200, property.HasValue);
            Assert.AreEqual(status == 200 ? 4294967297L : 0L, property.Value);
            Assert.AreEqual("detail", result.Resources[0].PropertyStatuses[0].Description);
            Assert.AreEqual(1, result.Resources[0].PropertyStatuses[0].Errors.Count);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("<other/>")]
        [DataRow("<D:Multistatus xmlns:D=\"DAV:\"/>")]
        [DataRow("<D:multistatus xmlns:D=\"DAV:\"><D:response/></D:multistatus>")]
        [DataRow("<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>/dav/file</D:href></D:response></D:multistatus>")]
        [DataRow("<D:multistatus xmlns:D=\"DAV:\">not structural XML</D:multistatus>")]
        [DataRow("<!DOCTYPE x [<!ENTITY e 'bad'>]><D:multistatus xmlns:D=\"DAV:\"/>")]
        [DataRow("<D:multistatus xmlns:D=\"DAV:\"><D:response><X:href xmlns:X=\"urn:test\">/dav/file</X:href><D:status>HTTP/1.1 200 OK</D:status></D:response></D:multistatus>")]
        [DataRow("<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>/dav/file</D:href><D:status>garbage</D:status></D:response></D:multistatus>")]
        public async Task Malformed_or_wrong_structure_is_not_a_successful_empty_snapshot(string xml)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var error = await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.PropFindAsync(File));
            Assert.AreEqual(207, error.ResponseStatusCode);
        }

        [TestMethod]
        public async Task Inner_lookalikes_remain_property_content_not_resource_identity_or_outcomes()
        {
            var xml = Multi(Resource("<X:xml><D:href>https://evil.example/</D:href><D:response><D:status>HTTP/1.1 403 Forbidden</D:status></D:response></X:xml>"));
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.PropFindAsync(File);
            result.EnsureSucceeded();
            Assert.AreEqual(1, result.Resources.Count);
            Assert.AreEqual("/dav/file", result.Resources[0].Hrefs[0]);
        }

        [DataTestMethod]
        [DataRow("response")]
        [DataRow("resource")]
        [DataRow("depth")]
        public async Task Configured_limits_fail_explicitly(string limit)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource("<X:large><X:a><X:b>" + new string('x', 1000) + "</X:b></X:a></X:large>"))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http, new DavClientOptions
            {
                XmlLimits = new DavXmlLimits
                {
                    MaxResponseCharacters = limit == "response" ? 100 : 10000,
                    MaxResourceCharacters = limit == "resource" ? 100 : 10000,
                    MaxDepth = limit == "depth" ? 3 : 64
                }
            });
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.PropFindAsync(File));
        }

        [TestMethod]
        public void Paths_keep_identity_and_explicit_resolution_rules()
        {
            using var client = new DavClient(Endpoint);
            Assert.AreEqual("https://example.com/dav/a%2Bb/100%25/A.txt", client.Resolve(DavPath.FromSegments("a+b", "100%", "A.txt")).AbsoluteUri);
            Assert.AreEqual("https://example.com/absolute", client.Resolve(DavPath.Parse("/absolute")).AbsoluteUri);
            Assert.AreNotEqual(client.Resolve(DavPath.Parse("A.txt")), client.Resolve(DavPath.Parse("a.txt")));
            Assert.AreEqual("literal%2Fslash", DavPath.Parse("literal%2Fslash").ToString());
            Assert.AreEqual("https://example.com/dav/literal%2Fslash", client.Resolve(DavPath.Parse("literal%2Fslash")).AbsoluteUri);
            foreach (var path in new[] { "https://evil.example/file", "http://example.com/file", "https://example.com:444/file", "https://user:pass@example.com/file" })
            {
                Assert.ThrowsException<ArgumentException>(() => client.Resolve(DavPath.Parse(path)));
            }
            foreach (var path in new[] { "//evil/file", "a b", "a\\b", "a#b", "bad%", "bad%xy" })
            {
                Assert.ThrowsException<ArgumentException>(() => DavPath.Parse(path));
            }
            foreach (var segment in new[] { ".", "..", "", "a/b", "a\\b" })
            {
                Assert.ThrowsException<ArgumentException>(() => DavPath.FromSegments(segment));
            }
            Assert.ThrowsException<ArgumentNullException>(() => DavPath.Parse(null));
            Assert.ThrowsException<ArgumentNullException>(() => DavPath.FromSegments(null));
            Assert.ThrowsException<ArgumentNullException>(() => client.Resolve(null));
        }

        [TestMethod]
        public async Task Conditions_preserve_tagging_alternatives_negation_and_validate_every_origin()
        {
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var lists = new[] { new DavConditionList(DavCondition.StateToken("urn:lock:1"), DavCondition.EntityTag("\"etag\"", true)), new DavConditionList(DavCondition.StateToken("urn:lock:2", true)) };
            var conditions = DavIfConditions.Tagged(new DavTaggedConditions(File, lists), new DavTaggedConditions(DavPath.Parse("/dav/target"), lists[1]));
            lists[0] = null;
            var result = await client.DeleteAsync(File, new DavRequestOptions { Conditions = conditions });
            result.EnsureSucceeded();
            Assert.AreEqual("<https://example.com/dav/file> (<urn:lock:1> Not [\"etag\"]) (Not <urn:lock:2>) <https://example.com/dav/target> (Not <urn:lock:2>)",
                handler.Requests.Single().Headers["If"].Single());
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.DeleteAsync(File, new DavRequestOptions
            {
                Conditions = DavIfConditions.Tagged(new DavTaggedConditions(DavPath.Parse("https://evil.example/file"), new DavConditionList(DavCondition.StateToken("urn:x"))))
            }));
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Concurrent_requests_snapshot_mutable_headers_before_send()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                Assert.AreEqual("before", request.Headers.GetValues("X-Test").Single());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var headers = new Dictionary<string, string> { ["X-Test"] = "before" };
            var task = client.DeleteAsync(File, new DavRequestOptions { Headers = headers });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            headers["X-Test"] = "after";
            release.TrySetResult();
            await task;
        }

        [TestMethod]
        public async Task Invalid_arguments_fail_before_network_io()
        {
            using var handler = new StubHttpMessageHandler(_ => throw new AssertFailedException("Unexpected I/O"));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.PropFindAsync(File, new PropFindOptions { Selection = DavPropertySelection.Selected }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.PropFindAsync(File, new PropFindOptions { Selection = DavPropertySelection.Names, Properties = new[] { DavProperties.ETag.Name } }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.PropFindAsync(File, new PropFindOptions { Selection = (DavPropertySelection)50 }));
            await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => client.PropFindAsync(File, new PropFindOptions { Depth = (DavDepth)50 }));
            await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => client.CopyAsync(File, DavPath.Parse("other"), new CopyOptions { Depth = DavDepth.One }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.MoveAsync(File, File));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.UpdatePropertiesAsync(File, Array.Empty<DavPropertyChange>()));
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => client.UpdatePropertiesAsync(File, null));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.UploadAsync(File, Stream.Null, new UploadOptions { ContentType = "text/plain", Headers = new Dictionary<string, string> { ["Depth"] = "1" } }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.HeadAsync(File, new DavRequestOptions { Headers = new Dictionary<string, string> { ["X-Test"] = "a\r\nb" } }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.DownloadAsync(File, new DownloadOptions { IfRange = new RangeConditionHeaderValue(DateTimeOffset.UtcNow) }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.DownloadAsync(File, new DownloadOptions { Range = new RangeHeaderValue(0, 1), IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"tag\"", true)) }));
            await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => client.AcquireLockAsync(File, new LockOptions { Scope = (DavLockScope)50 }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.AcquireLockAsync(File, new LockOptions { Owner = DavXmlValue.Text(new PropertyName("urn:test", "wrong"), "") }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.AcquireLockAsync(File, new LockOptions { Timeouts = Array.Empty<DavLockTimeout>() }));
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => client.ReportAsync(File, null));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.SyncCollectionAsync(File, null, Array.Empty<PropertyName>(), 0));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Typed_codecs_distinguish_invalid_missing_empty_and_denied_values()
        {
            var properties = "<D:getcontentlength>-1</D:getcontentlength><D:displayname><X:nested/></D:displayname>"
                + "<D:getetag>W/\"tag\"</D:getetag><D:getcontenttype>text/plain</D:getcontenttype><D:getcontentlanguage>en</D:getcontentlanguage>"
                + "<D:creationdate>2026-01-02T03:04:05+02:00</D:creationdate><D:getlastmodified>Wed, 01 Jan 2025 12:00:00 GMT</D:getlastmodified>"
                + "<D:resourcetype><D:collection/><X:custom/></D:resourcetype>"
                + "<D:supportedlock><D:lockentry><D:lockscope><D:exclusive/></D:lockscope><D:locktype><D:write/></D:locktype></D:lockentry></D:supportedlock>"
                + "<D:lockdiscovery/>";
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource(properties))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var resource = (await client.PropFindAsync(File)).Resources.Single();
            Assert.IsFalse(DavProperties.ContentLength.Read(resource).Single().HasValue);
            Assert.IsInstanceOfType<FormatException>(DavProperties.ContentLength.Read(resource).Single().DecodingError);
            Assert.IsFalse(DavProperties.DisplayName.Read(resource).Single().HasValue);
            Assert.AreEqual("W/\"tag\"", DavProperties.ETag.Read(resource).Single().Value);
            Assert.AreEqual("text/plain", DavProperties.ContentType.Read(resource).Single().Value);
            Assert.AreEqual("en", DavProperties.ContentLanguage.Read(resource).Single().Value);
            Assert.AreEqual(TimeSpan.FromHours(2), DavProperties.CreationDate.Read(resource).Single().Value.Offset);
            Assert.IsTrue(DavProperties.LastModified.Read(resource).Single().HasValue);
            Assert.AreEqual(2, DavProperties.ResourceTypes.Read(resource).Single().Value.Count);
            Assert.AreEqual("exclusive", DavProperties.SupportedLocks.Read(resource).Single().Value.Single().Scope.LocalName);
            Assert.AreEqual(0, DavProperties.LockDiscovery.Read(resource).Single().Value.Count);
            Assert.AreEqual(0, resource.GetProperties(new PropertyName("urn:missing", "missing")).Count());
        }

        [TestMethod]
        public async Task Missing_patch_outcomes_and_invalid_sync_tokens_fail_explicitly()
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource())));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.UpdatePropertiesAsync(File, new[] { DavPropertyChange.Remove(new PropertyName("urn:test", "missing")) }));
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.SyncCollectionAsync(DavPath.Root, "urn:previous", Array.Empty<PropertyName>()));
        }

        [TestMethod]
        public async Task Cancellation_and_metadata_deadline_cover_async_xml_reads()
        {
            foreach (var callerCancellation in new[] { false, true })
            {
                using var stalled = new PrefixThenStallStream("<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>");
                using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)207) { Content = new StreamContent(stalled) });
                using var http = new System.Net.Http.HttpClient(handler);
                using var client = new DavClient(Endpoint, http, new DavClientOptions { MetadataTimeout = callerCancellation ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(100) });
                using var cancellation = new CancellationTokenSource();
                if (callerCancellation) { cancellation.CancelAfter(100); }
                try
                {
                    await client.PropFindAsync(File, cancellationToken: cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Fail("The read should have been cancelled.");
                }
                catch (OperationCanceledException) { }
            }
        }
    }
}
