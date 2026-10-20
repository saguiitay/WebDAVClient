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
using static WebDAVClient.UnitTests.DavClientTests;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DavProtocolEdgeTests
    {
        private static DavPath Target => DavClientTests.File;

        [DataTestMethod]
        [DataRow("preserve")]
        [DataRow("default")]
        public async Task Streamed_and_buffered_properties_preserve_inherited_xml_context(string space)
        {
            const string xml = """
                <multistatus xmlns="DAV:" xmlns:X="urn:outer" xml:lang="fr" xml:space="preserve">
                  <response><href>/dav/file</href><propstat><prop>
                    <X:note X:type="X:kind">  before <X:child/> after  </X:note>
                    <X:reset xml:lang="" xml:space="default"> text </X:reset>
                    <X:local xmlns:X="urn:inner"><X:value/></X:local>
                  </prop><status>HTTP/1.1 200 OK</status></propstat></response>
                </multistatus>
                <!-- trailing comment --> <?done yes?>
                """;
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml.Replace("xml:space=\"preserve\"", "xml:space=\"" + space + "\"")));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var buffered = (await client.PropFindAsync(Target)).Resources.Single();
            await using var listing = await client.OpenListingAsync(Target);
            var streamed = new List<DavResourceResponse>();
            await foreach (var resource in listing.ReadResourcesAsync())
            {
                streamed.Add(resource);
            }
            Assert.IsTrue(listing.IsComplete);
            var expected = buffered.PropertyStatuses.Single().Properties;
            var actual = streamed.Single().PropertyStatuses.Single().Properties;
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.IsTrue(XNode.DeepEquals(expected[i].Value.ToElement(), actual[i].Value.ToElement()),
                    $"Streamed XML context differs for {expected[i].Name}.");
            }
            var note = actual[0].Value.ToElement();
            Assert.AreEqual(space, (string)note.Attribute(XNamespace.Xml + "space"));
            Assert.AreEqual("fr", (string)note.Attribute(XNamespace.Xml + "lang"));
            Assert.AreEqual("urn:outer", note.GetNamespaceOfPrefix("X").NamespaceName);
            Assert.AreEqual("", (string)actual[1].Value.ToElement().Attribute(XNamespace.Xml + "lang"));
            Assert.AreEqual("default", (string)actual[1].Value.ToElement().Attribute(XNamespace.Xml + "space"));
            Assert.AreEqual("urn:inner", actual[2].Value.ToElement().GetNamespaceOfPrefix("X").NamespaceName);
        }

        [DataTestMethod]
        [DataRow("Application/XML")]
        [DataRow("TEXT/XML")]
        [DataRow("application/dav+XML")]
        public async Task Xml_media_types_are_case_insensitive_for_optional_error_bodies(string mediaType)
        {
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = XmlResponse("<D:error xmlns:D=\"DAV:\"><D:lock-token-submitted/></D:error>", 423);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.DeleteAsync(Target);
            Assert.AreEqual(423, result.StatusCode);
            Assert.AreEqual("lock-token-submitted", result.Errors.Single().Name.LocalName);
        }

        [DataTestMethod]
        [DataRow("bytes", "bytes", "identity")]
        [DataRow("BYTES", "bytes", "identity")]
        [DataRow("bytes", "BYTES", "Identity")]
        public async Task Range_unit_and_content_coding_names_are_case_insensitive(string requestedUnit, string responseUnit, string coding)
        {
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(new byte[]
                        {
                            42
                        })
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 1)
                {
                    Unit = responseUnit
                };
                response.Content.Headers.ContentEncoding.Add(coding);
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            using var result = await client.DownloadAsync(Target, new DownloadOptions
            {
                Range = new RangeHeaderValue(0, 0)
                {
                    Unit = requestedUnit
                }
            });
            Assert.AreEqual(42, result.Content.ReadByte());
        }

        [DataTestMethod]
        [DataRow(-1)]
        [DataRow(1)]
        public async Task Changed_last_modified_cannot_be_accepted_for_date_validated_resume(int change)
        {
            var validator = DateTimeOffset.UnixEpoch.AddDays(10000);
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(new byte[3])
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 2, 3);
                response.Content.Headers.LastModified = validator.AddSeconds(change);
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.DownloadAsync(Target, new DownloadOptions
            {
                Range = new RangeHeaderValue(0, 2),
                IfRange = new RangeConditionHeaderValue(validator)
            }));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Bounded_range_above_four_gibibytes_clips_at_eof_with_matching_date_validator(bool hasLength)
        {
            const long start = (long)uint.MaxValue + 100;
            var validator = DateTimeOffset.UnixEpoch.AddDays(10000);
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new StreamContent(new PrefixThenStallStream("xyz"))
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, start + 2, start + 3);
                response.Content.Headers.LastModified = validator;
                if (hasLength)
                {
                    response.Content.Headers.ContentLength = 3;
                }
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            using var result = await client.DownloadAsync(Target, new DownloadOptions
            {
                Range = new RangeHeaderValue(start, start + 99),
                IfRange = new RangeConditionHeaderValue(validator)
            });
            Assert.AreEqual("bytes=" + start + "-" + (start + 99), handler.Requests.Single().Headers["Range"].Single());
            var buffer = new byte[3];
            await result.Content.ReadExactlyAsync(buffer);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("xyz"), buffer);
            Assert.AreEqual(hasLength ? (long?)3 : null, result.ContentLength);
        }

        [TestMethod]
        public async Task Structured_property_roundtrip_survives_proppatch_serialization_without_flattening()
        {
            const string xml = """
                <D:multistatus xmlns:D="DAV:" xmlns:X="urn:custom" xmlns:T="urn:type" xml:lang="de">
                  <D:response><D:href>/dav/file</D:href><D:propstat><D:prop>
                    <X:xml T:type="T:value"> text <![CDATA[<&>]]><T:child code="1"/> tail </X:xml>
                    <plain xmlns="">a&amp;b</plain>
                  </D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>
                </D:multistatus>
                """;
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var values = (await client.PropFindAsync(Target)).Resources.Single().PropertyStatuses.Single().Properties;
            (await client.UpdatePropertiesAsync(Target, values.Select(v => DavPropertyChange.Set(v.Value)).ToArray())).EnsureSucceeded();
            var sent = XElement.Parse(handler.Requests.Last().BodyAsString(), LoadOptions.PreserveWhitespace);
            var elements = sent.Elements().Select(e => e.Element(XName.Get("prop", "DAV:")).Elements().Single()).ToArray();
            for (int i = 0; i < values.Count; i++)
            {
                Assert.IsTrue(XNode.DeepEquals(values[i].Value.ToElement(), elements[i]));
            }
            Assert.AreEqual("", elements[1].Name.NamespaceName);
            Assert.AreEqual("a&b", elements[1].Value);
        }

        [DataTestMethod]
        [DataRow("HTTP/ 200 OK")]
        [DataRow("HTTP/garbage 200 OK")]
        [DataRow("HTTP/1.x 200 OK")]
        [DataRow("HTTP//.1 200 OK")]
        [DataRow("HTTP/:.1 200 OK")]
        [DataRow("HTTP/1-1 200 OK")]
        [DataRow("HTTP/1./ 200 OK")]
        [DataRow("HTTP/1.: 200 OK")]
        [DataRow("HTTP/1.1 2x0 OK")]
        public async Task Malformed_status_line_cannot_confirm_a_successful_resource(string status)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi("<D:response><D:href>/dav/file</D:href><D:status>" + status + "</D:status></D:response>")));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var error = await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.PropFindAsync(Target));
            Assert.IsInstanceOfType<XmlException>(error.InnerException);
        }

        [TestMethod]
        public async Task Exact_property_statuses_include_informational_redirect_and_server_failures()
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource(status: 102) + Resource(status: 302, href: "/dav/other") + Resource(status: 599, href: "/dav/last"))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.PropFindAsync(Target);
            CollectionAssert.AreEqual(new[]
                {
                    102, 302, 599
                }, result.Resources.Select(r => r.PropertyStatuses.Single().StatusCode.Value).ToArray());
            Assert.AreEqual(102, Assert.ThrowsException<WebDAVException>(result.EnsureSucceeded).FailedStatusCode);
        }

        [TestMethod]
        public async Task Root_and_resource_extension_errors_and_multiple_hrefs_are_retained()
        {
            var xml = Multi("""
                <D:response><D:href>/dav/a</D:href><D:href>/dav/b</D:href><D:status>HTTP/1.1 423 Locked</D:status>
                <D:error><D:lock-token-submitted/><X:why code="one"/></D:error><X:context/></D:response>
                <X:root-extra><D:response><D:href>not a resource</D:href></D:response></X:root-extra>
                """);
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.DeleteAsync(Target);
            var resource = result.Resources.Single();
            Assert.AreEqual(2, resource.Hrefs.Count);
            Assert.AreEqual(2, resource.Errors.Count);
            Assert.AreEqual("one", (string)resource.Errors[1].ToElement().Attribute("code"));
            Assert.AreEqual("context", resource.Extensions.Single().Name.LocalName);
            Assert.AreEqual(2, Assert.ThrowsException<WebDAVException>(result.EnsureSucceeded).ResourceStatuses.Count);
            await using var listing = await client.OpenListingAsync(Target);
            await foreach (var item in listing.ReadResourcesAsync())
            {
                Assert.AreEqual(2, item.Hrefs.Count);
            }
            Assert.AreEqual("root-extra", listing.Extensions.Single().Name.LocalName);
        }

        [DataTestMethod]
        [DataRow("response")]
        [DataRow("resource")]
        [DataRow("depth")]
        public async Task Streaming_enforces_each_xml_budget_and_never_certifies_partial_data(string limit)
        {
            var xml = Multi(Resource("<X:deep><X:nested>" + new string('x', 2048) + "</X:nested></X:deep>"));
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http, new DavClientOptions
            {
                XmlLimits = new DavXmlLimits
                {
                    MaxResponseCharacters = limit == "response" ? 256 : 10000,
                    MaxResourceCharacters = limit == "resource" ? 256 : 10000,
                    MaxDepth = limit == "depth" ? 3 : 64
                }
            });
            await using var listing = await client.OpenListingAsync(Target);
            await Assert.ThrowsExceptionAsync<WebDAVException>(async () =>
            {
                await foreach (var item in listing.ReadResourcesAsync())
                {
                }
            });
            Assert.IsFalse(listing.IsComplete);
        }

        [DataTestMethod]
        [DataRow("xml:lang")]
        [DataRow("xmlns:large")]
        public async Task Streamed_resource_size_budget_includes_inherited_context(string attribute)
        {
            var xml = "<D:multistatus xmlns:D=\"DAV:\" " + attribute + "=\"urn:" + new string('x', 1024) + "\">" + Resource() + "</D:multistatus>";
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http, new DavClientOptions
            {
                XmlLimits = new DavXmlLimits
                {
                    MaxResourceCharacters = 512
                }
            });
            await using var listing = await client.OpenListingAsync(Target);
            await Assert.ThrowsExceptionAsync<WebDAVException>(async () =>
            {
                await foreach (var item in listing.ReadResourcesAsync())
                {
                }
            });
            Assert.IsFalse(listing.IsComplete);
        }

        [DataTestMethod]
        [DataRow("utf-8")]
        [DataRow("utf-16")]
        [DataRow("utf-16BE")]
        public async Task Unicode_values_and_declared_encodings_survive_both_xml_read_paths(string encodingName)
        {
            var encoding = Encoding.GetEncoding(encodingName);
            const string text = "caf\u00e9 \u6f22 \U0001f600";
            var xml = "<?xml version=\"1.0\" encoding=\"" + encodingName + "\"?>" + Multi(Resource("<D:displayname>" + text + "</D:displayname>"));
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray();
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)207)
            {
                Content = new ByteArrayContent(bytes)
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            Assert.AreEqual(text, DavProperties.DisplayName.Read((await client.PropFindAsync(Target)).Resources.Single()).Single().Value);
            await using var listing = await client.OpenListingAsync(Target);
            int count = 0;
            await foreach (var item in listing.ReadResourcesAsync())
            {
                count++;
                Assert.AreEqual(text, DavProperties.DisplayName.Read(item).Single().Value);
            }
            Assert.AreEqual(1, count);
            Assert.IsTrue(listing.IsComplete);
        }

        [TestMethod]
        public async Task Response_character_budget_boundary_is_inclusive_for_both_read_paths()
        {
            var xml = Multi(Resource());
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            foreach (var budget in new[]
            {
                xml.Length, xml.Length - 1
            })
            {
                using var client = new DavClient(Endpoint, http, new DavClientOptions
                {
                    XmlLimits = new DavXmlLimits
                    {
                        MaxResponseCharacters = budget
                    }
                });
                if (budget == xml.Length)
                {
                    (await client.PropFindAsync(Target)).EnsureSucceeded();
                    await using var listing = await client.OpenListingAsync(Target);
                    await foreach (var item in listing.ReadResourcesAsync())
                    {
                    }
                    Assert.IsTrue(listing.IsComplete);
                }
                else
                {
                    await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.PropFindAsync(Target));
                }
            }
        }

        [TestMethod]
        public async Task Typed_property_decoders_keep_invalid_values_explicit_and_do_not_swallow_programming_errors()
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource(
                "<D:getcontentlength>9223372036854775808</D:getcontentlength><D:creationdate>invalid</D:creationdate>"
                + "<D:getlastmodified>invalid</D:getlastmodified><D:supportedlock><D:lockentry><D:lockscope/>"
                + "<D:locktype><D:write/></D:locktype></D:lockentry></D:supportedlock>"))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var resource = (await client.PropFindAsync(Target)).Resources.Single();
            Assert.IsInstanceOfType<OverflowException>(DavProperties.ContentLength.Read(resource).Single().DecodingError);
            Assert.IsFalse(DavProperties.CreationDate.Read(resource).Single().HasValue);
            Assert.IsFalse(DavProperties.LastModified.Read(resource).Single().HasValue);
            Assert.IsInstanceOfType<XmlException>(DavProperties.SupportedLocks.Read(resource).Single().DecodingError);
            var custom = new DavProperty<string>(DavProperties.ContentLength.Name, _ => throw new InvalidOperationException("decoder defect"));
            Assert.ThrowsException<InvalidOperationException>(() => custom.Read(resource).Single());
        }

        [TestMethod]
        public async Task Snapshots_are_read_only_and_xml_edits_do_not_mutate_sibling_results()
        {
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = XmlResponse(Multi(Resource()));
                response.Headers.Add("X-Test", new[]
                    {
                        "first", "second"
                    });
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.PropFindAsync(Target);
            Assert.ThrowsException<NotSupportedException>(() => ((IDictionary<string, IReadOnlyList<string>>)result.Headers).Add("X-New", Array.Empty<string>()));
            Assert.ThrowsException<NotSupportedException>(() => ((IList<string>)result.Headers["X-Test"])[0] = "changed");
            Assert.ThrowsException<NotSupportedException>(() => ((IList<DavResourceResponse>)result.Resources).Clear());
            Assert.ThrowsException<NotSupportedException>(() => ((IList<string>)result.Resources[0].Hrefs)[0] = "changed");
            result.Document.ToElement().RemoveNodes();
            Assert.AreEqual(1, result.Resources.Count);
            Assert.AreEqual("first", result.GetHeader("x-test"));
        }
    }
}
