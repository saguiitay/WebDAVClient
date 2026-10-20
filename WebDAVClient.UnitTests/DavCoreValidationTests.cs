using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.Protocol;
using WebDAVClient.UnitTests.Helpers;
using static WebDAVClient.UnitTests.DavClientTests;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DavCoreValidationTests
    {
        [DataTestMethod]
        [DataRow("")]
        [DataRow("relative")]
        [DataRow("../file")]
        [DataRow("?query")]
        [DataRow("//example.com/dav/file")]
        [DataRow("https://other.example/dav/file")]
        [DataRow("http://example.com/dav/file")]
        [DataRow("https://example.com:444/dav/file")]
        [DataRow("ftp://example.com/dav/file")]
        [DataRow("http://")]
        [DataRow("https://user@example.com/dav/file")]
        [DataRow("/dav/file#fragment")]
        [DataRow("/dav/a b")]
        [DataRow("/dav/café")]
        [DataRow("/dav/%")]
        [DataRow("/dav/%2")]
        [DataRow("/dav/%g2")]
        [DataRow("/dav/%2g")]
        [DataRow("/dav/./file")]
        [DataRow("/dav/../file")]
        [DataRow("/dav/%2e/file")]
        [DataRow("/dav/.%2e/file")]
        [DataRow("https://example.com/dav/../file")]
        [DataRow("/dav/\\file")]
        [DataRow("/dav/[file]")]
        public async Task Core_resource_hrefs_reject_invalid_escaping_forms_origins_and_dot_segments(string href)
        {
            var member = XElement.Parse(Multi(Resource())).Elements().Single();
            member.Element(XName.Get("href", "DAV:")).Value = href;
            await RejectBothAsync(Multi(member.ToString()));
        }

        [DataTestMethod]
        [DataRow("/dav/file")]
        [DataRow("/dav/a%20b")]
        [DataRow("/dav/a%2Fb")]
        [DataRow("/dav/a%252Fb")]
        [DataRow("/dav/caf%C3%A9")]
        [DataRow("/dav/caf%C3%A9%20%E4%B8%AD%E6%96%87%20%2B%20%25%20%23.bin")]
        [DataRow("https://example.com/dav/caf%C3%A9%20%E4%B8%AD%E6%96%87%20%2B%20%25%20%23.bin")]
        [DataRow("/dav/file?name=caf%C3%A9%20%25%20%23")]
        [DataRow("/dav/file?x=/../")]
        [DataRow("/dav/.hidden")]
        [DataRow("https://EXAMPLE.com:443/dav/file")]
        [DataRow("https://example.com/dav/file?q=a%20b")]
        [DataRow("https://example.com")]
        public async Task Core_href_validation_preserves_escaped_identity_and_query_without_following_links(string href)
        {
            var member = Resource("<X:dead><D:href>relative owner data</D:href></X:dead>", href: href);
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(member)));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            Assert.AreEqual(href, (await client.PropFindAsync(DavClientTests.File)).Resources.Single().Hrefs.Single());
            await using var listing = await client.OpenListingAsync(DavClientTests.File);
            await foreach (var resource in listing.ReadResourcesAsync())
            {
                Assert.AreEqual(href, resource.Hrefs.Single());
            }
            Assert.IsTrue(listing.IsComplete);
            Assert.AreEqual(2, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow("caf%C3%A9%20%E4%B8%AD%E6%96%87%20%2B%20%25%20%23.bin",
            "/dav/caf%c3%a9%20%e4%b8%ad%e6%96%87%20%2b%20%25%20%23.bin", true)]
        [DataRow("caf%C3%A9", "/dav/caf%c3%a9", true)]
        [DataRow("caf%C3%A9", "https://EXAMPLE.com:443/dav/caf%c3%a9", true)]
        [DataRow("file%2Fname", "/dav/file%2fname", true)]
        [DataRow("file?name=%C3%A9%2B", "/dav/file?name=%c3%a9%2b", true)]
        [DataRow("file%41", "/dav/fileA", true)]
        [DataRow("fileA", "/dav/file%41", true)]
        [DataRow("file%7E", "/dav/file~", true)]
        [DataRow("file~", "/dav/file%7e", true)]
        [DataRow("file?name=%41", "/dav/file?name=A", true)]
        [DataRow("file?name=%7E", "/dav/file?name=~", true)]
        [DataRow("File", "/dav/file", false)]
        [DataRow("file?name=A", "/dav/file?name=a", false)]
        [DataRow("file%2Fname", "/dav/file/name", false)]
        [DataRow("file%252F", "/dav/file%252f", false)]
        [DataRow("file%2F", "/dav/file%3f", false)]
        [DataRow("file%2F", "/dav/file%2b", false)]
        [DataRow("file%2F", "/dav/fileXYZ", false)]
        [DataRow("fileXYZ", "/dav/file%2f", false)]
        [DataRow("file", "/dav/files", false)]
        [DataRow("file?x=1", "/dav/file?x=2", false)]
        [DataRow("file?a=%26", "/dav/file?a=&", false)]
        [DataRow("file", "https://other.example/dav/file", false)]
        public async Task Proppatch_target_identity_ignores_only_escape_hex_case_not_path_or_query_identity(
            string reference, string responseHref, bool same)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource("<X:dead/>", href: responseHref))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var changes = new[] { DavPropertyChange.Set(new DavXmlValue(new XElement(XName.Get("dead", "urn:test"), "value"))) };
            if (same)
            {
                var result = await client.UpdatePropertiesAsync(DavPath.Parse(reference), changes);
                result.EnsureSucceeded();
                Assert.AreEqual(responseHref, result.Resources.Single().Hrefs.Single());
            }
            else
            {
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.UpdatePropertiesAsync(DavPath.Parse(reference), changes));
            }
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Duplicate_hrefs_and_mixed_resource_formats_cannot_certify_core_snapshots(bool mixed)
        {
            await RejectBothAsync(Multi(Resource() + Resource(href: mixed ? "https://example.com/dav/other" : "/dav/file")));
            await RejectBothAsync(Multi("<D:response><D:href>/dav/file</D:href><D:href>/dav/file</D:href>"
                + "<D:status>HTTP/1.1 423 Locked</D:status></D:response>"));
        }

        [TestMethod]
        public async Task Absolute_ipv6_origins_and_multiple_distinct_status_hrefs_remain_valid()
        {
            var endpoint = new Uri("https://[::1]/dav/");
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Member(423, "https://[::1]:443/dav/file")
                .Replace("<D:status>", "<D:href>https://[::1]/dav/alias</D:href><D:status>"))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(endpoint, http);
            var result = await client.DeleteAsync(DavClientTests.File);
            Assert.AreEqual(2, result.Resources.Single().Hrefs.Count);
        }

        public static IEnumerable<object[]> InvalidCoreShapes()
        {
            yield return new object[] { Multi(Resource() + "<D:responsedescription/><D:responsedescription/>") };
            foreach (var extra in new[]
            {
                "<D:responsedescription/><D:responsedescription/>",
                "<D:error><X:a/></D:error><D:error><X:b/></D:error>",
                "<D:location><D:href>/a</D:href></D:location><D:location><D:href>/b</D:href></D:location>",
                "<D:location/>", "<D:location><D:href>/a</D:href><D:href>/b</D:href></D:location>",
                "<D:location>mixed<D:href>/a</D:href></D:location>",
                "<D:location><D:href>relative</D:href></D:location>",
                "<D:location><D:href>//other.example/path</D:href></D:location>",
                "<D:location><D:href>1invalid:value</D:href></D:location>",
                "<D:error/>", "<D:error>mixed<X:a/></D:error>", "mixed text"
            })
            {
                yield return new object[] { Multi(Resource().Replace("</D:response>", extra + "</D:response>")) };
            }
            foreach (var extra in new[]
            {
                "<D:responsedescription/><D:responsedescription/>",
                "<D:error><X:a/></D:error><D:error><X:b/></D:error>",
                "<D:error/>", "<D:error>mixed<X:a/></D:error>", "<D:prop/>", "<D:status>HTTP/1.1 200 OK</D:status>",
                "mixed text"
            })
            {
                yield return new object[] { Multi(Resource().Replace("</D:propstat>", extra + "</D:propstat>")) };
            }
            yield return new object[] { Multi(Resource().Replace("<D:prop>", "<D:prop>mixed")) };
        }

        [DataTestMethod]
        [DynamicData(nameof(InvalidCoreShapes), DynamicDataSourceType.Method)]
        public async Task Core_cardinality_and_element_only_content_fail_buffered_and_streamed_without_completion(string xml)
        {
            await RejectBothAsync(xml);
        }

        [TestMethod]
        public async Task Generic_report_and_projection_keep_profile_defined_cardinality_and_resource_forms()
        {
            var xml = Multi(Member(404, "relative") + Member(404, "relative")
                + "<D:responsedescription>one</D:responsedescription><D:responsedescription>two</D:responsedescription>");
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var report = await client.ReportAsync(DavClientTests.File,
                DavXmlValue.Text(new WebDAVClient.Model.PropertyName("urn:profile", "report"), ""));
            Assert.AreEqual(2, report.Resources.Count);
            Assert.AreEqual("relative", report.Resources[0].Hrefs[0]);
            Assert.AreEqual(2, report.Document.ToElement().Elements(XName.Get("responsedescription", "DAV:")).Count());
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
            int count = 0;
            await foreach (var resource in DavXml.ReadMultiStatusAsync(stream, new DavXmlLimits(), legacy: true))
            {
                count++;
            }
            Assert.AreEqual(2, count);
        }

        [DataTestMethod]
        [DataRow("https://other.example/target")]
        [DataRow("mailto:owner@example.com")]
        [DataRow("/dav/redirect")]
        public async Task Core_extensions_and_redirect_locations_keep_their_normative_flexibility(string location)
        {
            var xml = Multi(Member(302).Replace("</D:response>", """
                <X:extension><D:location/><D:error/></X:extension>
                <D:location><X:href>ignored</X:href><D:href>LOCATION</D:href></D:location>
                <D:error><X:condition><D:href>foreign condition data</D:href></X:condition></D:error>
                <D:responsedescription>redirect</D:responsedescription></D:response>
                """.Replace("LOCATION", location)) + "<D:responsedescription>complete</D:responsedescription><X:tail/>");
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            Assert.AreEqual(location, (await client.PropFindAsync(DavClientTests.File)).Resources.Single().Location);
            await using var listing = await client.OpenListingAsync(DavClientTests.File);
            await foreach (var resource in listing.ReadResourcesAsync())
            {
                Assert.AreEqual(302, resource.StatusCode);
            }
            Assert.IsTrue(listing.IsComplete);
            Assert.AreEqual(2, listing.Extensions.Count);
        }

        [DataTestMethod]
        [DataRow("resourcetype", "mixed<D:collection/>")]
        [DataRow("supportedlock", "mixed")]
        [DataRow("supportedlock", "<D:lockentry>mixed<D:lockscope><D:shared/></D:lockscope><D:locktype><D:write/></D:locktype></D:lockentry>")]
        public async Task Core_element_only_properties_keep_decoding_errors_instead_of_flattening_text(string property, string value)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(Resource("<D:" + property + ">" + value + "</D:" + property + ">"))));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var resource = (await client.PropFindAsync(DavClientTests.File)).Resources.Single();
            if (property == "resourcetype")
            {
                var result = DavProperties.ResourceTypes.Read(resource).Single();
                Assert.IsFalse(result.HasValue);
                Assert.IsInstanceOfType<XmlException>(result.DecodingError);
            }
            else
            {
                var result = DavProperties.SupportedLocks.Read(resource).Single();
                Assert.IsFalse(result.HasValue);
                Assert.IsInstanceOfType<XmlException>(result.DecodingError);
            }
        }

        [DataTestMethod]
        [DataRow("<D:error xmlns:D=\"DAV:\"/>")]
        [DataRow("<D:error xmlns:D=\"DAV:\">text<D:condition/></D:error>")]
        public async Task Empty_or_mixed_error_containers_are_malformed_not_server_rejections(string xml)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml, 423));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var error = await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.DeleteAsync(DavClientTests.File));
            Assert.IsInstanceOfType<XmlException>(error.InnerException);
        }

        [DataTestMethod]
        [DataRow(200)]
        [DataRow(201)]
        [DataRow(204)]
        public async Task Refresh_requests_are_bodyless_but_successful_responses_require_a_200_lockdiscovery_document(int status)
        {
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)status));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.RefreshLockAsync(DavClientTests.File, "urn:lock:1"));
            Assert.IsNull(handler.Requests.Single().Body);
            Assert.IsFalse(handler.Requests.Single().Headers.ContainsKey("Depth"));
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow(201)]
        [DataRow(204)]
        public async Task Refresh_does_not_accept_an_acquisition_status_or_unconfirmed_success_even_with_valid_xml(int status)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(LockXml(), status));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.RefreshLockAsync(DavClientTests.File, "urn:lock:1"));
        }

        [DataTestMethod]
        [DataRow("lockscope")]
        [DataRow("locktype")]
        [DataRow("depth")]
        [DataRow("owner")]
        [DataRow("timeout")]
        [DataRow("locktoken")]
        [DataRow("lockroot")]
        public async Task Duplicate_lock_singletons_are_rejected_by_acquisition_refresh_and_typed_discovery(string field)
        {
            var document = XElement.Parse(LockXml());
            var active = document.Descendants(XName.Get("activelock", "DAV:")).Single();
            active.Add(new XElement(active.Element(XName.Get(field, "DAV:"))));
            await RejectLockAsync(document);
        }

        [DataTestMethod]
        [DataRow("locktoken", false)]
        [DataRow("locktoken", true)]
        [DataRow("lockroot", false)]
        [DataRow("lockroot", true)]
        public async Task Lock_uri_containers_require_one_scalar_href(string field, bool duplicate)
        {
            var document = XElement.Parse(LockXml());
            var element = document.Descendants(XName.Get(field, "DAV:")).Single();
            if (duplicate)
            {
                element.Add(new XElement(element.Elements().Single()));
            }
            else
            {
                element.RemoveNodes();
            }
            await RejectLockAsync(document);
        }

        [DataTestMethod]
        [DataRow("locktoken", "not-a-uri")]
        [DataRow("locktoken", "urn:lock:%zz")]
        [DataRow("locktoken", "urn:lock:token#fragment")]
        [DataRow("locktoken", "urn:lock:café")]
        public async Task Lock_discovery_rejects_invalid_token_uris(string field, string value)
        {
            var document = XElement.Parse(LockXml());
            document.Descendants(XName.Get(field, "DAV:")).Single().Elements().Single().Value = value;
            await RejectLockAsync(document);
        }

        [DataTestMethod]
        [DataRow("/dav/café 中文 + % #.bin")]
        [DataRow("not-a-uri")]
        [DataRow("https://other.example/root#metadata")]
        public async Task Lockroot_is_scalar_metadata_not_an_implicit_resource_target_or_ownership_condition(string root)
        {
            var document = XElement.Parse(LockXml("Second-10").Replace("<D:shared/>", "<D:exclusive/>"));
            document.Descendants(XName.Get("lockroot", "DAV:")).Single().Elements().Single().Value = root;
            using var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method.Method == "UNLOCK")
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (request.Method.Method == "PROPFIND")
                {
                    return XmlResponse(Multi(Resource(document.Elements().Single().ToString())));
                }
                var response = XmlResponse(document.ToString(), 200);
                response.Headers.TryAddWithoutValidation("Lock-Token", "<urn:lock:1>");
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.AcquireLockAsync(DavClientTests.File);
            result.EnsureSucceeded();
            Assert.AreEqual(root, result.Locks.Single().Root);
            Assert.AreEqual(TimeSpan.FromSeconds(10), result.Locks.Single().Timeout.Duration);
            var resource = (await client.PropFindAsync(DavClientTests.File)).Resources.Single();
            Assert.AreEqual(root, DavProperties.LockDiscovery.Read(resource).Single().Value.Single().Root);
            var lease = await DavLockLease.AcquireAsync(client, DavClientTests.File);
            Assert.AreEqual(DavLockLeaseState.Active, lease.State);
            Assert.AreEqual(root, (await lease.RenewAsync()).Root);
            await lease.ReleaseAsync();
            Assert.AreEqual(5, handler.Requests.Count);
            Assert.IsTrue(handler.Requests.All(r => r.RequestUri == client.Resolve(DavClientTests.File)));
        }

        [TestMethod]
        public async Task Duplicate_lockdiscovery_cannot_confirm_one_lock_response()
        {
            var document = XElement.Parse(LockXml());
            document.Add(new XElement(document.Elements().Single()));
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(document.ToString(), 200));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.AcquireLockAsync(DavClientTests.File));
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.RefreshLockAsync(DavClientTests.File, "urn:lock:1"));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("properties")]
        public async Task Recursive_lock_failures_require_at_least_one_resource_status(string shape)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(Multi(shape.Length == 0 ? "" : Resource())));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.AcquireLockAsync(DavClientTests.File));
        }

        [TestMethod]
        public async Task Acquisition_accepts_header_field_ows_and_lease_retains_the_bare_token()
        {
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = XmlResponse(LockXml().Replace("<D:shared/>", "<D:exclusive/>"), 200);
                response.Headers.TryAddWithoutValidation("Lock-Token", " \t<urn:lock:1>\t ");
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var lease = await DavLockLease.AcquireAsync(client, DavClientTests.File);
            Assert.AreEqual("urn:lock:1", lease.Token);
        }

        [DataTestMethod]
        [DataRow("urn:lock:1", false)]
        [DataRow("urn:lock:1", true)]
        [DataRow("opaquelocktoken:0x123456789abcdef", true)]
        [DataRow("urn:lock:caf%C3%A9%20%25%20%23", true)]
        [DataRow("https://tokens.example/caf%C3%A9%20%E4%B8%AD%20%25%20%23", false)]
        public async Task Confirmed_bare_tokens_preserve_opaque_identity_and_produce_bracketed_unlock_headers(string token, bool bare)
        {
            var wireHeader = " \t" + (bare ? token : "<" + token + ">") + "\t ";
            using var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Method.Method == "UNLOCK")
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                var response = XmlResponse(LockXml().Replace("<D:shared/>", "<D:exclusive/>")
                    .Replace("urn:lock:1", token), 200);
                response.Headers.TryAddWithoutValidation("Lock-Token", wireHeader);
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.AcquireLockAsync(DavClientTests.File);
            result.EnsureSucceeded();
            Assert.AreEqual("<" + token + ">", result.LockToken);
            Assert.AreEqual(wireHeader, result.GetHeader("Lock-Token"));
            Assert.AreEqual(token, result.Locks.Single().Token);
            var lease = await DavLockLease.AcquireAsync(client, DavClientTests.File);
            Assert.AreEqual(token, lease.Token);
            await lease.ReleaseAsync();
            Assert.AreEqual("<" + token + ">", handler.Requests.Last().Headers["Lock-Token"].Single());
            Assert.AreEqual(3, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" \t")]
        [DataRow("<>")]
        [DataRow("urn:lock:1>")]
        [DataRow("<urn:lock:1")]
        [DataRow("relative")]
        [DataRow("/dav/file")]
        [DataRow("urn:lock:other")]
        [DataRow("<urn:lock:other>")]
        [DataRow("urn:lock:bad%zz")]
        [DataRow("urn:lock:1#fragment")]
        [DataRow("<urn:lock:1>, <urn:lock:other>")]
        [DataRow("urn:lock:1, urn:lock:other")]
        public async Task Acquisition_does_not_guess_missing_invalid_or_contradictory_header_tokens(string header)
        {
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = XmlResponse(LockXml(), 200);
                if (header != null)
                {
                    response.Headers.TryAddWithoutValidation("Lock-Token", header);
                }
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var error = await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.AcquireLockAsync(DavClientTests.File));
            Assert.IsInstanceOfType<XmlException>(error.InnerException);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow("multiple-headers")]
        [DataRow("missing-body-token")]
        [DataRow("duplicate-body-token")]
        public async Task Acquisition_requires_one_header_and_one_unambiguous_matching_discovery_token(string problem)
        {
            var document = XElement.Parse(LockXml());
            var active = document.Descendants(XName.Get("activelock", "DAV:")).Single();
            if (problem == "missing-body-token")
            {
                active.Element(XName.Get("locktoken", "DAV:")).Remove();
            }
            else if (problem == "duplicate-body-token")
            {
                active.AddAfterSelf(new XElement(active));
            }
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = XmlResponse(document.ToString(), 200);
                response.Headers.TryAddWithoutValidation("Lock-Token", "urn:lock:1");
                if (problem == "multiple-headers")
                {
                    response.Headers.TryAddWithoutValidation("Lock-Token", "urn:lock:1");
                }
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var error = await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.AcquireLockAsync(DavClientTests.File));
            Assert.IsInstanceOfType<XmlException>(error.InnerException);
        }

        private static async Task RejectBothAsync(string xml)
        {
            using var handler = new StubHttpMessageHandler(_ => XmlResponse(xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var buffered = await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.PropFindAsync(DavClientTests.File));
            Assert.IsInstanceOfType<XmlException>(buffered.InnerException);
            await using var listing = await client.OpenListingAsync(DavClientTests.File);
            var streamed = await Assert.ThrowsExceptionAsync<WebDAVException>(async () =>
            {
                await foreach (var resource in listing.ReadResourcesAsync())
                {
                }
            });
            Assert.IsInstanceOfType<XmlException>(streamed.InnerException);
            Assert.IsFalse(listing.IsComplete);
            Assert.AreEqual(2, handler.Requests.Count);
        }

        private static async Task RejectLockAsync(XElement document)
        {
            using var handler = new StubHttpMessageHandler(r => r.Method.Method == "PROPFIND"
                ? XmlResponse(Multi(Resource(document.Elements().Single().ToString()))) : XmlResponse(document.ToString(), 200));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.AcquireLockAsync(DavClientTests.File));
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.RefreshLockAsync(DavClientTests.File, "urn:lock:1"));
            var resource = (await client.PropFindAsync(DavClientTests.File)).Resources.Single();
            var decoded = DavProperties.LockDiscovery.Read(resource).Single();
            Assert.IsFalse(decoded.HasValue);
            Assert.IsInstanceOfType<XmlException>(decoded.DecodingError);
        }
    }
}
