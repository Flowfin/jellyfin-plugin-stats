"""What every reading here does before it asks its own question.

A reading drives a real server over the server's own API, and its first steps
are the same whichever question it asks: wait until the server answers with
its version, finish the first-run wizard so that there is an account to ask
with, and sign in as that account. Those steps live here once and each reading
imports them, so a lesson one dispatch taught - a reset that arrives as an
operating-system error, a wizard route that answers 404 until the user has
been asked for - is learned by every reading and not only by the one that
paid for it.

Nothing here is a test, and nothing here decides anything: a reading calls
these, reads what comes back, and says what it read. Every request goes to the
address the reading was given, which is a server that run started. Nothing
here reads the network otherwise.
"""

import http.client
import json
import time
import urllib.error
import urllib.request

ACCOUNT = "reading"
SECRET = "a-password-this-run-invented"

# The header a Jellyfin client sends on every request. The server refuses a
# request carrying no client identity, so a reading has to look like a client
# even where it asks a question no client asks.
CLIENT = (
    'MediaBrowser Client="reading", Device="hosted-runner", '
    'DeviceId="reading", Version="1.0.0"'
)


def call(base, method, path, body=None, token=None, timeout=30):
    """Make one request and return its status and decoded answer."""
    url = base + path
    data = None
    headers = {"Accept": "application/json"}
    authorization = CLIENT
    if token:
        authorization = authorization + ', Token="{0}"'.format(token)
    headers["Authorization"] = authorization
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=timeout) as answer:
            raw = answer.read().decode("utf-8")
            return answer.status, (json.loads(raw) if raw.strip() else None)
    except urllib.error.HTTPError as failure:
        # A body is truncated because a server that is still starting answers
        # with a whole HTML page, and a page pasted into a log buries the one
        # line that says what happened.
        body = failure.read().decode("utf-8", "replace")
        return failure.code, body[:400]
    except (OSError, http.client.HTTPException, ValueError) as failure:
        # A server that has bound its port and is not yet answering resets the
        # connection rather than refusing it, and the reset arrives as an
        # ordinary operating-system error rather than as the request library's
        # own. Catching only the latter turned the wait below into a single
        # attempt that raised, which is what the first dispatch of the settings
        # page reading did. Everything a failed request can raise is reported
        # as status zero, so the caller decides whether to wait or to give up.
        return 0, "{0}: {1}".format(type(failure).__name__, failure)


def wait_for_the_server(base, seconds):
    """Ask the public endpoint until the server has finished starting.

    A 200 is not the condition. A server that has bound its port and is still
    assembling itself answers this endpoint and serves its startup page from
    the rest, so the first dispatch that got past the reset went straight on to
    a wizard step that came back 503 with a page of HTML. What separates the
    two states is whether the answer carries the server's version, so that is
    what is waited for.
    """
    started = time.monotonic()
    while time.monotonic() - started < seconds:
        status, answer = call(base, "GET", "/System/Info/Public")
        if status == 200 and isinstance(answer, dict) and answer.get("Version"):
            print(
                "server answered after {0:.0f}s: version {1}, wizard completed {2}".format(
                    time.monotonic() - started,
                    answer.get("Version"),
                    answer.get("StartupWizardCompleted"),
                )
            )
            return answer
        time.sleep(2)
    print("FAIL the server did not answer /System/Info/Public within {0}s".format(seconds))
    return None


def finish_the_wizard(base):
    """Walk the first-run wizard, which is what creates an account to ask with."""
    steps = (
        (
            "POST",
            "/Startup/Configuration",
            {
                "UICulture": "en-US",
                "MetadataCountryCode": "US",
                "PreferredMetadataLanguage": "en",
            },
        ),
        # Asking for the first user is what creates one. The wizard's own
        # client reads this before it writes, and the write answers 404
        # without it, because naming a user it cannot find is what that route
        # reports as not found. The third dispatch of the settings page
        # reading stopped exactly here.
        ("GET", "/Startup/User", None),
        ("POST", "/Startup/User", {"Name": ACCOUNT, "Password": SECRET}),
        (
            "POST",
            "/Startup/RemoteAccess",
            {"EnableRemoteAccess": True, "EnableAutomaticPortMapping": False},
        ),
        ("POST", "/Startup/Complete", None),
    )
    for method, path, body in steps:
        status, answer = call(base, method, path, body)
        print("  {0} {1} -> {2}".format(method, path, status))
        if status >= 400 or status == 0:
            print("  FAIL {0}".format(answer))
            return False
    return True


def sign_in(base):
    """Authenticate as the account the wizard created."""
    status, answer = call(
        base, "POST", "/Users/AuthenticateByName", {"Username": ACCOUNT, "Pw": SECRET}
    )
    if status != 200 or not isinstance(answer, dict):
        print("FAIL authentication answered {0}: {1}".format(status, answer))
        return None
    print("signed in as the account the wizard created")
    return answer.get("AccessToken")
