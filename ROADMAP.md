# WebDAV Client Roadmap

This document outlines the recommended improvements and missing features for the WebDAV Client library to enhance its functionality, performance, and usability.

## 🎯 Major Missing Features

### 1. Advanced Authentication Support
**Priority: High**
**Target: v3.0.0**

> **Status update (2.7.0)**: Bearer token / OAuth 2.0 authentication has shipped via two new `Client` constructor overloads (static token + async refreshable provider) backed by the public `WebDAVClient.Authentication.BearerTokenAuthenticationHandler`. The remaining items below — `TokenCredential` / `ApiKeyCredential` integration, `AuthenticationOptions`, and the fluent `ClientBuilder` — are still planned for v3.0.0.

The current implementation only supports basic authentication and Windows authentication. We need to add support for modern authentication methods using established .NET patterns:

#### Modern Authentication Patterns

Instead of custom authentication enums, leverage standard .NET authentication abstractions:

```csharp
// Enhanced Client constructor overloads for modern authentication

// Existing constructor - maintain backward compatibility
public Client(ICredentials credential = null, TimeSpan? uploadTimeout = null, IWebProxy proxy = null)
{
    // Keep existing basic/Windows authentication support
}

// Token-based authentication (OAuth2, Azure AD, JWT)
public Client(TokenCredential tokenCredential, TimeSpan? uploadTimeout = null, IWebProxy proxy = null)
{
    // For Azure AD, OAuth2, JWT tokens using System.ClientModel.Primitives.TokenCredential
    // Integrates seamlessly with Azure Identity libraries
}

// API Key authentication
public Client(ApiKeyCredential apiKeyCredential, string headerName = "X-API-Key", TimeSpan? uploadTimeout = null, IWebProxy proxy = null)
{
    // For API key authentication using System.ClientModel.Primitives.ApiKeyCredential
}

// Custom authentication handler for maximum flexibility
public Client(Func<HttpRequestMessage, CancellationToken, ValueTask> authenticationHandler, TimeSpan? uploadTimeout = null, IWebProxy proxy = null)
{
    // For digest authentication, custom schemes, or complex auth flows
    // Handler is called before each request to add authentication
}

// Fluent builder pattern integration
public Client(AuthenticationOptions authOptions, TimeSpan? uploadTimeout = null, IWebProxy proxy = null)
{
    // Used with fluent builder for complex authentication scenarios
}
```

#### New Authentication Support Classes

```csharp
// Authentication options for complex scenarios
public class AuthenticationOptions
{
    public TokenCredential? TokenCredential { get; set; }
    public ApiKeyCredential? ApiKeyCredential { get; set; }
    public string? ApiKeyHeaderName { get; set; } = "X-API-Key";
    public ICredentials? Credentials { get; set; }
    public Func<HttpRequestMessage, CancellationToken, ValueTask>? CustomHandler { get; set; }
    public Dictionary<string, string>? StaticHeaders { get; set; }
}

// Bearer token helper for simple scenarios
public static class BearerToken
{
    public static TokenCredential FromString(string token) => new StaticTokenCredential(token);
    public static TokenCredential FromProvider(Func<CancellationToken, ValueTask<AccessToken>> provider) 
        => new DelegatingTokenCredential(provider);
}
```

#### Standard Authentication Types Supported

- **`System.ClientModel.Primitives.TokenCredential`**
  - OAuth2 tokens
  - Azure AD authentication
  - JWT tokens
  - Custom token providers
  
- **`System.ClientModel.Primitives.ApiKeyCredential`**
  - API key in headers
  - API key in query parameters
  - Custom API key placement

- **`System.Net.ICredentials`** (existing)
  - Basic authentication
  - Windows authentication
  - Network credentials

- **Custom Authentication Handlers**
  - Digest authentication
  - Multi-factor authentication
  - Complex custom schemes
  - Dynamic token refresh

#### Integration Examples

