using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace OnvifDiscoverable.Server;

class WsDiscoveryListener(OnvifDeviceDescription device)
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.255.255.250");
    private const int Port = 3702;

    private static readonly XNamespace OnvifNwNs = "http://www.onvif.org/ver10/network/wsdl";
    private static readonly XName NetworkVideoTransmitter = OnvifNwNs.GetName("NetworkVideoTransmitter");

    // InstanceId identifies this run of the process; clients use it to detect restarts.
    private readonly uint _instanceId = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private uint _messageNumber = 0;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
        udp.JoinMulticastGroup(MulticastAddress);

        try
        {
            while (true)
            {
                var result = await udp.ReceiveAsync(cancellationToken);
                string requestXml = Encoding.UTF8.GetString(result.Buffer);

                Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] Received {result.Buffer.Length} bytes from {result.RemoteEndPoint}");

                if (!requestXml.Contains("Probe"))
                {
                    Console.WriteLine("  (not a Probe — ignoring)");
                    continue;
                }

                if (!WsDiscoveryProbeRequest.TryParse(requestXml, out WsDiscoveryProbeRequest? probe))
                {
                    Console.WriteLine("  Probe failed to parse:");
                    Console.WriteLine(requestXml);
                    continue;
                }

                Console.WriteLine($"  Probe MessageID: {probe.MessageId}");

                if (probe.Types.Count > 0 && !probe.Types.Contains(NetworkVideoTransmitter))
                {
                    Console.WriteLine($"  Ignoring Probe for types [{string.Join(", ", probe.Types)}] — not NetworkVideoTransmitter");
                    continue;
                }
                else
                {
                    Console.WriteLine($"  Probe types: [{string.Join(", ", probe.Types)}]");
                }

                var probeMatch = new WsDiscoveryProbeMatch
                {
                    EndpointAddress = device.EndpointAddress,
                    Types = device.Types,
                    Scopes = device.Scopes,
                    XAddrs = [device.XAddrs],
                };

                var response = probeMatch.ToXml(probe.MessageId, _instanceId, ++_messageNumber);
                var bytes = Encoding.UTF8.GetBytes(response);
                await udp.SendAsync(bytes.AsMemory(), result.RemoteEndPoint, cancellationToken);

                Console.WriteLine($"  Sent ProbeMatch ({bytes.Length} bytes, MessageNumber {_messageNumber}) to {result.RemoteEndPoint}:");
                Console.WriteLine(response);
                Console.WriteLine("  ---");
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested via token — normal shutdown path.
        }
    }
}
