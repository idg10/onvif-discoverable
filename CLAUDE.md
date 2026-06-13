# CLAUDE.md

## Project Overview

`onvif-discoverable` is a .NET 10 console application that implements a **WS-Discovery responder** for ONVIF devices. It listens on the WS-Discovery multicast address (`239.255.255.250:3702`) and responds to Probe requests by advertising a simulated `NetworkVideoTransmitter` device — making the process discoverable to ONVIF camera clients.

## Tech Stack

- **.NET 10.0**, C#, single-project console app
- No external NuGet dependencies (BCL only: `System.Net.Sockets`, `System.Xml.Linq`)
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

# Run (xaddrs-url is the ONVIF device service URL advertised in ProbeMatch responses)
dotnet run --project src/OnvifDiscoverable.Server/ -- http://192.168.1.10:8080/onvif/device_service

# Publish as native AOT executable
dotnet publish -c Release -p:PublishAot=true src/OnvifDiscoverable.Server/
```

There are no tests at this time.

## Key Implementation Notes

**`Program.cs`** — arg parsing, constructs `OnvifDeviceDescription`, starts `WsDiscoveryListener`.

**`OnvifDeviceDescription`** — record holding the device attributes advertised in ProbeMatch responses: `XAddrs`, `EndpointAddress`, `Types`, `Scopes`.

**`WsDiscoveryListener`** — joins the WS-Discovery multicast group, receives UDP datagrams, validates Probe requests, and sends ProbeMatch responses. Accepts a `CancellationToken`; Ctrl+C is wired up in `Program.cs`.

**Known TODOs in the code:**
- The endpoint UUID (`urn:uuid:314ba71f-...`) has a `// TBD` comment — its intended source/meaning is not yet decided

## WS-Discovery / ONVIF Namespaces

The code uses these XML namespaces — keep them consistent:

| Prefix | URI |
|--------|-----|
| `s12` | `http://www.w3.org/2003/05/soap-envelope` |
| `wsa` | `http://schemas.xmlsoap.org/ws/2004/08/addressing` |
| `wsd` | `http://schemas.xmlsoap.org/ws/2005/04/discovery` |
| `dn` | `http://www.onvif.org/ver10/network/wsdl` |

## Conventions

- Single-file approach is intentional — keep logic in `Program.cs` unless it grows significantly
- Always use braces on `if`/`else` blocks, even single-statement ones — enforced by `.editorconfig` (`csharp_prefer_braces = true:warning`)
- No culture-sensitive code (`InvariantGlobalization: true`)
- Console output is the only logging mechanism; no logging framework is used

### Nullability

Nullable reference types are enabled project-wide. Follow standard .NET nullability annotation conventions:

- `Try`-pattern methods must use `out T?` (nullable) with `[NotNullWhen(true)]` from `System.Diagnostics.CodeAnalysis` — not `out T` with a sentinel default — so the compiler can track nullability through the success path.
- Annotate any other methods that conditionally populate output/return values with the appropriate attributes (`[NotNullWhen]`, `[MaybeNullWhen]`, `[MemberNotNullWhen]`, etc.) rather than suppressing warnings with `!` or dummy values.
