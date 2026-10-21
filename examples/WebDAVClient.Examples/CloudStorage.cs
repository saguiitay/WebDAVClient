using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient.Examples;

public static class CloudStorage
{
    public static async Task RunAsync(DavClient client, bool nextcloud, DavPath remoteFile,
        string localFile, TextWriter output, CancellationToken token)
    {
        var properties = new List<PropertyName>
        {
            DavProperties.DisplayName.Name,
            DavProperties.ResourceTypes.Name,
            DavProperties.ETag.Name,
            new PropertyName("http://owncloud.org/ns", "fileid"),
            new PropertyName("http://owncloud.org/ns", "permissions"),
            new PropertyName("http://owncloud.org/ns", "size")
        };
        properties.Add(nextcloud
            ? new PropertyName("http://nextcloud.org/ns", "has-preview")
            : new PropertyName("http://owncloud.org/ns", "id"));

        await ListingAndProperties.RunAsync(client, properties, output, token);
        await GettingStarted.DownloadAsync(client, remoteFile, localFile, output, token);
    }
}
