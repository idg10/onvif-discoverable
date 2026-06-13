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

            if (probe.Type != NetworkVideoTransmitter)
            {
                Console.WriteLine($"WS-Discovery Probe request: ignoring request for {probe.Type}");
                continue;
            }

            var response = BuildProbeMatchResponse(probe.MessageId);
            var bytes = Encoding.UTF8.GetBytes(response);
            await udp.SendAsync(bytes, bytes.Length, result.RemoteEndPoint);
        }
    }

    private string BuildProbeMatchResponse(string incomingMessageId)
    {
        string messageId = $"uuid:{Guid.NewGuid()}";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing"
                        xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
                        xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
              <e:Header>
                <w:MessageID>{messageId}</w:MessageID>
                <w:RelatesTo>{incomingMessageId}</w:RelatesTo>
                <w:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
                <w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches</w:Action>
              </e:Header>
              <e:Body>
                <d:ProbeMatches>
                  <d:ProbeMatch>
                    <w:EndpointReference>
                      <w:Address>{device.EndpointAddress}</w:Address>
                    </w:EndpointReference>
                    <d:Types>{device.Types}</d:Types>
                    <d:Scopes>{device.Scopes}</d:Scopes>
                    <d:XAddrs>{device.XAddrs}</d:XAddrs>
                    <d:MetadataVersion>1</d:MetadataVersion>
                  </d:ProbeMatch>
                </d:ProbeMatches>
              </e:Body>
            </e:Envelope>
            """;
    }
}
