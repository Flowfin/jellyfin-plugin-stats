"""Follow the install route README.md documents on a fresh server, and say what happened.

This is a READING and not a test. It gates nothing, it is run by hand, and
what it produces is output somebody pastes into an issue. It needs a running
server and a port, which the headless policy refuses every test, and the
server it drives reads the published manifest from outside the run, which
that policy refuses too. The departure is declared in docs/headless-tests.md
rather than taken here in silence.

What it answers is issue #81's second condition: that a fresh server can
install this plugin by following the documented route. The route is the two
steps a person takes in the dashboard - add one repository address to the
server's list, then install the plugin from the catalogue that address serves
- and this drives the same two steps over the API the dashboard uses, in two
halves:

  install   1. finish the first-run wizard, so there is an account to ask with
            2. read the repository list a fresh server starts with
            3. add the address to it, and read the list back
            4. read the catalogue and find this plugin in it
            5. ask the server to install it, naming no version, so that the
               server picks the one it would pick for a person
  loaded    6. after the restart an install asks for, read the plugin list
               and find this plugin in it, active, at the version step 4 said
               the server would pick
            7. read the plugin's configuration, which only a loaded plugin's
               own type can answer

Two halves because a plugin installed into a running server is loaded at the
next start, and the restart in between belongs to whatever started the
server: this file has no hand on the container. The first half writes what it
expects into a file and the second half reads it back, so the two runs agree
on what the catalogue offered without either of them remembering it.

What the server does in step 5 is what makes the reading worth taking. It
fetches the archive from the address the catalogue names, hashes it, and
refuses the install when the hash is not the checksum the catalogue carries.
So an install that completes is one where the published archive and the
published checksum agreed at the server, which is a stronger reading than
comparing the two from here.

Every request goes to the address given on the command line, which is a
server this run started. The one other address this reading names is the
manifest, and it is handed to the server rather than read here.
"""

import json
import sys
import urllib.parse

from jellyfin_server import call, finish_the_wizard, sign_in, wait_for_the_server

# The plugin's own identifier and name, as build.yaml declares them. The
# catalogue is searched by identifier and the install is asked for by name,
# because that is the pair the server's install route takes.
PLUGIN_ID = "29e90267-52ee-4bec-b4fb-870b8f5ddc53"
PLUGIN_NAME = "Playback Statistics"

# The name the address is listed under. The server keys nothing on it; it is
# what a person sees in the repository list.
REPOSITORY_NAME = "Flowfin"


def version_tuple(text):
    """Order versions the way the server does: numerically, part by part."""
    try:
        return tuple(int(part) for part in str(text).split("."))
    except ValueError:
        return ()


def same_id(candidate):
    """Compare identifiers the way the server writes them.

    The server's own JSON converter writes every GUID in the form without
    dashes, so the catalogue answers `guid` as thirty-two hex digits and the
    plugin list answers `Id` the same way, while build.yaml and the manifest
    carry the dashed form. The first dispatch of this reading compared the two
    forms as strings, found nothing under this plugin in a catalogue that held
    it, and stopped at step 4 with a FAIL that was its own. Both sides are
    reduced to the digits before they are compared.
    """
    return str(candidate).replace("-", "").lower() == PLUGIN_ID.replace("-", "")


def one_line(plugin):
    return "  {0} {1} status {2}".format(plugin.get("Name"), plugin.get("Version"), plugin.get("Status"))


def start(base):
    """Wait, finish the wizard if it is still open, sign in."""
    public = wait_for_the_server(base, 240)
    if public is None:
        return None, None
    if not public.get("StartupWizardCompleted"):
        print("finishing the first-run wizard")
        if not finish_the_wizard(base):
            return None, None
    token = sign_in(base)
    if token is None:
        return None, None
    return public, token


