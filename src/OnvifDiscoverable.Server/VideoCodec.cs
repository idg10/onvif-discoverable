namespace OnvifDiscoverable.Server;

/// <summary>
/// The video codec the device advertises over ONVIF. This must match what the RTSP stream at
/// <see cref="OnvifDeviceDescription.RtspStreamUri"/> actually carries — a strict client
/// (e.g. Windows) sets up a decoder for the advertised codec and produces no picture if the
/// wire format differs.
/// </summary>
enum VideoCodec
{
    /// <summary>H.264 (advertised as ONVIF encoding <c>H264</c>).</summary>
    H264,

    /// <summary>Motion JPEG (advertised as ONVIF encoding <c>JPEG</c>).</summary>
    Mjpeg,
}
