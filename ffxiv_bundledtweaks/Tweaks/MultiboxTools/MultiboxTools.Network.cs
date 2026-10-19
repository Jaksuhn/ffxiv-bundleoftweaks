using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
    private const int TcpPort = 47832;
    private const int BeaconPort = 47833;
    private const int BeaconFreshMs = 5000;
    private static readonly byte[] BeaconBytes = "CBTMBX1"u8.ToArray();

    private IPEndPoint? _beaconEndpoint;
    private long _beaconSeenTick;

    private async Task<bool> TryTcpSessionAsync(CancellationToken ct) {
        var endpoint = ResolveRemoteHub();
        if (endpoint == null)
            return false;

        using var tcp = new TcpClient();
        try {
            await tcp.ConnectAsync(endpoint, ct);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch {
            return false;
        }

        Log($"Connected to multibox hub at {endpoint}");
        try {
            await RunSessionAsync(tcp.GetStream(), tcp: true, ct);
            return true;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            if (!ct.IsCancellationRequested && Config.AllowNetwork)
                Warning(ex, "Multibox client error");
            return true;
        }
    }

    private IPEndPoint? ResolveRemoteHub() {
        lock (_lock) {
            if (_beaconEndpoint != null && Environment.TickCount64 - _beaconSeenTick < BeaconFreshMs)
                return _beaconEndpoint;
        }

        return null;
    }

    private Task RunLanHubAsync(CancellationToken ct)
        => Task.WhenAll(RunTcpHubAsync(ct), RunBeaconAsync(ct));

    private async Task RunTcpHubAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            TcpListener? listener = null;
            try {
                listener = new TcpListener(IPAddress.Any, TcpPort);
                listener.Start();
                Log($"Multibox hub listening on TCP {TcpPort}");
                while (!ct.IsCancellationRequested) {
                    var client = await listener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => ServeTcpClientAsync(client, ct), ct);
                }
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (Exception ex) {
                if (!ct.IsCancellationRequested)
                    Warning(ex, "Multibox TCP hub error");
                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) { break; }
            }
            finally {
                listener?.Stop();
            }
        }
    }

    private async Task ServeTcpClientAsync(TcpClient tcp, CancellationToken ct) {
        try {
            await ServeClientAsync(tcp.GetStream(), ct);
        }
        finally {
            tcp.Dispose();
        }
    }

    private async Task RunBeaconAsync(CancellationToken ct) {
        var payload = new byte[BeaconBytes.Length + 2];
        BeaconBytes.CopyTo(payload, 0);
        BitConverter.TryWriteBytes(payload.AsSpan(BeaconBytes.Length), (ushort)TcpPort);

        using var udp = new UdpClient { EnableBroadcast = true };
        while (!ct.IsCancellationRequested) {
            foreach (var endpoint in BroadcastEndpoints()) {
                try {
                    await udp.SendAsync(payload, endpoint, ct);
                }
                catch (OperationCanceledException) {
                    return;
                }
                catch {
                    // don't let this stop other beacons
                }
            }

            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ListenBeaconAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            UdpClient? udp = null;
            try {
                udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, BeaconPort));
                while (!ct.IsCancellationRequested) {
                    var result = await udp.ReceiveAsync(ct);
                    RememberBeacon(result);
                }
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (Exception ex) {
                if (!ct.IsCancellationRequested)
                    Warning(ex, "Multibox discovery listen failed");
                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) { break; }
            }
            finally {
                udp?.Dispose();
            }
        }
    }

    private void RememberBeacon(UdpReceiveResult result) {
        var buffer = result.Buffer;
        if (buffer.Length < BeaconBytes.Length + 2)
            return;
        if (!buffer.AsSpan(0, BeaconBytes.Length).SequenceEqual(BeaconBytes))
            return;

        var port = BitConverter.ToUInt16(buffer, BeaconBytes.Length);
        if (port == 0)
            return;

        lock (_lock) {
            _beaconEndpoint = new IPEndPoint(result.RemoteEndPoint.Address, port);
            _beaconSeenTick = Environment.TickCount64;
        }
    }

    private static IEnumerable<IPEndPoint> BroadcastEndpoints() {
        yield return new IPEndPoint(IPAddress.Broadcast, BeaconPort);

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var address in nic.GetIPProperties().UnicastAddresses) {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.IPv4Mask is not { } mask)
                    continue;

                var ip = address.Address.GetAddressBytes();
                var bits = mask.GetAddressBytes();
                if (ip.Length != 4 || bits.Length != 4)
                    continue;

                for (var i = 0; i < 4; i++)
                    ip[i] = (byte)(ip[i] | ~bits[i]);
                yield return new IPEndPoint(new IPAddress(ip), BeaconPort);
            }
        }
    }
}
