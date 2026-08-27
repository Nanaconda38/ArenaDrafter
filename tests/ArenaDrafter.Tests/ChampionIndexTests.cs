using ArenaDrafter;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Media;

namespace ArenaDrafter.Tests;

[TestClass]
public sealed class ChampionIndexTests
{
    private const string ValidSnapshot = """
        {"protocol":1,"type":"snapshot","revision":2,"champions":[
          {"id":10,"typeId":101,"baseId":100,"name":"Alpha","grade":6,"ascension":1,"level":60,"empowerment":2,"locked":true,"inStorage":false,"inBathhouse":false,"awakening":4,"rarity":5,"affinity":1,"faction":2},
          {"id":11,"typeId":101,"baseId":100,"name":"Alpha","grade":5,"ascension":1,"level":50,"empowerment":0,"locked":false,"inStorage":true,"inBathhouse":false,"awakening":0,"rarity":5,"affinity":1,"faction":2}
        ]}
        """;

    private const string ValidBattle = """
        {"protocol":1,"type":"battle","revision":3,"active":true,"kind":6,"stageId":42,"round":2,"turn":7,"activeHeroId":1,"finished":false,"autoMode":false,"heroes":[
          {"id":1,"typeId":21110,"baseId":21110,"name":"Alpha","team":"Ally","level":60,"grade":6,"slot":0,"health":12345,"maxHealth":50000,"dead":false,"skills":[{"typeId":10,"slot":0,"name":"Opening hit","target":2,"cooldown":0,"maxCooldown":3,"disabled":false,"requiresTarget":true}],"effects":[{"typeId":20,"turns":2}]},
          {"id":200,"typeId":31110,"baseId":31110,"name":"Enemy","team":"Enemy","level":60,"grade":6,"slot":0,"health":40000,"maxHealth":40000,"dead":false,"skills":[],"effects":[]}
        ],"hudVisible":true,"modeChangeAvailable":true,"skillSelectionAvailable":true,"hudSkillCount":1,"hudSkills":[{"index":0,"typeId":10,"cooldown":0,"passive":false}]}
        """;

    private const string ValidLiveArena = """
        {"protocol":1,"type":"liveArena","matchmaking":false,"position":null,"ui":{"menuVisible":false,"queueAvailable":false,"finishVisible":false,"refillVisible":false,"refillCanConfirm":false},"draft":{"revision":4,"phase":"heroBan","firstTurn":"opponent","turn":"player","leagueId":12,"allowDuplicatePicks":true,"secondsRemaining":17,"turnSeconds":30,"playerHeroes":[{"slot":0,"id":10,"typeId":4756,"baseId":4750,"name":"Siphi"}],"enemyHeroes":[{"slot":0,"typeId":9816,"baseId":9810,"name":"Leminisi"}],"bestEnemyBlockedSlot":0,"playerBlockedSlot":null,"enemyBlockedSlot":0,"playerLeaderSlot":null,"enemyLeaderSlot":null,"battleSetupReady":false},"transport":{"active":false,"friendly":false,"finished":false,"revision":null,"turnRevision":null,"phase":null,"turn":null,"queuedCommands":0}}
        """;

    [TestMethod]
    public void ValidSnapshotPreservesDuplicateChampionTypesAsInstances()
    {
        var snapshot = SnapshotParser.Parse(ValidSnapshot, 1);
        Assert.AreEqual(2, snapshot.Champions.Length, "Both champion instances must be preserved.");
        Assert.AreEqual(snapshot.Champions[0].TypeId, snapshot.Champions[1].TypeId, "The fixture must contain duplicate champion types.");
        Assert.AreNotEqual(snapshot.Champions[0].Id, snapshot.Champions[1].Id, "Duplicate champion types must have distinct instance identifiers.");
    }

    [TestMethod]
    public void InvalidJsonIsRejected()
    {
        try { SnapshotParser.Parse("not-json", 0); }
        catch (System.Text.Json.JsonException) { return; }
        Assert.Fail("Malformed JSON must be rejected.");
    }

    [TestMethod]
    public void StaleRevisionIsRejected() =>
        Assert.ThrowsException<InvalidDataException>(() => SnapshotParser.Parse(ValidSnapshot, 2), "A stale snapshot revision must be rejected.");

    [TestMethod]
    public void DuplicateInstanceIdentifiersAreRejected()
    {
        var duplicated = ValidSnapshot.Replace("\"id\":11", "\"id\":10", StringComparison.Ordinal);
        Assert.ThrowsException<InvalidDataException>(() => SnapshotParser.Parse(duplicated, 1), "Duplicate instance identifiers must be rejected.");
    }

