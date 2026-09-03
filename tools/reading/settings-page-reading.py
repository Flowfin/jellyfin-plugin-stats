"""Take one reading of what the plugin settings page receives from a server.

This is a READING and not a test. It gates nothing, it is run by hand, and
what it produces is output somebody pastes into an issue. It needs a running
server, which the headless policy refuses every test - the departure is
declared in docs/headless-tests.md rather than taken here in silence.

What it answers, and it is one question: two properties of the plugin
configuration have a getter and no setter, are held out of the stored file,
and are read by the settings page. Nothing has ever observed that such a
property arrives in what the page receives. The reading drives a real server
over its own API, in the order the page does:

  1. finish the first-run wizard, so there is an account to ask with
  2. read the plugin configuration the way the page reads it
  3. write it back the way the page writes it
  4. read the stored file the server wrote
  5. read the configuration again

The two names have to be absent from step 4 and present in steps 2 and 5. If
they are present in step 4 the claim they are held out of the file is wrong;
if they are absent from 2 or 5 the settings page has been drawing a value the
server never sends, and that is the first thing to repair rather than the
thing issue #31 builds on.

Every request goes to the address given on the command line, which is a
server this run started. Nothing here reads the network otherwise. The
steps every reading takes first - waiting for the server, the wizard, the
sign-in - are in jellyfin_server.py beside this file.
"""

import json
import sys

from jellyfin_server import call, finish_the_wizard, sign_in, wait_for_the_server

# The plugin's own identifier, as Plugin.cs declares it.
PLUGIN_ID = "29e90267-52ee-4bec-b4fb-870b8f5ddc53"

# The two properties this reading is about. Both have a getter and no setter
# and both are marked so the stored file never carries them.
GETTER_ONLY = ("WhyTheStoreCouldNotBeOpened", "OldestStoredPlay")

# A setting with an ordinary getter and setter, read alongside the two above
# so that a missing answer can be told from a server that sent nothing at all.
A_STORED_SETTING = "CaptureEnabled"

def report(name, present, where):
    print("  {0:<30} {1} in {2}".format(name, "PRESENT" if present else "absent ", where))


def main():
    base = sys.argv[1].rstrip("/")
    stored_file = sys.argv[2] if len(sys.argv) > 2 else ""

    public = wait_for_the_server(base, 240)
    if public is None:
        return 1

    if not public.get("StartupWizardCompleted"):
        print("finishing the first-run wizard")
        if not finish_the_wizard(base):
            return 1

    token = sign_in(base)
    if token is None:
        return 1

    path = "/Plugins/{0}/Configuration".format(PLUGIN_ID)

    print("")
    print("== 2. what the settings page receives")
    status, first = call(base, "GET", path, token=token)
    print("GET {0} -> {1}".format(path, status))
    if status != 200 or not isinstance(first, dict):
        print("FAIL the server did not answer with a configuration object: {0}".format(first))
        return 1
    print(json.dumps(first, indent=2, sort_keys=True))

    print("")
    print("== 3. writing it back, the way the page saves")
    status, answer = call(base, "POST", path, first, token=token)
    print("POST {0} -> {1}".format(path, status))
    saved = 0 < status < 400
    if not saved:
        print("the save was refused: {0}".format(answer))

    print("")
    print("== 4. what the server stored")
    stored = ""
    if stored_file:
        try:
            with open(stored_file, encoding="utf-8") as handle:
                stored = handle.read()
        except OSError as failure:
            print("could not read the stored file: {0}".format(failure))
    if stored:
        print(stored)

    print("")
    print("== 5. what the settings page receives, after the save")
    status, second = call(base, "GET", path, token=token)
    print("GET {0} -> {1}".format(path, status))
    if status != 200 or not isinstance(second, dict):
        print("FAIL the server did not answer with a configuration object: {0}".format(second))
        return 1

    print("")
    print("== the reading")
    failed = False
    for name in GETTER_ONLY:
        in_first = name in first
        in_second = name in second
        in_stored = ("<{0}>".format(name) in stored) if stored else None
        report(name, in_first, "the answer before the save")
        report(name, in_second, "the answer after the save")
        if in_stored is None:
            print("  {0:<30} the stored file was not read, nothing is claimed of it".format(name))
        else:
            report(name, in_stored, "the stored file")
        if not in_first or not in_second:
            print("  FAIL {0} does not arrive in what the page receives".format(name))
            failed = True
        if in_stored:
            print("  FAIL {0} is in the stored file, which it is declared not to be".format(name))
            failed = True
        print("  value before the save: {0!r}".format(first.get(name)))
        print("  value after the save:  {0!r}".format(second.get(name)))

    present = A_STORED_SETTING in first
    report(A_STORED_SETTING, present, "the answer before the save")
    if not present:
        print("  FAIL an ordinary stored setting is missing too, so this run says nothing")
        print("       about getter-only properties: it says the answer is not a configuration")
        failed = True

    if not saved:
        print("")
        print("the save was refused, so nothing above is a statement about what a save stores")
        failed = True

    if stored_file and not stored:
        print("")
        print("the stored file was empty or unreadable, so the fourth leg of the reading is absent")
        failed = True

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
