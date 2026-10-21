#nullable enable
using System;
using System.IO;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WebDAVClient.Transfers
{
    /// <summary>
    /// Bounded-memory, restartable identity-representation downloads to new local files.
    /// </summary>
    public static class DavFileTransfers
    {
        private const int c_bufferSize = 64 * 1024;

        private const int c_checkpointInterval = 1024 * 1024;

        private const int c_maxCheckpointSize = 16 * 1024;

        /// <summary>
        /// Downloads into destination + ".davpart", checkpointing durable prefixes every MiB,
        /// and atomically renames the validated file to a previously nonexistent destination.
        /// Call again with the same arguments after interruption to resume.
        /// </summary>
        /// <param name="client">
        /// Caller-owned client. Injected transports must disable decompression and redirects.
        /// </param>
        /// <param name="path">
        /// Remote file reference; its resolved URI is bound to the checkpoint.
        /// </param>
        /// <param name="destination">
        /// New destination file. Its parent directory must exist. Reserve the associated
        /// .davpart, .davcheckpoint, .davcheckpoint.new and .davlock names for this workflow.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels network and disk reads/writes. Durable checkpoints remain usable.
        /// </param>
        /// <returns>
        /// A task completed only after validated EOF, durable flush and atomic publication.
        /// </returns>
        /// <exception cref="InvalidDataException">
        /// The checkpoint, staged prefix, strong ETag, representation length or range is invalid.
        /// No automatic restart or representation replacement is attempted.
        /// </exception>
        /// <exception cref="IOException">
        /// Local state conflicts, concurrent use, filesystem failure or interrupted transfer.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Cancellation was requested before publication.
        /// </exception>
        /// <remarks>
        /// Requires a known Content-Length and a strong ETag, including for empty files.
        /// HTTP rejections and malformed ranges use the existing DavClient exception contracts.
        /// Local SHA-256 detects accidental checkpoint/prefix corruption, not a malicious writer.
        /// The lock sentinel is intentionally retained; do not delete it while a transfer may run.
        /// Never retries requests. Existing destination files are never overwritten.
        /// </remarks>
        public static async Task DownloadFileAsync(this DavClient client, DavPath path, string destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentException.ThrowIfNullOrEmpty(destination);
            var uri = client.Resolve(path).AbsoluteUri;
            destination = Path.GetFullPath(destination);
            var partial = destination + ".davpart";
            var checkpoint = destination + ".davcheckpoint";
            cancellationToken.ThrowIfCancellationRequested();
            using var gate = new FileStream(destination + ".davlock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(destination))
            {
                throw new IOException("Destination already exists. It is never replaced; reconcile any completed transfer before cleanup.");
            }
            var state = File.Exists(checkpoint) ? await ReadCheckpointAsync(checkpoint, uri, cancellationToken).ConfigureAwait(false) : null;
            if (File.Exists(partial) != (state != null))
            {
                throw new InvalidDataException("Staged data and checkpoint must both exist. Reconcile or remove both before restarting.");
            }
            using var file = new FileStream(partial, state == null ? FileMode.CreateNew : FileMode.Open,
                FileAccess.ReadWrite, FileShare.None, c_bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            bool resuming = state != null;
            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[c_bufferSize];
                if (state != null)
                {
                    await VerifyPrefixAsync(file, state, hash, buffer, cancellationToken).ConfigureAwait(false);
                    file.SetLength(state.Offset);
                }
                if (state?.Complete != true)
                {
                    var offset = state?.Offset ?? 0;
                    var options = new DownloadOptions
                    {
                        RequireIdentityEncoding = true,
                        IfMatch = state?.ETag,
                        Range = offset == 0 ? null : new RangeHeaderValue(offset, null),
                        IfRange = offset == 0 ? null : new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(state!.ETag))
                    };
                    using var response = await client.DownloadAsync(path, options, cancellationToken).ConfigureAwait(false);
                    response.EnsureHttpSuccess();
                    var tag = response.ETag;
                    if (!IsStrongTag(tag) || state != null && state.ETag != tag)
                    {
                        throw new InvalidDataException("A stable strong ETag is required. Reconcile before restarting a changed representation.");
                    }
                    var length = response.ContentLength;
                    if (offset > 0)
                    {
                        // DownloadAsync has already validated the exact open-ended range and body length.
                        var range = ContentRangeHeaderValue.Parse(response.ContentRange!);
                        length = range.Length;
                    }
                    if (!length.HasValue || length < 0 || state != null && state.Length != length)
                    {
                        throw new InvalidDataException("A stable explicit representation length is required.");
                    }
                    state ??= new Checkpoint(1, uri, tag!, length.Value, 0, Digest(hash), false);
                    await SaveCheckpointAsync(file, checkpoint, state, cancellationToken).ConfigureAwait(false);
                    long committed = state.Offset;
                    while (true)
                    {
                        int count = await response.Content!.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                        if (count == 0)
                        {
                            break;
                        }
                        if (count > state.Length - offset)
                        {
                            throw new InvalidDataException("Response exceeds the declared representation length.");
                        }
                        await file.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                        hash.AppendData(buffer, 0, count);
                        offset += count;
                        if (offset - committed >= c_checkpointInterval && offset < state.Length)
                        {
                            state = state with
                            {
                                Offset = offset,
                                Sha256 = Digest(hash)
                            };
                            await SaveCheckpointAsync(file, checkpoint, state, cancellationToken).ConfigureAwait(false);
                            committed = offset;
                        }
                    }
                    if (offset != state.Length)
                    {
                        throw new InvalidDataException("Response ended before the declared representation length.");
                    }
                    state = state with
                    {
                        Offset = offset,
                        Sha256 = Digest(hash),
                        Complete = true
                    };
                    await SaveCheckpointAsync(file, checkpoint, state, cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                file.Dispose();
                File.Move(partial, destination, false);
                File.Delete(checkpoint);
            }
            catch
            {
                file.Dispose();
                if (!resuming && !File.Exists(checkpoint))
                {
                    File.Delete(partial);
                }
                throw;
            }
        }

        private static bool IsStrongTag(string? value) =>
            EntityTagHeaderValue.TryParse(value, out var tag) && !tag.IsWeak && tag.Tag != "*";

        private static string Digest(IncrementalHash hash) => Convert.ToHexString(hash.GetCurrentHash());

        private static async Task VerifyPrefixAsync(FileStream file, Checkpoint state, IncrementalHash hash, byte[] buffer, CancellationToken token)
        {
            if (file.Length < state.Offset)
            {
                throw new InvalidDataException("Staged data is shorter than the durable checkpoint.");
            }
            long remaining = state.Offset;
            while (remaining > 0)
            {
                int count = (int)Math.Min(buffer.Length, remaining);
                await file.ReadExactlyAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                hash.AppendData(buffer, 0, count);
                remaining -= count;
            }
            if (Digest(hash) != state.Sha256)
            {
                throw new InvalidDataException("Staged prefix SHA-256 does not match the checkpoint.");
            }
        }

        private static async Task<Checkpoint> ReadCheckpointAsync(string path, string uri, CancellationToken token)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, c_maxCheckpointSize, FileOptions.Asynchronous);
            if (stream.Length > c_maxCheckpointSize)
            {
                throw new InvalidDataException("Checkpoint exceeds the supported size.");
            }
            var data = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(data, token).ConfigureAwait(false);
            if (data.Length < 65 || data[64] != '\n' ||
                Encoding.ASCII.GetString(data, 0, 64) != Convert.ToHexString(SHA256.HashData(data.AsSpan(65))))
            {
                throw new InvalidDataException("Checkpoint checksum is invalid.");
            }
            Checkpoint? state;
            try
            {
                state = JsonSerializer.Deserialize<Checkpoint>(data.AsSpan(65));
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Checkpoint JSON is invalid.", exception);
            }
            if (state == null || state.Version != 1 || state.Uri != uri || !IsStrongTag(state.ETag) ||
                state.Length < 0 || state.Offset < 0 || state.Offset > state.Length ||
                state.Sha256 == null || state.Sha256.Length != 64 || state.Complete && state.Offset != state.Length)
            {
                throw new InvalidDataException("Checkpoint does not describe this resource and a valid durable prefix.");
            }
            return state;
        }

        private static async Task SaveCheckpointAsync(FileStream file, string path, Checkpoint state, CancellationToken token)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(state);
            var prefix = Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(payload)) + "\n");
            if (prefix.Length + payload.Length > c_maxCheckpointSize)
            {
                throw new InvalidDataException("Resource identity exceeds the checkpoint size limit.");
            }
            await file.FlushAsync(token).ConfigureAwait(false);
            file.Flush(true);
            using (var stream = new FileStream(path + ".new", FileMode.Create, FileAccess.Write, FileShare.None,
                c_maxCheckpointSize, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(prefix, token).ConfigureAwait(false);
                await stream.WriteAsync(payload, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(true);
            }
            File.Move(path + ".new", path, true);
        }

        private sealed record Checkpoint(int Version, string Uri, string ETag, long Length, long Offset, string Sha256, bool Complete);
    }
}
