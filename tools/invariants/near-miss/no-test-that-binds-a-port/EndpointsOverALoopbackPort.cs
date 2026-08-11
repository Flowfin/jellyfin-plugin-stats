// Near miss for no-test-that-binds-a-port.
//
// The endpoints need testing and the shortest route there is the one every
// getting-started page shows: stand the plugin up in a real host, let the
// operating system hand out a free port, and call it. It avoids the privileged
// port the elevation rule already refuses, it asks for no rights, and on a
// developer's machine it is quiet, so nothing about it feels like a policy
// question.
//
// What it costs is the property the whole suite is held to. A bound port is a
// resource shared with everything else on the runner: two jobs on one machine
// race for it, a firewall prompt appears on the machine somebody is sitting at,
// and a run that leaves the listener up leaves it up until the process dies.
// The port also buys nothing here. What is being tested is which caller a
// controller lets through, and that answer is the same over a socket as over an
// in-memory transport.
//
// The probe below is the part that reads as careful rather than as a violation:
// asking for port zero and reading back what was granted is exactly how somebody
// avoids a fixed port, so the mistake arrives dressed as the fix for a different
// one.

private IHost? _host;

[OneTimeSetUp]
public async Task StartTheHostTheEndpointTestsCallOver()
{
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();

    _host = Host.CreateDefaultBuilder()
        .ConfigureWebHostDefaults(web => web.UseKestrel().UseUrls($"http://[::1]:{port}"))
        .Build();

    await _host.StartAsync().ConfigureAwait(false);
}
