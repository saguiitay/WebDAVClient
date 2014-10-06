# WebDAVClient

## Abount

Strongly-typed, async WebDAV Client implementation in C#.

WebDAVClient is based originally on <https://github.com/kvdb/WebDAVClient>. I've added Async support (instead of Callback), as well strong-types responses.

## Contact

You can contact me on twitter [@saguiitay](https://twitter.com/saguiitay).

## NuGet

WebDAVClient is available as a [NuGet package](https://www.nuget.org/packages/WebDAVClient/)

## Release Notes

+ **1.0.0**   Initial release.

# Usage

` csharp
// Basic authentication required
var client = new WebDAVClient.Client(new NetworkCredential { UserName = "USERNAME" , Password = "PASSWORD"});
// OR without authentication
var client = new WebDAVClient.Client(new NetworkCredential());

// Set basic information for WebDAV provider
client.Server = "https://webdav.4shared.com";
client.BasePath = "/";

// Retrieve list of items in root folder
var files = await client.List();

// Find folder named 'Test'
var folder = files.FirstOrDefault(f => f.Href.EndsWith("/Test/"));

// Reload folder 'Test'
var folderReloaded = await client.Get(folder.Href);

// Retrieve list of items in 'Test' folder
var folderFiles = await client.List(folder.Href);

// Find first file in 'Test' folder
var folderFile = folderFiles.FirstOrDefault(f => f.IsCollection == false);
// Downlaod file into a Stream
var stream = await client.Download(folderFile.Href);

// Upload a file (with a random name) to 'Test' folder
var tempName = Path.GetRandomFileName();
var fileUploaded = await client.Upload(folder.Href, File.OpenRead(@"<PATH TO FILE>"), tempName);

// Create a folder (with a random name) in the 'Test' folder
tempName = Path.GetRandomFileName();
var folderCreated = await client.CreateDir(folder.Href, tempName);
```
