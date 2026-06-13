namespace OnvifDiscoverable.Server;

record OnvifDeviceDescription(
    string XAddrs,
    string EndpointAddress,
    string Types = "dn:NetworkVideoTransmitter",
    string Scopes = "onvif://www.onvif.org/Profile/Streaming");
