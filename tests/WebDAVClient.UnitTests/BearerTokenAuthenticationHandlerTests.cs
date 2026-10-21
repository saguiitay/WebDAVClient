using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Authentication;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    /// <summary>
    /// Behavioural tests for <see cref="BearerTokenAuthenticationHandler"/> and
    /// the bearer-token <see cref="Client"/> constructors. Verifies the
    /// <c>Authorization: Bearer &lt;token&gt;</c> header is injected on every
    /// outbound request, that the async token provider is invoked per request
    /// (refresh scenario), and the constructor argument-validation contract.
    /// </summary>
    [TestClass]
    public class BearerTokenAuthenticationHandlerTests
    {
        private static HttpResponseMessage Ok() => new HttpResponseMessage(HttpStatusCode.OK);

        private static System.Net.Http.HttpClient BuildClient(BearerTokenAuthenticationHandler auth, StubHttpMessageHandler stub)
        {
            auth.InnerHandler = stub;
            return new System.Net.Http.HttpClient(auth);
        }

        [TestMethod]
        public async Task Static_token_constructor_sets_Bearer_Authorization_header()
        {
            var stub = new StubHttpMessageHandler(_ => Ok());
            using var http = BuildClient(new BearerTokenAuthenticationHandler("abc.def.ghi"), stub);

            await http.GetAsync("http://example.com/x");

            Assert.AreEqual(1, stub.Requests.Count);
            CollectionAssert.AreEqual(new[] { "Bearer abc.def.ghi" }, stub.Requests[0].Headers["Authorization"]);
        }

        [TestMethod]
        public async Task Static_token_constructor_sets_header_on_every_request()
        {
            var stub = new StubHttpMessageHandler(_ => Ok());
            using var http = BuildClient(new BearerTokenAuthenticationHandler("token-1"), stub);

            await http.GetAsync("http://example.com/a");
            await http.GetAsync("http://example.com/b");
            await http.GetAsync("http://example.com/c");

            Assert.AreEqual(3, stub.Requests.Count);
            foreach (var req in stub.Requests)
            {
                CollectionAssert.AreEqual(new[] { "Bearer token-1" }, req.Headers["Authorization"]);
            }
        }

        [TestMethod]
        public async Task Provider_constructor_invokes_provider_per_request_for_refresh()
        {
            int calls = 0;
            Task<string> Provider(CancellationToken ct)
            {
                var n = Interlocked.Increment(ref calls);
                return Task.FromResult("token-" + n);
            }

            var stub = new StubHttpMessageHandler(_ => Ok());
            using var http = BuildClient(new BearerTokenAuthenticationHandler(Provider), stub);

            await http.GetAsync("http://example.com/1");
            await http.GetAsync("http://example.com/2");
            await http.GetAsync("http://example.com/3");

            Assert.AreEqual(3, calls, "provider must be invoked for every outbound request");
            CollectionAssert.AreEqual(new[] { "Bearer token-1" }, stub.Requests[0].Headers["Authorization"]);
            CollectionAssert.AreEqual(new[] { "Bearer token-2" }, stub.Requests[1].Headers["Authorization"]);
            CollectionAssert.AreEqual(new[] { "Bearer token-3" }, stub.Requests[2].Headers["Authorization"]);
        }

        [TestMethod]
        public async Task Provider_returning_null_skips_Authorization_header()
        {
            var stub = new StubHttpMessageHandler(_ => Ok());
            using var http = BuildClient(new BearerTokenAuthenticationHandler(_ => Task.FromResult<string>(null)), stub);

            await http.GetAsync("http://example.com/x");

            Assert.AreEqual(1, stub.Requests.Count);
            Assert.IsFalse(stub.Requests[0].Headers.ContainsKey("Authorization"),
                "null provider result must omit the Authorization header rather than send 'Bearer '");
        }

        [TestMethod]
        public async Task Provider_returning_empty_string_skips_Authorization_header()
        {
            var stub = new StubHttpMessageHandler(_ => Ok());
            using var http = BuildClient(new BearerTokenAuthenticationHandler(_ => Task.FromResult(string.Empty)), stub);

            await http.GetAsync("http://example.com/x");

            Assert.IsFalse(stub.Requests[0].Headers.ContainsKey("Authorization"));
        }

        [TestMethod]
        public void Provider_constructor_throws_on_null_provider()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new BearerTokenAuthenticationHandler((Func<CancellationToken, Task<string>>)null));
        }

        [TestMethod]
        public void Static_constructor_throws_on_null_token()
        {
            Assert.ThrowsException<ArgumentException>(() => new BearerTokenAuthenticationHandler((string)null));
        }

        [TestMethod]
        public void Static_constructor_throws_on_empty_token()
        {
            Assert.ThrowsException<ArgumentException>(() => new BearerTokenAuthenticationHandler(string.Empty));
        }

        [TestMethod]
        public void Static_constructor_throws_on_whitespace_token()
        {
            Assert.ThrowsException<ArgumentException>(() => new BearerTokenAuthenticationHandler("   "));
        }

        [TestMethod]
        public void Client_string_token_constructor_throws_on_null_token()
        {
            Assert.ThrowsException<ArgumentException>(() => new Client((string)null));
        }

        [TestMethod]
        public void Client_string_token_constructor_throws_on_empty_token()
        {
            Assert.ThrowsException<ArgumentException>(() => new Client(string.Empty));
        }

        [TestMethod]
        public void Client_provider_constructor_throws_on_null_provider()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new Client((Func<CancellationToken, Task<string>>)null));
        }

        [TestMethod]
        public void Client_string_token_constructor_constructs_without_throwing()
        {
            using var client = new Client("a-token");
            Assert.IsNotNull(client);
        }

        [TestMethod]
        public void Client_provider_constructor_constructs_without_throwing()
        {
            using var client = new Client(_ => Task.FromResult("a-token"));
            Assert.IsNotNull(client);
        }
    }
}
