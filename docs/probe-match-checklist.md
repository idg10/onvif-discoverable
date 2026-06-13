# WS-Discovery ProbeMatch Conformance Checklist

Requirements drawn from:
- **ONVIF Core Specification v25.12**, §7 (Device discovery)
- **ONVIF Profile S Specification v1.3**, §9 (Device Discovery)
- **[Microsoft: Network Cameras](https://learn.microsoft.com/en-us/windows-hardware/drivers/stream/network-cameras)** — notes that Windows has a strict WS-Discovery implementation and does not work around non-compliant responses

Items are marked as `[x]` (implemented), `[ ]` (not yet implemented), or `[?]` (needs investigation).

> **Current focus:** making the `dn:NetworkVideoTransmitter` ProbeMatch response accepted by Windows 11.

---

## 1. Responding to Probe requests

- [x] Listen on WS-Discovery multicast address `239.255.255.250:3702`
- [x] Respond to probes with no `d:Types` filter (wildcard — matches any type)
- [x] Respond to probes for `dn:NetworkVideoTransmitter`
- [ ] Respond to probes for `tds:Device` (`http://www.onvif.org/ver10/device/wsdl`)  
  *Core §7.3.2.1 defines `tds:Device` as the primary ONVIF device management type. Experimental observation: Windows 11 currently probes only for `dn:NetworkVideoTransmitter`, not `tds:Device`, so this is not an immediate blocker. May become relevant once `dn:NetworkVideoTransmitter` responses are accepted.*

---

## 2. SOAP envelope and WS-Addressing headers

- [x] SOAP 1.2 envelope (`http://www.w3.org/2003/05/soap-envelope`)
- [x] `wsa:Action` = `http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches`
- [x] `wsa:MessageID` — fresh `uuid:` URI per response
- [x] `wsa:RelatesTo` — echoes the incoming `wsa:MessageID`
- [x] `wsa:To` = `http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous`  
  *Was incorrectly set to `urn:schemas-xmlsoap-org:ws:2005:04:discovery` (the discovery endpoint URI from the incoming Probe request). Fixed.*
- [x] `d:AppSequence` header element with `InstanceId` and `MessageNumber` attributes  
  *Present in all real camera captures (e.g. `<d:AppSequence InstanceId="1637072188" MessageNumber="17"/>`). `InstanceId` is set to the Unix timestamp at process start; `MessageNumber` is a per-instance counter incremented for each response sent.*

---

## 3. `wsa:EndpointReference / wsa:Address` (Core §7.3.1)

- [x] Format is `urn:uuid:…` (URN:UUID per RFC 4122), as required by ONVIF (overrides the WS-Discovery default URI format)
- [ ] Value is **stable and globally unique** across all network interfaces of the device and does not change across reboots  
  *Currently hardcoded to a fixed UUID with a `// TBD` comment. The value is stable, but its source is undefined. Should be derived from something hardware-bound (e.g. MAC address) or provisioned at install time.*

---

## 4. `d:Types` (Core §7.3.2.1, Profile S §9.2)

- [x] Includes `dn:NetworkVideoTransmitter` (`http://www.onvif.org/ver10/network/wsdl`)  
  *Profile S §9.2: required for backward compatibility*
- [ ] Includes `tds:Device` (`http://www.onvif.org/ver10/device/wsdl`)  
  *Core §7.3.2.1: the primary ONVIF device management type. Profile S §9.2 says a device "may omit" it, but every real camera capture found includes it alongside `dn:NetworkVideoTransmitter`. Hikvision sends `dn:NetworkVideoTransmitter tds:Device`.*

---

## 5. `d:Scopes` (Core §7.3.2.2, Profile S §9.1)

All ONVIF scope URIs follow the form `onvif://www.onvif.org/<path>`.  
Scope matching uses RFC 3986 path-prefix matching: `onvif://www.onvif.org/hardware/D1` does **not** match a probe for `onvif://www.onvif.org/hardware/D1-566`.

### 5a. Mandatory scopes

- [x] `onvif://www.onvif.org/Profile/Streaming`  
  *Profile S §9.1: required to indicate Profile S compliance*
- [x] `onvif://www.onvif.org/hardware/<value>`  
  *Core §7.3.2.2 Table 8: **"A device shall include at least one hardware entry into its scope list."***
- [x] `onvif://www.onvif.org/name/<value>`  
  *Core §7.3.2.2 Table 8: **"A device shall include at least one name entry into its scope list."***

### 5b. Optional but standardised scopes

- [ ] `onvif://www.onvif.org/location/<value>` — physical location (free-form string or path)
- [ ] `onvif://www.onvif.org/hardware/mac/<macaddress>` — MAC address of network interface
- [ ] `onvif://www.onvif.org/serialnumber/<serialnumber>` — unique serial number

---

## 6. `d:XAddrs` (Core §7.3.2.3, §7.3.3)

- [x] Contains the device service URL (passed as CLI argument)
- [ ] URL scheme and IP address should match the interface on which the probe was received  
  *Core §7.3.2.3: "A URI shall be provided for each protocol (http, https) and externally available IP address." If the device is reachable on multiple interfaces or via both HTTP and HTTPS, each should be listed.*
- [ ] Consider including a port-80 entry  
  *Core §7.3.2.3: "The device should provide a port 80 device service entry in order to allow firewall traversal."*

---

## 7. `d:MetadataVersion` (WS-Discovery spec)

- [x] Present and set to `1`
- [ ] Must be incremented whenever the device's metadata (types, scopes, XAddrs) changes  
  *Not relevant while the values are static, but worth noting for future dynamic configurations.*

---

## 8. Hello message (Core §7.2)

- [ ] Device should send a multicast `Hello` message when it starts up (joins the network)  
  *Core §7.2: "A device in discoverable mode sends multicast Hello messages once connected to the network." This allows clients to detect the device without polling. Currently the application only responds to Probe requests; it never sends Hello.*

---

## 9. Scope matching rule (Core §7.3.3)

- [ ] Device shall support the `http://schemas.xmlsoap.org/ws/2005/04/discovery/rfc3986` scope matching rule  
  *Core §7.3.3: required. This rule uses RFC 3986 URI comparison — a probe scope matches if the device scope starts with the probe scope (path-prefix). Currently probe requests are accepted regardless of scopes; the matching rule would need to be applied if a probe includes a `d:Scopes` filter element.*

---

## 10. Post-discovery streaming (not WS-Discovery, but required for the overall goal)

These items affect what happens *after* Windows has discovered the device. They have no bearing on the ProbeMatch response, but are required before Windows can actually stream video.

> **Important:** Windows supports only **MJPEG and H.264** codecs (RTP over UDP). MPEG4 is not supported.  
> Source: [Microsoft: Network Cameras](https://learn.microsoft.com/en-us/windows-hardware/drivers/stream/network-cameras).  
> Profile S §8.2 (H.264) is the relevant section for Windows compatibility.

- [ ] Device must implement the ONVIF **media service** at the URL advertised in `d:XAddrs`, responding to SOAP calls including `GetVideoEncoderConfigurationOptions`, `GetProfiles`, `GetStreamUri`, etc.
- [ ] `GetVideoEncoderConfigurationOptions` response must declare H.264 support (Profile S §8.2)
- [ ] Device must be able to stream H.264 video over RTP/UDP (Profile S §8.2)
- [ ] Device must send a key frame on demand when `SetSynchronizationPoint` is called (Profile S §8.2)

---

## Summary of likely causes of Windows non-discovery

Based on the above and experimental observation (Windows 11 probes for `dn:NetworkVideoTransmitter` only):

1. ~~**Missing mandatory `name` and `hardware` scopes.**~~ Now implemented. (Item 5a)
2. **Wrong `wsa:To` in ProbeMatches response.** We send the discovery endpoint URI; real cameras send the anonymous addressing URI. Windows's strict implementation likely rejects responses with the wrong addressing. (Item 2) — **most likely current blocker**
3. **Missing `d:AppSequence` header.** Every real camera includes this; we omit it entirely. (Item 2)
4. **Missing `tds:Device` in `d:Types`.** Every real camera includes both types. (Item 4)
5. **Windows probes for `tds:Device`, which we currently ignore.** (Item 1) — *not currently observed, revisit once above are fixed*
