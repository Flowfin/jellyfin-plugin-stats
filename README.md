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

Plays are recorded, kept and read back. The capture path, the store, the
aggregation over it and the actions that answer from it are built, and the
settings page and two report pages are declared to the server. What is not built
is the page a user opens about their own numbers, and the per-user reads behind
it, which is issue #61. Every section below says which parts are built and which
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

    grep -E 'new QueuedPlayWriter|new CaptureGate|AddSingleton<IPlaybackEventSink' Jellyfin.Plugin.Stats/PluginServiceRegistrator.cs
            serviceCollection.AddSingleton(provider => new QueuedPlayWriter(
            serviceCollection.AddSingleton<IPlaySink>(provider => new CaptureGate(
            serviceCollection.AddSingleton<IPlaybackEventSink>(provider => provider.GetRequiredService<PlayTracker>());

They are read back too. Every action the plugin serves, matched by name rather
than by line so this paste does not go stale the next time a method moves:

    grep -oE 'ActionResult<[A-Za-z]+>> [A-Za-z]+' Jellyfin.Plugin.Stats/Api/*.cs
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:ActionResult<TopTitles>> GetTopTitles
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:ActionResult<BreakdownReport>> GetBreakdown
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:ActionResult<DailyUsage>> GetDailyUsage
    Jellyfin.Plugin.Stats/Api/YourConsentController.cs:ActionResult<ConsentState>> GetConsent
    Jellyfin.Plugin.Stats/Api/YourConsentController.cs:ActionResult<ConsentState>> SetConsent
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs:ActionResult<PlaysDeleted>> DeleteMyPlays
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:ActionResult<YearsHeld>> GetYears
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:ActionResult<YearInReview>> GetYear

The three on `AggregateReportsController` are the server-wide reports and name
nobody. The other five answer about one account, and each of them asks whether
the caller is that account before it reads anything:

    grep -c 'CallerIdentity.AsksForTheirOwnRows' Jellyfin.Plugin.Stats/Api/Your*.cs
    Jellyfin.Plugin.Stats/Api/YourConsentController.cs:2
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs:1
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:2

What is still a plan rather than a description is the page a user opens about
themselves. The plays, watched time, top items and completion the top of this
file promises a user have no per-user read behind them: the five above answer a
calendar year, which years an account has, and the consent and deletion
controls. That page and those reads are issue #61.

## Installing

The 10.11 line has published its first release, and the 12.0 line has not:

    gh api repos/Flowfin/jellyfin-plugin-stats/releases --jq '.[].tag_name'
    0.1.0.0-stable

Distribution is through Flowfin's own plugin manifest rather than the official
catalogue, so installing means adding one repository URL to the server's plugin
repository list and then installing the plugin from the catalogue that URL
serves. The address is

    https://flowfin.dev/manifest.json

and it is added once: one manifest carries every Flowfin plugin, so a server
that has it for one of them already has it for this one. One archive per server
line, and the server picks the one matching its version.

What that address answers about this plugin, and the version a 10.11 server
would take from it:

    curl -s https://flowfin.dev/manifest.json \
      | jq -r '.[] | select(.name == "Playback Statistics") | .versions[] | "\(.version) targetAbi \(.targetAbi)"'
    0.1.0.0 targetAbi 10.11.0.0

A 12.0 server finds nothing to install under that entry until the 12.0 line
publishes, which is issue #80.

Nobody has followed this route on a fresh server and written down what happened.
The address answers, the entry names a release that exists, and the checksum it
publishes is the one in the release's `.md5` - but an install is a thing a
server does, and none of those three readings is one. That recording is what
issue #81 is still open for, and this paragraph is not it.

The two lines are told apart by the version number, because a catalogue admits
one tag shape and a suffix cannot carry the difference. The 10.11 line's
releases start at `0.1.0.0` and the 12.0 line's start at `1.0.0.0`. **The
leading number says which server line a release is for and not how finished the
plugin is.** `1.0.0.0` is the same unfinished interface `0.1.0.0` carries, built
from the same source, and reading it as a mature release and the other as a
provisional one is the mistake this paragraph exists against.

## Configuration

The plugin's settings page appears on the server dashboard under Plugins, and
the fields on it are this plugin's own rather than the upstream template's:

    grep -oE 'id="[A-Z][A-Za-z]+"' Jellyfin.Plugin.Stats/Configuration/configPage.html | grep -v '^id="Stats'
    id="CaptureEnabled"
    id="PlayRowRetentionDays"
    id="DailyAggregateRetentionDays"
    id="RollupTimeZone"
    id="ExcludedUserIds"
    id="ExcludedItemTypes"
    id="MaximumRangeDays"
    id="MaximumRowsPerResponse"

[The configuration reference](docs/configuration.md) is the account of each one:
what it does, what it accepts, what it defaults to, and what changing it does
not do. It is read there rather than repeated here, where the two could
disagree.

## Building from source

Requires the .NET SDK for the line you are building against.

    dotnet build

    dotnet publish -c Release

## Licence

GPL-3.0-or-later. The full text is in [LICENSE](LICENSE). Jellyfin's own
libraries are GPLv3, so a plugin linked against them is GPLv3 once compiled.

See [NOTICE.md](NOTICE.md) for the intended-use notice.
