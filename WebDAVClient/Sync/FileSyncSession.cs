#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebDAVClient.Extensions;
using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient.Sync
{
    /// <summary>
    /// A local edit, missing validator, or unsupported report prevents safe synchronization.
    /// No automatic conflict resolution or token reset is performed.
    /// </summary>
    public sealed class FileSyncConflictException : IOException
    {
        /// <summary>
        /// Creates a conflict with a diagnostic description.
        /// </summary>
        public FileSyncConflictException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Durable, explicit pull synchronization of immediate files in one RFC 6578 collection.
    /// The caller must exclusively own both local directories, prevent concurrent local edits during
    /// each call, and disable transport redirects and automatic decompression. Subcollections, links and automatic bidirectional
    /// synchronization are deliberately unsupported. The client remains caller-owned.
    /// </summary>
    public sealed class FileSyncSession
    {
        private static readonly PropertyName s_etag = new PropertyName("DAV:", "getetag");

        private static readonly PropertyName s_resourceType = new PropertyName("DAV:", "resourcetype");

        private readonly DavClient m_client;

        private readonly Uri m_collection;

        private readonly string m_directory;

        private readonly string m_stateDirectory;

        private readonly SemaphoreSlim m_gate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Binds a client and collection to an existing data directory and a separate existing state
        /// directory on the same filesystem. Paths must be absolute, nonoverlapping and free of
        /// symbolic links/reparse points. The filesystem must support atomic file replacement.
        /// State contains sensitive names, validators and tokens, but never credentials.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// A path is unsafe or the collection is not a query-free collection URI.
        /// </exception>
        public FileSyncSession(DavClient client, DavPath collection, string directory, string stateDirectory)
        {
            ArgumentNullException.ThrowIfNull(client);
            m_client = client;
            m_collection = client.Resolve(collection);
            if (!m_collection.AbsolutePath.EndsWith("/", StringComparison.Ordinal) ||
                m_collection.Query.Length != 0 || m_collection.Fragment.Length != 0)
            {
                throw new ArgumentException("Use a collection URI ending in '/'.", nameof(collection));
            }
            m_directory = CheckDirectory(directory);
            m_stateDirectory = CheckDirectory(stateDirectory);
            if (Contains(m_directory, m_stateDirectory) || Contains(m_stateDirectory, m_directory))
            {
                throw new ArgumentException("Data and state directories must not overlap.");
            }
            if (!string.Equals(Path.GetPathRoot(m_directory), Path.GetPathRoot(m_stateDirectory), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Data and state must be on the same filesystem.");
            }
        }

        /// <summary>
        /// Recovers any durable pending batch, then fetches and applies one complete sync-level 1 batch.
        /// Returns its committed token. Missing files in an initial batch never imply deletion.
        /// All outcomes and paths are validated before downloads; every download is conditional on a
        /// strong ETag and requires an identity representation. Downloads are streamed to disk and
        /// flushed before publishing the recovery journal.
        /// A failed/canceled batch never advances the checkpoint ahead of applied content. After journal publication, cancellation
        /// can leave partially applied files; the next call replays idempotently before making requests.
        /// </summary>
        /// <exception cref="FileSyncConflictException">
        /// Local content diverged, validators are insufficient, or the report is incomplete/unsupported.
        /// Preserve the state directory and reconcile the conflict explicitly before retrying.
        /// </exception>
        public async Task<string> PullAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var lease = OpenLease();
                await RecoverAsync(cancellationToken).ConfigureAwait(false);
                var state = await ReadAsync<State>("checkpoint.json", cancellationToken).ConfigureAwait(false) ?? NewState();
                Validate(state);
                var batch = await m_client.SyncCollectionAsync(DavPath.Parse(m_collection.AbsoluteUri), state.Token,
                    new[] { s_etag, s_resourceType }, cancellationToken: cancellationToken).ConfigureAwait(false);
                batch.Response.EnsureHttpSuccess();
                if (batch.HasMore)
                {
                    throw Conflict("Truncated batches are not committed.");
                }
                var changes = Parse(batch, state);
                var next = NewState();
                next.Token = batch.SyncToken!;
                next.Entries = new Dictionary<string, Entry>(state.Entries, StringComparer.Ordinal);
                var journal = new Journal { State = next, Changes = changes };
                try
                {
                    foreach (var change in changes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (change.ETag != null)
                        {
                            change.Stage = Guid.NewGuid().ToString("N") + ".stage";
                            using var download = await m_client.DownloadAsync(RemotePath(change.Name),
                                new DownloadOptions
                                {
                                    IfMatch = change.ETag,
                                    RequireIdentityEncoding = true
                                }, cancellationToken).ConfigureAwait(false);
                            download.EnsureHttpSuccess();
                            if (download.StatusCode != 200 || download.GetHeader("ETag") != change.ETag)
                            {
                                throw Conflict("A full download with the reported strong ETag is required.");
                            }
                            using (var output = new FileStream(StatePath(change.Stage), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                            {
                                await download.Content!.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                                output.Flush(true);
                            }
                            change.Hash = await HashAsync(StatePath(change.Stage), cancellationToken).ConfigureAwait(false);
                            next.Entries[change.Name] = new Entry { ETag = change.ETag, Hash = change.Hash! };
                        }
                        else
                        {
                            next.Entries.Remove(change.Name);
                        }
                    }
                    foreach (var change in changes)
                    {
                        await CheckContentAsync(change, cancellationToken).ConfigureAwait(false);
                    }
                    await WriteAsync("pending.json", journal, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    if (!File.Exists(StatePath("pending.json")))
                    {
                        CleanStages(changes);
                    }
                    throw;
                }
                await RecoverAsync(cancellationToken).ConfigureAwait(false);
                return next.Token;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Reads a file's last committed strong ETag, or null for an untracked file. Completes pending
        /// local recovery first. This is a conditional-write validator, not proof of remote identity.
        /// </summary>
        public async Task<string?> GetSynchronizedETagAsync(string name, CancellationToken cancellationToken = default)
        {
            ValidateName(name);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var lease = OpenLease();
                await RecoverAsync(cancellationToken).ConfigureAwait(false);
                var state = await ReadAsync<State>("checkpoint.json", cancellationToken).ConfigureAwait(false) ?? NewState();
                Validate(state);
                return state.Entries.GetValueOrDefault(name)?.ETag;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Explicitly uploads caller-owned content. A null expected ETag means create-only
        /// (If-None-Match: *); replacement requires a strong ETag from the last synchronized revision.
        /// Rejections are returned as results, including 412 conflicts. This never advances local state.
        /// On an ambiguous transport failure, reconcile before retrying and reopen content.
        /// </summary>
        public Task<DavMultiStatusResult> UploadAsync(string name, Stream content, string? expectedETag = null,
            CancellationToken cancellationToken = default)
        {
            ValidateName(name);
            if (expectedETag != null)
            {
                ValidateETag(expectedETag);
            }
            return m_client.UploadAsync(RemotePath(name), content, new UploadOptions
            {
                IfMatch = expectedETag,
                IfNoneMatch = expectedETag == null ? "*" : null
            }, cancellationToken);
        }

        /// <summary>
        /// Explicitly deletes one remote file conditionally on its last synchronized strong ETag.
        /// Never infers a remote deletion from local absence. Rejections, including 412, remain results.
        /// The next pull reconciles the deletion; this method never changes the checkpoint.
        /// </summary>
        public Task<DavMultiStatusResult> DeleteRemoteAsync(string name, string expectedETag,
            CancellationToken cancellationToken = default)
        {
            ValidateName(name);
            ValidateETag(expectedETag);
            return m_client.DeleteAsync(RemotePath(name), new DavRequestOptions { IfMatch = expectedETag }, cancellationToken);
        }

        private List<Change> Parse(DavSyncResult batch, State state)
        {
            var changes = new List<Change>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var resource in batch.Response.Resources)
            {
                if (resource.Hrefs.Count != 1 || resource.Errors.Count != 0 ||
                    resource.PropertyStatuses.Any(p => p.StatusCode != 200 || p.Errors.Count != 0))
                {
                    throw Conflict("Ambiguous or denied resource outcomes.");
                }
                var reference = resource.Hrefs[0];
                DavPath.Parse(reference);
                // Check before URI normalization removes dot segments.
                if (reference.Split('/').Any(s => Uri.UnescapeDataString(s) is "." or ".."))
                {
                    throw Conflict("Dot segments in a report href.");
                }
                var uri = m_client.Resolve(DavPath.Parse(new Uri(m_collection, reference).AbsoluteUri));
                if (uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                    !uri.AbsoluteUri.StartsWith(m_collection.AbsoluteUri, StringComparison.Ordinal))
                {
                    throw Conflict("A report href escaped its collection.");
                }
                if (uri == m_collection)
                {
                    if (resource.StatusCode.HasValue || resource.PropertyStatuses.Count == 0)
                    {
                        throw Conflict("The collection did not report successful properties.");
                    }
                    continue;
                }
                var name = Uri.UnescapeDataString(uri.AbsoluteUri.Substring(m_collection.AbsoluteUri.Length));
                ValidateName(name);
                if (!seen.Add(name) || state.Entries.Keys.Any(k => k != name && string.Equals(k, name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw Conflict("Duplicate or case-aliased file names.");
                }
                var change = new Change { Name = name, OldHash = state.Entries.GetValueOrDefault(name)?.Hash };
                if (resource.StatusCode == 404 && resource.PropertyStatuses.Count == 0)
                {
                    changes.Add(change);
                    continue;
                }
                if (resource.StatusCode.HasValue)
                {
                    throw Conflict("Unsuccessful resource outcome.");
                }
                var etags = resource.GetProperties(s_etag).ToArray();
                var types = resource.GetProperties(s_resourceType).ToArray();
                if (etags.Length != 1 || types.Length != 1 || !etags[0].Value.TryGetText(out var etag) ||
                    types[0].Value.ToElement().Nodes().Any())
                {
                    throw Conflict("Only files with one strong ETag and empty resourcetype are supported.");
                }
                ValidateETag(etag!);
                change.ETag = etag;
                changes.Add(change);
            }
            return changes;
        }

        private async Task RecoverAsync(CancellationToken cancellationToken)
        {
            var journal = await ReadAsync<Journal>("pending.json", cancellationToken).ConfigureAwait(false);
            if (journal == null)
            {
                foreach (var stage in Directory.EnumerateFiles(m_stateDirectory, "*.stage"))
                {
                    if (IsStageName(Path.GetFileName(stage)))
                    {
                        File.Delete(StatePath(Path.GetFileName(stage)));
                    }
                }
                return;
            }
            Validate(journal.State);
            foreach (var change in journal.Changes)
            {
                ValidateName(change.Name);
                if (change.Stage != null && !IsStageName(change.Stage))
                {
                    throw Conflict("Invalid recovery staging name.");
                }
                await CheckContentAsync(change, cancellationToken).ConfigureAwait(false);
                var target = LocalPath(change.Name);
                var hash = await HashAsync(target, cancellationToken).ConfigureAwait(false);
                if (hash != change.Hash)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (change.Hash == null)
                    {
                        File.Delete(target);
                    }
                    else
                    {
                        var stage = StatePath(change.Stage!);
                        if (await HashAsync(stage, cancellationToken).ConfigureAwait(false) != change.Hash)
                        {
                            throw Conflict("Recovery content is missing or corrupt.");
                        }
                        File.Move(stage, target, true);
                    }
                }
            }
            await WriteAsync("checkpoint.json", journal.State, cancellationToken).ConfigureAwait(false);
            File.Delete(StatePath("pending.json"));
            CleanStages(journal.Changes);
        }

        private async Task CheckContentAsync(Change change, CancellationToken cancellationToken)
        {
            var hash = await HashAsync(LocalPath(change.Name), cancellationToken).ConfigureAwait(false);
            if (hash != change.OldHash && hash != change.Hash)
            {
                throw Conflict("Local content changed: " + change.Name);
            }
        }

        private State NewState() => new State { Identity = m_collection.AbsoluteUri + "\n" + m_directory };

        private void Validate(State state)
        {
            if (state.Version != 1 || state.Identity != NewState().Identity || state.Token != null &&
                (!Uri.TryCreate(state.Token, UriKind.Absolute, out _) || state.Token.Any(char.IsWhiteSpace)))
            {
                throw Conflict("Unsupported or mismatched checkpoint. Do not reuse state across roots.");
            }
            foreach (var name in state.Entries.Keys)
            {
                ValidateName(name);
            }
        }

        private FileStream OpenLease()
        {
            CheckDirectory(m_directory);
            CheckDirectory(m_stateDirectory);
            return new FileStream(StatePath("session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private static string CheckDirectory(string path)
        {
            if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
            {
                throw new ArgumentException("Use an existing absolute directory.", nameof(path));
            }
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            for (var directory = new DirectoryInfo(full); directory != null; directory = directory.Parent)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ArgumentException("Symbolic links and reparse points are unsupported.", nameof(path));
                }
            }
            return full;
        }

        private static bool Contains(string parent, string child) =>
            string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private static bool IsStageName(string name) => name.Length == 38 &&
            Guid.TryParseExact(name.Substring(0, 32), "N", out _) && name.EndsWith(".stage", StringComparison.Ordinal);

        private static void ValidateName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (name.Length == 0 || name is "." or ".." || name.EndsWith(".", StringComparison.Ordinal) ||
                name.EndsWith(" ", StringComparison.Ordinal) || name.Any(c => c < 32 || "/\\:<>\"|?*".Contains(c)) ||
                stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                 "123456789¹²³".Contains(stem[3])))
            {
                throw Conflict("Unsupported portable file name.");
            }
        }

        private static void ValidateETag(string etag)
        {
            if (!EntityTagHeaderValue.TryParse(etag, out var parsed) || parsed.IsWeak || parsed.Tag == "*")
            {
                throw Conflict("A strong ETag is required.");
            }
        }

        private DavPath RemotePath(string name) => DavPath.Parse(m_collection.AbsoluteUri + Uri.EscapeDataString(name));

        private string LocalPath(string name)
        {
            var path = Path.Combine(m_directory, name);
            CheckFile(path);
            return path;
        }

        private string StatePath(string name)
        {
            var path = Path.Combine(m_stateDirectory, name);
            CheckFile(path);
            return path;
        }

        private static void CheckFile(string path)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                return;
            }
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            {
                throw Conflict("Directories and symbolic links cannot be synchronized as files.");
            }
        }

        private static async Task<string?> HashAsync(string path, CancellationToken cancellationToken)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }

        private async Task<T?> ReadAsync<T>(string name, CancellationToken cancellationToken)
        {
            var path = StatePath(name);
            if (!File.Exists(path))
            {
                return default;
            }
            using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw Conflict("Empty synchronization state.");
        }

        private async Task WriteAsync<T>(string name, T value, CancellationToken cancellationToken)
        {
            var staging = StatePath("writing.json");
            using (var stream = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, StatePath(name), true);
        }

        private void CleanStages(IEnumerable<Change> changes)
        {
            foreach (var change in changes)
            {
                if (change.Stage != null)
                {
                    File.Delete(StatePath(change.Stage));
                }
            }
        }

        private static FileSyncConflictException Conflict(string message) => new FileSyncConflictException(message);

        private sealed class State
        {
            public State()
            {
            }

            public int Version
            {
                get;
                set;
            } = 1;

            public string Identity
            {
                get;
                set;
            } = "";

            public string? Token
            {
                get;
                set;
            }

            public Dictionary<string, Entry> Entries
            {
                get;
                set;
            } = new Dictionary<string, Entry>(StringComparer.Ordinal);
        }

        private sealed class Entry
        {
            public Entry()
            {
            }

            public string ETag
            {
                get;
                set;
            } = "";

            public string Hash
            {
                get;
                set;
            } = "";
        }

        private sealed class Change
        {
            public Change()
            {
            }

            public string Name
            {
                get;
                set;
            } = "";

            public string? OldHash
            {
                get;
                set;
            }

            public string? Hash
            {
                get;
                set;
            }

            public string? ETag
            {
                get;
                set;
            }

            public string? Stage
            {
                get;
                set;
            }
        }

        private sealed class Journal
        {
            public Journal()
            {
            }

            public State State
            {
                get;
                set;
            } = new State();

            public List<Change> Changes
            {
                get;
                set;
            } = new List<Change>();
        }
    }
}
