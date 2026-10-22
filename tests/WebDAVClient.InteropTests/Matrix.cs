using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebDAVClient.InteropTests;

internal sealed record Observation(string Operation, string Outcome, int? HttpStatus = null, int[]? MemberStatuses = null,
    int[]? PropertyStatuses = null, string? Detail = null);

internal sealed class Matrix
{
    public int SchemaVersion => 1;

    public string SuiteVersion => "1.1";

    public DateTimeOffset StartedUtc
    {
        get;
    } = DateTimeOffset.UtcNow;

    public string Runtime
    {
        get;
    } = RuntimeInformation.FrameworkDescription;

    public string OperatingSystem
    {
        get;
    } = RuntimeInformation.OSDescription;

    public string Architecture
    {
        get;
    } = RuntimeInformation.ProcessArchitecture.ToString();

    public required string Server
    {
        get;
        init;
    }

    public required string ServerVersion
    {
        get;
        init;
    }

    public string ServerVersionSource => "fixture-pin-or-operator-label";

    public required string Authentication
    {
        get;
        init;
    }

    public required string Transport
    {
        get;
        init;
    }

    public bool Loopback
    {
        get;
        init;
    }

    public string? SourceRevision
    {
        get;
        init;
    }

    public string SourceRevisionScope => "repository revision at execution; result-only commit may follow";

    public string RunTree
    {
        get;
        init;
    } = "webdavclient-interop-" + Guid.NewGuid().ToString("N");

    public List<string> AdvertisedDav
    {
        get;
    } = [];

    public List<string> AdvertisedMethods
    {
        get;
    } = [];

    public List<string> AdvertisedReports
    {
        get;
    } = [];

    public List<Observation> Observations
    {
        get;
    } = [];

    internal static IEnumerable<string> RequiredOperations =>
        ["isolation-preflight", "create-tree", "unicode-put", "unicode-get", "head-metadata", "conditional-put",
            "conditional-integrity", "conditional-create", "conditional-etag-create", "conditional-if-match",
            "conditional-if-match-integrity", "conditional-stale-put", "conditional-stale-integrity",
            "copy", "copy-no-overwrite", "move", "delete-file", "cleanup-tree"];

    public bool Succeeded => RequiredOperations.All(name => Observations.Any(o => o.Operation == name && o.Outcome == "passed")) &&
        Observations.All(o => o.Outcome is "passed" or "unsupported" or "unavailable-property");

    internal static string Classify(int status, bool optional, bool advertised)
    {
        if (status is 401 or 403 or 407)
        {
            return "denied";
        }
        if (status is >= 300 and < 400)
        {
            return "redirected";
        }
        if (optional && !advertised && status is 405 or 501)
        {
            return "unsupported";
        }
        return "failed";
    }

    internal async Task SaveAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }) + Environment.NewLine).ConfigureAwait(false);
    }
}
