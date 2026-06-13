using OnvifDiscoverable.Server;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: OnvifDiscoverable.Server <xaddrs-url> <rtsp-url> <name> <hardware>");
    Console.Error.WriteLine("  e.g. OnvifDiscoverable.Server http://192.168.1.10:8080/onvif/device_service rtsp://192.168.1.10:8554/stream \"My Camera\" \"Acme Model X\"");
    return 1;
}

// TBD: EndpointAddress should be derived or configurable
var device = new OnvifDeviceDescription
{
    XAddrs = new Uri(args[0]),
    RtspStreamUri = new Uri(args[1]),
    EndpointAddress = "urn:uuid:314ba71f-a192-4054-9436-e19eb037f074",
    Name = args[2],
    Hardware = args[3],
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine("Starting ONVIF services...");
await Task.WhenAll(
    new WsDiscoveryListener(device).RunAsync(cts.Token),
    new OnvifHttpListener(device, cts).RunAsync(cts.Token)
);

return 0;
