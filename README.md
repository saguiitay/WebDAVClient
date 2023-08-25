# WebDAVClient

## Overview

WebDAV Client for .Net Core, strongly typed, async and open-sourced, imnplemented in C#.

WebDAVClient is based originally on <https://github.com/kvdb/WebDAVClient>. I've added Async support (instead of Callback), as well strong-types responses.

## NuGet

WebDAVClient is available as a [NuGet package](https://www.nuget.org/packages/WebDAVClient/)

## Features

* Available as a NuGet packages
* Fully support Async/Await
* Strong-typed
* Implemented using HttpClient, which means support for extendibility such as throttling and monitoring
* Supports Unauthenticated or Windows Authentication-based access
* Supports custom Certificate validation
* Supports the full WebDAV API:
  * Retrieving files & folders
  * Listing items in a folder
  * Creating & deleting folders
  * Downloading & uploading files
  * Downloading & uploading partial content
  * Moving & copying files and folders

## Release Notes

+ **2.2.1**   Minor packaging improvements
+ **2.2.0**   Improvement: 
  - Implement `IDisposable` to avoid `HttpClient` leak
  - Added support for `CancellationToken`
  - Various performance improvements to reduce memory allocations
  - Minor code cleanup
+ **2.1.0**   Bug fixes: Fixed handling of port
+ **2.0.0**   BREAKING CHANGES!!!
  - Added support for .Net Core 3.0 & .Net Standard 2.0
+ **1.1.3**   Improvement: 
  - Ignore WhiteSpaces while parsing response XML
  - Enable Windows Authentication
  - Support curstom certificate validation
  - Add download partial content
  - Improved testability by using a wrapper for HttpClient
+ **1.1.2**   Improvements: 
  - Make WebDAVClient work on Mono and make NuGet compatible with Xamarin projects
  - Provide a IWebProxy parameter for HttpClient
  - Change type of ContentLength from int to long
  - Improved compatibility to SVN
+ **1.1.1**   Improvements: 
  - Improved parsing of server responses
  - Improved the way we calculate URLs
+ **1.1.0**   Improvement: Improved parsing of server values
+ **1.0.23**   Improvement: Improve the way we handle path with "invalid" characters
+ **1.0.22**   Bug fixes and improvements:
  - Improved the way we identify the root folder 
  - Fixed calculation of URLs
+ **1.0.20**   Improvements:
  - Added support for default/custom UserAgent
  - Improved ToString method of WebDAVException
+ **1.0.19**   Improvement: Added support for uploads timeout
+ **1.0.18**   Improvement: Added MoveFolder and MoveFile methods
+ **1.0.17**   Improvement: Added DeleteFolder and DeleteFile methods
+ **1.0.16**   Improvements:
  - Improved filtering of listed folder
  - Disable ExpectContinue header from requests
+ **1.0.15**   Bug fixes: Trim trailing slashes from HRef of non-collection (files) items
+ **1.0.14**   BREAKING  CHANGES: Replaced Get() method with separated GetFolder() and GetFile() methods.
+ **1.0.13**   Improvement: Replaced deserialization mechanism with a more fault-tolerant one.
+ **1.0.12**   Bug fixes: Removed disposing of HttpMessageResponse when returning content as Stream
+ **1.0.11**   Improvements: 
  - Introduced new IClient interface
  - Added new WebDAVConflictException
  - Dispose HttpRequestMessage and HttpResponseMessage after use
+ **1.0.10**   Bug fixes: Correctly handle NoContent server responses as valid responses
+ **1.0.9**   Bug fixes: Improved handling of BasePath
+ **1.0.8**   Bug fixes: Handle cases when CreationDate is null
+ **1.0.7**   Async improvements: Added ConfigureAwait(false) to all async calls
+ **1.0.5**   Bug fixes and improvements: 
  - Decode Href property of files/folders
  - Complete Http send operation as soon as headers are read
+ **1.0.3**   Various bug fixes.
+ **1.0.1**   Improved error handling and authentication
+ **1.0.0**   Initial release.


# Usage

``` csharp
// Basic authentication required
IClient c = new Client(new NetworkCredential { UserName = "USERNAME" , Password = "PASSWORD"});
// OR without authentication
var client = new WebDAVClient.Client(new NetworkCredential());

// Set basic information for WebDAV provider
c.Server = "https://dav.dumptruck.goldenfrog.com/";
c.BasePath = "/dav/";

// List items in the root folder
var files = await c.List();

// Find folder named 'Test'
var folder = files.FirstOrDefault(f => f.Href.EndsWith("/Test/"));
// Reload folder 'Test'
var folderReloaded = await c.GetFolder(folder.Href);

// Retrieve list of items in 'Test' folder
var folderFiles = await c.List(folderReloaded.Href);
// Find first file in 'Test' folder
var folderFile = folderFiles.FirstOrDefault(f => f.IsCollection == false);

var tempFileName = Path.GetTempFileName();

// Download item into a temporary file
using (var tempFile = File.OpenWrite(tempFileName))
using (var stream = await c.Download(folderFile.Href))
	await stream.CopyToAsync(tempFile);

// Update file back to webdav
var tempName = Path.GetRandomFileName();
using (var fileStream = File.OpenRead(tempFileName))
{
	var fileUploaded = await c.Upload(folder.Href, fileStream, tempName);
}

// Create a folder
var tempFolderName = Path.GetRandomFileName();
var isfolderCreated = await c.CreateDir("/", tempFolderName);

// Delete created folder
var folderCreated = await c.GetFolder("/" + tempFolderName);
await c.DeleteFolder(folderCreated.Href);
```

## Contact

You can contact me on twitter [@saguiitay](https://twitter.com/saguiitay).
