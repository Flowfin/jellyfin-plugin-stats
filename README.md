# Playback Statistics

A Jellyfin plugin that turns playback into statistics the server owner and the
people using it can actually read: what was played, when, from which client,
how much of it was watched, and how often the server had to transcode.

Statistics respect each user's privacy. Personal detail is visible only to the
user it is about, unless that user chooses otherwise, and server-wide views name
nobody.

## State of the work

This repository holds the plan and the build scaffolding. No playback is
captured yet and there is no release to install. Every section below says which
parts are built and which are not, so nothing here reads as a promise about
today.

## Which servers it runs on

Two server lines are supported: Jellyfin 10.11, which runs on .NET 9, and
Jellyfin 12.0, which runs on .NET 10. One artifact is built per line, and a
server outside those lines is not supported.

The plugin compiles for both, and the packaging workflow makes one archive per
line so a server is offered the one that declares its own version:

    grep -n 'TargetFrameworks' Jellyfin.Plugin.Stats/Jellyfin.Plugin.Stats.csproj
    10:    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>

    grep -n 'Build the package for' .github/workflows/package.yml
    58:      - name: Build the package for the 10.11 line
    92:      - name: Build the package for the 12.0 line

What is not proved is the floor of each line. The build resolves whatever
package version the project asks for, so nothing here shows the plugin still
compiles against the oldest server release in a line it claims. That is issue
#17, and the support matrix holding the version detail is issue #79.

## What it stores and who can see it

One row per play in the plugin's own data folder: which user played which item,
when it started and stopped, how much was watched, the client and device it
played on, and whether the server transcoded and why. A user reads their own
history; everyone else reads aggregates that name nobody. No network address,
no user agent and no library file path is kept.

Not built yet, and the paragraph above describes the design rather than today.
The plugin now subscribes to the server's playback events, and the sink those
events reach discards every one of them:

    grep -n 'AddSingleton<IPlaybackEventSink' Jellyfin.Plugin.Stats/PluginServiceRegistrator.cs
    27:        serviceCollection.AddSingleton<IPlaybackEventSink, DiscardingPlaybackEventSink>();

So a server running this plugin as it stands keeps no playback data at all. The
store, the write path and the retention sweep are open issues, and until they
land the sentences above are a plan and not a description.

## Installing

There is no release yet:

    gh api repos/iderex/jellyfin-plugin-stats/releases --jq 'length'
    0

When there is one it will be distributed through this repository's own plugin
manifest rather than the official catalogue, so installing means adding a
repository URL to the server and then installing the plugin from it. One
archive per server line, and the server picks the one matching its version.

## Configuration

The plugin's settings page appears on the server dashboard under Plugins. Every
field on it is still the upstream template's, so there is nothing on it worth
setting; the real page is issue #65, and the reference for every setting is
issue #78.

## Building from source

Requires the .NET SDK for the line you are building against.

    dotnet build

    dotnet publish -c Release

## Licence

GPL-3.0-or-later. The full text is in [LICENSE](LICENSE). Jellyfin's own
libraries are GPLv3, so a plugin linked against them is GPLv3 once compiled.
