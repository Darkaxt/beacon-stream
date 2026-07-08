# Extraction Map

| Source | Planned Use | Copy Source Now | Boundary |
| --- | --- | --- | --- |
| Sunshine | Streaming protocol, capture, encode, audio, input reference; Milestone 10/17/18 external process boundary, Beacon-owned connection metadata, and wrapper readiness manifest only | No | `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs` passes Beacon plans, optional connection descriptors, and a Beacon-owned manifest path to a configured wrapper without copying Sunshine source |
| Moonlight/Artemis Android clients | Android client behavior reference for local-vs-server ownership | No | `src/Beacon.Android` is original Java code for Beacon's control plane only; no client streaming source is copied |
| Apollo | SudoVDA integration, display lifecycle lessons, dynamic app discovery reference | No | Milestone 2/3 only after focused source audit |
| Apollo `third-party/sudovda/sudovda-ioctl.h` | SudoVDA interface GUID, protocol version, and IOCTL contract constants for C# driver boundary | Adapted protocol facts | `src/Beacon.Platform.Windows/Displays/WindowsDisplayApi.cs` |
| Local Steam files | Installed apps, library folders, and non-Steam shortcut launch ids | Parsed local user data only | `src/Beacon.Core/Games/Steam` and `src/Beacon.GameProbe` |
| Local Heroic files | Installed GOG and sideloaded app metadata | Parsed local user data only | `src/Beacon.Core/Games/Heroic` |
| Local Hydra database | Installed game rows and executable hints | Read-only SQLite query | `src/Beacon.Core/Games/Hydra` |
| SteamGridDB | Cover artwork lookup when an API key is supplied | API responses only, no key committed | `src/Beacon.Core/Games/SteamGridDb` |
| Windows user32/ntdll/process APIs | Session activity inspection for launched processes, child processes, and top-level windows | Native API calls only | `src/Beacon.Platform.Windows/Sessions` |
| Vibeshine | HDR/driver research reference | No | Research notes only until a specific patch is chosen |
| Vibepollo | Settings complexity anti-patterns and selected research reference | No | Research notes only |
| ApolloDisplayRescue | WPF recovery behavior reference | No | Milestone 4 only after focused source audit |
