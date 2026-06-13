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
        using var udp = new UdpClient(Port);
        udp.JoinMulticastGroup(MulticastAddress);

        try
        {
            while (true)
            {
                var result = await udp.ReceiveAsync(cancellationToken);
                string requestXml = Encoding.UTF8.GetString(result.Buffer);

                Console.WriteLine("Received request");
                //Console.WriteLine(requestXml);

                if (!requestXml.Contains("Probe"))
                {
                    continue;
                }

                Console.WriteLine("Received Probe");

                if (!WsDiscoveryProbeRequest.TryParse(requestXml, out WsDiscoveryProbeRequest? probe))
                {
                    continue;
                }

                if (probe.Types.Count > 0 && !probe.Types.Contains(NetworkVideoTransmitter))
                {
                    Console.WriteLine($"WS-Discovery Probe request: ignoring request for [{string.Join(", ", probe.Types)}]");
                    continue;
                }
                else
                {
                    Console.WriteLine($"WS-Discovery Probe request types: [{string.Join(", ", probe.Types)}]");
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
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested via token — normal shutdown path.
        }
    }
}