def install(base, manifest, expected_path):
    public, token = start(base)
    if token is None:
        return 1
    server_version = public.get("Version")

    print("")
    print("== 2. the repository list a fresh server starts with")
    status, repositories = call(base, "GET", "/Repositories", token=token)
    print("GET /Repositories -> {0}".format(status))
    if status != 200 or not isinstance(repositories, list):
        print("FAIL the server did not answer with a repository list: {0}".format(repositories))
        return 1
    for entry in repositories:
        print("  {0!r:<24} {1} enabled={2}".format(entry.get("Name"), entry.get("Url"), entry.get("Enabled")))

    print("")
    print("== 3. adding the address to it")
    if any(entry.get("Url") == manifest for entry in repositories):
        print("FAIL the address is already in the list, so this is not a fresh server")
        return 1
    wanted = list(repositories) + [{"Name": REPOSITORY_NAME, "Url": manifest, "Enabled": True}]
    status, answer = call(base, "POST", "/Repositories", wanted, token=token)
    print("POST /Repositories -> {0}".format(status))
    if not 0 < status < 400:
        print("FAIL the list was not saved: {0}".format(answer))
        return 1
    status, repositories = call(base, "GET", "/Repositories", token=token)
    print("GET /Repositories -> {0}".format(status))
    listed = [entry for entry in repositories or [] if entry.get("Url") == manifest]
    if status != 200 or not listed or not listed[0].get("Enabled"):
        print("FAIL the address is not in the list the server reads back: {0}".format(repositories))
        return 1
    print("  the address is listed, enabled, as {0!r}".format(listed[0].get("Name")))

    print("")
    print("== 4. what the catalogue that address serves says about this plugin")
    status, packages = call(base, "GET", "/Packages", token=token, timeout=120)
    print("GET /Packages -> {0}".format(status))
    if status != 200 or not isinstance(packages, list):
        print("FAIL the server did not answer with a catalogue: {0}".format(packages))
        return 1
    print("  {0} package(s) across every repository the server has".format(len(packages)))
    # The server tags every version with the repository it came from and drops
    # the versions its own version cannot take before it answers, so what is
    # left under this identifier from this address is exactly what the server
    # would offer a person.
    offered = []
    for package in packages:
        if not same_id(package.get("guid")):
            continue
        for version in package.get("versions") or []:
            if version.get("repositoryUrl") == manifest:
                offered.append(version)
    if not offered:
        print(
            "FAIL the catalogue from {0} offers nothing under {1} to a {2} server".format(
                manifest, PLUGIN_ID, server_version
            )
        )
        # What the catalogue does hold under this name and from this address,
        # so that a reader can tell a missing entry from a comparison that
        # missed it, which is the difference the first dispatch could not show.
        for package in packages:
            by_name = str(package.get("name")).lower() == PLUGIN_NAME.lower()
            from_here = any(
                version.get("repositoryUrl") == manifest for version in package.get("versions") or []
            )
            if by_name or from_here:
                print("     held: name {0!r} guid {1!r}".format(package.get("name"), package.get("guid")))
                for version in package.get("versions") or []:
                    print(
                        "       version {0} targetAbi {1} from {2}".format(
                            version.get("version"), version.get("targetAbi"), version.get("repositoryUrl")
                        )
                    )
        return 1
    for version in offered:
        print(
            "  version {0} targetAbi {1} checksum {2}".format(
                version.get("version"), version.get("targetAbi"), version.get("checksum")
            )
        )
        print("    {0}".format(version.get("sourceUrl")))
    newest = max(offered, key=lambda version: version_tuple(version.get("version")))
    print("  the server picks the newest it can take, which is {0}".format(newest.get("version")))
    with open(expected_path, "w", encoding="utf-8") as handle:
        json.dump(
            {
                "version": newest.get("version"),
                "checksum": newest.get("checksum"),
                "sourceUrl": newest.get("sourceUrl"),
                "serverVersion": server_version,
            },
            handle,
            indent=2,
        )

    print("")
    print("== 5. asking the server to install it, naming no version")
    path = "/Packages/Installed/{0}?{1}".format(
        urllib.parse.quote(PLUGIN_NAME),
        urllib.parse.urlencode({"assemblyGuid": PLUGIN_ID, "repositoryUrl": manifest}),
    )
    # The server fetches the archive, hashes it and unpacks it inside this one
    # request, so it is given longer than the others.
    status, answer = call(base, "POST", path, token=token, timeout=300)
    print("POST {0} -> {1}".format(path, status))
    if status == 404:
        print("FAIL the server found nothing to install under that name and identifier")
        return 1
    if not 0 < status < 400:
        print("FAIL the install did not complete: {0}".format(answer))
        print("     a checksum that did not match, or an archive that could not be fetched,")
        print("     fails here, and the server's own log says which")
        return 1
    print("  the server fetched the archive, checked its checksum against the catalogue,")
    print("  and unpacked it; a restart is what loads it")

    print("")
    print("== what the plugin list says before the restart, for the record")
    status, plugins = call(base, "GET", "/Plugins", token=token)
    print("GET /Plugins -> {0}".format(status))
    for plugin in plugins or []:
        if same_id(plugin.get("Id")):
            print(one_line(plugin))
    return 0


