using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WebDAVClient.Model;

namespace WebDAVClient
{
    public interface IClient : IDisposable
    {
        /// <summary>
        /// Specify the WebDAV hostname (required).
        /// </summary>
        string Server { get; set; }

        /// <summary>
        /// Specify the path of a WebDAV directory to use as 'root' (default: /)
        /// </summary>
        string BasePath { get; set; }

        /// <summary>
        /// Specify a port to use
        /// </summary>
        int? Port { get; set; }

        /// <summary>
        /// Specify the UserAgent (and UserAgent version) string to use in requests
        /// </summary>
        string UserAgent { get; set; }

        /// <summary>
        /// Specify the UserAgent (and UserAgent version) string to use in requests
        /// </summary>
        string UserAgentVersion { get; set; }

        /// <summary>
        /// Specify additional headers to be sent with every request
        /// </summary>
        ICollection<KeyValuePair<string, string>> CustomHeaders { get; set; }

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <param name="path">List only files in this path. Defaults to <c>/</c> (the configured base path).</param>
        /// <param name="depth">
        /// PROPFIND <c>Depth</c> header value. <c>0</c> requests the resource itself only,
        /// <c>1</c> (the default) requests the resource and its immediate children, and
        /// <c>null</c> requests infinite depth (<c>Depth: infinity</c>) — note that many
        /// servers reject infinite-depth PROPFIND requests on collections.
        /// </param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash).</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task<IEnumerable<Item>> List(string path = "/", int? depth = 1, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get folder information from the server.
        /// </summary>
        /// <param name="path">Path of the folder on the server. Defaults to <c>/</c> (the configured base path).</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>An item representing the retrieved folder.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task<Item> GetFolder(string path = "/", CancellationToken cancellationToken = default);

        /// <summary>
        /// Get file information from the server.
        /// </summary>
        /// <param name="path">Path and filename of the file on the server. Defaults to <c>/</c> (the configured base path).</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>An item representing the retrieved file.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task<Item> GetFile(string path = "/", CancellationToken cancellationToken = default);

