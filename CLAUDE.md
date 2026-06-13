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

**Program.cs** is the entire implementation (~128 lines). It:

1. Joins the WS-Discovery multicast group on all interfaces
2. Receives UDP datagrams in a `while(true)` loop
3. Validates incoming SOAP/XML Probe requests (checks SOAP envelope, WS-Addressing headers, WS-Discovery action, and that the type is `dn:NetworkVideoTransmitter`)
4. Sends back a `ProbeMatches` SOAP response with a hardcoded endpoint UUID and a placeholder XAddr

**Known TODOs in the code:**
- The endpoint UUID (`urn:uuid:314ba71f-...`) has a `// TBD` comment — its intended source/meaning is not yet decided
- No graceful shutdown (Ctrl+C handling)

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
- No culture-sensitive code (`InvariantGlobalization: true`)
- Console output is the only logging mechanism; no logging framework is used
