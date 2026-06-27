using System.Xml.Linq;

namespace OnvifDiscoverable.Server;

record OnvifDeviceDescription
{
    private static readonly XNamespace OnvifNwNs = "http://www.onvif.org/ver10/network/wsdl";

    public required Uri XAddrs { get; init; }
    public required Uri RtspStreamUri { get; init; }
    public required string EndpointAddress { get; init; }

    /// <summary>
    /// The video codec advertised in the media service responses. Must match what the RTSP
    /// stream actually carries (see <see cref="VideoCodec"/>).
    /// </summary>
    public VideoCodec Codec { get; init; } = VideoCodec.H264;

    /// <summary>
    /// Human-readable name of the device, used to populate the mandatory
    /// <c>onvif://www.onvif.org/name/…</c> scope (Core §7.3.2.2).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Hardware model identifier, used to populate the mandatory
    /// <c>onvif://www.onvif.org/hardware/…</c> scope (Core §7.3.2.2).
    /// </summary>
    public required string Hardware { get; init; }

    public IReadOnlyList<XName> Types { get; init; } = [OnvifNwNs.GetName("NetworkVideoTransmitter")];

    /// <summary>
    /// Assembled from the mandatory Profile/Streaming, name, and hardware scopes.
    /// Additional scopes may be appended via <see cref="AdditionalScopes"/>.
    /// </summary>
    public IReadOnlyList<Uri> Scopes =>
    [
        new Uri("onvif://www.onvif.org/Profile/Streaming"),
        new Uri($"onvif://www.onvif.org/name/{Uri.EscapeDataString(Name)}"),
        new Uri($"onvif://www.onvif.org/hardware/{Uri.EscapeDataString(Hardware)}"),
        .. AdditionalScopes,
    ];

    /// <summary>
    /// Optional extra scopes appended after the mandatory ones.
    /// </summary>
    public IReadOnlyList<Uri> AdditionalScopes { get; init; } = [];
}