        /// <summary>
        /// Download a file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>A stream with the content of the downloaded file. The caller owns the stream and must dispose it.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task<Stream> Download(string remoteFilePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Download a part of file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="startBytes">Start bytes of content</param>
        /// <param name="endBytes">End bytes of content</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>A stream with the partial content of the downloaded file. The caller owns the stream and must dispose it.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task<Stream> DownloadPartial(string remoteFilePath, long startBytes, long endBytes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Upload a file to the server
        /// </summary>
        /// <param name="remoteFilePath">Target directory path on the server (excluding the filename) where the file will be created.</param>
        /// <param name="content">The stream containing the file content to upload. Must be readable and positioned at the start of the data to send.</param>
        /// <param name="name">The target filename to create under <paramref name="remoteFilePath"/>.</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>True if the file was uploaded successfully. False otherwise.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task<bool> Upload(string remoteFilePath, Stream content, string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Upload a part of a file to the server.
        /// </summary>
        /// <param name="remoteFilePath">Target path excluding the servername and base path</param>
        /// <param name="content">The content to upload. Must match the length of <paramref name="endBytes"/> minus <paramref name="startBytes"/></param>
        /// <param name="name">The target filename. The file must exist on the server</param>
        /// <param name="startBytes">StartByte on the target file</param>
        /// <param name="endBytes">EndByte on the target file</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>True if the file part was uploaded successfully. False otherwise.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task<bool> UploadPartial(string remoteFilePath, Stream content, string name, long startBytes, long endBytes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Create a directory on the server
        /// </summary>
        /// <param name="remotePath">Destination path of the directory on the server</param>
        /// <param name="name">The name of the folder to create</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>True if the folder was created successfully. False otherwise.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVConflictException">Thrown when the server returns 409 (Conflict), typically because a parent path is missing.</exception>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns any other non-success status.</exception>
        Task<bool> CreateDir(string remotePath, string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a folder from the server.
        /// </summary>
        /// <param name="path">Path of the folder on the server. Defaults to <c>/</c> (the configured base path).</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task DeleteFolder(string path = "/", CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a file from the server.
        /// </summary>
        /// <param name="path">Path and filename of the file on the server. Defaults to <c>/</c> (the configured base path).</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task DeleteFile(string path = "/", CancellationToken cancellationToken = default);

        /// <summary>
        /// Move a folder on the server
        /// </summary>
        /// <param name="srcFolderPath">Source path of the folder on the server</param>
        /// <param name="dstFolderPath">Destination path of the folder on the server</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>True if the folder was moved successfully. False otherwise.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task<bool> MoveFolder(string srcFolderPath, string dstFolderPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Move a file on the server
        /// </summary>
        /// <param name="srcFilePath">Source path and filename of the file on the server</param>
        /// <param name="dstFilePath">Destination path and filename of the file on the server</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>True if the file was moved successfully. False otherwise.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task<bool> MoveFile(string srcFilePath, string dstFilePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Copies a folder on the server
        /// </summary>
        /// <param name="srcFolderPath">Source path of the folder on the server</param>
        /// <param name="dstFolderPath">Destination path of the folder on the server</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>True if the folder was copied successfully. False otherwise.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task<bool> CopyFolder(string srcFolderPath, string dstFolderPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Copies a file on the server
        /// </summary>
        /// <param name="srcFilePath">Source path and filename of the file on the server</param>
        /// <param name="dstFilePath">Destination path and filename of the file on the server</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>True if the file was copied successfully. False otherwise.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status.</exception>
        Task<bool> CopyFile(string srcFilePath, string dstFilePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Acquire an exclusive write lock on a file (RFC 4918 §9.10, <c>Depth: 0</c>).
        /// </summary>
        /// <param name="filePath">Path and filename of the file on the server.</param>
        /// <param name="timeoutSeconds">
        /// Requested lock timeout in seconds (sent as <c>Timeout: Second-{n}</c>). The server may grant a shorter
        /// timeout — inspect <see cref="LockInfo.TimeoutSeconds"/> on the returned value. Must be greater than zero.
        /// </param>
        /// <param name="owner">
        /// Free-form identifier for the lock owner, embedded in the request body's <c>&lt;D:owner&gt;</c> element so
        /// that other clients (and humans inspecting the lock) can identify who holds it. When <c>null</c>, defaults
        /// to <c>WebDAVClient</c>.
        /// </param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>A <see cref="LockInfo"/> describing the granted lock; <see cref="LockInfo.Token"/> is the value to pass to <see cref="UnlockFile"/> / <see cref="RefreshLock"/>.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server refuses the lock (e.g. <c>423 Locked</c>) or returns any other non-success status.</exception>
        Task<LockInfo> LockFile(string filePath, int timeoutSeconds = 600, string owner = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Acquire an exclusive write lock on a folder (RFC 4918 §9.10, <c>Depth: infinity</c>) — locks the
        /// collection and all of its members.
        /// </summary>
        /// <param name="folderPath">Path of the folder on the server.</param>
        /// <param name="timeoutSeconds">
        /// Requested lock timeout in seconds. The server may grant a shorter timeout. Must be greater than zero.
        /// </param>
        /// <param name="owner">
        /// Free-form identifier for the lock owner. Defaults to <c>WebDAVClient</c> when <c>null</c>.
        /// </param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>A <see cref="LockInfo"/> describing the granted lock.</returns>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server refuses the lock or returns a non-success status. <c>207 Multi-Status</c> on a depth-infinity LOCK indicates a partial failure (RFC 4918 §9.10.6) and is also surfaced as <c>WebDAVException</c>.</exception>
        Task<LockInfo> LockFolder(string folderPath, int timeoutSeconds = 600, string owner = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Release a lock previously acquired with <see cref="LockFile"/> (RFC 4918 §9.11).
        /// </summary>
        /// <param name="filePath">Path and filename of the file on the server.</param>
        /// <param name="lockToken">
        /// The lock token returned by <see cref="LockFile"/> (i.e. <see cref="LockInfo.Token"/>). May be passed in
        /// either bare form (<c>opaquelocktoken:...</c>) or wrapped in angle brackets (<c>&lt;opaquelocktoken:...&gt;</c>);
        /// the client normalizes it before sending the <c>Lock-Token</c> header.
        /// </param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <exception cref="System.ArgumentException">Thrown when <paramref name="lockToken"/> is <c>null</c>, empty, or malformed.</exception>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-<c>204</c> status (e.g. <c>409 Conflict</c> when the token does not match the lock).</exception>
        Task UnlockFile(string filePath, string lockToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// Release a lock previously acquired with <see cref="LockFolder"/> (RFC 4918 §9.11).
        /// </summary>
        /// <param name="folderPath">Path of the folder on the server.</param>
        /// <param name="lockToken">
        /// The lock token returned by <see cref="LockFolder"/>. Bare or angle-bracketed forms are both accepted.
        /// </param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <exception cref="System.ArgumentException">Thrown when <paramref name="lockToken"/> is <c>null</c>, empty, or malformed.</exception>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-<c>204</c> status.</exception>
        Task UnlockFolder(string folderPath, string lockToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// Refresh an existing lock by sending a body-less LOCK request with the <c>If</c> header carrying the
        /// current lock token (RFC 4918 §9.10.2). Use this before <c>TimeoutSeconds</c> elapses to keep the lock alive.
        /// </summary>
        /// <param name="path">Path of the locked file or folder on the server.</param>
        /// <param name="lockToken">
        /// The current lock token, in either bare or angle-bracketed form.
        /// </param>
        /// <param name="timeoutSeconds">
        /// Requested new timeout in seconds. The server may grant a shorter value. Must be greater than zero.
        /// </param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>
        /// A <see cref="LockInfo"/> reflecting the refreshed lock state. If the server's response carries no
        /// <c>activelock</c> body (some servers do this), the returned <see cref="LockInfo.Token"/> is the
        /// caller-supplied token and the other fields will be <c>null</c>.
        /// </returns>
        /// <exception cref="System.ArgumentException">Thrown when <paramref name="lockToken"/> is <c>null</c>, empty, or malformed.</exception>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown when the server returns a non-success status (e.g. <c>412 Precondition Failed</c> when the token no longer matches an active lock).</exception>
        Task<LockInfo> RefreshLock(string path, string lockToken, int timeoutSeconds = 600, CancellationToken cancellationToken = default);

        /// <summary>
        /// Set (create or replace) a single dead property on a resource (RFC 4918 §9.2 PROPPATCH).
        /// </summary>
        /// <param name="path">Path of the file or folder on the server.</param>
        /// <param name="propertyName">
        /// Local XML name of the property. Must be a valid XML <c>NCName</c> (no colons, no spaces,
        /// must start with a letter or underscore).
        /// </param>
        /// <param name="propertyNamespace">
        /// Namespace URI for the property. Required and must be non-empty. The reserved <c>DAV:</c>
        /// namespace is rejected because RFC 4918 §15 declares those properties protected; use a
        /// custom namespace (e.g. <c>http://example.com/ns</c>) for application metadata.
        /// </param>
        /// <param name="value">
        /// Text value to assign. The value is XML-escaped before being sent. <c>null</c> is treated
        /// the same as the empty string. To remove a property, call <see cref="RemoveProperty"/>
        /// instead.
        /// </param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns><c>true</c> when the server confirms the property was set successfully.</returns>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <paramref name="propertyName"/> is not a valid XML <c>NCName</c>, when
        /// <paramref name="propertyNamespace"/> is null/empty, or when <paramref name="propertyNamespace"/>
        /// equals <c>DAV:</c>.
        /// </exception>
        /// <exception cref="WebDAVClient.Helpers.WebDAVConflictException">
        /// Thrown when the server returns <c>409 Conflict</c> at the HTTP layer, or when an individual
        /// <c>D:propstat</c> in the multistatus response carries a <c>409 Conflict</c> status (e.g. the
        /// resource is in a state that does not permit the property change).
        /// </exception>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">
        /// Thrown when the HTTP status is anything other than <c>207 Multi-Status</c>, when the multistatus
        /// body is missing or malformed (no parsable <c>D:propstat</c> with a status line), or when the
        /// per-property status is non-2xx (e.g. <c>403 Forbidden</c> for a protected property,
        /// <c>424 Failed Dependency</c> per RFC 4918 §11.4).
        /// </exception>
        Task<bool> SetProperty(string path, string propertyName, string propertyNamespace, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Remove a single dead property from a resource (RFC 4918 §9.2 PROPPATCH).
        /// </summary>
        /// <param name="path">Path of the file or folder on the server.</param>
        /// <param name="propertyName">Local XML name of the property. Must be a valid XML <c>NCName</c>.</param>
        /// <param name="propertyNamespace">
        /// Namespace URI for the property. Required and must be non-empty; <c>DAV:</c> is rejected.
        /// </param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>
        /// <c>true</c> when the server confirms the removal. Per RFC 4918 §9.2, removing a property that
        /// does not exist is not an error — the server reports <c>200 OK</c> for that property and this
        /// method returns <c>true</c>.
        /// </returns>
        /// <exception cref="System.ArgumentException">
        /// Thrown for an invalid <paramref name="propertyName"/>, an empty <paramref name="propertyNamespace"/>,
        /// or <paramref name="propertyNamespace"/> equal to <c>DAV:</c>.
        /// </exception>
        /// <exception cref="WebDAVClient.Helpers.WebDAVConflictException">Thrown for <c>409 Conflict</c> at either the HTTP layer or in a <c>D:propstat</c>.</exception>
        /// <exception cref="WebDAVClient.Helpers.WebDAVException">Thrown for any other non-success status or a malformed multistatus body.</exception>
        Task<bool> RemoveProperty(string path, string propertyName, string propertyNamespace, CancellationToken cancellationToken = default);
    }
}