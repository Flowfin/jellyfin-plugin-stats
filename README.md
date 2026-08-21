> [!NOTE]
>
> **Part of [Flowfin](https://github.com/Flowfin).** It works with any Jellyfin
> server, and with the Flowfin clients.

# Playback Statistics

A Jellyfin plugin that turns playback into statistics the server owner and the
people using it can actually read: what was played, when, from which client,
how much of it was watched, and how often the server had to transcode.

Statistics respect each user's privacy. Personal detail is visible only to the
user it is about, unless that user chooses otherwise, and server-wide views name
nobody.

## State of the work

This repository holds the plan, the capture path and the build scaffolding.
Plays are recorded and kept, nothing reads them back yet, and there is no
release to install. Every section below says which parts are built and which
are not, so nothing here reads as a promise about today.

## Which servers it runs on

Two server lines are supported: Jellyfin 10.11, which runs on .NET 9, and
Jellyfin 12.0, which runs on .NET 10. One artifact is built per line, and a
server outside those lines is not supported.

The version detail is in [the support matrix](docs/support-matrix.md): the
framework each artifact targets, the oldest server of its line each is compiled
against, and the abi its package declares to a server. Every cell of that table
is checked against the value the build uses, so it is read there rather than
repeated here where the two could disagree.

## What it stores and who can see it

One row per play in the plugin's own data folder: which user played which item,
when it started and stopped, how much was watched, the client and device it
played on, and whether the server transcoded and why. No network address, no
user agent and no library file path is kept.

[What this plugin records, and who can read it](docs/what-is-stored.md) is the
account of it, column by column, with the readers and the absences named. Its
field list is compared against the statement the store runs, so it cannot fall
behind the schema without a red check.

Those rows are written today. The subscription, the gate that decides whether a
play is recorded, and the queue that opens the store are assembled in one place:

    grep -nE 'new QueuedPlayWriter|new CaptureGate|AddSingleton<IPlaybackEventSink' Jellyfin.Plugin.Stats/PluginServiceRegistrator.cs
    36:        serviceCollection.AddSingleton(provider => new QueuedPlayWriter(
    44:        serviceCollection.AddSingleton<IFinishedPlaySink>(provider => new CaptureGate(
    48:        serviceCollection.AddSingleton<IPlaybackEventSink, PlayTracker>();

What is not built is the reading. The plugin serves no endpoint, so nothing
hands a row back to anybody, and the sentence at the top of this file about a
user reading their own history and everybody else reading aggregates that name
nobody is a plan rather than a description. The document above says which parts
of it a server has today and which are still open issues.

## Installing

There is no release yet:

    gh api repos/Flowfin/jellyfin-plugin-stats/releases --jq 'length'
    0

When there is one it will be distributed through this repository's own plugin
manifest rather than the official catalogue, so installing means adding a
repository URL to the server and then installing the plugin from it. One
archive per server line, and the server picks the one matching its version.

The two lines are told apart by the version number, because a catalogue admits
one tag shape and a suffix cannot carry the difference. The 10.11 line's
releases start at `0.1.0.0` and the 12.0 line's start at `1.0.0.0`. **The
leading number says which server line a release is for and not how finished the
plugin is.** `1.0.0.0` is the same unfinished interface `0.1.0.0` carries, built
from the same source, and reading it as a mature release and the other as a
provisional one is the mistake this paragraph exists against.

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

See [NOTICE.md](NOTICE.md) for the intended-use notice.
