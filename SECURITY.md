# Security boundaries

ArenaDrafter fails closed. A missing or inconsistent build file, signature, process path, IL2CPP export, class, field, pointer, collection size, resource type, price, or champion value stops the affected automation path.

## Allowed operations

- Launch Plarium Play and connect to the single validated official RAID process.
- Verify pinned SHA-256 fingerprints and the Plarium Authenticode subject.
- Write only the probe DLL path into RAID and invoke `LoadLibraryW`.
- Read the connected account's champion collection and official loaded catalog data.
- Journal Live Arena state without raw player account identifiers.
- Extract official RAID portraits and skill icons from local AssetBundles.
- Simulate drafts locally without submitting commands.
- After explicit arming, submit only guarded Live Arena queue, pick, ban, leader, result-return, reward, refill, Auto/Manual, configured opener-skill, and validated target actions.
- Prefer a visible owned free refill through `ApplyItem()` before considering a paid refill.
- Submit a paid refill only when the current-session opt-in is enabled and the exact visible Gems price is observed, bounded, sent by the host, and revalidated immediately before purchase.
- Record bounded passive reward, battle, and Mythical click-path diagnostics after an explicit user request.

## Prohibited operations

- Any Arena mode other than Live Arena.
- Gameplay mouse or keyboard input.
- Combat automation outside the explicitly configured Live Arena opener and RAID Auto mode.
- Any purchase or refill without an exact visible resource type and explicit current-session opt-in.
- Persistent hooks, patches, detours, direct game-memory writes, or network interception.
- Anti-cheat bypass, concealment, persistence, credential collection, or raw opponent account-identifier collection.
- Committing user strategies, opener configurations, dashboards, logs, traces, caches, generated builds, or account-specific data.