```csharp
// Azure AD integration
var tokenCredential = new DefaultAzureCredential();
var client = new Client(tokenCredential);

// API Key authentication
var apiKey = new ApiKeyCredential("your-api-key");
var client = new Client(apiKey, "Authorization"); // Custom header name

// JWT Bearer token
var bearerToken = BearerToken.FromString("eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9...");
var client = new Client(bearerToken);

// Custom digest authentication
var digestHandler = async (request, cancellationToken) =>
{
    // Implement digest authentication logic
    var authHeader = await CreateDigestAuthHeader(request, cancellationToken);
    request.Headers.Authorization = new AuthenticationHeaderValue("Digest", authHeader);
};
var client = new Client(digestHandler);

// Fluent builder pattern
var client = ClientBuilder.Create()
    .WithServer("https://dav.example.com")
    .WithTokenCredential(new DefaultAzureCredential())
    .WithRetryPolicy(3, TimeSpan.FromSeconds(2))
    .Build();
```

#### Benefits of This Approach

- **Standard .NET Patterns**: Uses established authentication abstractions
- **Azure Integration**: Seamless integration with Azure Identity libraries
- **OAuth2/JWT Support**: Built-in support for modern token-based authentication
- **Extensibility**: Custom handlers allow any authentication scheme
- **Backward Compatibility**: Existing `ICredentials` constructor remains unchanged
- **Dependency Injection Friendly**: Works well with DI containers
- **Testability**: Easy to mock and test authentication flows

#### Package Dependencies

```xml
<!-- Required for modern authentication support -->
<PackageReference Include="System.ClientModel.Primitives" Version="1.0.0" />

<!-- Optional - for Azure AD integration -->
<PackageReference Include="Azure.Identity" Version="1.10.4" />
```

### 2. WebDAV Lock/Unlock Operations
**Priority: High**
**Target: v3.0.0**

> **Status update (2.7.0)**: Shipped. `IClient` now exposes `LockFile` / `LockFolder` / `UnlockFile` / `UnlockFolder` / `RefreshLock` returning a strongly-typed `LockInfo`, and the `If` lock-token header is sent on PUT / DELETE / MOVE / COPY when callers pass the new optional `lockToken` (and source / destination variants) parameters.

Critical WebDAV operations that are currently missing:

#### New Methods to Implement
```csharp
// Lock operations
Task<string> LockFile(string filePath, int timeoutSeconds = 600, CancellationToken cancellationToken = default);
Task<string> LockFolder(string folderPath, int timeoutSeconds = 600, CancellationToken cancellationToken = default);
Task UnlockFile(string filePath, string lockToken, CancellationToken cancellationToken = default);
Task UnlockFolder(string folderPath, string lockToken, CancellationToken cancellationToken = default);
Task<bool> RefreshLock(string path, string lockToken, int timeoutSeconds = 600, CancellationToken cancellationToken = default);
Task<LockInfo> GetLockInfo(string path, CancellationToken cancellationToken = default);
```

#### New Model Classes
```csharp
public class LockInfo
{
    public string Token { get; set; }
    public string Owner { get; set; }
    public DateTime ExpirationDate { get; set; }
    public string LockType { get; set; }
    public string LockScope { get; set; }
}
```

### 3. WebDAV Properties Management (PROPPATCH)
**Priority: Medium**
**Target: v3.1.0**

> **Status update (2.7.0)**: Shipped. `IClient.SetProperty(path, name, namespace, value)` and `IClient.RemoveProperty(path, name, namespace)` are available; targeted PROPFIND via `<prop>` / `<propname/>` also shipped (see the new `List` / `GetFolder` / `GetFile` overloads taking `IEnumerable<PropertyName>` and the `*PropertyNames` methods).

The client can read properties but cannot set or manage custom properties:

