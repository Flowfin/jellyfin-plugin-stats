<!-- markdownlint-disable MD033 MD041 -->

> [!NOTE]
>
> **Part of [Flowfin](https://github.com/Flowfin).** It works with any Jellyfin
> server, and with the Flowfin clients.
>
> **Status: In-Development**, the first rung of the maturity ladder
> (In-Development, Alpha, Beta, Release Candidate, Full Release). It is
> installed by adding this project's own repository to Jellyfin, see
> [Installing](#installing).

<h1 align="center">Playback Statistics</h1>

<p align="center">
<img alt="Playback Statistics" src="https://raw.githubusercontent.com/Flowfin/jellyfin-plugin-stats/master/img/logo.png" width="240"/>
<br/>
<br/>
<a href="https://github.com/Flowfin/jellyfin-plugin-stats/blob/master/LICENSE">
<img alt="GPL-3.0-or-later" src="https://img.shields.io/github/license/Flowfin/jellyfin-plugin-stats.svg"/>
</a>
<a href="https://github.com/Flowfin/jellyfin-plugin-stats/releases">
<img alt="Latest release for Jellyfin 10.11" src="https://img.shields.io/github/v/release/Flowfin/jellyfin-plugin-stats?filter=0.*&amp;display_name=tag&amp;label=Jellyfin%2010.11"/>
</a>
<a href="https://github.com/Flowfin/jellyfin-plugin-stats/releases">
<img alt="Latest release for Jellyfin 12" src="https://img.shields.io/github/v/release/Flowfin/jellyfin-plugin-stats?filter=1.*&amp;display_name=tag&amp;label=Jellyfin%2012"/>
</a>
<a href="https://github.com/Flowfin/jellyfin-plugin-stats/actions/workflows/test.yaml">
<img alt="Build status" src="https://github.com/Flowfin/jellyfin-plugin-stats/actions/workflows/test.yaml/badge.svg"/>
</a>
<a href="https://github.com/Flowfin/jellyfin-plugin-stats/wiki">
<img alt="Documentation" src="https://img.shields.io/badge/docs-wiki-blue"/>
</a>
<a href="https://securityscorecards.dev/viewer/?uri=github.com/Flowfin/jellyfin-plugin-stats">
<img alt="OpenSSF Scorecard" src="https://api.securityscorecards.dev/projects/github.com/Flowfin/jellyfin-plugin-stats/badge"/>
</a>
</p>

<p align="center">
Playback statistics for a Jellyfin server, recorded by the server itself and
kept private to the person they are about.
</p>

## What it is

The server records one row per finished play into a SQLite file of its own:
what was played, when, from which client and device, how much was watched,
whether it reached the end, and whether the server had to transcode and why.
Nothing leaves the server.

A signed-in person reads their own figures, their own year and their own
consent, and deletes their own history. An administrator reads the server-wide
reports, which name nobody. The one exception is the server's year in review,
where an account is named only if that account itself recorded its agreement to
be named. No route hands one account another account's rows, and an
administrator is refused there like anybody else.

Today those answers are served over the server's API and no statistics page is
drawn in the dashboard. The views are built and are not shown yet: the
dashboard translates a plugin page before it inserts it, which breaks the code
the views are written in, so the plugin declares its settings page and nothing
else until it serves that code itself. That is release 0.2.0.0, issues
[#335](https://github.com/Flowfin/jellyfin-plugin-stats/issues/335) and
[#336](https://github.com/Flowfin/jellyfin-plugin-stats/issues/336), and the
page a person opens about themselves is
[#337](https://github.com/Flowfin/jellyfin-plugin-stats/issues/337).

## What it is not

- Not a recap application that wants a key over the whole server.
- No custom query endpoint. Every request chooses from a closed set of shapes.
- No call to anything outside the server. No network address, no user agent and
  no library file path is stored.
- No elevated route to one person's history. An administrator cannot read, and
  cannot record, what an account said about being named.

## Installing

Distribution is through Flowfin's own plugin manifest rather than the official
catalogue. Add one address under **Dashboard > Plugins > Repositories**:

```text
https://flowfin.dev/manifest.json
```

Then find **Playback Statistics** under **Dashboard > Plugins > Catalog**,
install it, and **restart Jellyfin**. One manifest carries every Flowfin
plugin, so a server that has the address for one of them already has it for
this one.

Two server lines are served from that one address, and the server takes the
archive matching the line it is on: **Jellyfin 10.11** on .NET 9, whose version
stream starts at `0.1.0.0`, and **Jellyfin 12.0** on .NET 10, whose stream
starts at `1.0.0.0`. **The leading number says which server line a release is
for and not how finished the plugin is.** The 12.0 line has published nothing
yet.

Upgrading, uninstalling and what removing the plugin deletes are on the
[Installation](https://github.com/Flowfin/jellyfin-plugin-stats/wiki/Installation)
wiki page.

## Configuration

The settings page is at **Dashboard > Plugins > Playback Statistics**: whether
plays are recorded at all, which accounts and item types are left out, how long
a raw play row and how long a daily aggregate are kept, the zone a day is
counted in, and two caps on what a report may ask for. The two retention
windows delete rather than hide, and one of the two deletions cannot be undone.
The
[Configuration](https://github.com/Flowfin/jellyfin-plugin-stats/wiki/Configuration)
wiki page says what each setting accepts and what changing it does not do.

## Documentation

Full documentation is in the
**[Wiki](https://github.com/Flowfin/jellyfin-plugin-stats/wiki)**:

- [Installation](https://github.com/Flowfin/jellyfin-plugin-stats/wiki/Installation),
  [Pages](https://github.com/Flowfin/jellyfin-plugin-stats/wiki/Pages),
  [Configuration](https://github.com/Flowfin/jellyfin-plugin-stats/wiki/Configuration),
  [Troubleshooting](https://github.com/Flowfin/jellyfin-plugin-stats/wiki/Troubleshooting).
- [What is stored and who can read it](https://github.com/Flowfin/jellyfin-plugin-stats/wiki/What-is-stored-and-who-can-read-it),
  [Privacy, consent and deletion](https://github.com/Flowfin/jellyfin-plugin-stats/wiki/Privacy-consent-and-deletion),
  [Transcode reasons](https://github.com/Flowfin/jellyfin-plugin-stats/wiki/Transcode-reasons).
- [Support matrix and releases](https://github.com/Flowfin/jellyfin-plugin-stats/wiki/Support-matrix-and-releases),
  [Release process](https://github.com/Flowfin/jellyfin-plugin-stats/wiki/Release-process),
  [Changelog](CHANGELOG.md).

The checked detail stays in this repository, because the test suite reads it:
[configuration](docs/configuration.md),
[what is stored](docs/what-is-stored.md),
[the support matrix](docs/support-matrix.md),
[transcode reasons](docs/transcode-reasons.md),
[where the data lives](docs/plugin-data.md),
[what the log contains](docs/what-the-log-contains.md).

## Privacy and security

Statistics respect each user's privacy: personal detail is readable only by the
account it is about, server-wide answers name nobody, and an account appears by
name in the server's year in review only where that account recorded its own
agreement. The server log carries identifiers and never a user name or an item
title, and the store deliberately holds no network address, no user agent and
no file path.

Found a vulnerability? Please report it **privately** through GitHub's
["Report a vulnerability"](https://github.com/Flowfin/jellyfin-plugin-stats/security/advisories/new),
not the public issue tracker. [SECURITY.md](SECURITY.md) says what is in scope
and what is not.

## Contributing

Issues and pull requests are welcome. Building needs the .NET SDK for the line
you are building against, .NET 9 for Jellyfin 10.11 and .NET 10 for Jellyfin
12.0, and the test suite runs on both, so the .NET 10 SDK is what a full run
takes. Node 24 runs the page module suite.

```text
dotnet build
dotnet test
npm test
```

## Licence

GPL-3.0-or-later, in [LICENSE](LICENSE). Jellyfin's own libraries are GPLv3, so
a plugin linked against them is GPLv3 once compiled. See [NOTICE.md](NOTICE.md)
for the intended-use notice.
