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
    public class DavLifetimeTests
    {
        private static DavPath Target => DavClientTests.File;

        public static IEnumerable<object[]> MetadataCases()
        {
            foreach (var operation in new[]
            {
                "OPTIONS", "PROPFIND", "PROPPATCH", "MKCOL", "DELETE", "MOVE", "COPY", "PUT", "LOCK", "REFRESH", "UNLOCK", "REPORT", "SYNC", "GET", "LIST", "REJECTED-LIST"
            })
            {
                foreach (var acquisition in new[]
                {
                    false, true
                })
                {
                    foreach (var deadline in new[]
                    {
                        false, true
                    })
                    {
                        yield return new object[]
                        {
                            operation, acquisition, deadline
                        };
                    }
                }
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(MetadataCases), DynamicDataSourceType.Method)]
        public async Task Every_metadata_path_cancels_and_releases_its_response(string operation, bool acquisition, bool deadline)
        {
            using var stalled = new PrefixThenStallStream("<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>");
            using var factory = new StallingContent();
            var response = new TrackingResponse(operation == "GET" || operation == "REJECTED-LIST" ? 423 : 207)
            {
                Content = acquisition ? factory : new StreamContent(stalled)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
            using var handler = new AsyncHttpMessageHandler((_, _) => Task.FromResult<HttpResponseMessage>(response));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http, new DavClientOptions
            {
                MetadataTimeout = deadline ? TimeSpan.FromMilliseconds(250) : Timeout.InfiniteTimeSpan
            });
            using var cancellation = new CancellationTokenSource();
            var pending = InvokeAsync(client, operation, cancellation.Token);
            await (acquisition ? factory.Started.Task : stalled.Stalled.Task).WaitAsync(TimeSpan.FromSeconds(5));
            if (!deadline)
            {
                cancellation.Cancel();
            }
            await AssertCancelledAsync(pending);
            Assert.AreEqual(!deadline, cancellation.IsCancellationRequested);
            Assert.AreEqual(1, response.DisposeCount);
            Assert.IsTrue(acquisition ? factory.IsDisposed : stalled.IsDisposed);
            Assert.AreEqual(1, handler.Requests.Count, "Cancellation must not trigger automatic replay.");
        }

        [DataTestMethod]
        [DataRow(403)]
        [DataRow(409)]
        [DataRow(423)]
        [DataRow(302)]
        public async Task Rejected_listing_preserves_error_xml_before_enumeration_and_releases_response_once(int status)
        {
            var response = new TrackingResponse(status)
            {
                Content = new StringContent("<D:error xmlns:D=\"DAV:\" xmlns:X=\"urn:detail\"><D:propfind-finite-depth>"
                    + "<X:detail xml:lang=\"en\">Try depth one</X:detail></D:propfind-finite-depth></D:error>",
                    Encoding.UTF8, "application/xml")
            };
            using var handler = new StubHttpMessageHandler(_ => response);
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await using var listing = await client.OpenListingAsync(Target);
            Assert.AreEqual(status, listing.StatusCode);
            Assert.AreEqual("propfind-finite-depth", listing.Errors.Single().Name.LocalName);
            var detail = listing.Errors[0].ToElement().Element(XName.Get("detail", "urn:detail"));
            Assert.AreEqual("Try depth one", detail.Value);
            Assert.AreEqual("en", detail.Attribute(XNamespace.Xml + "lang").Value);
            Assert.AreEqual(0, response.DisposeCount);
            await using var iterator = listing.ReadResourcesAsync().GetAsyncEnumerator();
            if (status == 409)
            {
                await Assert.ThrowsExceptionAsync<WebDAVConflictException>(() => iterator.MoveNextAsync().AsTask());
            }
            else
            {
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => iterator.MoveNextAsync().AsTask());
            }
            Assert.IsFalse(listing.IsComplete);
            Assert.AreEqual(1, listing.Errors.Count);
            listing.Dispose();
            Assert.AreEqual(1, response.DisposeCount);
        }

        [DataTestMethod]
        [DataRow("<D:error xmlns:D=\"DAV:\">", 1000)]
        [DataRow("<D:error xmlns:D=\"DAV:\"><D:propfind-finite-depth/></D:error>", 10)]
        public async Task Malformed_or_oversized_listing_rejection_disposes_response_before_throwing(string xml, int limit)
        {
            var response = new TrackingResponse(403)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml")
            };
            using var handler = new StubHttpMessageHandler(_ => response);
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http, new DavClientOptions
            {
                XmlLimits = new DavXmlLimits
                {
                    MaxResourceCharacters = limit
                }
            });
            var exception = await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.OpenListingAsync(Target));
            Assert.AreEqual(403, exception.ResponseStatusCode);
            Assert.IsInstanceOfType<System.Xml.XmlException>(exception.InnerException);
            Assert.AreEqual(1, response.DisposeCount);
        }

        [TestMethod]
        public async Task Opening_a_successful_listing_does_not_acquire_its_body()
        {
            using var content = new StallingContent();
            var response = new TrackingResponse(207)
            {
                Content = content
            };
            using var handler = new StubHttpMessageHandler(_ => response);
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await using (var listing = await client.OpenListingAsync(Target))
            {
                Assert.IsFalse(content.Started.Task.IsCompleted);
                Assert.IsFalse(listing.IsComplete);
                Assert.AreEqual(0, listing.Errors.Count);
            }
            Assert.AreEqual(1, response.DisposeCount);
        }

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task Upload_cancellation_propagates_to_serialization_without_changing_input_ownership(bool disposeContent, bool beforeSend)
        {
            using var content = new PrefixThenStallStream("prefix");
            using var cancellation = new CancellationTokenSource();
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                await request.Content.CopyToAsync(Stream.Null, token);
                throw new AssertFailedException("An incomplete upload cannot succeed.");
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            if (beforeSend)
            {
                cancellation.Cancel();
            }
            var pending = client.UploadAsync(Target, content, new UploadOptions
            {
                DisposeContent = disposeContent
            }, cancellation.Token);
            if (!beforeSend)
            {
                await content.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
            }
            await AssertCancelledAsync(pending);
            Assert.AreEqual(disposeContent, content.IsDisposed);
            Assert.AreEqual(beforeSend ? 0 : 1, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow(false, "rejected")]
        [DataRow(true, "rejected")]
        [DataRow(false, "transport")]
        [DataRow(true, "transport")]
        [DataRow(false, "invalid")]
        [DataRow(true, "invalid")]
        public async Task Upload_failure_ownership_changes_only_after_argument_validation(bool disposeContent, string failure)
        {
            using var content = new MemoryStream(new byte[]
                {
                    1
                });
            using var handler = new StubHttpMessageHandler(_ => failure == "transport"
                ? throw new HttpRequestException("connection lost")
                : new HttpResponseMessage(HttpStatusCode.PreconditionFailed));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var options = new UploadOptions
            {
                DisposeContent = disposeContent,
                ContentType = failure == "invalid" ? "invalid media type" : "application/octet-stream"
            };
            if (failure == "invalid")
            {
                Assert.ThrowsException<FormatException>(() => client.UploadAsync(Target, content, options));
            }
            else if (failure == "transport")
            {
                await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.UploadAsync(Target, content, options));
            }
            else
            {
                Assert.AreEqual(412, (await client.UploadAsync(Target, content, options)).StatusCode);
            }
            Assert.AreEqual(!disposeContent || failure == "invalid", content.CanRead);
            Assert.AreEqual(failure == "invalid" ? 0 : 1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Successful_download_stream_acquisition_remains_cancellable()
        {
            using var content = new StallingContent();
            var response = new TrackingResponse(200)
            {
                Content = content
            };
            using var handler = new AsyncHttpMessageHandler((_, _) => Task.FromResult<HttpResponseMessage>(response));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            using var cancellation = new CancellationTokenSource();
            var pending = client.DownloadAsync(Target, cancellationToken: cancellation.Token);
            await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await AssertCancelledAsync(pending);
            Assert.AreEqual(1, response.DisposeCount);
            Assert.IsTrue(content.IsDisposed);
        }

        [TestMethod]
        public async Task Download_read_tokens_and_response_lifetime_are_independent_of_completed_request_and_client()
        {
            using var body = new PrefixThenStallStream("abc");
            var response = new TrackingResponse(200)
            {
                Content = new StreamContent(body)
            };
            using var handler = new AsyncHttpMessageHandler((_, _) => Task.FromResult<HttpResponseMessage>(response));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http, new DavClientOptions
            {
                MetadataTimeout = TimeSpan.FromMilliseconds(1)
            });
            using var requestCancellation = new CancellationTokenSource();
            await using var download = await client.DownloadAsync(Target, cancellationToken: requestCancellation.Token);
            requestCancellation.Cancel();
            client.Dispose();
            var buffer = new byte[3];
            await download.Content.ReadExactlyAsync(buffer);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("abc"), buffer);
            using var readCancellation = new CancellationTokenSource();
            var pending = download.Content.ReadAsync(buffer, readCancellation.Token).AsTask();
            await body.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            readCancellation.Cancel();
            await AssertCancelledAsync(pending);
            Assert.AreEqual(0, response.DisposeCount);
            download.Dispose();
            await download.DisposeAsync();
            Assert.AreEqual(1, response.DisposeCount);
            Assert.IsTrue(body.IsDisposed);
        }

        [TestMethod]
        public async Task Not_modified_never_acquires_or_parses_content_and_releases_once()
        {
            using var content = new StallingContent();
            content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
            var response = new TrackingResponse(304)
            {
                Content = content
            };
            using var handler = new AsyncHttpMessageHandler((_, _) => Task.FromResult<HttpResponseMessage>(response));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            using var result = await client.DownloadAsync(Target, new DownloadOptions
            {
                IfNoneMatch = "\"known\""
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(result.NotModified);
            Assert.IsNull(result.Content);
            Assert.IsFalse(content.Started.Task.IsCompleted);
            result.Dispose();
            result.Dispose();
            Assert.AreEqual(1, response.DisposeCount);
            Assert.IsTrue(content.IsDisposed);
        }

        [DataTestMethod]
        [DataRow("request")]
        [DataRow("enumeration")]
        [DataRow("dispose")]
        public async Task Listing_can_cancel_between_resources_and_never_certifies_partial_completion(string source)
        {
            using var stream = new PrefixThenStallStream("<D:multistatus xmlns:D=\"DAV:\">" + Resource() + "<D:response><D:href>");
            var response = new TrackingResponse(207)
            {
                Content = new StreamContent(stream)
            };
            using var handler = new AsyncHttpMessageHandler((_, _) => Task.FromResult<HttpResponseMessage>(response));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            using var requestCancellation = new CancellationTokenSource();
            using var enumerationCancellation = new CancellationTokenSource();
            await using var listing = await client.OpenListingAsync(Target, cancellationToken: requestCancellation.Token);
            await using var iterator = listing.ReadResourcesAsync(enumerationCancellation.Token).GetAsyncEnumerator();
            Assert.IsTrue(await iterator.MoveNextAsync());
            var pending = iterator.MoveNextAsync().AsTask();
            await stream.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            switch (source)
            {
                case "request":
                    requestCancellation.Cancel();
                    break;
                case "enumeration":
                    enumerationCancellation.Cancel();
                    break;
                default:
                    listing.Dispose();
                    break;
            }
            await AssertCancelledAsync(pending);
            Assert.IsFalse(listing.IsComplete);
            Assert.AreEqual(1, response.DisposeCount);
            Assert.IsTrue(stream.IsDisposed);
        }

        [TestMethod]
        public async Task Listing_deadline_runs_while_caller_is_between_resources()
        {
            var response = new TrackingResponse(207)
            {
                Content = new StringContent(Multi(Resource() + Resource()))
            };
            using var handler = new AsyncHttpMessageHandler((_, _) => Task.FromResult<HttpResponseMessage>(response));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http, new DavClientOptions
            {
                MetadataTimeout = TimeSpan.FromMilliseconds(250)
            });
            await using var listing = await client.OpenListingAsync(Target);
            await using var iterator = listing.ReadResourcesAsync().GetAsyncEnumerator();
            Assert.IsTrue(await iterator.MoveNextAsync());
            await Task.Delay(500);
            await AssertCancelledAsync(iterator.MoveNextAsync().AsTask());
            Assert.IsFalse(listing.IsComplete);
            Assert.AreEqual(1, response.DisposeCount);
        }

        [TestMethod]
        public async Task Concurrent_operations_do_not_share_conditions_or_request_headers()
        {
            const int count = 16;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int requests = 0;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                if (Interlocked.Increment(ref requests) == count)
                {
                    entered.TrySetResult();
                }
                await release.Task.WaitAsync(token);
                string id = request.RequestUri.Segments.Last();
                Assert.AreEqual(id, request.Headers.GetValues("X-Operation").Single());
                Assert.AreEqual("\"" + id + "\"", request.Headers.IfMatch.Single().ToString());
                Assert.AreEqual("(<urn:lock:" + id + ">)", request.Headers.GetValues("If").Single());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var headers = new Dictionary<string, string>();
            var tasks = new List<Task<DavMultiStatusResult>>();
            for (int i = 0; i < count; i++)
            {
                string id = "item" + i;
                headers["X-Operation"] = id;
                tasks.Add(client.DeleteAsync(DavPath.FromSegments(id), new DavRequestOptions
                {
                    Headers = headers,
                    IfMatch = "\"" + id + "\"",
                    Conditions = DavIfConditions.Untagged(new DavConditionList(DavCondition.StateToken("urn:lock:" + id)))
                }, deadline.Token));
            }
            await entered.Task.WaitAsync(deadline.Token);
            headers.Clear();
            release.TrySetResult();
            foreach (var result in await Task.WhenAll(tasks))
            {
                result.EnsureSucceeded();
            }
            Assert.AreEqual(count, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Mutable_property_instruction_timeout_and_range_collections_are_snapshotted_before_yielding()
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                await release.Task.WaitAsync(token);
                switch (request.Method.Method)
                {
                    case "PROPFIND":
                    case "PROPPATCH":
                        var xml = XElement.Parse(await request.Content.ReadAsStringAsync(token));
                        Assert.IsTrue(xml.Descendants(XName.Get("displayname", "DAV:")).Any());
                        Assert.IsFalse(xml.Descendants(XName.Get("getetag", "DAV:")).Any());
                        return XmlResponse(Multi(Resource()));
                    case "LOCK":
                        Assert.AreEqual("Second-30", request.Headers.GetValues("Timeout").Single());
                        return XmlResponse(LockXml(), 200);
                    case "GET":
                        Assert.AreEqual("bytes=0-2", request.Headers.Range.ToString());
                        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                        {
                            Content = new ByteArrayContent(new byte[3])
                        };
                        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 2, 3);
                        return response;
                    default:
                        throw new AssertFailedException("Unexpected method");
                }
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var properties = new[]
            {
                DavProperties.DisplayName.Name
            };
            var changes = new[]
            {
                DavPropertyChange.Remove(DavProperties.DisplayName.Name)
            };
            var timeouts = new[]
            {
                DavLockTimeout.Finite(TimeSpan.FromSeconds(30))
            };
            var range = new RangeHeaderValue(0, 2);
            var find = client.PropFindAsync(Target, new PropFindOptions
            {
                Selection = DavPropertySelection.Selected,
                Properties = properties
            }, deadline.Token);
            var patch = client.UpdatePropertiesAsync(Target, changes, cancellationToken: deadline.Token);
            var locking = client.AcquireLockAsync(Target, new LockOptions
            {
                Timeouts = timeouts
            }, deadline.Token);
            var download = client.DownloadAsync(Target, new DownloadOptions
            {
                Range = range
            }, deadline.Token);
            properties[0] = DavProperties.ETag.Name;
            changes[0] = DavPropertyChange.Remove(DavProperties.ETag.Name);
            timeouts[0] = DavLockTimeout.Infinite;
            range.Ranges.Add(new RangeItemHeaderValue(5, 6));
            release.TrySetResult();
            (await find).EnsureSucceeded();
            (await patch).EnsureSucceeded();
            (await locking).EnsureSucceeded();
            using var result = await download;
            Assert.IsNotNull(result.Content);
        }

        private static async Task InvokeAsync(DavClient client, string operation, CancellationToken token)
        {
            switch (operation)
            {
                case "OPTIONS":
                    await client.GetCapabilitiesAsync(Target, cancellationToken: token);
                    break;
                case "PROPFIND":
                    await client.PropFindAsync(Target, cancellationToken: token);
                    break;
                case "PROPPATCH":
                    await client.UpdatePropertiesAsync(Target, new[]
                        {
                            DavPropertyChange.Remove(DavProperties.DisplayName.Name)
                        }, cancellationToken: token);
                    break;
                case "MKCOL":
                    await client.CreateCollectionAsync(Target, cancellationToken: token);
                    break;
                case "DELETE":
                    await client.DeleteAsync(Target, cancellationToken: token);
                    break;
                case "MOVE":
                    await client.MoveAsync(Target, DavPath.Parse("target"), cancellationToken: token);
                    break;
                case "COPY":
                    await client.CopyAsync(Target, DavPath.Parse("target"), cancellationToken: token);
                    break;
                case "PUT":
                    await client.UploadAsync(Target, Stream.Null, cancellationToken: token);
                    break;
                case "LOCK":
                    await client.AcquireLockAsync(Target, cancellationToken: token);
                    break;
                case "REFRESH":
                    await client.RefreshLockAsync(Target, "urn:lock", cancellationToken: token);
                    break;
                case "UNLOCK":
                    await client.UnlockAsync(Target, "urn:lock", cancellationToken: token);
                    break;
                case "REPORT":
                    await client.ReportAsync(Target, DavXmlValue.Text(new PropertyName("urn:report", "request"), ""), cancellationToken: token);
                    break;
                case "SYNC":
                    await client.SyncCollectionAsync(Target, "urn:before", Array.Empty<PropertyName>(), cancellationToken: token);
                    break;
                case "GET":
                    using (await client.DownloadAsync(Target, cancellationToken: token))
                    {
                    }
                    break;
                case "LIST":
                case "REJECTED-LIST":
                    await using (var listing = await client.OpenListingAsync(Target, cancellationToken: token))
                    {
                        await foreach (var item in listing.ReadResourcesAsync())
                        {
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        private static async Task AssertCancelledAsync(Task task)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail("Expected cancellation.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        private sealed class TrackingResponse : HttpResponseMessage
        {
            public int DisposeCount
            {
                get;
                private set;
            }

            public TrackingResponse(int status) : base((HttpStatusCode)status)
            {
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeCount++;
                }
                base.Dispose(disposing);
            }
        }

        private sealed class StallingContent : HttpContent
        {
            public TaskCompletionSource Started
            {
                get;
            } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool IsDisposed
            {
                get;
                private set;
            }

            protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new AssertFailedException("The content factory must be cancelled.");
            }

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    IsDisposed = true;
                }
                base.Dispose(disposing);
            }
        }
    }
}
