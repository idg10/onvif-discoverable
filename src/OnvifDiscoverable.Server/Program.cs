using OnvifDiscoverable.Server;

if (args.Length is < 4 or > 5)
{
    Console.Error.WriteLine("Usage: OnvifDiscoverable.Server <xaddrs-url> <rtsp-url> <name> <hardware> [codec]");
    Console.Error.WriteLine("  codec: h264 (default) or mjpeg — must match what the RTSP stream actually carries");
    Console.Error.WriteLine("  e.g. OnvifDiscoverable.Server http://192.168.1.10:8080/onvif/device_service rtsp://192.168.1.10:8554/stream \"My Camera\" \"Acme Model X\" h264");
    return 1;
}

VideoCodec codec;
switch ((args.Length == 5 ? args[4] : "h264").ToLowerInvariant())
{
    case "h264":
        codec = VideoCodec.H264;
        break;
    case "mjpeg":
    case "jpeg":
        codec = VideoCodec.Mjpeg;
        break;
    default:
        Console.Error.WriteLine($"Unknown codec '{args[4]}'. Use 'h264' or 'mjpeg'.");
        return 1;
}

// A fresh endpoint UUID on every run guarantees that clients — in particular Windows, which
// caches discovered cameras by this identity (its device node is swd#networkcamera#<uuid>) —
// treat this as a brand-new device rather than reusing stale state from a previous run.
// TODO: for production this should instead be stable across restarts (derived from hardware
// or provisioned at install), per docs/probe-match-checklist.md item 3.
string endpointAddress = $"urn:uuid:{Guid.NewGuid()}";

var device = new OnvifDeviceDescription
{
    XAddrs = new Uri(args[0]),
    RtspStreamUri = new Uri(args[1]),
    EndpointAddress = endpointAddress,
    Name = args[2],
    Hardware = args[3],
    Codec = codec,
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Device endpoint: {endpointAddress}");
Console.WriteLine($"Advertised codec: {codec}");
Console.WriteLine("Starting ONVIF services...");
await Task.WhenAll(
    new WsDiscoveryListener(device).RunAsync(cts.Token),
    new OnvifHttpListener(device, cts).RunAsync(cts.Token)
);

return 0;
