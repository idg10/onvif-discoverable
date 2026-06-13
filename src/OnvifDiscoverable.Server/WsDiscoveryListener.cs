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

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient(Port);
        udp.JoinMulticastGroup(MulticastAddress);

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await udp.ReceiveAsync(cancellationToken);
            string requestXml = Encoding.UTF8.GetString(result.Buffer);

            Console.WriteLine("Received request:");
            Console.WriteLine(requestXml);

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

            var probeMatch = new WsDiscoveryProbeMatch
            {
                EndpointAddress = device.EndpointAddress,
                Types = device.Types,
                Scopes = device.Scopes,
                XAddrs = [device.XAddrs],
            };

            var response = probeMatch.ToXml(probe.MessageId);
            var bytes = Encoding.UTF8.GetBytes(response);
            await udp.SendAsync(bytes, bytes.Length, result.RemoteEndPoint);
        }
    }
}
