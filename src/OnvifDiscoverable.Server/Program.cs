using OnvifDiscoverable.Server;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: OnvifDiscoverable.Server <xaddrs-url>");
    Console.Error.WriteLine("  e.g. OnvifDiscoverable.Server http://192.168.1.10:8080/onvif/device_service");
    return 1;
}

// TBD: EndpointAddress should be derived or configurable
var device = new OnvifDeviceDescription
{
    XAddrs = new Uri(args[0]),
    EndpointAddress = "urn:uuid:314ba71f-a192-4054-9436-e19eb037f074",
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine("WS-Discovery responder running...");
await new WsDiscoveryListener(device).RunAsync(cts.Token);

return 0;
