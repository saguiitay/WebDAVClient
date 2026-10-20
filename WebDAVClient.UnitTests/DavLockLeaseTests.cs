using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.Protocol;
using WebDAVClient.UnitTests.Helpers;
using static WebDAVClient.UnitTests.DavClientTests;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DavLockLeaseTests
    {
        private static HttpResponseMessage Granted(string timeout = "Second-10", string header = "<urn:lock:1>", string xml = null)
        {
            var response = XmlResponse(xml ?? LockXml(timeout).Replace("<D:shared/>", "<D:exclusive/>"), 200);
            if (header != null)
            {
                response.Headers.TryAddWithoutValidation("Lock-Token", header);
            }
            return response;
        }

        [DataTestMethod]
        [DataRow(DavLockScope.Shared, DavDepth.Infinity)]
        [DataRow(DavLockScope.Exclusive, DavDepth.Zero)]
        public async Task Explicit_acquisition_and_renewal_preserve_requested_lock_identity(DavLockScope scope, DavDepth depth)
        {
            var xml = LockXml("Infinite").Replace("<D:shared/>", scope == DavLockScope.Shared ? "<D:shared/>" : "<D:exclusive/>")
                .Replace("<D:depth>0", depth == DavDepth.Infinity ? "<D:depth>infinity" : "<D:depth>0");
            using var handler = new StubHttpMessageHandler(_ => Granted(xml: xml));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var lease = await DavLockLease.AcquireAsync(client, File, new LockOptions { Scope = scope, Depth = depth });
            Assert.AreEqual(DavLockLeaseState.Active, lease.State);
            await lease.RenewAsync();
            Assert.IsTrue(lease.Observation.Timeout.IsInfinite);
            Assert.AreEqual(DavLockLeaseState.Active, lease.State);
        }

        [DataTestMethod]
        [DataRow("Second-10", DavLockLeaseState.Active)]
        [DataRow("Infinite", DavLockLeaseState.Active)]
        [DataRow(null, DavLockLeaseState.Uncertain)]
        public async Task Acquisition_tracks_only_confirmed_timeouts_and_releases_once(string timeout, DavLockLeaseState expected)
        {
            var clock = new ManualClock();
            using var handler = new StubHttpMessageHandler(r => r.Method.Method == "UNLOCK"
                ? new HttpResponseMessage(HttpStatusCode.NoContent) : Granted(timeout));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var lease = await DavLockLease.AcquireAsync(client, File, timeProvider: clock);
            Assert.AreSame(File, lease.Path);
            Assert.AreEqual("urn:lock:1", lease.Token);
            Assert.AreEqual("exclusive", lease.Observation.Scope.LocalName);
            Assert.AreEqual(expected, lease.State);
            if (expected == DavLockLeaseState.Uncertain)
            {
                Assert.ThrowsException<InvalidOperationException>(() => lease.CreateConditions());
            }
            else
            {
                var result = await client.DeleteAsync(File, new DavRequestOptions { Conditions = lease.CreateConditions() });
                Assert.AreEqual(200, result.StatusCode);
                Assert.AreEqual("<https://example.com/dav/file> (<urn:lock:1>)", handler.Requests.Last().Headers["If"].Single());
            }
            clock.Advance(TimeSpan.FromSeconds(11));
            Assert.AreEqual(timeout == "Second-10" ? DavLockLeaseState.Expired : expected, lease.State);
            await lease.DisposeAsync();
            await lease.DisposeAsync();
            await lease.ReleaseAsync();
            Assert.AreEqual(DavLockLeaseState.Released, lease.State);
            Assert.AreEqual(1, handler.Requests.Count(r => r.Method.Method == "UNLOCK"));
            Assert.AreEqual("<urn:lock:1>", handler.Requests.Last().Headers["Lock-Token"].Single());
            Assert.ThrowsException<InvalidOperationException>(() => lease.CreateConditions());
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => lease.RenewAsync());
            await client.HeadAsync(File);
        }

        [TestMethod]
        public async Task Local_deadline_includes_acquisition_and_refresh_network_time_and_allows_explicit_reconciliation()
        {
            var clock = new ManualClock();
            using var handler = new StubHttpMessageHandler(r =>
            {
                clock.Advance(TimeSpan.FromSeconds(6));
                return r.Method.Method == "LOCK" ? Granted() : new HttpResponseMessage(HttpStatusCode.NoContent);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var lease = await DavLockLease.AcquireAsync(client, File, timeProvider: clock);
            Assert.AreEqual(DavLockLeaseState.Active, lease.State);
            clock.Advance(TimeSpan.FromSeconds(4));
            Assert.AreEqual(DavLockLeaseState.Expired, lease.State);
            Assert.ThrowsException<InvalidOperationException>(() => lease.CreateConditions());
            var observation = await lease.RenewAsync(new[] { DavLockTimeout.Finite(TimeSpan.FromSeconds(20)) });
            Assert.AreSame(observation, lease.Observation);
            Assert.AreEqual(DavLockLeaseState.Active, lease.State);
            Assert.IsNull(handler.Requests[1].Body);
            Assert.IsFalse(handler.Requests[1].Headers.ContainsKey("Depth"));
            Assert.AreEqual("(<urn:lock:1>)", handler.Requests[1].Headers["If"].Single());
            Assert.AreEqual("Second-20", handler.Requests[1].Headers["Timeout"].Single());
            await client.DeleteAsync(File, new DavRequestOptions { Conditions = lease.CreateConditions("\"version\"") });
            Assert.AreEqual("<https://example.com/dav/file> (<urn:lock:1> [\"version\"])", handler.Requests.Last().Headers["If"].Single());
            clock.Advance(TimeSpan.FromSeconds(4));
            Assert.AreEqual(DavLockLeaseState.Expired, lease.State);
            await lease.ReleaseAsync();
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("<>")]
        [DataRow("urn:lock:1>")]
        [DataRow("<urn:lock:1")]
        [DataRow("<not absolute>")]
        [DataRow("<urn:lock:wrong>")]
        public async Task Unconfirmed_acquisition_is_never_exposed_as_a_lease(string header)
        {
            using var handler = new StubHttpMessageHandler(_ => Granted(header: header));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => DavLockLease.AcquireAsync(client, File));
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow("duplicate")]
        [DataRow("missing")]
        [DataRow("wrong-type")]
        [DataRow("wrong-scope")]
        [DataRow("wrong-depth")]
        [DataRow("multiple-headers")]
        public async Task Acquisition_requires_exactly_one_matching_write_lock(string problem)
        {
            var xml = XElement.Parse(LockXml().Replace("<D:shared/>", "<D:exclusive/>"));
            XNamespace dav = "DAV:";
            var discovery = xml.Element(dav + "lockdiscovery");
            var active = discovery.Element(dav + "activelock");
            if (problem == "duplicate")
            {
                discovery.Add(new XElement(active));
            }
            else if (problem == "missing")
            {
                active.Element(dav + "locktoken").Remove();
            }
            else if (problem == "wrong-type")
            {
                active.Element(dav + "locktype").ReplaceNodes(new XElement(dav + "unknown"));
            }
            else if (problem == "wrong-scope")
            {
                active.Element(dav + "lockscope").ReplaceNodes(new XElement(dav + "shared"));
            }
            else if (problem == "wrong-depth")
            {
                active.Element(dav + "depth").Value = "infinity";
            }
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = Granted(xml: xml.ToString());
                if (problem == "multiple-headers")
                {
                    response.Headers.TryAddWithoutValidation("Lock-Token", "<urn:lock:2>");
                }
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => DavLockLease.AcquireAsync(client, File));
        }

        [DataTestMethod]
        [DataRow("bodyless")]
        [DataRow("wrong-token")]
        [DataRow("rejected")]
        [DataRow("transport")]
        [DataRow("unknown-timeout")]
        [DataRow("changed-scope")]
        [DataRow("changed-depth")]
        public async Task Renewal_never_substitutes_requested_timeout_or_reacquires(string problem)
        {
            int attempts = 0;
            using var handler = new StubHttpMessageHandler(r =>
            {
                if (r.Method.Method == "UNLOCK")
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (++attempts == 1)
                {
                    return Granted();
                }
                return problem switch
                {
                    "bodyless" => new HttpResponseMessage(HttpStatusCode.OK),
                    "wrong-token" => Granted(xml: LockXml().Replace("urn:lock:1", "urn:other")),
                    "rejected" => new HttpResponseMessage(HttpStatusCode.PreconditionFailed),
                    "transport" => throw new HttpRequestException("Disconnected"),
                    "changed-scope" => Granted(xml: LockXml()),
                    "changed-depth" => Granted(xml: LockXml().Replace("<D:shared/>", "<D:exclusive/>").Replace("<D:depth>0", "<D:depth>infinity")),
                    _ => Granted(timeout: null)
                };
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var lease = await DavLockLease.AcquireAsync(client, File);
            if (problem == "unknown-timeout")
            {
                await lease.RenewAsync();
            }
            else if (problem == "transport")
            {
                await Assert.ThrowsExceptionAsync<HttpRequestException>(() => lease.RenewAsync());
            }
            else
            {
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => lease.RenewAsync());
            }
            Assert.AreEqual(DavLockLeaseState.Uncertain, lease.State);
            Assert.ThrowsException<InvalidOperationException>(() => lease.CreateConditions());
            Assert.AreEqual(2, attempts);
            await lease.ReleaseAsync();
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Failed_release_is_uncertain_and_never_replayed_by_disposal(bool transportFailure)
        {
            using var handler = new StubHttpMessageHandler(r => r.Method.Method != "UNLOCK" ? Granted()
                : transportFailure ? throw new HttpRequestException("Disconnected")
                : new HttpResponseMessage(HttpStatusCode.Conflict));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var lease = await DavLockLease.AcquireAsync(client, File);
            if (transportFailure)
            {
                await Assert.ThrowsExceptionAsync<HttpRequestException>(() => lease.ReleaseAsync());
            }
            else
            {
                await Assert.ThrowsExceptionAsync<WebDAVConflictException>(() => lease.ReleaseAsync());
            }
            Assert.AreEqual(DavLockLeaseState.Uncertain, lease.State);
            await lease.DisposeAsync();
            await lease.ReleaseAsync();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => lease.RenewAsync());
            Assert.AreEqual(2, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Cancellation_before_operations_preserves_confirmed_state_and_null_arguments_do_not_send()
        {
            using var handler = new StubHttpMessageHandler(_ => Granted());
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => DavLockLease.AcquireAsync(null, File));
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => DavLockLease.AcquireAsync(client, null));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => DavLockLease.AcquireAsync(client, File, cancellationToken: cancellation.Token));
            var lease = await DavLockLease.AcquireAsync(client, File);
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => lease.RenewAsync(cancellationToken: cancellation.Token));
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => lease.ReleaseAsync(cancellation.Token));
            Assert.AreEqual(DavLockLeaseState.Active, lease.State);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Renewal_and_concurrent_release_are_serialized_without_background_work()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int locks = 0;
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                if (request.Method.Method == "UNLOCK")
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (Interlocked.Increment(ref locks) > 1)
                {
                    entered.SetResult();
                    await resume.Task.WaitAsync(token);
                }
                return Granted();
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var lease = await DavLockLease.AcquireAsync(client, File);
            var renewal = lease.RenewAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(DavLockLeaseState.Uncertain, lease.State);
            using var cancellation = new CancellationTokenSource();
            var cancelledRelease = lease.ReleaseAsync(cancellation.Token);
            var release = lease.ReleaseAsync();
            var dispose = lease.DisposeAsync().AsTask();
            Assert.AreEqual(2, handler.Requests.Count);
            cancellation.Cancel();
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => cancelledRelease);
            resume.SetResult();
            await Task.WhenAll(renewal, release, dispose);
            Assert.AreEqual(DavLockLeaseState.Released, lease.State);
            Assert.AreEqual(3, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Refresh_submits_exactly_one_token_without_body_or_depth()
        {
            using var handler = new StubHttpMessageHandler(_ => Granted());
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            var result = await client.RefreshLockAsync(File, "urn:lock:1", new[] { DavLockTimeout.Infinite });
            result.EnsureSucceeded();
            var request = handler.Requests.Single();
            Assert.IsNull(request.Body);
            Assert.IsFalse(request.Headers.ContainsKey("Depth"));
            Assert.AreEqual("(<urn:lock:1>)", request.Headers["If"].Single());
            Assert.AreEqual("Infinite", request.Headers["Timeout"].Single());
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => client.RefreshLockAsync(File, null));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.RefreshLockAsync(File, "not absolute"));
            Assert.AreEqual(1, handler.Requests.Count);
        }

        private sealed class ManualClock : TimeProvider
        {
            private long m_ticks;

            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public override long GetTimestamp() => m_ticks;

            public void Advance(TimeSpan duration)
            {
                m_ticks += duration.Ticks;
            }
        }
    }
}
