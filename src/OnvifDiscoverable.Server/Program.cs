using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

var multicastAddress = IPAddress.Parse("239.255.255.250");
var port = 3702;

using var udp = new UdpClient(port);
udp.JoinMulticastGroup(multicastAddress);

string soapNs = "http://www.w3.org/2003/05/soap-envelope";
string wsaNs = "http://schemas.xmlsoap.org/ws/2004/08/addressing";
string wdsNs = "http://schemas.xmlsoap.org/ws/2005/04/discovery";
string onvifNwNs = "http://www.onvif.org/ver10/network/wsdl";
XName soapEnvelopeName = XName.Get("Envelope", soapNs);
XName soapHeaderName = XName.Get("Header", soapNs);
XName wsaToName = XName.Get("To", wsaNs);
XName wsaActionName = XName.Get("Action", wsaNs);
XName wsaMessageIdName = XName.Get("MessageID", wsaNs);
XName soapBodyName = XName.Get("Body", soapNs);
XName wsdProbe = XName.Get("Probe", wdsNs);
XName wsdTypes = XName.Get("Types", wdsNs);

Console.WriteLine("WS-Discovery responder running...");

while (true)
{
    var result = await udp.ReceiveAsync();
    string requestXml = Encoding.UTF8.GetString(result.Buffer);

    Console.WriteLine("Received request:");
    Console.WriteLine(requestXml);

    if (requestXml.Contains("Probe"))
    {
        Console.WriteLine("Received Probe");

        XDocument requestDoc = XDocument.Parse(requestXml);
        XElement? envelope = requestDoc.Element(soapEnvelopeName);
        XElement? header = envelope?.Element(soapHeaderName);
        XElement? body = envelope?.Element(soapBodyName);
        if (header is null || body is null)
        {
            Console.WriteLine("Probe does not appear to be a SOAP request");
        }
        else
        {
            if (header.Element(wsaToName)?.Value != "urn:schemas-xmlsoap-org:ws:2005:04:discovery")
            {
                Console.WriteLine("Probe does not appear to be a WS-Discovery request: expected wsa:To value not present");
            }
            else if (header.Element(wsaActionName)?.Value != "http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe")
            {
                Console.WriteLine("Probe does not appear to be a WS-Discovery Probe request: expected wsa:Action value not present");
            }
            else if (header.Element(wsaMessageIdName)?.Value is not string messageId)
            {
                Console.WriteLine("Probe does not appear to be a WS-Discovery Probe request: no wsa:MessageID");
            }
            else if (body.Element(wsdProbe) is not XElement probeElement)
            {
                Console.WriteLine("Probe does not appear to be a WS-Discovery Probe request: no wsd:Probe");
            }
            else if (probeElement.Element(wsdTypes) is not XElement typesElement)
            {
                Console.WriteLine("Probe does not appear to be a WS-Discovery Probe request: no wsd:Types");
            }
            else if (typesElement.Value.Split(':') is not [string prefix, string localName]
                || typesElement.GetNamespaceOfPrefix(prefix) != onvifNwNs
                || localName != "NetworkVideoTransmitter")
            {
                Console.WriteLine("WS-Discovery Probe request: is not for NetworkVideoTransmitter");
            }
            else
            {
                var response = BuildProbeMatchResponse(messageId);
                var bytes = Encoding.UTF8.GetBytes(response);

                await udp.SendAsync(bytes, bytes.Length, result.RemoteEndPoint);
            }
        }
    }
}


// TBD: not sure what this is.
const string endpointReference = "urn:uuid:314ba71f-a192-4054-9436-e19eb037f074";

string BuildProbeMatchResponse(string incomingMessageId)
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
            <w:Action>
              http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches
            </w:Action>
          </e:Header>
          <e:Body>
            <d:ProbeMatches>
              <d:ProbeMatch>
                <w:EndpointReference>
                  <w:Address>{endpointReference}</w:Address>
                </w:EndpointReference>
                <d:Types>dn:NetworkVideoTransmitter</d:Types>
                <d:Scopes>
                  onvif://www.onvif.org/Profile/Streaming
                </d:Scopes>
                <d:XAddrs>
                  http://YOUR_IP:YOUR_PORT/onvif/device_service
                </d:XAddrs>
                <d:MetadataVersion>1</d:MetadataVersion>
              </d:ProbeMatch>
            </d:ProbeMatches>
          </e:Body>
        </e:Envelope>
        """;
}
