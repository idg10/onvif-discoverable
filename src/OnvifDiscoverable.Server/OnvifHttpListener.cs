using System.Net;
using System.Text;
using System.Xml.Linq;

namespace OnvifDiscoverable.Server;

class OnvifHttpListener(OnvifDeviceDescription device, CancellationTokenSource cts)
{
    private static readonly XNamespace SoapNs = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace WsaNs  = "http://schemas.xmlsoap.org/ws/2004/08/addressing";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();

        string prefix = $"http://{device.XAddrs.Host}:{device.XAddrs.Port}/onvif/";
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5) // ERROR_ACCESS_DENIED
        {
            Console.Error.WriteLine($"""
                Error: access denied when starting the ONVIF HTTP listener on {prefix}
                On Windows, non-administrator processes must be granted a URL reservation.
                Run the following commands once from an elevated (Administrator) prompt, then restart:

                  netsh http add urlacl url={prefix} user={Environment.UserDomainName}\{Environment.UserName}
                  netsh advfirewall firewall add rule name="ONVIF HTTP" dir=in action=allow protocol=TCP localport={device.XAddrs.Port}

                Note: the firewall rule targets the port rather than the executable because HttpListener
                uses the Windows http.sys kernel driver (PID 4), which owns the TCP socket — an
                executable-based firewall rule will not match inbound connections on this port.

                To remove both when no longer needed:

                  netsh http delete urlacl url={prefix}
                  netsh advfirewall firewall delete rule name="ONVIF HTTP"
                """);

            // TODO: Claude added this to enable failure at this point to shut down the
            // whole app. I don't like the coupling this introduces, but fixing this isn't
            // a priority - at this stage, I just want this tool to become usable. But if
            // I do make this tool generally available, I would prefer either to:
            //  1) have an initial startup phase for individual service that must
            //      complete before we decide we're ready to kick off the long-running
            //      RunAsync
            //  2) stick with just RunAsync, but provide some mechanism to signal that
            //      we failed to start, so that the main program can shut down the whole
            //      app and return a suitable exit code.
            cts.Cancel();
            return;
        }

        Console.WriteLine($"ONVIF HTTP service listening on {prefix}");
        cancellationToken.Register(listener.Stop);

        try
        {
            while (true)
            {
                var context = await listener.GetContextAsync();
                // Fire-and-forget: don't block the accept loop on request handling.
                _ = HandleRequestAsync(context);
            }
        }
        catch (HttpListenerException)
        {
            // Thrown when listener.Stop() is called via cancellation — normal shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Also possible during shutdown.
        }
    }

    private Uri MediaServiceUri
    {
        get
        {
            var b = new UriBuilder(device.XAddrs);
            int lastSlash = b.Path.LastIndexOf('/');
            b.Path = (lastSlash >= 0 ? b.Path[..lastSlash] : "") + "/media_service";
            return b.Uri;
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();

            string? action = ExtractSoapAction(body);

            Console.WriteLine($"{context.Request.HttpMethod} {context.Request.Url?.PathAndQuery} → {action ?? "(no action)"}");

            string responseBody = action switch
            {
                "http://www.onvif.org/ver10/device/wsdl/GetServiceCapabilities"            => BuildGetServiceCapabilitiesResponse(),
                "http://www.onvif.org/ver10/device/wsdl/GetSystemDateAndTime"              => BuildGetSystemDateAndTimeResponse(),
                "http://www.onvif.org/ver10/device/wsdl/GetHostname"                       => BuildGetHostnameResponse(),
                "http://www.onvif.org/ver10/device/wsdl/GetNetworkProtocols"               => BuildGetNetworkProtocolsResponse(),
                "http://www.onvif.org/ver10/device/wsdl/GetCapabilities"                   => BuildGetCapabilitiesResponse(),
                "http://www.onvif.org/ver10/device/wsdl/GetDeviceInformation"              => BuildGetDeviceInformationResponse(),
                "http://www.onvif.org/ver10/media/wsdl/GetVideoSources"                    => BuildGetVideoSourcesResponse(),
                "http://www.onvif.org/ver10/media/wsdl/GetProfiles"                        => BuildGetProfilesResponse(),
                "http://www.onvif.org/ver10/media/wsdl/GetProfile"                         => BuildGetProfileResponse(),
                "http://www.onvif.org/ver10/media/wsdl/GetVideoEncoderConfigurationOptions" => BuildGetVideoEncoderConfigurationOptionsResponse(),
                "http://www.onvif.org/ver10/media/wsdl/GetStreamUri"                       => BuildGetStreamUriResponse(),
                _ => BuildFaultResponse($"Unsupported action: {action ?? "(none)"}"),
            };

            Console.WriteLine($"Response:\n{responseBody}\n---");

            byte[] responseBytes = Encoding.UTF8.GetBytes(responseBody);
            context.Response.ContentType = "application/soap+xml; charset=utf-8";
            context.Response.ContentLength64 = responseBytes.Length;
            context.Response.StatusCode = 200;
            await context.Response.OutputStream.WriteAsync(responseBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling ONVIF HTTP request: {ex.Message}");
            try
            {
                context.Response.StatusCode = 500;
            }
            catch
            {
                // Response may already be partially written; ignore secondary failure.
            }
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static string? ExtractSoapAction(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var envelope = doc.Root;

            // Prefer explicit wsa:Action header.
            string? action = envelope
                ?.Element(SoapNs + "Header")
                ?.Element(WsaNs + "Action")
                ?.Value;

            if (action != null)
            {
                return action;
            }

            // Fall back to deriving the action from the body element name.
            // ONVIF clients frequently omit the wsa:Action header and rely on
            // the body element alone. The action URI follows the ONVIF convention:
            // {namespace}/{localName} (e.g. GetServiceCapabilities in namespace
            // http://www.onvif.org/ver10/device/wsdl becomes the full action URI).
            var bodyChild = envelope
                ?.Element(SoapNs + "Body")
                ?.Elements()
                .FirstOrDefault();

            if (bodyChild?.Name.Namespace is XNamespace ns && ns != XNamespace.None)
            {
                string nsUri = ns.NamespaceName;
                return nsUri.EndsWith('/') ? $"{nsUri}{bodyChild.Name.LocalName}"
                                           : $"{nsUri}/{bodyChild.Name.LocalName}";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string XmlEscape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private string WrapSoapBody(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <env:Envelope xmlns:env="http://www.w3.org/2003/05/soap-envelope"
                      xmlns:tds="http://www.onvif.org/ver10/device/wsdl"
                      xmlns:trt="http://www.onvif.org/ver10/media/wsdl"
                      xmlns:tt="http://www.onvif.org/ver10/schema">
          <env:Body>
        {body}
          </env:Body>
        </env:Envelope>
        """;

    private string BuildGetSystemDateAndTimeResponse()
    {
        var now = DateTimeOffset.UtcNow;
        return WrapSoapBody($"""
                <tds:GetSystemDateAndTimeResponse>
                  <tds:SystemDateAndTime>
                    <tt:DateTimeType>Manual</tt:DateTimeType>
                    <tt:DaylightSavings>false</tt:DaylightSavings>
                    <tt:UTCDateTime>
                      <tt:Time>
                        <tt:Hour>{now.Hour}</tt:Hour>
                        <tt:Minute>{now.Minute}</tt:Minute>
                        <tt:Second>{now.Second}</tt:Second>
                      </tt:Time>
                      <tt:Date>
                        <tt:Year>{now.Year}</tt:Year>
                        <tt:Month>{now.Month}</tt:Month>
                        <tt:Day>{now.Day}</tt:Day>
                      </tt:Date>
                    </tt:UTCDateTime>
                  </tds:SystemDateAndTime>
                </tds:GetSystemDateAndTimeResponse>
            """);
    }

    private string BuildGetCapabilitiesResponse() =>
        WrapSoapBody($"""
                <tds:GetCapabilitiesResponse>
                  <tds:Capabilities>
                    <tt:Media>
                      <tt:XAddr>{XmlEscape(MediaServiceUri.ToString())}</tt:XAddr>
                      <tt:StreamingCapabilities>
                        <tt:RTPMulticast>false</tt:RTPMulticast>
                        <tt:RTP_TCP>false</tt:RTP_TCP>
                        <tt:RTP_RTSP_TCP>true</tt:RTP_RTSP_TCP>
                      </tt:StreamingCapabilities>
                    </tt:Media>
                  </tds:Capabilities>
                </tds:GetCapabilitiesResponse>
            """);

    private string BuildGetDeviceInformationResponse() =>
        WrapSoapBody($"""
                <tds:GetDeviceInformationResponse>
                  <tds:Manufacturer>{XmlEscape(device.Name)}</tds:Manufacturer>
                  <tds:Model>{XmlEscape(device.Hardware)}</tds:Model>
                  <tds:FirmwareVersion>1.0</tds:FirmwareVersion>
                  <tds:SerialNumber>000000000001</tds:SerialNumber>
                  <tds:HardwareId>{XmlEscape(device.Hardware)}</tds:HardwareId>
                </tds:GetDeviceInformationResponse>
            """);

    private string BuildGetProfilesResponse() =>
        WrapSoapBody("""
                <trt:GetProfilesResponse>
                  <trt:Profiles token="profile_0" fixed="true">
                    <tt:Name>Main Profile</tt:Name>
                    <tt:VideoSourceConfiguration token="vsc_0">
                      <tt:Name>Video Source</tt:Name>
                      <tt:UseCount>1</tt:UseCount>
                      <tt:SourceToken>video_source_0</tt:SourceToken>
                      <tt:Bounds x="0" y="0" width="1920" height="1080"/>
                    </tt:VideoSourceConfiguration>
                    <tt:VideoEncoderConfiguration token="vec_0">
                      <tt:Name>H264 Encoder</tt:Name>
                      <tt:UseCount>1</tt:UseCount>
                      <tt:Encoding>H264</tt:Encoding>
                      <tt:Resolution>
                        <tt:Width>1920</tt:Width>
                        <tt:Height>1080</tt:Height>
                      </tt:Resolution>
                      <tt:Quality>50</tt:Quality>
                      <tt:RateControl>
                        <tt:FrameRateLimit>30</tt:FrameRateLimit>
                        <tt:EncodingInterval>1</tt:EncodingInterval>
                        <tt:BitrateLimit>4096</tt:BitrateLimit>
                      </tt:RateControl>
                      <tt:H264>
                        <tt:GovLength>30</tt:GovLength>
                        <tt:H264Profile>Main</tt:H264Profile>
                      </tt:H264>
                      <tt:Multicast>
                        <tt:Address>
                          <tt:Type>IPv4</tt:Type>
                          <tt:IPv4Address>0.0.0.0</tt:IPv4Address>
                        </tt:Address>
                        <tt:Port>0</tt:Port>
                        <tt:TTL>0</tt:TTL>
                        <tt:AutoStart>false</tt:AutoStart>
                      </tt:Multicast>
                      <tt:SessionTimeout>PT60S</tt:SessionTimeout>
                    </tt:VideoEncoderConfiguration>
                  </trt:Profiles>
                </trt:GetProfilesResponse>
            """);

    private string BuildGetProfileResponse() =>
        WrapSoapBody("""
                <trt:GetProfileResponse>
                  <trt:Profile token="profile_0" fixed="true">
                    <tt:Name>Main Profile</tt:Name>
                    <tt:VideoSourceConfiguration token="vsc_0">
                      <tt:Name>Video Source</tt:Name>
                      <tt:UseCount>1</tt:UseCount>
                      <tt:SourceToken>video_source_0</tt:SourceToken>
                      <tt:Bounds x="0" y="0" width="1920" height="1080"/>
                    </tt:VideoSourceConfiguration>
                    <tt:VideoEncoderConfiguration token="vec_0">
                      <tt:Name>H264 Encoder</tt:Name>
                      <tt:UseCount>1</tt:UseCount>
                      <tt:Encoding>H264</tt:Encoding>
                      <tt:Resolution>
                        <tt:Width>1920</tt:Width>
                        <tt:Height>1080</tt:Height>
                      </tt:Resolution>
                      <tt:Quality>50</tt:Quality>
                      <tt:RateControl>
                        <tt:FrameRateLimit>30</tt:FrameRateLimit>
                        <tt:EncodingInterval>1</tt:EncodingInterval>
                        <tt:BitrateLimit>4096</tt:BitrateLimit>
                      </tt:RateControl>
                      <tt:H264>
                        <tt:GovLength>30</tt:GovLength>
                        <tt:H264Profile>Main</tt:H264Profile>
                      </tt:H264>
                      <tt:Multicast>
                        <tt:Address>
                          <tt:Type>IPv4</tt:Type>
                          <tt:IPv4Address>0.0.0.0</tt:IPv4Address>
                        </tt:Address>
                        <tt:Port>0</tt:Port>
                        <tt:TTL>0</tt:TTL>
                        <tt:AutoStart>false</tt:AutoStart>
                      </tt:Multicast>
                      <tt:SessionTimeout>PT60S</tt:SessionTimeout>
                    </tt:VideoEncoderConfiguration>
                  </trt:Profile>
                </trt:GetProfileResponse>
            """);

    private string BuildGetVideoEncoderConfigurationOptionsResponse() =>
        WrapSoapBody("""
                <trt:GetVideoEncoderConfigurationOptionsResponse>
                  <trt:Options>
                    <tt:H264>
                      <tt:ResolutionsAvailable>
                        <tt:Width>1920</tt:Width>
                        <tt:Height>1080</tt:Height>
                      </tt:ResolutionsAvailable>
                      <tt:GovLengthRange Min="1" Max="300"/>
                      <tt:FrameRateRange Min="1" Max="30"/>
                      <tt:EncodingIntervalRange Min="1" Max="1"/>
                      <tt:H264ProfilesSupported>Main</tt:H264ProfilesSupported>
                    </tt:H264>
                  </trt:Options>
                </trt:GetVideoEncoderConfigurationOptionsResponse>
            """);

    private string BuildGetStreamUriResponse() =>
        WrapSoapBody($"""
                <trt:GetStreamUriResponse>
                  <trt:MediaUri>
                    <tt:Uri>{XmlEscape(device.RtspStreamUri.ToString())}</tt:Uri>
                    <tt:InvalidAfterConnect>false</tt:InvalidAfterConnect>
                    <tt:InvalidAfterReboot>false</tt:InvalidAfterReboot>
                    <tt:Timeout>PT60S</tt:Timeout>
                  </trt:MediaUri>
                </trt:GetStreamUriResponse>
            """);

    private string BuildGetServiceCapabilitiesResponse() =>
        WrapSoapBody("""
                <tds:GetServiceCapabilitiesResponse>
                  <tds:Capabilities>
                    <tds:Network IPFilter="false" ZeroConfiguration="false" IPVersion6="false"
                                 DynDNS="false" Dot11Configuration="false" Dot1XConfigurations="0"
                                 HostnameFromDHCP="false" NTP="0" DHCPv6="false"/>
                    <tds:Security TLS1.0="false" TLS1.1="false" TLS1.2="false"
                                  OnboardKeyGeneration="false" AccessPolicyConfig="false"
                                  DefaultAccessPolicy="false" Dot1X="false"
                                  RemoteUserHandling="false" X.509Token="false"
                                  SAMLToken="false" KerberosToken="false"
                                  UsernameToken="false" HttpDigest="false" RELToken="false"/>
                    <tds:System DiscoveryResolve="false" DiscoveryBye="false"
                                RemoteDiscovery="false" SystemBackup="false"
                                SystemLogging="false" FirmwareUpgrade="false"
                                HttpFirmwareUpgrade="false" HttpSystemBackup="false"
                                HttpSystemLogging="false" HttpSupportInformation="false"
                                StorageConfiguration="false"/>
                  </tds:Capabilities>
                </tds:GetServiceCapabilitiesResponse>
            """);

    private string BuildGetVideoSourcesResponse() =>
        WrapSoapBody("""
                <trt:GetVideoSourcesResponse>
                  <trt:VideoSources token="video_source_0">
                    <tt:Framerate>30</tt:Framerate>
                    <tt:Resolution>
                      <tt:Width>1920</tt:Width>
                      <tt:Height>1080</tt:Height>
                    </tt:Resolution>
                  </trt:VideoSources>
                </trt:GetVideoSourcesResponse>
            """);

    private string BuildGetNetworkProtocolsResponse() =>
        WrapSoapBody($"""
                <tds:GetNetworkProtocolsResponse>
                  <tds:NetworkProtocols>
                    <tt:Name>HTTP</tt:Name>
                    <tt:Enabled>true</tt:Enabled>
                    <tt:Port>{device.XAddrs.Port}</tt:Port>
                  </tds:NetworkProtocols>
                  <tds:NetworkProtocols>
                    <tt:Name>RTSP</tt:Name>
                    <tt:Enabled>true</tt:Enabled>
                    <tt:Port>{device.RtspStreamUri.Port}</tt:Port>
                  </tds:NetworkProtocols>
                </tds:GetNetworkProtocolsResponse>
            """);

    private string BuildGetHostnameResponse() =>
        WrapSoapBody($"""
                <tds:GetHostnameResponse>
                  <tds:HostnameInformation>
                    <tt:FromDHCP>false</tt:FromDHCP>
                    <tt:Name>{XmlEscape(Environment.MachineName)}</tt:Name>
                  </tds:HostnameInformation>
                </tds:GetHostnameResponse>
            """);

    private static string BuildFaultResponse(string reason)
    {
        Console.WriteLine($"ONVIF SOAP fault: {reason}");
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <env:Envelope xmlns:env="http://www.w3.org/2003/05/soap-envelope">
              <env:Body>
                <env:Fault>
                  <env:Code><env:Value>env:Sender</env:Value></env:Code>
                  <env:Reason><env:Text xml:lang="en">{XmlEscape(reason)}</env:Text></env:Reason>
                </env:Fault>
              </env:Body>
            </env:Envelope>
            """;
    }
}
