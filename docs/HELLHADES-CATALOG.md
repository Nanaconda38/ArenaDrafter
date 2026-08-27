# HellHades Arena catalog update

The application never queries HellHades at runtime. An authorized maintainer compiles one embedded snapshot before a release:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Update-HellHadesArenaCatalog.ps1
```

The updater downloads the HellHades RAID export once, keeps Rare, Epic, Legendary, and Mythical champions with a released RAID identity, and fetches the form-specific rating records for Mythical champions. It writes `src/RslArenaResearch/Data/hellhades-arena-catalog.json` atomically.

## Identity and language compatibility

Runtime matching uses RAID's numeric `HeroType.BaseId` only. It never compares the localized RAID name with the English HellHades name.

HellHades currently exports the fully ascended hero identity, whose final digit is `6`. The updater derives the model Base ID by removing that final ascension digit. It rejects non-positive identities, non-model results, duplicate Base IDs, unknown role tokens, malformed forms, and implausible catalog sizes. A missing entry is treated as a new or not-yet-tagged champion and uses the existing RAID marker fallback. A rarity difference is reported for maintenance but never disconnects the probe: RAID's loaded rarity remains authoritative while the BaseId-matched Arena roles stay available.

The English name, HellHades post ID, source URL, and source update date are retained for audit only. HellHades article text and portrait URLs are not embedded. The application continues to use official RAID resources for portraits.

## Update policy

Run the updater for every application data release and after announced champion releases or balance changes. The source endpoint advertises a one-day cache, so scheduled checks should run no more than once per day. Review the generated diff, build in Release, and run the tests before shipping.

Mythical `arenaRoles` is the union used during drafting. Each form is also stored separately under `forms` for future form-aware decisions.