    [TestMethod]
    public void ActiveBattleSnapshotIsParsed()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2);
        Assert.IsTrue(battle.Active, "The battle must be active.");
        Assert.AreEqual(2, battle.Heroes.Length, "Both battle teams must be preserved.");
        Assert.AreEqual(1, battle.ActiveHeroId, "The active battle hero identifier must be preserved.");
        Assert.IsTrue(battle.HudVisible, "The visible battle HUD must be exposed before automation submits an action.");
        Assert.IsTrue(battle.SkillSelectionAvailable, "The battle skill selector readiness must be preserved.");
        Assert.AreEqual(1, battle.HudSkillCount, "The visible battle skill count must be preserved.");
        Assert.AreEqual(10, battle.HudSkills[0].TypeId, "The exact visible HUD skill identity must be preserved for diagnostics.");
        var missingHudSkills = ValidBattle.Replace(",\"hudSkills\":[{\"index\":0,\"typeId\":10,\"cooldown\":0,\"passive\":false}]", string.Empty, StringComparison.Ordinal);
        Assert.ThrowsException<InvalidDataException>(() => BattleSnapshotParser.Parse(missingHudSkills, 2),
            "Missing HUD skill diagnostics must fail closed.");
    }

    [TestMethod]
    public void DuplicateBattleHeroIdentifiersAreRejected()
    {
        var duplicated = ValidBattle.Replace("\"id\":200", "\"id\":1", StringComparison.Ordinal);
        Assert.ThrowsException<InvalidDataException>(() => BattleSnapshotParser.Parse(duplicated, 2), "Duplicate battle hero identifiers must be rejected.");
    }

    [TestMethod]
    public void LiveArenaDraftIsParsedAndInvalidSlotsAreRejected()
    {
        var draft = LiveArenaSnapshotParser.Parse(ValidLiveArena);
        Assert.AreEqual("heroBan", draft.Draft.Phase, "The Live Arena draft phase must be preserved.");
        Assert.AreEqual(12, draft.Draft.LeagueId, "The Live Arena league must be preserved.");
        Assert.IsTrue(draft.Draft.AllowDuplicatePicks, "The exact duplicate-pick rule must be preserved.");
        Assert.AreEqual(17, draft.Draft.SecondsRemaining, "The visible RAID draft timer must be preserved.");
        Assert.AreEqual("Leminisi", draft.Draft.EnemyHeroes[0].Name, "The opponent pick must be preserved.");
        var invalid = ValidLiveArena.Replace("\"enemyBlockedSlot\":0", "\"enemyBlockedSlot\":5", StringComparison.Ordinal);
        Assert.ThrowsException<InvalidDataException>(() => LiveArenaSnapshotParser.Parse(invalid), "Out-of-range Live Arena slots must be rejected.");
        var invalidLeague = ValidLiveArena.Replace("\"leagueId\":12", "\"leagueId\":99", StringComparison.Ordinal);
        Assert.ThrowsException<InvalidDataException>(() => LiveArenaSnapshotParser.Parse(invalidLeague), "Unknown Live Arena leagues must be rejected.");
        var invalidTimer = ValidLiveArena.Replace("\"secondsRemaining\":17", "\"secondsRemaining\":601", StringComparison.Ordinal);
        Assert.ThrowsException<InvalidDataException>(() => LiveArenaSnapshotParser.Parse(invalidTimer), "Out-of-range Live Arena timers must be rejected.");
        var invalidUi = ValidLiveArena.Replace("\"refillCanConfirm\":false", "\"refillCanConfirm\":true", StringComparison.Ordinal);
        Assert.ThrowsException<InvalidDataException>(() => LiveArenaSnapshotParser.Parse(invalidUi), "A hidden refill must never be confirmable.");
    }

    [TestMethod]
    public void StaticChampionCatalogRejectsDuplicateBaseIdentities()
    {
        const string valid = "{\"protocol\":1,\"type\":\"catalog\",\"champions\":[{\"typeId\":101,\"baseId\":100,\"name\":\"Alpha\",\"rarity\":5,\"skills\":[{\"typeId\":10,\"slot\":0,\"name\":\"Hit\",\"target\":2,\"cooldown\":0}]}]}";
        Assert.AreEqual("Alpha", CatalogParser.Parse(valid).Champions.Single().Name, "The catalog champion name must be preserved.");
        const string mythical = "{\"protocol\":1,\"type\":\"catalog\",\"champions\":[{\"typeId\":101,\"baseId\":100,\"name\":\"Alpha\",\"rarity\":6,\"skills\":[{\"typeId\":10,\"slot\":0,\"name\":\"Base hit\",\"target\":2,\"cooldown\":0,\"variant\":0,\"requiresTarget\":true},{\"typeId\":20,\"slot\":0,\"name\":\"Alternate hit\",\"target\":2,\"cooldown\":0,\"variant\":1,\"requiresTarget\":true},{\"typeId\":21,\"slot\":1,\"name\":\"Alternate area skill\",\"target\":2,\"cooldown\":3,\"variant\":1,\"requiresTarget\":false},{\"typeId\":22,\"slot\":2,\"name\":\"Alternate support skill\",\"target\":1,\"cooldown\":4,\"variant\":1,\"requiresTarget\":false}]}]}";
        Assert.AreEqual(4, CatalogParser.Parse(mythical).Champions.Single().Skills.Length, "Both mythical forms must preserve all of their active skill slots.");
        const string duplicatedFormSlot = "{\"protocol\":1,\"type\":\"catalog\",\"champions\":[{\"typeId\":101,\"baseId\":100,\"name\":\"Alpha\",\"rarity\":6,\"skills\":[{\"typeId\":10,\"slot\":0,\"name\":\"Hit\",\"target\":2,\"cooldown\":0,\"variant\":1},{\"typeId\":20,\"slot\":0,\"name\":\"Other\",\"target\":2,\"cooldown\":0,\"variant\":1}]}]}";
        Assert.ThrowsException<InvalidDataException>(() => CatalogParser.Parse(duplicatedFormSlot), "Duplicate slots inside one mythical form must be rejected.");
        const string duplicated = "{\"protocol\":1,\"type\":\"catalog\",\"champions\":[{\"typeId\":101,\"baseId\":100,\"name\":\"Alpha\",\"rarity\":5,\"skills\":[]},{\"typeId\":102,\"baseId\":100,\"name\":\"Alpha variant\",\"rarity\":5,\"skills\":[]}]}";
        Assert.ThrowsException<InvalidDataException>(() => CatalogParser.Parse(duplicated), "Duplicate catalog base identities must be rejected.");
    }

    [TestMethod]
    public void DraftPlannerBuildsABanResilientTeamAndRejectsDuplicateChampionTypes()
    {
        ArenaCandidate[] pool =
        [
            new(1, 101, 1001, "Speed one", ArenaRole.Initiative | ArenaRole.Opener, 0),
            new(2, 102, 1002, "Speed two", ArenaRole.Initiative | ArenaRole.Utility, 1),
            new(3, 103, 1003, "Damage one", ArenaRole.Damage, 2),
            new(4, 104, 1004, "Damage two", ArenaRole.Damage | ArenaRole.Control, 3),
            new(5, 105, 1005, "Controller", ArenaRole.Control | ArenaRole.Opener, 4),
            new(6, 106, 1006, "Cleanser", ArenaRole.Cleanse | ArenaRole.Sustain, 5),
            new(7, 107, 1007, "Protector", ArenaRole.Protection, 6),
            new(8, 108, 1008, "Utility", ArenaRole.Utility | ArenaRole.Opener, 7),
            new(9, 109, 1009, "Hybrid damage", ArenaRole.Damage | ArenaRole.Utility, 8),
            new(10, 110, 1010, "Hybrid support", ArenaRole.Cleanse | ArenaRole.Protection, 9)
        ];
        var recommendation = ArenaDraftPlanner.Recommend(pool, [], [], 1, 2);
        Assert.AreEqual(1, recommendation.Picks.Length, "The requested pick batch size must be preserved.");
        Assert.IsTrue(ArenaDraftPlanner.EvaluateTeam(recommendation.TargetTeam).ValidAfterEveryBan, "The target team must remain structurally valid after every possible ban.");
        var duplicated = pool.ToArray();
        duplicated[9] = duplicated[9] with { BaseId = duplicated[0].BaseId };
        Assert.ThrowsException<InvalidDataException>(() => ArenaDraftPlanner.Recommend(duplicated, [], [], 1, 2), "Duplicate champion base identifiers must be rejected.");
    }

    [TestMethod]
    public void CompiledArenaCatalogUsesRaidBaseIdInsteadOfLocalizedName()
    {
        var champions = Enumerable.Range(0, 100).Select(index => new
        {
            baseId = 1000 + index * 10,
            hellHadesPostId = 2000 + index,
            englishName = $"English champion {index}",
            rarity = 3 + index % 4,
            arenaRoles = new[] { index == 0 ? "speedmanipulator" : "damagedealer" },
            forms = new[] { new { form = 1, arenaRating = 8, arenaRoles = new[] { index == 0 ? "speedmanipulator" : "damagedealer" } } },
            sourceUrl = $"https://hellhades.com/raid/champions/champion-{index}/",
            sourceUpdated = "August 13, 2026"
        }).ToArray();
        var json = JsonSerializer.Serialize(new
        {
            version = 1,
            generatedUtc = DateTimeOffset.UtcNow,
            source = "https://hellhades.com/wp-json/hh-api/v3/raid/export",
            champions
        });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var catalog = HellHadesArenaCatalog.Load(stream);
        var localizedRaidChampion = new ChampionCatalogWire(1006, 1000, "Champion localisé", 3, []);
        var rarityDrift = new ChampionCatalogWire(1016, 1010, "Autre nom localisé", 3, []);
        var compatibility = catalog.ValidateAgainstRaid([localizedRaidChampion, rarityDrift]);

        Assert.AreEqual(ArenaRole.Initiative | ArenaRole.Utility, catalog.RolesFor(localizedRaidChampion.BaseId));
        Assert.AreEqual(ArenaRole.Damage, catalog.RolesFor(rarityDrift.BaseId), "Role data must remain usable when only the advisory HellHades rarity is stale.");
        Assert.AreEqual(2, compatibility.Matched);
        CollectionAssert.AreEqual(new[] { 1010 }, compatibility.RarityMismatchBaseIds, "All rarity differences must be reported without disconnecting RAID.");
    }

    [TestMethod]
    public void EmbeddedArenaCatalogContainsMythicalForms()
    {
        var catalog = HellHadesArenaCatalog.LoadEmbedded();

        Assert.AreEqual(943, catalog.Count);
        Assert.IsTrue(catalog.TryGetChampion(8250, out var ladyMikage));
        Assert.AreEqual("Lady Mikage", ladyMikage?.EnglishName);
        Assert.AreEqual(2, ladyMikage?.Forms.Count);
        Assert.IsTrue((ladyMikage?.Roles & ArenaRole.Initiative) != 0);
        Assert.IsTrue((ladyMikage?.Forms.Single(form => form.Form == 2).Roles & ArenaRole.Control) != 0);

        Assert.IsTrue(catalog.TryGetChampion(10620, out var hornedViperEntry));
        Assert.IsNotNull(hornedViperEntry);
        Assert.AreEqual("Horned Viper", hornedViperEntry.EnglishName);
        Assert.AreEqual(3, hornedViperEntry.Rarity, "Horned Viper must remain compiled as a Rare champion.");
        var hornedViper = catalog.ValidateAgainstRaid([new ChampionCatalogWire(10626, 10620, "Vipère cornue", 3, [])]);
        Assert.AreEqual(0, hornedViper.RarityMismatchBaseIds.Length,
            "The localized RAID identity must match the corrected HellHades snapshot by BaseId.");
        Assert.IsTrue((catalog.RolesFor(10620) & ArenaRole.Control) != 0);
    }

    [TestMethod]
    public void KnownOpponentRolesDriveAutomaticBanOutsideThePlayerPool()
    {
        var pool = Enumerable.Range(1, 10).Select(index => new ArenaStrategyCandidate(index, 100 + index, 1000 + index, $"Player {index}",
            index % 2 == 0 ? ArenaRole.Damage | ArenaRole.Control : ArenaRole.Initiative | ArenaRole.Utility, index, index)).ToList();
        var strategy = new ArenaStrategyFile(ArenaStrategyFile.CurrentVersion, pool, []);
        var player = Enumerable.Range(0, 5).Select(slot => new LiveArenaHeroWire(slot, slot + 1, 101 + slot, 1001 + slot, $"Player {slot + 1}")).ToArray();
        var enemy = Enumerable.Range(0, 5).Select(slot => new LiveArenaHeroWire(slot, null, 501 + slot, 5001 + slot, $"Localized enemy {slot + 1}")).ToArray();
        var snapshot = new LiveArenaSnapshotMessage(1, "liveArena", false, null,
            new LiveArenaDraftWire(8, "heroBan", "player", "player", 21, false, player, enemy, null, null, null, null, null, false),
            new(false, false, false, null, null, null, null, 0), new(false, false, false, false, false));

        var decision = LiveArenaDecisionEngine.Decide(snapshot, strategy, new Dictionary<int, ArenaRole> { [5004] = ArenaRole.Initiative | ArenaRole.Opener });

        Assert.AreEqual(3, decision?.Values.Single(), "The imported Base ID roles must identify the strongest opponent threat without using its localized name.");
    }

    [TestMethod]
    public void FiveChampionPoolWorksWhenRaidAllowsSharedPicks()
    {
        var pool = Enumerable.Range(1, 5).Select(index => new ArenaStrategyCandidate(index, 100 + index, 1000 + index, $"Player {index}",
            index < 3 ? ArenaRole.Initiative | ArenaRole.Utility : ArenaRole.Damage | ArenaRole.Protection, index, index)).ToList();
        var strategy = new ArenaStrategyFile(ArenaStrategyFile.CurrentVersion, pool, []);
        var enemy = new[] { new LiveArenaHeroWire(0, null, 101, 1001, "Shared champion") };
        var snapshot = new LiveArenaSnapshotMessage(1, "liveArena", false, null,
            new LiveArenaDraftWire(1, "heroPick", "player", "player", 12, true, [], enemy, null, null, null, null, null, false),
            new LiveArenaTransportWire(false, false, false, null, null, null, null, 0),
            new LiveArenaUiWire(false, false, false, false, false));

        Assert.AreEqual("pick", LiveArenaDecisionEngine.Decide(snapshot, strategy)?.Action,
            "An opponent pick must not remove the same champion from a duplicate-allowed draft.");
    }

    [TestMethod]
    public void PresetLineupUsesOrderedSubstitutesOnlyWhenRequired()
    {
        var slots = new List<ArenaPresetSlot>
        {
            new([new(1, 101, 1001, "Primary one"), new(11, 111, 2001, "Substitute one")]),
            new([new(2, 102, 1002, "Primary two")]),
            new([new(3, 103, 1003, "Primary three")]),
            new([new(4, 104, 1004, "Primary four")]),
            new([new(5, 105, 1005, "Primary five")])
        };

        var exclusive = PresetLineupResolver.Resolve(slots, [], [1001], false, 2);
        CollectionAssert.AreEqual(new long[] { 11, 2 }, exclusive.Picks.Select(candidate => candidate.InstanceId).ToArray(),
            "An exclusive opponent pick must select the first substitute and preserve slot order.");
        StringAssert.Contains(exclusive.Explanation, "Substitute one (substitute 1)");

        var shared = PresetLineupResolver.Resolve(slots, [], [1001], true, 2);
        CollectionAssert.AreEqual(new long[] { 1, 2 }, shared.Picks.Select(candidate => candidate.InstanceId).ToArray(),
            "A shared opponent pick must not displace the configured primary champion.");

        var continued = PresetLineupResolver.Resolve(slots, [2001], [1001], false, 2);
        CollectionAssert.AreEqual(new long[] { 2, 3 }, continued.Picks.Select(candidate => candidate.InstanceId).ToArray(),
            "Later pick batches must continue after the champion RAID already accepted.");
    }

    [TestMethod]
    public void PresetPickRuleCanOverrideASlotWithoutOptionalConditions()
    {
        var slots = PresetSlots();
        var rule = PickRule("Counter Siphi", [5001], 1, new(91, 191, 9001, "Tormin"));

        var matched = PresetLineupResolver.Resolve(slots, [], [5001], false, 2, [rule], "player");
        CollectionAssert.AreEqual(new long[] { 1, 91 }, matched.Picks.Select(candidate => candidate.InstanceId).ToArray());
        Assert.IsTrue(matched.RuleEvaluations.Single().Applied);
        StringAssert.Contains(matched.Explanation, "rule Counter Siphi");

        var unmatched = PresetLineupResolver.Resolve(slots, [], [5002], false, 2, [rule], "player");
        CollectionAssert.AreEqual(new long[] { 1, 2 }, unmatched.Picks.Select(candidate => candidate.InstanceId).ToArray());
        Assert.IsFalse(unmatched.RuleEvaluations.Single().Matched);
    }

    [TestMethod]
    public void PresetPickRuleSupportsAllFiltersAndUnknownRolesDoNotCount()
    {
        var rule = PickRule("Filtered counter", [5001, 5002], 1, new(91, 191, 9001, "Counter")) with
        {
            EnemyMatch = ArenaChampionMatch.All,
            EnemyRoles = ArenaRole.Initiative | ArenaRole.Opener,
            MinimumEnemyRoleCount = 1,
            PlayerMatch = ArenaChampionMatch.Any,
            PlayerBaseIds = [1001],
            DraftRule = ArenaPickRuleDraft.Exclusive,
            FirstTurn = ArenaPickRuleFirstTurn.Opponent,
            MinimumVisibleEnemyPicks = 2
        };
        var roles = new Dictionary<int, ArenaRole> { [5002] = ArenaRole.Initiative };

        var matched = PresetLineupResolver.Resolve(PresetSlots(), [1001], [5001, 5002], false, 2, [rule], "opponent", roles);
        Assert.AreEqual(9001, matched.Team[1].BaseId);

        var unknownRoles = PresetLineupResolver.Resolve(PresetSlots(), [1001], [5001, 5002], false, 2, [rule], "opponent", new Dictionary<int, ArenaRole>());
        Assert.AreEqual(1002, unknownRoles.Team[1].BaseId, "An unknown opponent role must not satisfy a role-count filter.");
        var unknownDraftRule = PresetLineupResolver.Resolve(PresetSlots(), [1001], [5001, 5002], false, 2, [rule], "opponent", roles, false);
        Assert.AreEqual(1002, unknownDraftRule.Team[1].BaseId, "A shared/exclusive condition must not match while RAID's draft rule is unknown.");
    }

    [TestMethod]
    public void PresetPickRulesRespectPriorityReservationFallbackAndLateSlots()
    {
        var slots = PresetSlots();
        slots[0].Candidates.Add(new(11, 111, 2001, "Slot one substitute"));
        var targetWins = PickRule("Move primary", [5001], 2, slots[0].Candidates[0]);
        var resolution = PresetLineupResolver.Resolve(slots, [], [5001], false, 2, [targetWins], "player");
        Assert.AreEqual(2001, resolution.Team[0].BaseId, "The rule target must reserve a champion that normally belongs to another slot.");
        Assert.AreEqual(1001, resolution.Team[2].BaseId);

        var unavailable = targetWins with { Replacement = new(91, 191, 9001, "Blocked") };
        var fallback = PresetLineupResolver.Resolve(slots, [], [5001, 9001], false, 2, [unavailable], "player");
        Assert.AreEqual(1003, fallback.Team[2].BaseId);
        StringAssert.Contains(fallback.RuleEvaluations.Single().Explanation, "base substitutes");

        var late = PresetLineupResolver.Resolve(slots, [1003], [5001], false, 1, [targetWins], "player");
        Assert.AreEqual(1003, late.Team[2].BaseId);
        StringAssert.Contains(late.RuleEvaluations.Single().Explanation, "was locked");
    }

    [TestMethod]
    public void FirstMatchingRuleWinsPerSlotAndGloballyReservesItsReplacement()
    {
        var first = PickRule("First", [5001], 1, new(91, 191, 9001, "Shared counter"));
        var sameSlot = PickRule("Same slot", [5001], 1, new(92, 192, 9002, "Lower counter"));
        var sameChampion = PickRule("Same champion", [5001], 2, first.Replacement);

        var result = PresetLineupResolver.Resolve(PresetSlots(), [], [5001], false, 2, [first, sameSlot, sameChampion], "player");

        Assert.AreEqual(9001, result.Team[1].BaseId);
        Assert.AreEqual(1003, result.Team[2].BaseId);
        StringAssert.Contains(result.RuleEvaluations[1].Explanation, "higher-priority rule");
        StringAssert.Contains(result.RuleEvaluations[2].Explanation, "unavailable");
    }

    [TestMethod]
    public void AcceptedRuleReplacementStaysLockedWhenANoneConditionLaterExpires()
    {
        var rule = PickRule("Use until counter appears", [5009], 0, new(91, 191, 9001, "Early replacement")) with
        {
            EnemyMatch = ArenaChampionMatch.None
        };
        var first = PresetLineupResolver.Resolve(PresetSlots(), [], [], false, 1, [rule], "player");
        Assert.AreEqual(9001, first.Picks.Single().BaseId);

        var continued = PresetLineupResolver.Resolve(PresetSlots(), [9001], [5009], false, 2, [rule], "player");
        Assert.AreEqual(9001, continued.Team[0].BaseId, "A later opponent pick must not remap an accepted rule replacement.");
        CollectionAssert.AreEqual(new[] { 1002, 1003 }, continued.Picks.Select(candidate => candidate.BaseId).ToArray());
    }

    [TestMethod]
    public void VersionTwoStrategyMigratesToEmptyPickRules()
    {
        var json = JsonSerializer.Serialize(new
        {
            Version = 2,
            Pool = Array.Empty<ArenaStrategyCandidate>(),
            BanPriority = Array.Empty<int>(),
            DraftMode = ArenaDraftMode.PresetLineup,
            PresetLineup = PresetSlots(),
            LeaderPriority = Array.Empty<int>()
        });

        var migrated = ArenaStrategyFile.Parse(json);

        Assert.AreEqual(ArenaStrategyFile.CurrentVersion, migrated.Version);
        Assert.AreEqual(0, migrated.PickRules?.Count);
        Assert.AreEqual(ArenaDraftMode.PresetLineup, migrated.DraftMode);
    }

    [TestMethod]
    public void PresetLineupFailsClearlyWhenASlotHasNoLegalChampion()
    {
        var slots = Enumerable.Range(1, 5)
            .Select(index => new ArenaPresetSlot([new(index, 100 + index, 1000 + index, $"Primary {index}")]))
            .ToList();
        var exception = Assert.ThrowsException<InvalidOperationException>(() => PresetLineupResolver.Resolve(slots, [], [1003], false, 1));
        Assert.AreEqual("Slot 3 has no available champion. Add another substitute.", exception.Message);

        var duplicated = new ArenaStrategyFile(ArenaStrategyFile.CurrentVersion, [], [], ArenaDraftMode.PresetLineup,
            [new([new(1, 101, 1001, "One")]), new([new(1, 101, 1001, "Duplicate")]), new([]), new([]), new([])], []);
        Assert.ThrowsException<InvalidDataException>(() => duplicated.Validate(false), "Ambiguous champions shared by multiple slots must be rejected.");
    }

    [TestMethod]
    public void LegacyAdaptiveStrategyMigratesWithoutLosingPriorities()
    {
        var pool = new List<ArenaStrategyCandidate>
        {
            new(1, 101, 1001, "First", ArenaRole.Damage, 0, 1),
            new(2, 102, 1002, "Leader", ArenaRole.Initiative, 1, 0)
        };
        var legacyJson = JsonSerializer.Serialize(new { Version = 1, Pool = pool, BanPriority = new[] { 5001 } });

        var migrated = ArenaStrategyFile.Parse(legacyJson);

        Assert.AreEqual(ArenaStrategyFile.CurrentVersion, migrated.Version);
        Assert.AreEqual(ArenaDraftMode.AdaptiveDraft, migrated.DraftMode);
        Assert.AreEqual(5, migrated.PresetLineup?.Count);
        CollectionAssert.AreEqual(new[] { 1002, 1001 }, migrated.LeaderPriority);
        CollectionAssert.AreEqual(new[] { 5001 }, migrated.BanPriority);
    }

    [TestMethod]
    public void PresetLineupRunsThroughTheProductionDecisionEngine()
    {
        var slots = Enumerable.Range(1, 5)
            .Select(index => new ArenaPresetSlot([new(index, 100 + index, 1000 + index, $"Primary {index}")]))
            .ToList();
        slots[0].Candidates.Add(new(11, 111, 2001, "Substitute one"));
        var strategy = new ArenaStrategyFile(ArenaStrategyFile.CurrentVersion, [], [5001], ArenaDraftMode.PresetLineup, slots, [1002, 1001]);
        var enemy = new[] { new LiveArenaHeroWire(0, null, 501, 1001, "Enemy took primary one") };
        var snapshot = new LiveArenaSnapshotMessage(1, "liveArena", false, null,
            new LiveArenaDraftWire(1, "heroPick", "opponent", "player", 21, false, [], enemy, null, null, null, null, null, false),
            new(false, false, false, null, null, null, null, 0), new(false, false, false, false, false, true));

        var decision = LiveArenaDecisionEngine.Decide(snapshot, strategy);

        Assert.AreEqual("pick", decision?.Action);
        CollectionAssert.AreEqual(new[] { 11, 2 }, decision?.Values);
        StringAssert.Contains(decision?.Explanation, "Preset Lineup");

        var player = Enumerable.Range(0, 5).Select(slot => new LiveArenaHeroWire(slot, slot + 1, 101 + slot, 1001 + slot, $"Primary {slot + 1}")).ToArray();
        var opponents = Enumerable.Range(0, 5).Select(slot => new LiveArenaHeroWire(slot, null, 501 + slot, 5001 + slot, $"Enemy {slot + 1}")).ToArray();
        var banSnapshot = snapshot with { Draft = snapshot.Draft with { Revision = 2, Phase = "heroBan", PlayerHeroes = player, EnemyHeroes = opponents } };
        Assert.AreEqual(0, LiveArenaDecisionEngine.Decide(banSnapshot, strategy)?.Values.Single(), "Preset Lineup must keep the shared ban order.");
        var leaderSnapshot = banSnapshot with { Draft = banSnapshot.Draft with { Revision = 3, Phase = "leaderSelection", PlayerBlockedSlot = 0 } };
        Assert.AreEqual(1, LiveArenaDecisionEngine.Decide(leaderSnapshot, strategy)?.Values.Single(), "Preset Lineup must keep the shared leader order and skip the banned champion.");
    }

    [TestMethod]
    public void DraftSimulatorRunsProductionPicksBanAndLeaderWithoutRaid()
    {
        var pool = Enumerable.Range(1, 10).Select(index => new ArenaStrategyCandidate(index, 100 + index, 1000 + index, $"Player {index}",
            index % 3 == 0 ? ArenaRole.Damage | ArenaRole.Control : ArenaRole.Initiative | ArenaRole.Cleanse | ArenaRole.Utility, index, index)).ToList();
        var simulator = new ArenaDraftSimulator(new(ArenaStrategyFile.CurrentVersion, pool, [5002]), true, true);

        Assert.AreEqual("pick", simulator.RunPlayerTurn().Action);
        simulator.AddOpponentPick(501, 5001, "Enemy 1");
        simulator.AddOpponentPick(502, 5002, "Enemy 2");
        simulator.RunPlayerTurn();
        simulator.AddOpponentPick(503, 5003, "Enemy 3");
        simulator.AddOpponentPick(504, 5004, "Enemy 4");
        simulator.RunPlayerTurn();
        simulator.AddOpponentPick(505, 5005, "Enemy 5");
        var result = simulator.Resolve(0);

        Assert.AreEqual(5, simulator.PlayerHeroes.Count);
        Assert.AreEqual("ban", result.Ban.Action);
        Assert.AreEqual(1, result.Ban.Values.Single(), "The configured simulated ban must target Enemy 2.");
        Assert.AreEqual("leader", result.Leader.Action);
        Assert.AreNotEqual(0, result.Leader.Values.Single(), "The simulated opponent-banned champion cannot be the leader.");
        Assert.AreEqual(ArenaRole.Initiative | ArenaRole.Utility, ArenaRolePresets.FromName("Speed Booster"));
    }

    [TestMethod]
    public void LiveArenaCommandsAreBoundedAndPreservePickOrder()
    {
        Assert.AreEqual("LIVE_PICK 30,10", LiveArenaCommands.Pick([30, 10]), "Live Arena pick order must be preserved.");
        Assert.AreEqual("LIVE_BAN 4", LiveArenaCommands.Ban(4), "The selected ban slot must be preserved.");
        Assert.AreEqual("LIVE_LEADER 0", LiveArenaCommands.Leader(0), "The selected leader slot must be preserved.");
        Assert.AreEqual("LIVE_QUEUE", LiveArenaCommands.Queue);
        Assert.AreEqual("LIVE_REFILL 0", LiveArenaCommands.Refill(0));
        Assert.AreEqual("LIVE_REFILL 40", LiveArenaCommands.Refill(40));
        Assert.AreEqual("LIVE_RETURN", LiveArenaCommands.Return);
        Assert.AreEqual("LIVE_REWARD_CLAIM 4", LiveArenaCommands.ClaimReward(4));
        Assert.AreEqual("LIVE_REWARD_CLOSE", LiveArenaCommands.CloseRewardOverlay);
        Assert.ThrowsException<InvalidDataException>(() => LiveArenaCommands.ClaimReward(0));
        Assert.ThrowsException<InvalidDataException>(() => LiveArenaCommands.Pick([1, 2, 3]), "More than two picks must be rejected.");
        Assert.ThrowsException<InvalidDataException>(() => LiveArenaCommands.Ban(5), "Out-of-range slots must be rejected.");
    }

    [TestMethod]
    public void LiveArenaDecisionUsesExplicitBanPriorityAndAvoidsBannedLeader()
    {
        var pool = Enumerable.Range(1, 10).Select(index => new ArenaStrategyCandidate(index, 100 + index, 1000 + index, $"Player {index}",
            index % 2 == 0 ? ArenaRole.Damage | ArenaRole.Control : ArenaRole.Initiative | ArenaRole.Utility, index, index)).ToList();
        var strategy = new ArenaStrategyFile(ArenaStrategyFile.CurrentVersion, pool, [5003, 5001]);
        var player = Enumerable.Range(0, 5).Select(slot => new LiveArenaHeroWire(slot, slot + 1, 101 + slot, 1001 + slot, $"Player {slot + 1}")).ToArray();
        var enemy = Enumerable.Range(0, 5).Select(slot => new LiveArenaHeroWire(slot, null, 501 + slot, 5001 + slot, $"Enemy {slot + 1}")).ToArray();
        var transport = new LiveArenaTransportWire(false, false, false, null, null, null, null, 0);
        var banSnapshot = new LiveArenaSnapshotMessage(1, "liveArena", false, null,
            new LiveArenaDraftWire(8, "heroBan", "player", "player", 21, false, player, enemy, 0, null, null, null, null, false), transport,
            new LiveArenaUiWire(false, false, false, false, false));
        var ban = LiveArenaDecisionEngine.Decide(banSnapshot, strategy);
        Assert.AreEqual("ban", ban?.Action, "The ban phase must produce a ban decision.");
        Assert.AreEqual(2, ban?.Values.Single(), "The first explicit Base ID present in the enemy draft must win.");

        var leaderSnapshot = banSnapshot with { Draft = banSnapshot.Draft with { Phase = "leaderSelection", PlayerBlockedSlot = 0 } };
        var leader = LiveArenaDecisionEngine.Decide(leaderSnapshot, strategy);
        Assert.AreEqual("leader", leader?.Action, "The leader phase must produce a leader decision.");
        Assert.AreEqual(1, leader?.Values.Single(), "A banned highest-priority leader must be skipped.");
    }

    [TestMethod]
    public void LiveArenaDecisionDoesNotResubmitWhileTheSameRaidTurnSettles()
    {
        var pool = Enumerable.Range(1, 10).Select(index => new ArenaStrategyCandidate(index, 100 + index, 1000 + index, $"Player {index}",
            index % 2 == 0 ? ArenaRole.Damage | ArenaRole.Control : ArenaRole.Initiative | ArenaRole.Utility, index, index)).ToList();
        var strategy = new ArenaStrategyFile(ArenaStrategyFile.CurrentVersion, pool, []);
        var enemy = Enumerable.Range(0, 3).Select(slot => new LiveArenaHeroWire(slot, null, 501 + slot, 5001 + slot, $"Enemy {slot + 1}")).ToArray();
        var selected = Enumerable.Range(0, 2).Select(slot => new LiveArenaHeroWire(slot, slot + 1, 101 + slot, 1001 + slot, $"Player {slot + 1}")).ToArray();
        var locallyConfirmed = Enumerable.Range(0, 4).Select(slot => new LiveArenaHeroWire(slot, slot + 1, 101 + slot, 1001 + slot, $"Player {slot + 1}")).ToArray();
        var transport = new LiveArenaTransportWire(false, false, false, null, null, null, null, 0);
        var snapshot = new LiveArenaSnapshotMessage(1, "liveArena", false, null,
            new LiveArenaDraftWire(3, "heroPick", "opponent", "player", 21, false, selected, enemy, null, null, null, null, null, false), transport,
            new LiveArenaUiWire(false, false, false, false, false, true));

        var initial = LiveArenaDecisionEngine.Decide(snapshot, strategy);
        var settling = LiveArenaDecisionEngine.Decide(snapshot with { Draft = snapshot.Draft with { PlayerHeroes = locallyConfirmed } }, strategy);

        Assert.AreEqual(initial?.Key, settling?.Key, "Local selection confirmation must not create a second action inside the same RAID turn revision.");
    }

    [TestMethod]
    public void ContinuousSessionQueuesReturnsAndRefillsOnlyWithExplicitOptIn()
    {
        var draft = new LiveArenaDraftWire(null, null, null, null, null, null, [], [], null, null, null, null, null, false);
        var transport = new LiveArenaTransportWire(false, false, false, null, null, null, null, 0);
        var menu = new LiveArenaSnapshotMessage(1, "liveArena", false, null, draft, transport, new(true, true, false, false, false));
        Assert.AreEqual("queue", LiveArenaSessionPlanner.Decide(menu, false)?.Action);

        var refill = menu with { Ui = new(false, false, false, true, true) };
        Assert.IsNull(LiveArenaSessionPlanner.Decide(refill, false), "A refill must never be confirmed without explicit opt-in.");
        var freeDecision = LiveArenaSessionPlanner.Decide(refill, true);
        Assert.AreEqual("refill", freeDecision?.Action);
        Assert.AreEqual(0, freeDecision?.BeforeValue);
        var paidDecision = LiveArenaSessionPlanner.Decide(refill with { Ui = refill.Ui with { RefillGemPrice = 40 } }, true);
        Assert.AreEqual(40, paidDecision?.BeforeValue);

        var finish = menu with { Ui = new(false, false, true, false, false) };
        Assert.AreEqual("return", LiveArenaSessionPlanner.Decide(finish, false)?.Action);
    }

    [TestMethod]
    public void ContinuousSessionOnlyDefersThePinnedReturnRace()
    {
        Assert.AreEqual(20, LiveArenaSessionPlanner.DeferredReturnMaxAttempts);
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), LiveArenaSessionPlanner.DeferredReturnRetryDelay);
        Assert.IsTrue(LiveArenaSessionPlanner.IsDeferredReturn(
            "return", "live-deferred", LiveArenaSessionPlanner.DeferredReturnMessage));
        Assert.IsFalse(LiveArenaSessionPlanner.IsDeferredReturn(
            "queue", "live-deferred", LiveArenaSessionPlanner.DeferredReturnMessage));
        Assert.IsFalse(LiveArenaSessionPlanner.IsDeferredReturn(
            "return", "live-error", LiveArenaSessionPlanner.DeferredReturnMessage));
        Assert.IsFalse(LiveArenaSessionPlanner.IsDeferredReturn(
            "return", "live-deferred", "Another error"));
    }

    [TestMethod]
    public void LiveArenaDashboardTracksBattlesAndExactRefillGemCosts()
    {
        var run = LiveArenaDashboardStats.Empty
            .AddBattle(LiveArenaBattleOutcome.Win)
            .AddBattle(LiveArenaBattleOutcome.Loss)
            .AddBattle(LiveArenaBattleOutcome.Unknown)
            .AddRefill(0)
            .AddRefill(40);
        run.Validate();
        Assert.AreEqual(new LiveArenaDashboardStats(3, 1, 1, 1, 2, 40), run);

        var file = LiveArenaDashboardFile.Empty.RecordBattle(LiveArenaBattleOutcome.Win).RecordRefill(20).FinishRun(run);
        file.Validate();
        Assert.AreEqual(run, file.LastRun);
        Assert.AreEqual(new LiveArenaDashboardStats(1, 1, 0, 0, 1, 20), file.AllTime);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => run.AddRefill(10001));
    }

    [TestMethod]
    public void ContinuousSessionClaimsOnlyACompleteDailyRewardBatchAndClosesBlockingOverlays()
    {
        var draft = new LiveArenaDraftWire(null, null, null, null, null, null, [], [], null, null, null, null, null, false);
        var transport = new LiveArenaTransportWire(false, false, false, null, null, null, null, 0);
        var ready = new LiveArenaSnapshotMessage(1, "liveArena", false, null, draft, transport,
            new(true, true, false, false, false, false, false, true, 4));

        var first = LiveArenaSessionPlanner.Decide(ready, false);
        Assert.AreEqual("reward-claim", first?.Action);
        Assert.AreEqual(4, first?.BeforeValue);

        var partial = ready with { Ui = ready.Ui with { RewardBatchReady = false, RewardClaimableCount = 3 } };
        Assert.AreEqual("queue", LiveArenaSessionPlanner.Decide(partial, false)?.Action,
            "A partial batch must remain unclaimed while matchmaking continues.");
        var freeRefill = partial with { Ui = partial.Ui with { DailyBattleRefillReady = true } };
        Assert.AreEqual("reward-refill", LiveArenaSessionPlanner.Decide(freeRefill, false)?.Action,
            "The five-battle free refill must be claimed without waiting for unrelated daily rewards.");
        Assert.AreEqual("reward-claim", LiveArenaSessionPlanner.Decide(partial, false, rewardBatchInProgress: true)?.Action);
        Assert.IsNull(LiveArenaSessionPlanner.Decide(partial, false, rewardBatchInProgress: true, rewardClaimWaiting: true));

        var overlay = partial with { Ui = partial.Ui with { RewardOverlayVisible = true } };
        Assert.AreEqual("reward-close", LiveArenaSessionPlanner.Decide(overlay, false, rewardBatchInProgress: true, rewardClaimWaiting: true)?.Action,
            "A blocking reward overlay must be closed even while the server-side claim state is settling.");
    }

    [TestMethod]
    public void ContinuousSessionCountsItsLimitAndClassifiesObservableBattleResults()
    {
        Assert.IsFalse(LiveArenaSessionPlanner.LimitReached(4, 5));
        Assert.IsTrue(LiveArenaSessionPlanner.LimitReached(5, 5));
        Assert.IsTrue(LiveArenaSessionPlanner.LimitReached(6, 5));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => LiveArenaSessionPlanner.LimitReached(0, 0));

        var battle = BattleSnapshotParser.Parse(ValidBattle, 2) with { Finished = true };
        var win = battle with { Heroes = [battle.Heroes[0], battle.Heroes[1] with { Health = 0, Dead = true }] };
        var loss = battle with { Heroes = [battle.Heroes[0] with { Health = 0, Dead = true }, battle.Heroes[1]] };
        Assert.AreEqual(LiveArenaBattleOutcome.Win, LiveArenaSessionPlanner.Outcome(win));
        Assert.AreEqual(LiveArenaBattleOutcome.Loss, LiveArenaSessionPlanner.Outcome(loss));
        Assert.AreEqual(LiveArenaBattleOutcome.Unknown, LiveArenaSessionPlanner.Outcome(battle),
            "A surrender with surviving champions on both teams must not be guessed as a win or loss.");
    }

    [TestMethod]
    public void BattleOpenerUsesConfiguredSkillThenEnablesAuto()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2);
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion, [new(21110, [0], SkillTypeIds: [10])]);
        var first = BattleOpenerPlanner.Decide(battle, opener, new Dictionary<int, int> { [21110] = 0 });
        Assert.AreEqual("skill", first?.Action);
        Assert.AreEqual(10, first?.SkillTypeId);
        Assert.AreEqual(200, first?.TargetId, "A single-target enemy skill must receive a legal enemy target.");
        Assert.IsTrue(first?.ConsumesConfiguredStep);

        var initialAuto = BattleOpenerPlanner.Decide(
            battle, opener, new Dictionary<int, int> { [21110] = 0 }, initialAutoVerified: false);
        Assert.AreEqual("auto", initialAuto?.Action, "Every battle must enter Auto before any configured skill is considered.");

        var pause = BattleOpenerPlanner.Decide(battle with { AutoMode = true }, opener, new Dictionary<int, int> { [21110] = 0 });
        Assert.AreEqual("manual", pause?.Action, "Auto must pause only when the configured champion becomes active.");

        var autoWithoutSkillHud = battle with { AutoMode = true, HudSkillCount = 0, HudSkills = [] };
        Assert.AreEqual("manual", BattleOpenerPlanner.Decide(autoWithoutSkillHud, opener,
            new Dictionary<int, int> { [21110] = 0 })?.Action,
            "Auto mode hides the skill collection; it must still switch to Manual before waiting for the HUD.");

        var betweenConfiguredTurns = BattleOpenerPlanner.Decide(battle with { ActiveHeroId = 200 }, opener, new Dictionary<int, int> { [21110] = 0 });
        Assert.AreEqual("auto", betweenConfiguredTurns?.Action, "Auto must handle every turn that has no pending configured step.");

        var completed = BattleOpenerPlanner.Decide(battle, opener, new Dictionary<int, int> { [21110] = 1 });
        Assert.AreEqual("auto", completed?.Action, "Auto must start as soon as every configured opening step is complete.");
        Assert.AreEqual("BATTLE_AUTO", BattleCommands.Auto);
        Assert.AreEqual("BATTLE_MANUAL", BattleCommands.Manual);
        Assert.AreEqual("BATTLE_SKILL 10,0,200", BattleCommands.Skill(10, 0, 200));
        Assert.AreEqual("BATTLE_DIAGNOSTICS START", BattleCommands.StartDiagnostics);
        Assert.AreEqual("BATTLE_DIAGNOSTICS STOP", BattleCommands.StopDiagnostics);
        Assert.AreEqual("MYTHICAL_CLICK_TRACE START", BattleCommands.StartMythicalClickTrace);
        Assert.AreEqual("MYTHICAL_CLICK_TRACE STOP", BattleCommands.StopMythicalClickTrace);
        Assert.AreEqual("REWARD_DIAGNOSTICS START", RewardDiagnosticCommands.Start);
        Assert.AreEqual("REWARD_DIAGNOSTICS STOP", RewardDiagnosticCommands.Stop);
    }

    [TestMethod]
    public void BattleOpenerRejectsAmbiguousLegacySlotOnlySkills()
    {
        var legacy = new BattleOpenerFile(BattleOpenerFile.CurrentVersion, [new(21110, [0])]);

        Assert.ThrowsException<InvalidDataException>(legacy.Validate,
            "A slot without an exact skill identity must never control a battle.");
    }

    [TestMethod]
    public void BattleOpenerWaitsUntilRaidEnablesBattleModeChanges()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2) with { ModeChangeAvailable = false };
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion, [new(21110, [0], SkillTypeIds: [10])]);

        Assert.IsNull(BattleOpenerPlanner.Decide(battle, opener, new Dictionary<int, int> { [21110] = 0 }, initialAutoVerified: false),
            "The opener must wait instead of submitting Auto while RAID's battle mode control is temporarily disabled.");
    }

    [TestMethod]
    public void BattleOpenerUsesConfiguredSkillWhileManualModeControlIsDisabled()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2) with { ModeChangeAvailable = false, AutoMode = false };
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion, [new(21110, [0], SkillTypeIds: [10])]);

        var decision = BattleOpenerPlanner.Decide(battle, opener, new Dictionary<int, int> { [21110] = 0 });

        Assert.AreEqual("skill", decision?.Action,
            "A ready configured skill must not depend on RAID keeping its Auto/Manual control enabled after switching to Manual.");
    }

    [TestMethod]
    public void BattleOpenerWaitsUntilConfiguredSkillIsVisibleOnBattleHud()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2) with
        {
            AutoMode = false,
            SkillSelectionAvailable = false,
            HudSkillCount = 0
        };
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion, [new(21110, [0], SkillTypeIds: [10])]);

        Assert.IsNull(BattleOpenerPlanner.Decide(battle, opener, new Dictionary<int, int> { [21110] = 0 }),
            "The opener must wait until RAID exposes the configured skill on the battle HUD.");
    }

    [TestMethod]
    public void BattleOpenerAllowsLongSkillAnimationsWithoutWeakeningModeVerification()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(1), BattleOpenerPlanner.HudStabilizationDelay,
            "RAID 11.71 must receive a stable HUD before a configured skill is submitted.");
        Assert.AreEqual(TimeSpan.FromSeconds(30), BattleOpenerPlanner.VerificationTimeout("skill"));
        Assert.AreEqual(TimeSpan.FromSeconds(8), BattleOpenerPlanner.VerificationTimeout("auto"));
        Assert.AreEqual(TimeSpan.FromSeconds(8), BattleOpenerPlanner.VerificationTimeout("manual"));
    }

    [TestMethod]
    public void BattleOpenerRoutesAreaSkillsWithoutAnExplicitTarget()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2);
        var areaSkill = battle.Heroes[0].Skills[0] with { Name = "Area attack", Target = 2, RequiresTarget = false };
        var areaBattle = battle with { Heroes = [battle.Heroes[0] with { Skills = [areaSkill] }, battle.Heroes[1]] };
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion,
            [new(21110, [0], [BattleTargetPolicies.FirstEnemy], [areaSkill.TypeId])]);

        var decision = BattleOpenerPlanner.Decide(areaBattle, opener, new Dictionary<int, int> { [21110] = 0 });

        Assert.AreEqual("skill", decision?.Action);
        Assert.AreEqual(200, decision?.TargetId, "An area skill still needs a legal enemy completion target without using explicit target selection.");
        Assert.IsFalse(decision?.RequiresExplicitTarget, "The catalog marks an area skill as non-targeted.");
        Assert.AreEqual("All enemies", new ChampionSkillCatalogWire(10, 0, "Area attack", 2, 3, 0, false).TargetLabel);
        CollectionAssert.AreEqual(new[] { BattleTargetPolicies.Automatic }, BattleTargetPolicies.Options(2, false).ToArray());
    }

    [TestMethod]
    public void BattleOpenerUsesTheActiveHeroToCompleteASelfSkill()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2);
        var metamorph = battle.Heroes[0].Skills[0] with { TypeId = 92004, Slot = 3, Name = "Metamorph", Target = 0, RequiresTarget = false };
        var mythicalBattle = battle with
        {
            Heroes = [battle.Heroes[0] with { BaseId = 9200, Skills = [metamorph] }, battle.Heroes[1]],
            HudSkillCount = 4
        };
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion,
            [new(9200, [3], [BattleTargetPolicies.Automatic], [metamorph.TypeId])]);

        var decision = BattleOpenerPlanner.Decide(mythicalBattle, opener, new Dictionary<int, int> { [9200] = 0 });

        Assert.AreEqual("skill", decision?.Action);
        Assert.AreEqual(1, decision?.TargetId, "A self skill must use the active hero as RAID's internal completion target.");
        Assert.IsFalse(decision?.RequiresExplicitTarget, "The catalog marks a self skill as non-targeted.");
    }

    [TestMethod]
    public void BattleOpenerAppliesConfiguredAllyTargetPolicy()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2);
        var active = battle.Heroes[0] with { Skills = [battle.Heroes[0].Skills[0] with { Target = 1 }] };
        var healthyAlly = active with
        {
            Id = 2,
            TypeId = 21111,
            BaseId = 21111,
            Name = "Healthy ally",
            Slot = 1,
            Health = 50000,
            Skills = [],
            Effects = []
        };
        var targetedBattle = battle with { Heroes = [active, healthyAlly, battle.Heroes[1]] };
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion,
            [new(21110, [0], [BattleTargetPolicies.HighestHpAlly], [active.Skills[0].TypeId])]);

        var decision = BattleOpenerPlanner.Decide(targetedBattle, opener, new Dictionary<int, int> { [21110] = 0 });

        Assert.AreEqual("skill", decision?.Action);
        Assert.AreEqual(2, decision?.TargetId, "The configured highest-health ally must receive the single-target skill.");
    }

    [TestMethod]
    public void BattleOpenerUsesExactSkillIdentityAcrossMythicalForms()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2);
        var transformedSkill = battle.Heroes[0].Skills[0] with { TypeId = 55, Slot = 4, Name = "Alternate-form skill" };
        var transformedBattle = battle with
        {
            Heroes = [battle.Heroes[0] with { Skills = [battle.Heroes[0].Skills[0], transformedSkill] }, battle.Heroes[1]],
            HudSkillCount = 5
        };
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion,
            [new(21110, [0], [BattleTargetPolicies.FirstEnemy], [55])]);

        var decision = BattleOpenerPlanner.Decide(transformedBattle, opener, new Dictionary<int, int> { [21110] = 0 });

        Assert.AreEqual(55, decision?.SkillTypeId, "The exact configured skill must win over a same-form slot fallback.");
        Assert.AreEqual(4, decision?.SkillSlot, "The current RAID slot must be used after a form change.");
    }

    [TestMethod]
    public void BattleOpenerVerifiesMythicalTransformationFromBattleState()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2);
        var basicSkill = battle.Heroes[0].Skills[0] with { TypeId = 92001, Slot = 0, Cooldown = 0, MaxCooldown = 0 };
        var otherSkill = basicSkill with { TypeId = 92002, Slot = 1, Cooldown = 0, MaxCooldown = 5 };
        var metamorph = basicSkill with { TypeId = 92004, Slot = 3, Name = "Metamorph", Cooldown = 0, MaxCooldown = 4 };
        var before = battle with
        {
            Turn = 21,
            Heroes = [battle.Heroes[0] with { BaseId = 9200, Skills = [basicSkill, otherSkill, metamorph] }, battle.Heroes[1]]
        };
        var decision = new BattleOpenerDecision("skill", 92004, 3, 0, 9200, true, "Transform.");
        var timedOutTurn = before with
        {
            Turn = 22,
            Heroes = [before.Heroes[0] with { Skills = [basicSkill, otherSkill with { Cooldown = 4 }, metamorph] }, before.Heroes[1]]
        };
        Assert.IsFalse(BattleOpenerPlanner.IsActionApplied(decision, before, timedOutTurn),
            "A turn increment caused by RAID's timer must not masquerade as a successful transformation.");
        Assert.IsNotNull(BattleOpenerPlanner.TerminalFailureReason(decision, before, timedOutTurn),
            "A changed turn without the requested transformation must trigger immediate Auto recovery.");
        Assert.IsNull(BattleOpenerPlanner.TerminalFailureReason(decision, before, before),
            "An unchanged turn remains pending until positive or terminal evidence appears.");

        var alternateSkill = basicSkill with { TypeId = 892001, Name = "Alternate-form A1" };
        var transformed = before with
        {
            Heroes = [before.Heroes[0] with { Skills = [alternateSkill] }, before.Heroes[1]]
        };
        Assert.IsTrue(BattleOpenerPlanner.IsActionApplied(decision, before, transformed),
            "A transformation is verified when RAID replaces the configured form's skill identity.");
        Assert.IsNull(BattleOpenerPlanner.TerminalFailureReason(decision, before, transformed));
        Assert.AreEqual(0, BattleOpenerPlanner.ProgressAfterFailedAction(decision, 0),
            "A failed configured skill must remain pending; only positive state evidence may advance the opener.");
        Assert.AreEqual(0, BattleOpenerPlanner.ProgressAfterFailedAction(decision with { ConsumesConfiguredStep = false }, 0));
    }

    [TestMethod]
    public void BattleOpenerKeepsAConfiguredMythicalChainAcrossExtraTurnsAndHudRebuilds()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2);
        var metamorph = battle.Heroes[0].Skills[0] with
        {
            TypeId = 92004,
            Slot = 3,
            Name = "Metamorph",
            Target = 0,
            RequiresTarget = false,
            MaxCooldown = 4
        };
        var alternateA2 = metamorph with
        {
            TypeId = 892002,
            Slot = 1,
            Name = "Alternate A2",
            Target = 2,
            RequiresTarget = true,
            MaxCooldown = 5
        };
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion,
            [new(9200, [3, 1], [BattleTargetPolicies.Automatic, BattleTargetPolicies.FirstEnemy], [92004, 892002])]);
        var baseForm = battle with
        {
            Turn = 10,
            ActiveHeroId = 1,
            AutoMode = false,
            Heroes = [battle.Heroes[0] with { BaseId = 9200, Skills = [metamorph] }, battle.Heroes[1]],
            HudSkillCount = 4,
            HudSkills = [new(3, 92004, 0, false)]
        };

        var first = BattleOpenerPlanner.Decide(baseForm, opener, new Dictionary<int, int> { [9200] = 0 });
        Assert.AreEqual("skill", first?.Action);
        Assert.AreEqual(92004, first?.SkillTypeId);

        var alternate = baseForm with
        {
            Turn = 11,
            Heroes = [baseForm.Heroes[0] with { Skills = [alternateA2] }, baseForm.Heroes[1]],
            HudSkillCount = 2,
            HudSkills = [new(1, 892002, 0, false)]
        };
        var second = BattleOpenerPlanner.Decide(alternate, opener, new Dictionary<int, int> { [9200] = 1 });
        Assert.AreEqual("skill", second?.Action, "The extra turn must continue the same champion's configured chain.");
        Assert.AreEqual(892002, second?.SkillTypeId);

        var rebuildingHud = alternate with { HudSkillCount = 0, HudSkills = [] };
        Assert.IsNull(BattleOpenerPlanner.Decide(rebuildingHud, opener, new Dictionary<int, int> { [9200] = 1 }),
            "A form-transition HUD rebuild must wait instead of enabling Auto.");
        Assert.IsTrue(BattleOpenerPlanner.IsHudTransitionPending(rebuildingHud));
    }

    [TestMethod]
    public void BattleOpenerTargetsConfiguredSpecificAlly()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2);
        var active = battle.Heroes[0] with { Skills = [battle.Heroes[0].Skills[0] with { Target = 1 }] };
        var preferred = active with { Id = 2, BaseId = 22220, Name = "Preferred ally", Slot = 1, Skills = [], Effects = [] };
        var other = preferred with { Id = 3, BaseId = 33330, Name = "Other ally", Slot = 2 };
        var targetedBattle = battle with { Heroes = [active, other, preferred, battle.Heroes[1]] };
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion,
            [new(21110, [0], [BattleTargetPolicies.SpecificAlly], [10], [22220])]);

        var decision = BattleOpenerPlanner.Decide(targetedBattle, opener, new Dictionary<int, int> { [21110] = 0 });

        Assert.AreEqual(2, decision?.TargetId, "A named allied buff target must be selected by Base ID.");
    }

    [TestMethod]
    public void BattleOpenerSupportsAliveHeroSkillsTargetingEitherTeam()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2);
        var active = battle.Heroes[0] with { Skills = [battle.Heroes[0].Skills[0] with { Target = 10 }] };
        var targetedBattle = battle with { Heroes = [active, battle.Heroes[1]] };
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion,
            [new(21110, [0], [BattleTargetPolicies.FirstEnemy], [10])]);

        var decision = BattleOpenerPlanner.Decide(targetedBattle, opener, new Dictionary<int, int> { [21110] = 0 });

        Assert.AreEqual(200, decision?.TargetId, "AliveHeroes skills must support an explicitly configured enemy target.");
    }

    [TestMethod]
    public void BattleOpenerFocusesConfiguredEnemyThreat()
    {
        var battle = BattleSnapshotParser.Parse(ValidBattle, 2);
        var priorityEnemy = battle.Heroes[1] with
        {
            Id = 201,
            TypeId = 31111,
            BaseId = 31111,
            Name = "Priority enemy",
            Slot = 2,
            Health = 50000,
            MaxHealth = 50000
        };
        var targetedBattle = battle with { Heroes = [.. battle.Heroes, priorityEnemy] };
        var opener = new BattleOpenerFile(BattleOpenerFile.CurrentVersion,
            [new(21110, [0], [BattleTargetPolicies.ThreatPriority], [battle.Heroes[0].Skills[0].TypeId])]);

        var decision = BattleOpenerPlanner.Decide(
            targetedBattle,
            opener,
            new Dictionary<int, int> { [21110] = 0 },
            enemyThreatPriority: [31111]);

        Assert.AreEqual(201, decision?.TargetId, "The first living enemy from the configured threat list must be focused.");
        StringAssert.Contains(decision?.Explanation ?? string.Empty, "Priority enemy", "The planned action must name its exact target.");
    }

    [TestMethod]
    public void SearchAndFiltersCanBeCombined()
    {
        var rows = new[]
        {
            Row(10, "Alpha", 5, 1, false, false),
            Row(11, "Beta", 4, 2, true, false),
            Row(12, "Gamma", 5, 1, false, true)
        };
        var result = ChampionFilter.Apply(rows, "alp", "Legendary", "Magic", "Inventory").ToArray();
        Assert.AreEqual(1, result.Length, "Combined filters must return only the matching champion.");
        Assert.AreEqual(10L, result[0].Instance.Id, "The wrong champion passed the filters.");
    }

    [TestMethod]
    public void DefaultOrderUsesRarityThenLevelThenAwakening()
    {
        var rows = new[]
        {
            Row(10, "Rare", 3, 60, 6),
            Row(11, "Legendary low", 5, 50, 6),
            Row(12, "Legendary awake", 5, 60, 4),
            Row(13, "Legendary", 5, 60, 1)
        };
        var view = new ListCollectionView(rows);
        ChampionSorting.Apply(view);
        CollectionAssert.AreEqual(new long[] { 12, 13, 11, 10 }, view.Cast<ChampionRow>().Select(row => row.Instance.Id).ToArray(), "Champions must be sorted by descending rarity, level, then awakening.");
    }

    [TestMethod]
    public void UnexpectedProcessPathIsRejected()
    {
        Assert.IsTrue(BuildValidator.IsExpectedProcessPath(AppPaths.RaidExe), "The official RAID path must be accepted.");
        Assert.IsFalse(BuildValidator.IsExpectedProcessPath(Path.Combine(Path.GetTempPath(), "Raid.exe")), "A fake RAID path must be rejected.");
    }

    [TestMethod]
    public void IncorrectFileFingerprintIsRejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "test");
            Assert.ThrowsException<InvalidDataException>(() => BuildValidator.ValidateHash(path, new string('0', 64)), "An incorrect SHA-256 fingerprint must be rejected.");
            BuildValidator.ValidateHash(path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void InstalledRaidBuildPassesPinnedValidation()
    {
        var process = GameLauncher.FindRaid();
        if (process is null) Assert.Inconclusive("RAID is not running from the official installation path.");
        BuildValidator.Validate(process);
        Assert.AreEqual("11.71.0", BuildValidator.Version);
    }

    [TestMethod]
    public async Task NamedPipeCanBeRecreatedAfterFailedConnectionAttempt()
    {
        var first = new ProbeClient(Environment.ProcessId);
        await first.DisposeAsync();
        var second = new ProbeClient(Environment.ProcessId);
        await second.DisposeAsync();
    }

    [TestMethod]
    public async Task SubscriberFailureDoesNotStopProbeReadLoop()
    {
        var processId = Random.Shared.Next(100000, int.MaxValue);
        await using var probe = new ProbeClient(processId);
        using var client = new NamedPipeClientStream(".", $"ArenaDrafter-{processId}", PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var error = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var automation = new TaskCompletionSource<AutomationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.CatalogReceived += _ => throw new InvalidOperationException("Synthetic UI failure.");
        probe.ErrorReceived += message => error.TrySetResult(message);
        probe.AutomationReceived += message => automation.TrySetResult(message);

        var connect = probe.ConnectAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token);
        await connect;
        using var reader = new StreamReader(client, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        Assert.AreEqual("INIT 1", await reader.ReadLineAsync(timeout.Token));
        await writer.WriteLineAsync("{\"protocol\":1,\"type\":\"catalog\",\"champions\":[{\"typeId\":1006,\"baseId\":1000,\"name\":\"Test\",\"rarity\":3,\"skills\":[]}]}");
        await writer.WriteLineAsync("{\"protocol\":1,\"type\":\"automation\",\"state\":\"ready\",\"message\":\"still connected\"}");

        StringAssert.Contains(await error.Task.WaitAsync(timeout.Token), "catalog update could not be applied");
        Assert.AreEqual("still connected", (await automation.Task.WaitAsync(timeout.Token)).Message);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task KnownOfficialPortraitCanBeExtractedWithoutCPictures()
    {
        if (!Directory.Exists(AppPaths.RaidRoot)) Assert.Inconclusive("RAID resources are not installed on this machine.");
        var portrait = await new PortraitCache().GetAsync(21110);
        Assert.IsNotNull(portrait, "Known champion portrait 21110 must be extracted from official RAID AssetBundles.");
        Assert.IsTrue(File.Exists(portrait), "The extracted portrait cache file must exist.");
        Assert.AreEqual("8EBA67835C971979C540D932ED9DF760E952F10D913A4F3349BDC8F915037DA9", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(portrait))), "The sprite atlas must resolve to champion 21110, not another portrait at the same atlas coordinates.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task MarichkaFirstBanPortraitCanBeExtractedFromOfficialResources()
    {
        if (!Directory.Exists(AppPaths.RaidRoot)) Assert.Inconclusive("RAID resources are not installed on this machine.");
        var portrait = await new PortraitCache().GetAsync(7500);
        Assert.IsTrue(portrait is null || File.Exists(portrait), "Marichka (BaseId 7500) must resolve to an official portrait path or remain a clean cache miss for the visible fallback.");
        if (portrait is not null) Assert.IsTrue(new FileInfo(portrait).Length > 100, "Marichka's extracted portrait cache file must contain a decoded PNG.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task OfficialBaseAndMythicalAlternateSkillIconsCanBeExtracted()
    {
        if (!Directory.Exists(AppPaths.RaidRoot)) Assert.Inconclusive("RAID resources are not installed on this machine.");
        var cache = new SkillIconCache();
        var baseSkill = await cache.GetAsync(4750, 0, 0);
        var alternateSkill = await cache.GetAsync(8850, 0, 1);
        Assert.IsNotNull(baseSkill, "Siphi's A1 icon must be extracted from the official SkillIcons bundle.");
        Assert.IsNotNull(alternateSkill, "A Mythical alternate-form A1 icon must be extracted from the official SkillIcons bundle.");
        Assert.IsTrue(new FileInfo(baseSkill).Length > 100, "The base-form skill icon cache must contain a decoded PNG.");
        Assert.IsTrue(new FileInfo(alternateSkill).Length > 100, "The alternate-form skill icon cache must contain a decoded PNG.");
    }

    [TestMethod]
    public void BattleDiagnosticRecorderPersistsEveryEventImmediately()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rsl-battle-diagnostic-{Guid.NewGuid():N}");
        try
        {
            using var recorder = new BattleDiagnosticRecorder(directory);
            var path = recorder.Start(1234);
            recorder.RecordMarker("test", "diagnostic event");

            var contents = File.ReadAllText(path);
            StringAssert.Contains(contents, "\"eventType\":\"session-start\"");
            StringAssert.Contains(contents, "\"marker\":\"test\"");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void MythicalClickTraceRecorderAcceptsOnlyItsBoundedPayload()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rsl-mythical-click-{Guid.NewGuid():N}");
        try
        {
            using var recorder = new MythicalClickTraceRecorder(directory);
            var path = recorder.Start(1234);
            recorder.Record("{\"protocol\":1,\"type\":\"mythicalClickTrace\",\"sample\":{\"available\":true,\"commandState\":{\"available\":true,\"manualActiveAt56\":{\"value\":true}}}}");
            var contents = File.ReadAllText(path);
            StringAssert.Contains(contents, "\"type\":\"mythicalClickTrace\"");
            StringAssert.Contains(contents, "\"commandState\"");
            Assert.ThrowsException<InvalidDataException>(() =>
                recorder.Record("{\"protocol\":1,\"type\":\"rewardTrace\",\"sample\":{}}"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void RewardDiagnosticRecorderPersistsValidatedProbePayloadsImmediately()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rsl-reward-diagnostic-{Guid.NewGuid():N}");
        try
        {
            using var recorder = new RewardDiagnosticRecorder(directory);
            var path = recorder.Start(1234);
            recorder.Record("{\"protocol\":1,\"type\":\"rewardTrace\",\"contexts\":[]}");
            recorder.Stop("test-complete");

            var contents = File.ReadAllText(path);
            StringAssert.Contains(contents, "\"eventType\":\"session-start\"");
            StringAssert.Contains(contents, "\"type\":\"rewardTrace\"");
            StringAssert.Contains(contents, "\"reason\":\"test-complete\"");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void LoadedPortraitPropagatesToPresetAndLeaderRows()
    {
        var champion = Row(42, "Portrait test", 6, 60, 0);
        var preset = new PresetLineupCandidateRow(champion);
        var leader = new ArenaPoolRow { Champion = champion, Roles = ArenaRole.Utility };
        var presetNotifications = new List<string?>();
        var leaderNotifications = new List<string?>();
        preset.PropertyChanged += (_, args) => presetNotifications.Add(args.PropertyName);
        leader.PropertyChanged += (_, args) => leaderNotifications.Add(args.PropertyName);

        champion.Portrait = new DrawingImage(new DrawingGroup());

        CollectionAssert.Contains(presetNotifications, nameof(PresetLineupCandidateRow.Portrait));
        CollectionAssert.Contains(leaderNotifications, nameof(ArenaPoolRow.Portrait));
        Assert.IsNotNull(preset.Portrait);
        Assert.IsNotNull(leader.Portrait);
    }

    [TestMethod]
    public void LeaderReviewStatePersistsWithoutChangingStrategyValidation()
    {
        var strategy = new ArenaStrategyFile(ArenaStrategyFile.CurrentVersion, [], [], ArenaDraftMode.PresetLineup,
            ArenaStrategyFile.EmptyPresetLineup(), [], []) { LeaderPriorityReviewed = true };

        var parsed = ArenaStrategyFile.Parse(JsonSerializer.Serialize(strategy));

        Assert.IsTrue(parsed.LeaderPriorityReviewed, "Leader review state must survive strategy restoration.");
        parsed.Validate(false);
    }

    private static List<ArenaPresetSlot> PresetSlots() => Enumerable.Range(1, 5)
        .Select(index => new ArenaPresetSlot([new(index, 100 + index, 1000 + index, $"Primary {index}")]))
        .ToList();

    private static ArenaPickRule PickRule(string name, List<int> enemyBaseIds, int targetSlot, ArenaPresetCandidate replacement) => new(
        Guid.NewGuid(), name, true, ArenaChampionMatch.Any, enemyBaseIds, ArenaRole.None, 0,
        ArenaChampionMatch.Any, [], ArenaPickRuleDraft.Any, ArenaPickRuleFirstTurn.Any, 0, targetSlot, replacement);

    private static ChampionRow Row(long id, string name, int rarity, int affinity, bool storage, bool reserve) => new()
    {
        Instance = new ChampionInstance(id, 100, 100, 6, 0, 60, 0, 0, false, storage, reserve, 0),
        Definition = new ChampionDefinition(name, rarity, affinity, 1, null)
    };

    private static ChampionRow Row(long id, string name, int rarity, int level, int awakening) => new()
    {
        Instance = new ChampionInstance(id, 100, 100, 6, 0, level, 0, 0, false, false, false, awakening),
        Definition = new ChampionDefinition(name, rarity, 1, 1, null)
    };
}
