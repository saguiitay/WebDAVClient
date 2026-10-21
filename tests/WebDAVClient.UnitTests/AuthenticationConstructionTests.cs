using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class AuthenticationConstructionTests
    {
        [TestMethod]
        public void Named_legacy_null_calls_remain_source_compatible()
        {
            var timeout = TimeSpan.FromSeconds(30);
            var proxy = new WebProxy("http://localhost:8080");
            using var parameterless = new Client();
            using var namedNull = new Client(credential: null);
            using var withTimeout = new Client(credential: null, uploadTimeout: timeout);
            using var withProxy = new Client(credential: null, uploadTimeout: timeout, proxy: proxy);
            Assert.IsTrue(parameterless.OwnedHandler.UseDefaultCredentials);
            Assert.IsTrue(namedNull.OwnedHandler.UseDefaultCredentials);
            Assert.IsTrue(withTimeout.OwnedHandler.UseDefaultCredentials);
            Assert.AreSame(proxy, withProxy.OwnedHandler.Proxy);
        }

        [TestMethod]
        public void Explicit_authentication_factories_are_unambiguous_and_retain_authentication_policy()
        {
            var credential = new NetworkCredential("example", "not-a-secret");
            using var explicitCredentials = Client.CreateWithCredentials(credential);
            using var defaultCredentials = Client.CreateWithCredentials(null);
            using var bearer = Client.CreateWithBearerToken("example-token");
            var calls = 0;
            using var provider = Client.CreateWithBearerTokenProvider(_ =>
            {
                calls++;
                return Task.FromResult("example-token");
            });
            Assert.AreSame(credential, explicitCredentials.OwnedHandler.Credentials);
            Assert.IsTrue(defaultCredentials.OwnedHandler.UseDefaultCredentials);
            Assert.IsFalse(bearer.OwnedHandler.UseDefaultCredentials);
            Assert.IsFalse(provider.OwnedHandler.UseDefaultCredentials);
            Assert.AreEqual(0, calls);
            Assert.ThrowsException<ArgumentException>(() => Client.CreateWithBearerToken(null));
            Assert.ThrowsException<ArgumentNullException>(() => Client.CreateWithBearerTokenProvider(null));
        }
    }
}
