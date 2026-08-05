// Near miss for no-test-that-asks-for-elevation.
//
// A test starts a listener on the port the server uses by default, because that
// is the port the fixture's URL already carries. Binding it needs rights the
// runner does not have, and the line that makes it work locally is the one that
// puts a consent dialog in front of whoever is at the machine.

var start = new ProcessStartInfo("dotnet", "run --urls http://localhost:80")
{
    UseShellExecute = true,
    Verb = "runas",
};