def loaded(base, expected_path):
    with open(expected_path, encoding="utf-8") as handle:
        expected = json.load(handle)

    public, token = start(base)
    if token is None:
        return 1
    if public.get("Version") != expected.get("serverVersion"):
        print(
            "FAIL a different server answered: {0} installed, {1} now".format(
                expected.get("serverVersion"), public.get("Version")
            )
        )
        return 1

    print("")
    print("== 6. the plugin list after the restart")
    status, plugins = call(base, "GET", "/Plugins", token=token)
    print("GET /Plugins -> {0}".format(status))
    if status != 200 or not isinstance(plugins, list):
        print("FAIL the server did not answer with a plugin list: {0}".format(plugins))
        return 1
    found = [plugin for plugin in plugins if same_id(plugin.get("Id"))]
    if not found:
        print("FAIL {0} is not in the plugin list at all; what is:".format(PLUGIN_ID))
        for plugin in plugins:
            print(one_line(plugin))
        return 1
    failed = False
    for plugin in found:
        print(one_line(plugin))
        if plugin.get("Status") != "Active":
            print("  FAIL the plugin is listed and is not active")
            failed = True
        if version_tuple(plugin.get("Version")) != version_tuple(expected.get("version")):
            print(
                "  FAIL the catalogue offered {0} and the server loaded {1}".format(
                    expected.get("version"), plugin.get("Version")
                )
            )
            failed = True

    print("")
    print("== 7. the plugin's configuration, which only a loaded plugin's own type answers")
    path = "/Plugins/{0}/Configuration".format(PLUGIN_ID)
    status, configuration = call(base, "GET", path, token=token)
    print("GET {0} -> {1}".format(path, status))
    if status != 200 or not isinstance(configuration, dict):
        print("FAIL the server did not answer with a configuration object: {0}".format(configuration))
        failed = True
    else:
        print("  {0} setting(s) answered".format(len(configuration)))

    print("")
    print("== the reading")
    if failed:
        print("  FAIL a fresh {0} server following the documented route did not end up".format(public.get("Version")))
        print("       with this plugin loaded; the lines above say where it stopped")
        return 1
    print(
        "  a fresh {0} server, given the address, was offered {1} {2} from it,".format(
            public.get("Version"), PLUGIN_NAME, expected.get("version")
        )
    )
    print("  fetched the archive the catalogue named, found its checksum to be the one")
    print("  the catalogue carries, {0},".format(expected.get("checksum")))
    print("  and loaded it at the next start, active, at that version")
    return 0


def main():
    usage = "usage: install <server> <manifest> <expected-file> | loaded <server> <expected-file>"
    if len(sys.argv) < 3 or sys.argv[1] not in ("install", "loaded"):
        print(usage)
        return 2
    base = sys.argv[2].rstrip("/")
    if sys.argv[1] == "install":
        if len(sys.argv) != 5:
            print(usage)
            return 2
        return install(base, sys.argv[3], sys.argv[4])
    if len(sys.argv) != 4:
        print(usage)
        return 2
    return loaded(base, sys.argv[3])


if __name__ == "__main__":
    sys.exit(main())
