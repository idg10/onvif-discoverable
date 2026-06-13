using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace OnvifDiscoverable.Server;

/// <summary>
/// Describes a WS-Discovery Probe request message. Use <see cref="TryParse(string, out OnvifDiscoverable.Server.WsDiscoveryProbeRequest?)"/>
/// to parse a request from an XML string.
/// </summary>
internal class WsDiscoveryProbeRequest
{
    private static readonly XNamespace SoapNs = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace WsaNs  = "http://schemas.xmlsoap.org/ws/2004/08/addressing";
    private static readonly XNamespace WsdNs  = "http://schemas.xmlsoap.org/ws/2005/04/discovery";

    private static readonly XName SoapEnvelopeName = SoapNs.GetName("Envelope");
    private static readonly XName SoapHeaderName   = SoapNs.GetName("Header");
    private static readonly XName SoapBodyName     = SoapNs.GetName("Body");
    private static readonly XName WsaToName        = WsaNs.GetName("To");
    private static readonly XName WsaActionName    = WsaNs.GetName("Action");
    private static readonly XName WsaMessageIdName = WsaNs.GetName("MessageID");
    private static readonly XName WsdProbeName     = WsdNs.GetName("Probe");
    private static readonly XName WsdTypesName     = WsdNs.GetName("Types");

    public string MessageId { get; }
    public XName Type { get; }

    private WsDiscoveryProbeRequest(string messageId, XName type)
    {
        MessageId = messageId;
        Type = type;
    }

    public static bool TryParse(
        string xml,
        [NotNullWhen(true)] out WsDiscoveryProbeRequest? request)
    {
        request = null;

        XDocument doc = XDocument.Parse(xml);
        XElement? envelope = doc.Element(SoapEnvelopeName);
        XElement? header = envelope?.Element(SoapHeaderName);
        XElement? body = envelope?.Element(SoapBodyName);

        if (header is null || body is null)
        {
            Console.WriteLine("Probe does not appear to be a SOAP request");
            return false;
        }

        if (header.Element(WsaToName)?.Value != "urn:schemas-xmlsoap-org:ws:2005:04:discovery")
        {
            Console.WriteLine("Probe does not appear to be a WS-Discovery request: expected wsa:To value not present");
            return false;
        }

        if (header.Element(WsaActionName)?.Value != "http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe")
        {
            Console.WriteLine("Probe does not appear to be a WS-Discovery Probe request: expected wsa:Action value not present");
            return false;
        }

        if (header.Element(WsaMessageIdName)?.Value is not string messageId)
        {
            Console.WriteLine("Probe does not appear to be a WS-Discovery Probe request: no wsa:MessageID");
            return false;
        }

        if (body.Element(WsdProbeName) is not XElement probeElement)
        {
            Console.WriteLine("Probe does not appear to be a WS-Discovery Probe request: no wsd:Probe");
            return false;
        }

        if (probeElement.Element(WsdTypesName) is not XElement typesElement)
        {
            Console.WriteLine("Probe does not appear to be a WS-Discovery Probe request: no wsd:Types");
            return false;
        }

        if (typesElement.Value.Split(':') is not [string prefix, string localName]
            || typesElement.GetNamespaceOfPrefix(prefix) is not XNamespace typeNs)
        {
            Console.WriteLine("Probe wsd:Types is not a valid qualified name");
            return false;
        }

        request = new WsDiscoveryProbeRequest(messageId, typeNs.GetName(localName));
        return true;
    }
}