#### New Methods to Implement
```csharp
Task<bool> SetProperty(string path, string propertyName, string propertyValue, string nameSpace = "DAV:", CancellationToken cancellationToken = default);
Task<Dictionary<string, string>> GetCustomProperties(string path, string[] propertyNames = null, CancellationToken cancellationToken = default);
Task<bool> RemoveProperty(string path, string propertyName, string nameSpace = "DAV:", CancellationToken cancellationToken = default);
Task<bool> SetMultipleProperties(string path, Dictionary<string, object> properties, string nameSpace = "DAV:", CancellationToken cancellationToken = default);
```

### 4. WebDAV Search Support (DASL)
**Priority: Low**
**Target: v3.2.0**

Add support for WebDAV search capabilities:

```csharp
Task<IEnumerable<Item>> Search(string basePath, string query, SearchScope scope = SearchScope.Subtree, CancellationToken cancellationToken = default);
Task<IEnumerable<Item>> SearchByProperty(string basePath, string propertyName, string propertyValue, CancellationToken cancellationToken = default);
```

## 🚀 Performance and Reliability Improvements

### 5. Retry Mechanism with Exponential Backoff
**Priority: High**
**Target: v2.3.0**

Add built-in retry logic for transient failures:

#### New Configuration Properties
```csharp
public int MaxRetryAttempts { get; set; } = 3;
public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
public bool EnableExponentialBackoff { get; set; } = true;
public Func<Exception, bool> RetryPredicate { get; set; } = DefaultRetryPredicate;
```

#### Implementation Details
- Configurable retry attempts
- Exponential backoff strategy
- Custom retry predicates
- Circuit breaker pattern for persistent failures

### 6. Progress Reporting for Large Operations
**Priority: Medium**
**Target: v2.4.0**

Add progress callbacks for long-running operations:

#### Enhanced Method Signatures
```csharp
Task<bool> Upload(string remoteFilePath, Stream content, string name, IProgress<ProgressInfo> progress = null, CancellationToken cancellationToken = default);
Task<Stream> Download(string remoteFilePath, IProgress<ProgressInfo> progress = null, CancellationToken cancellationToken = default);
```

#### New Model Classes
```csharp
public class ProgressInfo
{
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public double PercentageComplete => TotalBytes > 0 ? (double)BytesTransferred / TotalBytes * 100 : 0;
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan EstimatedTimeRemaining { get; set; }
    public long TransferRate { get; set; } // bytes per second
}
```

### 7. Connection Pooling and Keep-Alive Optimization
**Priority: Medium**
**Target: v2.3.0**

Optimize HTTP connection management:

- Improve HttpClient configuration for connection reuse
- Add connection pool size configuration
- Implement proper keep-alive settings
- Add connection health monitoring

### 8. Memory Usage Optimization
**Priority: Medium**
**Target: v2.4.0**

Reduce memory allocations and improve garbage collection:

- Stream-based XML parsing to reduce memory footprint
- Object pooling for frequently created objects
- Lazy loading of properties
- Implement `IAsyncDisposable` where appropriate

## 🔍 Error Handling and Observability

### 9. Structured Logging Support
**Priority: High**
**Target: v2.3.0**

Add comprehensive logging infrastructure:

#### Integration with Microsoft.Extensions.Logging
```csharp
public Client(ICredentials credential = null, TimeSpan? uploadTimeout = null, 
    IWebProxy proxy = null, ILogger<Client> logger = null)
```

#### Logging Categories
- Request/Response details
- Authentication events
- Error conditions
- Performance metrics
- Connection events

### 10. Enhanced Exception Information
**Priority: High**
**Target: v2.3.0**

Improve exception context and debugging information:

#### Enhanced WebDAVException
```csharp
public class WebDAVException : Exception
{
    public string OperationType { get; set; }
    public string RequestUri { get; set; }
    public string RequestMethod { get; set; }
    public Dictionary<string, string> RequestHeaders { get; set; }
    public string ResponseContent { get; set; }
    public TimeSpan RequestDuration { get; set; }
    public string ServerVersion { get; set; }
}
```

### 11. Health Checking and Diagnostics
**Priority: Medium**
**Target: v3.0.0**

