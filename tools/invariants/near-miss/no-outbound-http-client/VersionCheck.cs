// Near miss for no-outbound-http-client.
//
// The configuration page grows a line saying whether a newer plugin version is
// available, and the cheapest way to fill it is to ask the catalogue for its
// manifest. It reads as harmless: nothing about a user leaves the server, the
// call is made once at start-up, and the answer is a version string.
//
// What it costs is the statement the whole privacy design rests on, that this
// plugin talks to nothing outside the server it runs in. That statement is not
// provable again by watching one quiet run, so the client is refused at the
// source instead.

private static readonly HttpClient Catalogue = new HttpClient();

public async Task<string> NewestPublishedVersion(CancellationToken token)
{
    var manifest = await Catalogue.GetStringAsync(CatalogueManifest, token).ConfigureAwait(false);
    return NewestVersionIn(manifest);
}
