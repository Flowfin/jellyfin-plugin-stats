# The pages are public, and the data is not

Every page this plugin adds to the dashboard can be fetched by anybody who can
reach the server, signed in or not. That is a property of the server rather than
a choice made here, and it is the reason the pages are written the way they are:
markup and code, with every value fetched after the page has loaded, over a
request that is authorized.

## Why the pages are public

Two endpoints are involved and only one of them is protected. Listing the
configuration pages needs elevation:

    gh api 'repos/jellyfin/jellyfin/contents/Jellyfin.Api/Controllers/DashboardController.cs?ref=v10.11.11' \
      --jq '.content' | base64 -d | grep -nE 'HttpGet\("web/Configuration|Authorize'
    48:    [HttpGet("web/ConfigurationPages")]
    49:    [Authorize(Policy = Policies.RequiresElevation)]
    72:    [HttpGet("web/ConfigurationPage")]

Returning a page's content carries no authorization attribute at all, and the
server configures a default policy without a fallback policy, so an action with
no attribute of its own is reached without authenticating:

    gh api 'repos/jellyfin/jellyfin/contents/Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs?ref=v10.11.11' \
      --jq '.content' | base64 -d | grep -c 'FallbackPolicy' ; echo "exit=$?"
    0
    exit=1

    gh api 'search/code?q=FallbackPolicy+repo:jellyfin/jellyfin' --jq '.total_count'
    0

Both readings are of the 10.11 line at `v10.11.11`. Whether the 12.0 line has
moved on this is not evaluated here.

What that endpoint returns is the embedded resource, handed back as a file with
nothing put into it on the way:

    gh api 'repos/jellyfin/jellyfin/contents/Jellyfin.Api/Controllers/DashboardController.cs?ref=v10.11.11' \
      --jq '.content' | base64 -d | sed -n '85,93p'
        string resourcePath = altPage.Item1.EmbeddedResourcePath;
        Stream? stream = plugin.GetType().Assembly.GetManifestResourceStream(resourcePath);
        if (stream is null)
        {
            _logger.LogError("Failed to get resource {Resource} from plugin {Plugin}", resourcePath, plugin.Name);
            return NotFound();
        }

        return File(stream, MimeTypes.GetMimeType(resourcePath));

So the bytes a stranger receives are the bytes in the assembly, and the question
of what a page discloses is entirely the question of what is compiled into it.

## What that means for a page in this plugin

A page contains markup and code. It contains no name, no identifier, no total,
no configuration value and no token. Everything a page shows is fetched after it
loads, by a request the server authorizes on its own terms, and a caller who may
not have the value gets no value rather than a page that already carries it.

The plugin's own identifier is in a page, and is meant to be. The configuration
page passes it to the two calls it makes to the server, and the same identifier
is in `build.yaml`, which is what a catalogue publishes. It identifies the
plugin and nobody using it.

## What holds it

Two checks, at the two ends of the same statement.

`no-server-value-in-a-page-asset` in `tools/invariants/rules` reads the tracked
files and refuses the marker shapes a page carries when something is expected to
fill it in: a Handlebars or template pair of braces, an ASP or ERB tag, a Razor
model reference, a percent-delimited token. It runs before anything is built.

`PageAssetTests` in the suite reads the compiled assembly, which is the copy a
server serves. It asserts that the embedded page assets are exactly the tracked
ones, that each embedded asset is byte for byte the file it was built from, that
none carries a substitution marker, and that the only identifier in any of them
is the plugin's own. The byte comparison is the one that catches a build step
writing a value in, which is the case the invariant rule cannot see because the
tracked file it reads is still clean.

Neither check can tell a value somebody typed into a page by hand from ordinary
page text. A hardcoded name is the same bytes as a heading, and no reading of
the tree separates them. That is what the review is for, and this document is
what a reviewer is checking a page against.
