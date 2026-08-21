# Changelog

## Where the version lives

`build.yaml` is the one tracked file that writes the version as a literal. It is
the file the plugin package carries to a server and to a catalogue, so it is the
one a wrong number is most expensive in. `Directory.Build.props` reads the number
back out of it, and the assembly, file and package versions all come from there:

    dotnet build Jellyfin.Plugin.Stats/Jellyfin.Plugin.Stats.csproj \
      -getProperty:Version -getProperty:AssemblyVersion -getProperty:FileVersion

Bump the version in `build.yaml` and add the entry here in the same change.
Nothing in the tree refuses a bump that leaves this file untouched. That is a
habit, not a gate, and it will stay one until a check is written for it.

## Unreleased

Nothing has been released from this repository. Entries collect here until the
first release exists.

- The 10.11 line's version stream starts at `0.1.0.0`, which `build.yaml` now
  carries in place of `0.0.0.0`. The 12.0 line's stream starts at `1.0.0.0`, so
  the leading number says which server line a release is for rather than how
  settled the plugin is, and `README.md` says so where a reader meets the
  number. Nothing is released by this: the number is raised before a tag exists
  because a release deleted to correct its number burns that tag permanently.

- The version is written once, in `build.yaml`, and read from there by the
  build. It was previously a literal in two files that disagreed with each
  other. The number the compiled assembly already carried is the one kept,
  because no release has been made and the higher number in the package
  manifest advertised one that does not exist.

- The plugin builds for both supported server lines: 10.11 on .NET 9 and 12.0
  on .NET 10, each against the Jellyfin packages that line publishes. It
  previously targeted one framework, compiled against a 10.9 server and
  declared a target abi no supported server has. Packaging now produces one
  package per line and reads the abi back out of each zip, so a package
  carrying the wrong line's abi fails the run instead of reaching a server.
