namespace WebDAVClient.InteropTests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--help" }))
        {
            Console.WriteLine("Opt-in independent WebDAV suite: set WEBDAV_INTEROP_RUN=1, ENDPOINT, SERVER and VERSION (all prefixed WEBDAV_INTEROP_), then --run. See interoperability/README.md. No server requests are made by dotnet test.");
            return 0;
        }
        if (!args.SequenceEqual(new[] { "--run" }))
        {
            Console.Error.WriteLine("Use --help or explicitly --run. No requests sent.");
            return 2;
        }
        try
        {
            var config = Configuration.Read(Environment.GetEnvironmentVariable);
            var matrix = await new CompatibilitySuite(config).RunAsync().ConfigureAwait(false);
            await matrix.SaveAsync(config.Output).ConfigureAwait(false);
            Console.WriteLine($"{matrix.Server} {matrix.ServerVersion}: {matrix.Observations.Count(o => o.Outcome == "passed")} passed, " +
                $"{matrix.Observations.Count(o => o.Outcome is "unsupported" or "unavailable-property")} unsupported/unavailable, " +
                $"{matrix.Observations.Count(o => o.Outcome is not ("passed" or "unsupported" or "unavailable-property"))} nonpassing. Matrix: {config.Output}");
            return matrix.Succeeded ? 0 : 1;
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Invalid or unsafe configuration. See --help and interoperability/README.md. No credentials or endpoint are logged.");
            return 2;
        }
    }
}
