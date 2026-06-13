# ONVIF Device Service Implementation Checklist

This document tracks the work required to implement the HTTP SOAP endpoints that Windows calls after WS-Discovery. See `probe-match-checklist.md` §10 for how this fits into the overall flow.

Windows's sequence after receiving a ProbeMatch:
1. Calls `GetCapabilities` on the device service (at the `d:XAddrs` URL)
2. Uses the returned media service URL to call `GetProfiles`, then `GetStreamUri`
3. Connects to the RTSP URL returned by `GetStreamUri`

Nothing is currently listening at `d:XAddrs`. This document covers stages 1 and 2; RTSP streaming is out of scope here.

---

## Infrastructure

- [ ] HTTP listener on the port/path in `d:XAddrs` (e.g. `http://0.0.0.0:8080/onvif/device_service`)
- [ ] Parse incoming HTTP POST bodies as SOAP 1.2 envelopes (`http://www.w3.org/2003/05/soap-envelope`)
- [ ] Extract the `wsa:Action` header to dispatch to the correct handler
- [ ] Return SOAP 1.2 responses with `Content-Type: application/soap+xml; charset=utf-8`
- [ ] Return a SOAP Fault (`env:Fault`) for unrecognised or malformed requests rather than an HTTP error  
  *ONVIF clients typically look for a SOAP-level fault, not an HTTP 400/404.*

---

## Device service (`/onvif/device_service`)

### `GetSystemDateAndTime` (Core §7.8.1)

Many clients — including some Windows paths — call this first, before `GetCapabilities`, to check reachability and clock skew.

- [ ] Respond with current UTC date and time
- [ ] `DateTimeType` = `NTP` or `Manual`; `DaylightSavings` = `false` is fine for a stub

### `GetCapabilities` (Core §8.2.1)

The primary response Windows needs in order to locate the media service.

- [ ] Return a `Media` capability entry containing the media service URL  
  e.g. `<tds:Media><tt:XAddr>http://192.168.1.10:8080/onvif/media_service</tt:XAddr>...`
- [ ] `RTP_TCP`, `RTP_RTSP_TCP` capability flags can be `false` for an initial stub
- [ ] Other capability categories (`Device`, `Events`, `Imaging`, `PTZ`) may be omitted or stubbed with empty entries

### `GetDeviceInformation` (Core §8.2.2)

- [ ] Return `Manufacturer`, `Model`, `FirmwareVersion`, `SerialNumber`, `HardwareId`  
  *Values can be the same strings passed as `name` and `hardware` CLI arguments; serial number and firmware can be fixed stubs.*

---

## Media service (`/onvif/media_service`)

This is a separate SOAP endpoint whose URL is advertised in the `GetCapabilities` response.

### `GetProfiles` (Profile S §5.3)

Returns the list of media profiles. Windows needs at least one profile to proceed.

- [ ] Return one `Profile` element with a `token` attribute (any stable string, e.g. `"profile_0"`)
- [ ] Profile must include a `VideoSourceConfiguration` and a `VideoEncoderConfiguration`
- [ ] `VideoEncoderConfiguration` must specify `H264` as the encoding  
  *Profile S §8.2: required for Windows compatibility*

### `GetVideoEncoderConfigurationOptions` (Profile S §8.2)

- [ ] Return an `H264` options block
- [ ] Include at least one supported `H264Profile` (`Baseline`, `Main`, or `High`)
- [ ] Include at least one `ResolutionsAvailable` entry

### `GetStreamUri` (Profile S §5.5)

The final step before Windows attempts RTSP.

- [ ] Accept a `ProfileToken` matching the token from `GetProfiles`
- [ ] Return an `Uri` element containing the RTSP stream URL  
  e.g. `rtsp://192.168.1.10:8554/stream`
- [ ] `InvalidAfterConnect` and `InvalidAfterReboot` can both be `false` for a stub
- [ ] `Timeout` can be `PT60S` or similar

---

## Namespaces

| Prefix | URI |
|--------|-----|
| `tds`  | `http://www.onvif.org/ver10/device/wsdl` |
| `trt`  | `http://www.onvif.org/ver10/media/wsdl` |
| `tt`   | `http://www.onvif.org/ver10/schema` |
| `env`  | `http://www.w3.org/2003/05/soap-envelope` |
| `wsa`  | `http://schemas.xmlsoap.org/ws/2004/08/addressing` |

---

## Implementation notes

**Minimal viable surface.** Windows needs only `GetCapabilities` → `GetProfiles` → `GetStreamUri` to proceed to RTSP. `GetSystemDateAndTime` and `GetDeviceInformation` are called but a missing or stubbed response won't block streaming. Implement them to avoid connection errors in logs, but don't let them delay getting the happy path working.

**SOAP dispatch.** The `wsa:Action` header identifies the operation. Key values:

| Operation | `wsa:Action` |
|-----------|--------------|
| `GetSystemDateAndTime` | `http://www.onvif.org/ver10/device/wsdl/GetSystemDateAndTime` |
| `GetCapabilities` | `http://www.onvif.org/ver10/device/wsdl/GetCapabilities` |
| `GetDeviceInformation` | `http://www.onvif.org/ver10/device/wsdl/GetDeviceInformation` |
| `GetProfiles` | `http://www.onvif.org/ver10/media/wsdl/GetProfiles` |
| `GetVideoEncoderConfigurationOptions` | `http://www.onvif.org/ver10/media/wsdl/GetVideoEncoderConfigurationOptions` |
| `GetStreamUri` | `http://www.onvif.org/ver10/media/wsdl/GetStreamUri` |

**No authentication required for discovery.** ONVIF WS-UsernameToken authentication is only required if the device is configured to require it. For a local stub, unauthenticated requests are fine.

**Single-process vs split listener.** The HTTP listener can live in the same process as the WS-Discovery listener, running concurrently under the same `CancellationToken`. `System.Net.HttpListener` (BCL, no extra dependencies) is sufficient for a minimal implementation.
