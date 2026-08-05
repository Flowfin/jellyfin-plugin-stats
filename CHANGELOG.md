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

- The version is written once, in `build.yaml`, and read from there by the
  build. It was previously a literal in two files that disagreed with each
  other. The number the compiled assembly already carried is the one kept,
  because no release has been made and the higher number in the package
  manifest advertised one that does not exist.
