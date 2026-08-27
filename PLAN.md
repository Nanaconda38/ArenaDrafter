# ArenaDrafter implementation status

## Delivered

- .NET 8 WPF application, C++17 x64 probe, and MSTest project.
- Strict Plarium process, signer, path, and RAID 11.71.0 fingerprint validation.
- Official champion catalog and inventory, storage, and reserve inspection.
- Responsive Live Arena strategy workspace with `Adaptive Draft` and `Preset Lineup`.
- Ordered substitutes, Pick Rules, ban priorities, leader priorities, autosave, undo, and drag-and-drop ordering.
- Local Draft Lab using the production pick, ban, and leader engine.
- Guarded continuous Live Arena matchmaking, draft submission, result return, and battle limits.
- Battle Opener sequences for Legendary and Mythical champions, including alternate forms and explicit target policies.
- Fail-closed Auto/Manual/skill transitions through RAID's visible battle HUD.
- Immediate five-battle free-refill reward collection and free-item-first token refills.
- Guarded paid refills using the currently observed and revalidated Gems amount.
- Last-run, current-session, and all-time Live Arena dashboard totals.
- Passive reward, manual battle, and Mythical click-path diagnostics.
- Build-time HellHades role catalog matched to localized RAID clients by `BaseId`.
- Focused parser, migration, strategy, planner, opener, dashboard, and security tests.

## Live validation still required

- Validate one complete regular Live Arena session: queue, draft, ban, leader, opener, result return, and next queue.
- Validate opponent-leave handling across multiple consecutive battles.
- Validate the five-battle free refill is claimed and consumed before any paid refill.
- Validate one paid refill and confirm the exact displayed Gems cost is recorded in the dashboard.
- Validate Battle Openers with no configured sequence, explicit ally/enemy targets, AoE skills, self skills, Mythical transformation, extra turns, and alternate-form follow-up skills.
- Stop immediately and preserve diagnostics on any unsupported RAID build or state mismatch.
