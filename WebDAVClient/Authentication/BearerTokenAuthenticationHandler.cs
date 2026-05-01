using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace WebDAVClient.Authentication
{
    /// <summary>
    /// <see cref="DelegatingHandler"/> that injects a <c>Bearer</c> token into the
    /// <c>Authorization</c> header of every outbound request, fetching the token
    /// from a caller-supplied async provider so callers can implement token
    /// refresh / rotation transparently (OAuth 2.0, OIDC, Azure AD, JWT, …).
    /// </summary>
    /// <remarks>
    /// The provider is invoked once per request with the request's
    /// <see cref="CancellationToken"/>. If the provider returns <c>null</c> or
    /// an empty string the <c>Authorization</c> header is omitted, allowing
    /// "anonymous request when no token is currently available" semantics
    /// without throwing.
    /// </remarks>
    public sealed class BearerTokenAuthenticationHandler : DelegatingHandler
    {
        private readonly Func<CancellationToken, Task<string>> m_tokenProvider;

        /// <summary>
        /// Creates a handler that asks <paramref name="tokenProvider"/> for the
        /// current bearer token before each outbound request.
        /// </summary>
        /// <param name="tokenProvider">Async token provider. Required.</param>
        public BearerTokenAuthenticationHandler(Func<CancellationToken, Task<string>> tokenProvider)
        {
            m_tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        }

        /// <summary>
        /// Convenience constructor for the static-token case (no refresh).
        /// </summary>
        /// <param name="bearerToken">The bearer token to send on every request. Required.</param>
        public BearerTokenAuthenticationHandler(string bearerToken)
        {
            if (string.IsNullOrWhiteSpace(bearerToken))
                throw new ArgumentException("Bearer token must be a non-empty string.", nameof(bearerToken));
            // Capture the token in a closure so the per-request hot path is the
            // same as the dynamic-provider case — no branching in SendAsync.
            var captured = bearerToken;
            m_tokenProvider = _ => Task.FromResult(captured);
        }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var token = await m_tokenProvider(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
