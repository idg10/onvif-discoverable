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
│       └── Program.cs                              # All application logic
```

## Build & Run

```bash
# Build
dotnet build src/

# Run (xaddrs-url and rtsp-url are HTTP/RTSP endpoints; name and hardware are advertised in ProbeMatch responses)
dotnet run --project src/OnvifDiscoverable.Server/ -- http://192.168.1.10:8080/onvif/device_service rtsp://192.168.1.10:8554/stream "My Camera" "Acme Model X"

# Publish as native AOT executable
dotnet publish -c Release -p:PublishAot=true src/OnvifDiscoverable.Server/
```

There are no tests at this time.

## Key Implementation Notes

**`Program.cs`** — arg parsing, constructs `OnvifDeviceDescription`, runs `WsDiscoveryListener` and `OnvifHttpListener` concurrently via `Task.WhenAll`. Ctrl+C is wired to a `CancellationTokenSource` shared by both listeners.

**`OnvifDeviceDescription`** — record holding the device attributes: `XAddrs` (ONVIF device service URL), `RtspStreamUri` (returned by `GetStreamUri`), `EndpointAddress`, `Name`, `Hardware`, `Types`, `Scopes`.

**`WsDiscoveryListener`** — joins the WS-Discovery multicast group, receives UDP datagrams, validates Probe requests, and sends ProbeMatch responses.

**`OnvifHttpListener`** — HTTP SOAP server. Listens on the host/port from `XAddrs`, dispatches on `wsa:Action`, and handles: `GetSystemDateAndTime`, `GetCapabilities`, `GetDeviceInformation` (device service) and `GetProfiles`, `GetVideoEncoderConfigurationOptions`, `GetStreamUri` (media service). The media service URL is derived from `XAddrs` by replacing the last path segment with `media_service`.

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
