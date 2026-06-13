using System.Xml.Linq;

namespace OnvifDiscoverable.Server;

record OnvifDeviceDescription
{
    private static readonly XNamespace OnvifNwNs = "http://www.onvif.org/ver10/network/wsdl";

    public required Uri XAddrs { get; init; }
    public required string EndpointAddress { get; init; }
    public IReadOnlyList<XName> Types { get; init; } = [OnvifNwNs.GetName("NetworkVideoTransmitter")];
    public IReadOnlyList<Uri> Scopes { get; init; } = [new Uri("onvif://www.onvif.org/Profile/Streaming")];
}
