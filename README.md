# ArenaDrafter

Windows research application for RAID: Shadow Legends Live Arena (RTA). It provides champion inspection, adaptive and preset drafting, pick rules, ban and leader priorities, a local Draft Lab, configurable battle openers, continuous Live Arena sessions, reward/refill handling, and session statistics.

This repository contains only the Live Arena application, its native IL2CPP probe, catalog compiler, and relevant tests. Classic Arena code, observers, mapper artifacts, saved teams, local strategy files, logs, traces, caches, and build outputs are intentionally excluded.

## Live Arena features

- `Adaptive Draft`: a 5-20 champion pool with multi-role priorities and opponent-aware planning.
- `Preset Lineup`: five deterministic lanes with ordered substitutes.
- Ordered `Pick Rules` for observable opponent picks and draft conditions.
- Shared searchable ban and leader priorities.
- `Draft Lab` simulation using the production decision engine without sending RAID commands.
- Legendary and Mythical Battle Openers with localized skill names, official icons, both Mythical forms, target policies, and guarded Auto/Manual transitions.
- Continuous sessions with a 1-999 battle limit, opponent-leave recovery, daily reward collection, free-refill-first handling, guarded paid refills, and dashboard statistics.
- Official RAID portraits and skill icons extracted from local AssetBundles.
- Embedded HellHades Arena role snapshot joined to RAID champions exclusively by numeric `BaseId`.

## Requirements

- Windows 11 x64 with administrator access
- .NET SDK 8
- CMake, Ninja, and MinGW-w64 on `PATH`
- RAID build 11.71.0 installed through Plarium Play

## Build and test

```powershell
dotnet restore RslArenaResearch.sln
dotnet build RslArenaResearch.sln --configuration Release
dotnet test RslArenaResearch.sln --configuration Release
```

The WPF build also compiles `native/RslArenaProbe/RslArenaProbe.dll` and copies it beside the application executable.

## Run

Start `src/RslArenaResearch/artifacts/current/Release/net8.0-windows/RslArenaResearch.exe`.

The application validates the official RAID process path, Authenticode signer, and pinned build fingerprints before loading the probe. Any mismatch fails closed.

Local user data is stored under `%LOCALAPPDATA%\RslArenaResearch` and is ignored by Git. No saved strategy, opener, dashboard, log, trace, portrait cache, or account-specific file belongs in this repository.

## Documentation

- [Pick Rules](docs/PICK-RULES.md)
- [HellHades catalog compilation](docs/HELLHADES-CATALOG.md)
- [Security boundaries](SECURITY.md)
- [Implementation status](PLAN.md)

## Supported build

- RAID 11.71.0
- `Raid.exe`: `45F41A9199400AABA7B7C44B8862C2C6F7F3BC2BBCBC3CE23E26A70013A4AF8F`
- `GameAssembly.dll`: `37294C7F2B7F70B0F949BE67A07B977ECBF489172EFD06B0807F83995A2B87D6`
- `global-metadata.dat`: `1711C7F5865713F3437ED578006E3FAD7480324076ABDF68A462B9EEAAE016CA`
