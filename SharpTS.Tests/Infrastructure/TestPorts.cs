using System.Net;
using System.Net.Sockets;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Ephemeral-port allocation for server tests. One implementation replaces the
/// byte-identical private copies that had spread across the HTTP test family
/// (2026-07 cleanup) — including the bind-release-rebind race they all share,
/// which now has a single place to fix.
/// </summary>
public static class TestPorts
{
    public static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
