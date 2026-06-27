# CLAUDE.md

## Project Overview

`onvif-discoverable` is a .NET 10 console application that implements a **WS-Discovery responder** for ONVIF devices. It listens on the WS-Discovery multicast address (`239.255.255.250:3702`) and responds to Probe requests by advertising a simulated `NetworkVideoTransmitter` device — making the process discoverable to ONVIF camera clients.

## Tech Stack

- **.NET 10.0**, C#, single-project console app
- No external NuGet dependencies (BCL only: `System.Net.Sockets`, `System.Net.HttpListener`, `System.Xml.Linq`)
- AOT compilation enabled (`PublishAot: true`)
- Nullable reference types enabled

## Project Structure

```
onvif-discoverable/
├── CLAUDE.md
├── LICENSE
├── src/
│   ├── OnvifDiscoverable.slnx                      # Solution file (modern .slnx format)
│   └── OnvifDiscoverable.Server/
│       ├── OnvifDiscoverable.Server.csproj
│       ├── Program.cs                              # Entry point: arg parsing, wiring, Ctrl+C
│       ├── OnvifDeviceDescription.cs               # Device attributes record
│       ├── WsDiscoveryListener.cs                  # WS-Discovery multicast responder
│       ├── WsDiscoveryProbeRequest.cs              # Parses incoming Probe requests
│       ├── WsDiscoveryProbeMatch.cs                # Builds ProbeMatch responses
│       ├── OnvifHttpListener.cs                    # ONVIF device/media SOAP HTTP server
│       ├── OnvifSchemaValidator.cs                 # Runtime response schema validation
│       └── Schemas/                                # Bundled ONVIF/WS-* XSDs (embedded)
```

## Build & Run

```bash
# Build
dotnet build src/

# Run (xaddrs-url and rtsp-url are HTTP/RTSP endpoints; name and hardware are advertised in
# ProbeMatch responses; optional codec is h264 (default) or mjpeg and must match the RTSP stream)
dotnet run --project src/OnvifDiscoverable.Server/ -- http://192.168.1.10:8080/onvif/device_service rtsp://192.168.1.10:8554/stream "My Camera" "Acme Model X" h264

# Publish as native AOT executable
dotnet publish -c Release -p:PublishAot=true src/OnvifDiscoverable.Server/
```

There are no tests at this time.

## Key Implementation Notes

**`Program.cs`** — arg parsing, constructs `OnvifDeviceDescription`, runs `WsDiscoveryListener` and `OnvifHttpListener` concurrently via `Task.WhenAll`. Ctrl+C is wired to a `CancellationTokenSource` shared by both listeners.

**`OnvifDeviceDescription`** — record holding the device attributes: `XAddrs` (ONVIF device service URL), `RtspStreamUri` (returned by `GetStreamUri`), `EndpointAddress`, `Name`, `Hardware`, `Codec` (`VideoCodec.H264`/`Mjpeg`), `Types`, `Scopes`. The advertised `Codec` must match what the RTSP stream actually carries.

**`WsDiscoveryListener`** — joins the WS-Discovery multicast group, receives UDP datagrams, validates Probe requests, and sends ProbeMatch responses.

**`OnvifHttpListener`** — HTTP SOAP server. Listens on the host/port from `XAddrs`, dispatches on `wsa:Action`, and handles: `GetSystemDateAndTime`, `GetCapabilities`, `GetDeviceInformation` (device service) and `GetProfiles`, `GetVideoEncoderConfigurationOptions`, `GetStreamUri` (media service). The media service URL is derived from `XAddrs` by replacing the last path segment with `media_service`. Every response is run through `OnvifSchemaValidator` before being sent.

**`OnvifSchemaValidator`** — validates each outgoing SOAP response against the official ONVIF/WS-* XML schemas at runtime, logging any schema violations to stderr. Windows's WS-Management stack is a strict (WWSAPI) parser that silently rejects non-conformant responses with `WS_E_INVALID_FORMAT`; this catches such mistakes at the point of emission. The schemas are bundled as embedded resources (`Schemas/*.xsd`) so no network access is needed. If the schema set fails to load, validation is skipped rather than blocking the server. Note: `System.Xml.Schema` validation is a development aid and may need gating before an AOT `publish` (it currently degrades gracefully if trimming removes anything it needs).

**`Schemas/`** — the 12 bundled `.xsd` files: the four ONVIF schemas (`onvif.xsd`, `common.xsd`, plus `tds.xsd`/`trt.xsd` extracted from the device/media WSDLs) and their eight transitive external imports (SOAP-envelope, WS-Addressing, WS-Notification `b-2`/`bf-2`/`t-1`, xmlmime, xop, `xml.xsd`). Embedded via the `EmbeddedResource` item in the `.csproj`.

**Known TODOs in the code:**
- The endpoint UUID (`urn:uuid:314ba71f-...`) has a `// TBD` comment — its intended source/meaning is not yet decided

## WS-Discovery / ONVIF Namespaces

The code uses these XML namespaces — keep them consistent:

| Prefix | URI | Used in |
|--------|-----|---------|
| `env` / `s12` | `http://www.w3.org/2003/05/soap-envelope` | all |
| `wsa` | `http://schemas.xmlsoap.org/ws/2004/08/addressing` | all |
| `wsd` / `d` | `http://schemas.xmlsoap.org/ws/2005/04/discovery` | WS-Discovery |
| `dn` | `http://www.onvif.org/ver10/network/wsdl` | WS-Discovery Types |
| `tds` | `http://www.onvif.org/ver10/device/wsdl` | device service |
| `trt` | `http://www.onvif.org/ver10/media/wsdl` | media service |
| `tt` | `http://www.onvif.org/ver10/schema` | ONVIF common schema |

## Conventions

- Single-file approach is intentional — keep logic in `Program.cs` unless it grows significantly
- Always use braces on `if`/`else` blocks, even single-statement ones — enforced by `.editorconfig` (`csharp_prefer_braces = true:warning`)
- No culture-sensitive code (`InvariantGlobalization: true`)
- Console output is the only logging mechanism; no logging framework is used

### Nullability

Nullable reference types are enabled project-wide. Follow standard .NET nullability annotation conventions:

- `Try`-pattern methods must use `out T?` (nullable) with `[NotNullWhen(true)]` from `System.Diagnostics.CodeAnalysis` — not `out T` with a sentinel default — so the compiler can track nullability through the success path.
- Annotate any other methods that conditionally populate output/return values with the appropriate attributes (`[NotNullWhen]`, `[MaybeNullWhen]`, `[MemberNotNullWhen]`, etc.) rather than suppressing warnings with `!` or dummy values.
