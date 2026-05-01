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
    }
}