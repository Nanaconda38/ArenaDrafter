# Preset Lineup pick rules

Pick Rules are available only in `Preset Lineup`. They modify one still-free team slot without changing the configured primary and substitute chain.

## Creating a rule

1. Open `Live Arena Strategy`, select `Preset Lineup`, then open `Pick Rules`.
2. Add one or more champions to `WHEN ENEMY PICKED` and choose `Any`, `All`, or `None`.
3. Choose an owned replacement champion and the target slot.
4. Save the rule. `Add optional conditions` is not required.

Optional conditions can require known opponent roles, specific locked player picks, a shared or exclusive draft, which side picked first, or a minimum number of visible opponent picks. Conditions inside one rule use AND. Unknown opponent roles never satisfy a role-count condition.

## Priority and fallback

- Rules are evaluated from top to bottom before every bot pick.
- The first matching rule for a free slot wins. Drag cards to change priority.
- A matched rule reserves its replacement for the target slot, even if that champion normally belongs to another slot.
- If two rules request the same replacement, the higher global rule reserves it.
- If a replacement is unavailable, that slot uses its normal primary/substitute chain; lower rules do not replace the failed rule.
- A slot already accepted by RAID is never changed. A rule that matches afterward is reported as late.
- Disabling, editing, or reordering rules is blocked while automation is armed.

Dry Run and Draft Lab use the production resolver. Their event logs show each tested condition as true or false, the winning rule, late matches, unavailable replacements, and the selected fallback.

Rules, ordered ban targets, leaders, and Preset Lineup are stored together in strategy file version 3. Version 1 and 2 files migrate automatically with an empty rule list.