> **Status update (2.7.0)**: `GetServerCapabilities` is partially covered by the new `IClient.GetServerOptions(path, ct)` returning a strongly-typed `ServerOptions` (DAV compliance classes + allowed methods). The full `CheckHealth` / `GetConnectionInfo` surface below remains planned.

Add server connectivity and health monitoring:

```csharp
Task<HealthCheckResult> CheckHealth(CancellationToken cancellationToken = default);
Task<ServerCapabilities> GetServerCapabilities(CancellationToken cancellationToken = default);
Task<ConnectionInfo> GetConnectionInfo(CancellationToken cancellationToken = default);
```

## 🎨 API Usability Improvements

### 12. Fluent Configuration API
**Priority: Medium**
**Target: v3.0.0**

Create a more intuitive configuration experience that integrates with modern authentication patterns:

```csharp
public static class ClientBuilder
{
    public static ClientConfiguration Create() => new ClientConfiguration();
}

public class ClientConfiguration
{
    public ClientConfiguration WithServer(string server);
    public ClientConfiguration WithBasePath(string basePath);
    public ClientConfiguration WithPort(int port);
    public ClientConfiguration WithTimeout(TimeSpan timeout);
    public ClientConfiguration WithUploadTimeout(TimeSpan uploadTimeout);
    public ClientConfiguration WithProxy(IWebProxy proxy);
    
    // Modern authentication methods
    public ClientConfiguration WithCredentials(ICredentials credentials);
    public ClientConfiguration WithTokenCredential(TokenCredential tokenCredential);
    public ClientConfiguration WithApiKey(ApiKeyCredential apiKeyCredential, string headerName = "X-API-Key");
    public ClientConfiguration WithBearerToken(string token);
    public ClientConfiguration WithCustomAuthentication(Func<HttpRequestMessage, CancellationToken, ValueTask> handler);
    
    // Configuration options
    public ClientConfiguration WithRetryPolicy(int maxAttempts, TimeSpan delay, bool exponentialBackoff = true);
    public ClientConfiguration WithCustomHeaders(Dictionary<string, string> headers);
    public ClientConfiguration WithUserAgent(string userAgent, string? version = null);
    public ClientConfiguration WithLogging(ILogger logger);
    public ClientConfiguration WithCertificateValidation(RemoteCertificateValidationCallback callback);
    
    // Build the client
    public Client Build();
}

// Extension methods for common scenarios
public static class ClientConfigurationExtensions
{
    public static ClientConfiguration WithBasicAuth(this ClientConfiguration config, string username, string password)
        => config.WithCredentials(new NetworkCredential(username, password));
        
    public static ClientConfiguration WithAzureAD(this ClientConfiguration config)
        => config.WithTokenCredential(new DefaultAzureCredential());
        
    public static ClientConfiguration WithJwtToken(this ClientConfiguration config, string jwtToken)
        => config.WithBearerToken(jwtToken);
}
```

#### Usage Examples

```csharp
// Basic authentication
var client = ClientBuilder.Create()
    .WithServer("https://dav.example.com")
    .WithBasePath("/dav/")
    .WithBasicAuth("username", "password")
    .WithRetryPolicy(3, TimeSpan.FromSeconds(2))
    .Build();

// Azure AD authentication
var client = ClientBuilder.Create()
    .WithServer("https://sharepoint.example.com")
    .WithAzureAD()
    .WithLogging(logger)
    .Build();

// API Key authentication
var client = ClientBuilder.Create()
    .WithServer("https://api.example.com")
    .WithApiKey(new ApiKeyCredential("your-key"), "X-API-Key")
    .WithTimeout(TimeSpan.FromMinutes(5))
    .Build();

// JWT Bearer token
var client = ClientBuilder.Create()
    .WithServer("https://dav.example.com")
    .WithJwtToken("eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9...")
    .WithCustomHeaders(new Dictionary<string, string> { ["X-Client-Version"] = "3.0" })
    .Build();

// Custom digest authentication
var client = ClientBuilder.Create()
    .WithServer("https://dav.example.com")
    .WithCustomAuthentication(async (request, ct) =>
    {
        var digestAuth = await CreateDigestAuth(request, ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Digest", digestAuth);
    })
    .Build();

// Complex configuration
var client = ClientBuilder.Create()
    .WithServer("https://enterprise-dav.example.com")
    .WithBasePath("/webdav/")
    .WithPort(8443)
    .WithTokenCredential(tokenCredential)
    .WithRetryPolicy(maxAttempts: 5, delay: TimeSpan.FromSeconds(1), exponentialBackoff: true)
    .WithTimeout(TimeSpan.FromMinutes(10))
    .WithUploadTimeout(TimeSpan.FromHours(1))
    .WithUserAgent("MyApp", "2.1.0")
    .WithLogging(logger)
    .WithCertificateValidation((sender, cert, chain, errors) => true) // Accept all certs
    .Build();
```

