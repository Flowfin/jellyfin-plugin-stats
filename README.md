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

Not built yet. The project produces a single assembly today and the packaging
metadata still declares the 10.9 line it was templated from:

    grep -n 'targetAbi\|framework' build.yaml
    5:targetAbi: "10.9.0.0"
    6:framework: "net8.0"

The multi-target build is issue #4, and the support matrix that will hold the
version detail is issue #79.

## What it stores and who can see it

One row per play in the plugin's own data folder: which user played which item,
when it started and stopped, how much was watched, the client and device it
played on, and whether the server transcoded and why. A user reads their own
history; everyone else reads aggregates that name nobody. No network address,
no user agent and no library file path is kept.

Not built yet. Nothing is written to disk today, because the store, the write
path and the retention sweep are still open issues. A server running this
plugin as it stands records nothing at all.

## Installing

There is no release yet, and the route by which the plugin will reach users is
undecided (issue #10). When there is one, it will be a plugin archive per
server line, installed the way any other Jellyfin plugin is.

## Configuration

The plugin's settings page appears on the server dashboard under Plugins. It is
still the placeholder page inherited from the upstream plugin template; the real
one is issue #65, and the reference for every setting is issue #78.

## Building from source

Requires the .NET SDK for the line you are building against.

    dotnet build

    dotnet publish -c Release

## Licence

GPL-3.0-or-later. The full text is in [LICENSE](LICENSE). Jellyfin's own
libraries are GPLv3, so a plugin linked against them is GPLv3 once compiled.
