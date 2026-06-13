using System.Xml.Linq;

namespace OnvifDiscoverable.Server;

/// <summary>
/// Represents a single <c>d:ProbeMatch</c> entry in a WS-Discovery ProbeMatches response,
/// describing a device that matched an incoming Probe request.
/// </summary>
internal class WsDiscoveryProbeMatch
{
    // Maps namespaces to the conventional prefixes used in WS-Discovery / ONVIF responses.
    private static readonly Dictionary<XNamespace, string> KnownPrefixes = new()
    {
        [XNamespace.Get("http://www.onvif.org/ver10/network/wsdl")] = "dn",
    };

    /// <summary>
    /// The WS-Addressing endpoint reference URI that uniquely identifies this device
    /// (serialised as <c>wsa:EndpointReference/wsa:Address</c>).
    /// </summary>
    public required string EndpointAddress { get; init; }

    /// <summary>
    /// The device types advertised by this endpoint, expressed as qualified names.
    /// Serialised as a space-separated list of <c>prefix:LocalName</c> tokens in
    /// <c>d:Types</c>, per the WS-Discovery spec (<c>list of xs:QName</c>).
    /// An empty list omits the element from the response.
    /// </summary>
    public IReadOnlyList<XName> Types { get; init; } = [];

    /// <summary>
    /// The administrative scopes this device belongs to, expressed as URIs.
    /// Serialised as a space-separated list in <c>d:Scopes</c>,
    /// per the WS-Discovery spec (<c>list of xs:anyURI</c>).
    /// An empty list omits the element from the response.
    /// </summary>
    public IReadOnlyList<Uri> Scopes { get; init; } = [];

    /// <summary>
    /// The transport addresses at which this device can be reached, expressed as URIs.
    /// Serialised as a space-separated list in <c>d:XAddrs</c>,
    /// per the WS-Discovery spec (<c>list of xs:anyURI</c>).
    /// An empty list omits the element from the response.
    /// </summary>
    public IReadOnlyList<Uri> XAddrs { get; init; } = [];

    /// <summary>
    /// The metadata version number, incremented whenever the device's metadata changes.
    /// Serialised as <c>d:MetadataVersion</c>. Defaults to <c>1</c>.
    /// </summary>
    public uint MetadataVersion { get; init; } = 1;

    /// <summary>
    /// Serialises this match as a complete WS-Discovery ProbeMatches SOAP envelope.
    /// </summary>
    /// <param name="relatesTo">
    /// The <c>wsa:MessageID</c> from the incoming Probe request, used to populate
    /// <c>wsa:RelatesTo</c> in the response header.
    /// </param>
    /// <returns>A UTF-8 XML string containing the ProbeMatches SOAP envelope.</returns>
    public string ToXml(string relatesTo)
    {
        string messageId = $"uuid:{Guid.NewGuid()}";
        string typesXml  = string.Join(" ", Types.Select(SerializeQName));
        string scopesXml = string.Join(" ", Scopes);
        string xAddrsXml = string.Join(" ", XAddrs);

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing"
                        xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
                        xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
              <e:Header>
                <w:MessageID>{messageId}</w:MessageID>
                <w:RelatesTo>{relatesTo}</w:RelatesTo>
                <w:To>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</w:To>
                <w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches</w:Action>
              </e:Header>
              <e:Body>
                <d:ProbeMatches>
                  <d:ProbeMatch>
                    <w:EndpointReference>
                      <w:Address>{EndpointAddress}</w:Address>
                    </w:EndpointReference>
                    <d:Types>{typesXml}</d:Types>
                    <d:Scopes>{scopesXml}</d:Scopes>
                    <d:XAddrs>{xAddrsXml}</d:XAddrs>
                    <d:MetadataVersion>{MetadataVersion}</d:MetadataVersion>
                  </d:ProbeMatch>
                </d:ProbeMatches>
              </e:Body>
            </e:Envelope>
            """;
    }

    private static string SerializeQName(XName name)
    {
        if (!KnownPrefixes.TryGetValue(name.Namespace, out string? prefix))
        {
            throw new InvalidOperationException($"No known prefix for namespace '{name.Namespace}'");
        }

        return $"{prefix}:{name.LocalName}";
    }
}