#### Async Configuration Support

For scenarios requiring async initialization:

```csharp
public static class AsyncClientBuilder
{
    public static async Task<Client> CreateAsync(Func<ClientConfiguration, Task<ClientConfiguration>> configure)
    {
        var config = ClientBuilder.Create();
        config = await configure(config);
        return config.Build();
    }
}

// Usage with async token acquisition
var client = await AsyncClientBuilder.CreateAsync(async config =>
    config.WithServer("https://dav.example.com")
          .WithTokenCredential(await GetTokenCredentialAsync())
          .WithRetryPolicy(3, TimeSpan.FromSeconds(2))
);
```

## 📅 Release Timeline

### Version 2.3.0 (Q1 2025)
- Retry mechanism with exponential backoff
- Structured logging support (Microsoft.Extensions.Logging)
- Enhanced exception information with better context
- Improved documentation and examples

### Version 2.4.0 (Q2 2025)
- Progress reporting for large operations
- Memory usage optimization
- Connection pooling and keep-alive optimization
- IAsyncDisposable support

### Version 3.0.0 (Q3 2025) - Major Release
- **Advanced authentication support with modern .NET patterns**
  - TokenCredential integration (Azure AD, OAuth2, JWT)
  - ApiKeyCredential support
  - Custom authentication handlers
- WebDAV Lock/Unlock operations (LOCK/UNLOCK methods)
- Fluent configuration API with authentication integration
- Nullable reference types support (.NET 8/9)
- Breaking changes cleanup and API improvements

### Version 3.1.0 (Q4 2025)
- WebDAV properties management (PROPPATCH method)
- Batch operations support for improved performance
- Health checking and diagnostics capabilities
- Performance monitoring and metrics collection

### Version 3.2.0 (Q1 2026)
- WebDAV search support (DASL extension)
- Async enumerable support for large listings
- Source generator for strongly-typed WebDAV properties
- Advanced connection management features

## 🎯 Authentication Migration Guide

### For v3.0.0 Breaking Changes

The authentication system will be modernized but maintain backward compatibility:

#### Current Usage (v2.x - Still Supported)
```csharp
// Basic authentication - continues to work
var client = new Client(new NetworkCredential("user", "pass"));

// Windows authentication - continues to work  
var client = new Client();
```

#### New Usage (v3.0+)
```csharp
// Modern token-based authentication
var client = new Client(new DefaultAzureCredential());

// API key authentication
var client = new Client(new ApiKeyCredential("key"), "X-API-Key");

// Fluent configuration
var client = ClientBuilder.Create()
    .WithServer("https://dav.example.com")
    .WithTokenCredential(tokenCredential)
    .Build();
```

#### Migration Benefits
- **No Breaking Changes**: Existing constructors remain functional
- **Modern Authentication**: Support for OAuth2, Azure AD, JWT
- **Better Testing**: Easier to mock authentication in unit tests
- **Cloud Ready**: Seamless integration with cloud identity providers
- **Extensible**: Custom authentication handlers for any scenario