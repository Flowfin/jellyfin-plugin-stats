// Near miss for no-test-that-needs-a-display-elevation-or-a-certificate-store.
//
// An endpoint test starts to fail against https. The fix that takes one line is
// the one the framework's own getting-started page suggests, and on a
// developer's machine it is silent because the certificate is already trusted.
// On a runner it changes the machine, and on Windows it raises a consent
// prompt in front of whoever is sitting there.

[OneTimeSetUp]
public void TrustTheDevelopmentCertificate()
{
    Process.Start("dotnet", "dev-certs https --trust").WaitForExit();
}
