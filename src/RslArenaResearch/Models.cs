using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;

namespace RslArenaResearch;

public sealed record ChampionInstance(
    long Id,
    int TypeId,
    int BaseId,
    int Grade,
    int Ascension,
    int Level,
    int Empowerment,
    int Marker,
    bool Locked,
    bool InStorage,
    bool InBathhouse,
    int Awakening);

public sealed record ChampionDefinition(
    string Name,
    int Rarity,
    int Affinity,
    int Faction,
    string? PortraitPath);

public sealed record ChampionWire(
    long Id,
    int TypeId,
    int BaseId,
    string Name,
    int Grade,
    int Ascension,
    int Level,
    int Empowerment,
    int Marker,
    bool Locked,
    bool InStorage,
    bool InBathhouse,
    int Awakening,
    int Rarity,
    int Affinity,
    int Faction);

public sealed record ChampionSkillCatalogWire(int TypeId, int Slot, string Name, int Target, int Cooldown, int Variant = 0, bool RequiresTarget = false) : INotifyPropertyChanged
{
    private ImageSource? icon;
    public string Label => $"A{Slot + 1}";
    public string FormLabel => Variant == 0 ? "Base form" : "Alternate form";
    public string CooldownLabel => Cooldown == 0 ? "Default attack" : $"{Cooldown}-turn cooldown";
    public string TargetLabel => (Target, RequiresTarget) switch
    {
        (0, _) => "Self",
        (1, true) => "Single ally",
        (1, false) => "All allies",
        (2 or 8, true) => "Single enemy",
        (2 or 8, false) => "All enemies",
        (3 or 11, true) => "Dead ally",
        (3 or 11, false) => "All dead allies",
        (4, true) => "Dead enemy",
        (4, false) => "All dead enemies",
        (5, _) => "All allies",
        (6, _) => "All enemies",
        (7, true) => "Single ally (not self)",
        (7, false) => "All allies (not self)",
        (9, _) => "All allies (not self)",
        (10, true) => "Any living champion",
        (10, false) => "All living champions",
        _ => "Automatic target"
    };
    [JsonIgnore]
    public ImageSource? Icon
    {
        get => icon;
        set
        {
            if (icon == value) return;
            icon = value;
            PropertyChanged?.Invoke(this, new(nameof(Icon)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed record ChampionCatalogWire(int TypeId, int BaseId, string Name, int Rarity, ChampionSkillCatalogWire[] Skills);
public sealed record CatalogMessage(
    [property: JsonPropertyName("protocol")] int Protocol,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("champions")] ChampionCatalogWire[] Champions);

public sealed record SnapshotMessage(
    [property: JsonPropertyName("protocol")] int Protocol,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("champions")] ChampionWire[] Champions);

public sealed record BattleSkillWire(int TypeId, int Slot, string Name, int Target, int Cooldown, int MaxCooldown, bool Disabled, bool RequiresTarget = false);
public sealed record BattleEffectWire(int TypeId, int Turns);
public sealed record BattleHudSkillWire(int Index, int TypeId, int Cooldown, bool Passive);
public sealed record BattleHeroWire(int Id, int TypeId, int BaseId, string Name, string Team, int Level, int Grade, int Slot, long Health, long MaxHealth, bool Dead, BattleSkillWire[] Skills, BattleEffectWire[] Effects);
public sealed record BattleSnapshotMessage(
    [property: JsonPropertyName("protocol")] int Protocol,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("kind")] int Kind,
    [property: JsonPropertyName("stageId")] int StageId,
    [property: JsonPropertyName("round")] int Round,
    [property: JsonPropertyName("turn")] int Turn,
    [property: JsonPropertyName("activeHeroId")] int ActiveHeroId,
    [property: JsonPropertyName("finished")] bool Finished,
    [property: JsonPropertyName("autoMode")] bool AutoMode,
    [property: JsonPropertyName("heroes")] BattleHeroWire[] Heroes,
    [property: JsonPropertyName("hudVisible")] bool HudVisible,
    [property: JsonPropertyName("modeChangeAvailable")] bool ModeChangeAvailable,
    [property: JsonPropertyName("skillSelectionAvailable")] bool SkillSelectionAvailable,
    [property: JsonPropertyName("hudSkillCount")] int HudSkillCount,
    [property: JsonPropertyName("hudSkills")] BattleHudSkillWire[] HudSkills);

public sealed record LiveArenaHeroWire(int Slot, long? Id, int TypeId, int BaseId, string Name);
public sealed record LiveArenaDraftWire(
    int? Revision,
    string? Phase,
    string? FirstTurn,
    string? Turn,
    int? LeagueId,
    bool? AllowDuplicatePicks,
    LiveArenaHeroWire[] PlayerHeroes,
    LiveArenaHeroWire[] EnemyHeroes,
    int? BestEnemyBlockedSlot,
    int? PlayerBlockedSlot,
    int? EnemyBlockedSlot,
    int? PlayerLeaderSlot,
    int? EnemyLeaderSlot,
    bool BattleSetupReady,
    int? SecondsRemaining = null,
    int? TurnSeconds = null);
public sealed record LiveArenaTransportWire(
    bool Active,
    bool Friendly,
    bool Finished,
    int? Revision,
    int? TurnRevision,
    string? Phase,
    string? Turn,
    int QueuedCommands);
public sealed record LiveArenaUiWire(
    bool MenuVisible,
    bool QueueAvailable,
    bool FinishVisible,
    bool RefillVisible,
    bool RefillCanConfirm,
    bool DraftVisible = false,
    bool RewardOverlayVisible = false,
    bool RewardBatchReady = false,
    int RewardClaimableCount = 0,
    bool DailyBattleRefillReady = false,
    int RefillGemPrice = 0);
public sealed record LiveArenaSnapshotMessage(
    int Protocol,
    string Type,
    bool Matchmaking,
    int? Position,
    LiveArenaDraftWire Draft,
    LiveArenaTransportWire Transport,
    LiveArenaUiWire Ui);

public sealed record AutomationMessage(string State, string Message);

public sealed class ChampionRow : INotifyPropertyChanged
{
    public required ChampionInstance Instance { get; init; }
    public required ChampionDefinition Definition { get; init; }
    private ImageSource? portrait;
    public ImageSource? Portrait
    {
        get => portrait;
        set
        {
            if (ReferenceEquals(portrait, value)) return;
            portrait = value;
            PropertyChanged?.Invoke(this, new(nameof(Portrait)));
        }
    }
    public string Name => string.IsNullOrWhiteSpace(Definition.Name) ? "Unavailable champion" : Definition.Name;
    public int RarityValue => Definition.Rarity;
    public int Level => Instance.Level;
    public int Awakening => Instance.Awakening;
    public string AwakeningDisplay => Instance.Awakening == 0 ? "" : new string('★', Instance.Awakening);
    public string RankAndAscension => $"{Instance.Grade} / {Instance.Ascension}";
    public string Rarity => Names.Rarity(Definition.Rarity);
    public string Affinity => Names.Affinity(Definition.Affinity);
    public string Faction => Names.Faction(Definition.Faction);
    public string Marker
    {
        get
        {
            var marker = Names.Marker(Instance.Marker);
            return string.Equals(marker, "None", StringComparison.OrdinalIgnoreCase) ? string.Empty : marker;
        }
    }
    public string Location => Instance.InBathhouse ? "Reserve" : Instance.InStorage ? "Storage" : "Inventory";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class BattleHeroRow(BattleHeroWire hero, int activeHeroId)
{
    public string Team => hero.Team;
    public string Name => hero.Name;
    public int Level => hero.Level;
    public string Rank => hero.Grade.ToString();
    public string Health => $"{hero.Health:N0} / {hero.MaxHealth:N0}";
    public string State => hero.Dead ? "Dead" : hero.Id == activeHeroId ? "Active" : "Ready";
    public string Skills => string.Join(", ", hero.Skills.Select(skill => $"{skill.TypeId}: {(skill.Disabled ? "disabled" : skill.Cooldown == 0 ? "ready" : $"{skill.Cooldown}/{skill.MaxCooldown}")}"));
    public string Effects => string.Join(", ", hero.Effects.Select(effect => $"{effect.TypeId} ({effect.Turns})"));
}

public static class LiveArenaCommands
{
    public static string Pick(IEnumerable<int> instanceIds)
    {
        var ids = instanceIds.ToArray();
        if (ids.Length is < 1 or > 2 || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length)
            throw new InvalidDataException("A Live Arena pick must contain one or two distinct champion instances.");
        return $"LIVE_PICK {string.Join(',', ids)}";
    }

    public static string Ban(int slot) => Slot("LIVE_BAN", slot);
    public static string Leader(int slot) => Slot("LIVE_LEADER", slot);
    public const string Queue = "LIVE_QUEUE";
    public static string Refill(int gemPrice)
    {
        if (gemPrice is < 0 or > 10000) throw new InvalidDataException("A Live Arena refill Gem price must be between zero and 10000.");
        return $"LIVE_REFILL {gemPrice}";
    }
    public const string Return = "LIVE_RETURN";
    public const string CloseRewardOverlay = "LIVE_REWARD_CLOSE";
    public static string ClaimReward(int claimableCount)
    {
        if (claimableCount is < 1 or > 4) throw new InvalidDataException("The Live Arena reward claim count must be between one and four.");
        return $"LIVE_REWARD_CLAIM {claimableCount}";
    }

    private static string Slot(string command, int slot)
    {
        if (slot is < 0 or > 4) throw new InvalidDataException("A Live Arena slot must be between zero and four.");
        return $"{command} {slot}";
    }
}

public static class ChampionSorting
{
    public static void Apply(ICollectionView view)
    {
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new(nameof(ChampionRow.RarityValue), ListSortDirection.Descending));
        view.SortDescriptions.Add(new(nameof(ChampionRow.Level), ListSortDirection.Descending));
        view.SortDescriptions.Add(new(nameof(ChampionRow.Awakening), ListSortDirection.Descending));
        view.SortDescriptions.Add(new(nameof(ChampionRow.Name), ListSortDirection.Ascending));
    }
}

public static class Names
{
    private static readonly string[] Rarities = ["Unknown", "Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythical"];
    private static readonly string[] Affinities = ["Unknown", "Magic", "Force", "Spirit", "Void"];
    private static readonly string[] Factions = ["Unknown", "Banner Lords", "High Elves", "Sacred Order", "Coven of Magi", "Ogryn Tribes", "Lizardmen", "Skinwalkers", "Orcs", "Demonspawn", "Undead Hordes", "Dark Elves", "Knights Revenant", "Barbarians", "Sylvan Watchers", "Shadowkin", "Dwarves", "The Olympians"];

    public static string Rarity(int value) => Lookup(Rarities, value);
    public static string Affinity(int value) => Lookup(Affinities, value);
    public static string Faction(int value) => Lookup(Factions, value);
    public static string Marker(int value) => value switch
    {
        0 => "None",
        1 => "Favorite",
        100 => "First",
        101 => "Second",
        102 => "Third",
        200 => "Attack",
        201 => "Defence",
        202 => "Support",
        203 => "Speed",
        300 => "Arena First",
        301 => "Arena Second",
        _ => "Unknown"
    };
    public static string BattleKind(int value) => value switch
    {
        1 => "PvE",
        2 => "Arena",
        3 => "Clan Boss",
        4 => "Tag Team Arena",
        5 => "Hydra",
        6 => "Live Arena",
        7 => "Siege",
        8 => "Chimera",
        9 => "Cooperation Event",
        _ => "Unknown"
    };
    private static string Lookup(string[] values, int value) => value >= 0 && value < values.Length ? values[value] : "Unknown";
}

public static class SnapshotParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static SnapshotMessage Parse(string json, long lastRevision)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("protocol", out var protocol) || protocol.GetInt32() != 1)
            throw new InvalidDataException("Unsupported probe protocol.");
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "snapshot")
            throw new InvalidDataException("Expected a snapshot message.");

        var snapshot = JsonSerializer.Deserialize<SnapshotMessage>(json, Options)
            ?? throw new InvalidDataException("Snapshot payload is empty.");
        if (snapshot.Revision <= lastRevision)
            throw new InvalidDataException("Snapshot revision is stale.");
        if (snapshot.Champions.GroupBy(champion => champion.Id).Any(group => group.Count() > 1))
            throw new InvalidDataException("Snapshot contains duplicate instance identifiers.");
        return snapshot;
    }
}

public static class CatalogParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static CatalogMessage Parse(string json)
    {
        var catalog = JsonSerializer.Deserialize<CatalogMessage>(json, Options)
            ?? throw new InvalidDataException("Champion catalog payload is empty.");
        if (catalog.Protocol != 1 || catalog.Type != "catalog" || catalog.Champions is null || catalog.Champions.Length is < 1 or > 10000
            || catalog.Champions.Any(champion => champion.TypeId <= 0 || champion.BaseId <= 0 || string.IsNullOrWhiteSpace(champion.Name)
                || champion.Rarity is < 1 or > 6 || champion.Skills is null
                || champion.Skills.Any(skill => skill.TypeId <= 0 || skill.Slot is < 0 or > 11 || string.IsNullOrWhiteSpace(skill.Name)
                    || skill.Target is < -1 or > 11 || skill.Cooldown is < 0 or > 100 || skill.Variant is < 0 or > 1)
                || champion.Skills.Select(skill => skill.TypeId).Distinct().Count() != champion.Skills.Length
                || champion.Skills.Select(skill => skill.Variant).Distinct().Count() > 2
                || champion.Skills.GroupBy(skill => skill.Variant).Any(form => form.Count() > 12
                    || form.Select(skill => skill.Slot).Distinct().Count() != form.Count()))
            || catalog.Champions.Select(champion => champion.BaseId).Distinct().Count() != catalog.Champions.Length)
            throw new InvalidDataException("Champion catalog payload is invalid.");
        return catalog;
    }
}

public static class BattleSnapshotParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static BattleSnapshotMessage Parse(string json, long lastRevision)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("protocol", out var protocol) || protocol.GetInt32() != 1)
            throw new InvalidDataException("Unsupported probe protocol.");
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "battle")
            throw new InvalidDataException("Expected a battle message.");
        var snapshot = JsonSerializer.Deserialize<BattleSnapshotMessage>(json, Options)
            ?? throw new InvalidDataException("Battle payload is empty.");
        if (snapshot.Revision <= lastRevision)
            throw new InvalidDataException("Battle revision is stale.");
        if (snapshot.Heroes is null || snapshot.HudSkills is null || snapshot.Heroes.Any(hero => hero.Skills is null || hero.Effects is null))
            throw new InvalidDataException("Battle arrays are missing.");
        if (snapshot.Heroes.GroupBy(hero => hero.Id).Any(group => group.Count() > 1))
            throw new InvalidDataException("Battle contains duplicate hero identifiers.");
        if (((snapshot.ModeChangeAvailable || snapshot.SkillSelectionAvailable || snapshot.HudSkillCount > 0) && !snapshot.HudVisible)
            || snapshot.HudSkillCount is < 0 or > 12 || snapshot.HudSkills.Length != snapshot.HudSkillCount
            || snapshot.HudSkills.Where((skill, index) => skill.Index != index || skill.TypeId <= 0 || skill.Cooldown is < 0 or > 100).Any())
            throw new InvalidDataException("Battle HUD controls are inconsistent.");
        if (snapshot.Heroes.Any(hero => hero.Id < 0 || hero.TypeId <= 0 || hero.BaseId <= 0 || hero.Team is not ("Ally" or "Enemy") || hero.Health < 0 || hero.MaxHealth < 0
            || hero.Skills.Where((skill, slot) => skill.TypeId <= 0 || skill.Slot != slot || string.IsNullOrWhiteSpace(skill.Name)
                || skill.Target is < -1 or > 11 || skill.Cooldown is < 0 or > 100 || skill.MaxCooldown is < 0 or > 100).Any()))
            throw new InvalidDataException("Battle contains an invalid hero value.");
        return snapshot;
    }
}

public static class LiveArenaSnapshotParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> DraftPhases = ["initialize", "heroPick", "heroBan", "leaderSelection", "startBattle", "opponentCanceled"];
    private static readonly HashSet<string> BattlePhases = ["connection", "battleTurn", "finishBattle", "canceled"];

    public static LiveArenaSnapshotMessage Parse(string json)
    {
        var snapshot = JsonSerializer.Deserialize<LiveArenaSnapshotMessage>(json, Options)
            ?? throw new InvalidDataException("Live Arena payload is empty.");
        if (snapshot.Protocol != 1 || snapshot.Type != "liveArena") throw new InvalidDataException("Expected a protocol-1 Live Arena message.");
        if (snapshot.Draft is null || snapshot.Transport is null || snapshot.Ui is null || snapshot.Draft.PlayerHeroes is null || snapshot.Draft.EnemyHeroes is null)
            throw new InvalidDataException("Live Arena state is incomplete.");
        if ((snapshot.Position is not null && snapshot.Position <= 0) || (snapshot.Draft.Revision is not null && snapshot.Draft.Revision < 0)
            || (snapshot.Transport.Revision is not null && snapshot.Transport.Revision < 0) || (snapshot.Transport.TurnRevision is not null && snapshot.Transport.TurnRevision < 0)
            || snapshot.Transport.QueuedCommands is < 0 or > 100000)
            throw new InvalidDataException("Live Arena revision or queue state is invalid.");
        if (snapshot.Draft.LeagueId is int leagueId && leagueId is not (0 or 1 or 2 or 3 or 4 or 11 or 12 or 13 or 14 or 21 or 22 or 23 or 24 or 30))
            throw new InvalidDataException("Live Arena league identifier is invalid.");
        if (snapshot.Draft.SecondsRemaining is < 0 or > 600 || snapshot.Draft.TurnSeconds is < 0 or > 600)
            throw new InvalidDataException("Live Arena timer value is invalid.");
        if ((snapshot.Ui.QueueAvailable && !snapshot.Ui.MenuVisible) || (snapshot.Ui.RefillCanConfirm && !snapshot.Ui.RefillVisible)
            || (snapshot.Ui.FinishVisible && snapshot.Ui.RefillVisible) || snapshot.Ui.RewardClaimableCount is < 0 or > 4
            || snapshot.Ui.RefillGemPrice is < 0 or > 10000 || (!snapshot.Ui.RefillVisible && snapshot.Ui.RefillGemPrice != 0)
            || (snapshot.Ui.RewardBatchReady && snapshot.Ui.RewardClaimableCount != 4))
            throw new InvalidDataException("Live Arena UI state is inconsistent.");
        if ((snapshot.Draft.Phase is not null && !DraftPhases.Contains(snapshot.Draft.Phase))
            || (snapshot.Transport.Phase is not null && !BattlePhases.Contains(snapshot.Transport.Phase))
            || !ValidTurn(snapshot.Draft.FirstTurn) || !ValidTurn(snapshot.Draft.Turn) || !ValidTurn(snapshot.Transport.Turn))
            throw new InvalidDataException("Live Arena phase or turn owner is invalid.");
        if (snapshot.Draft.PlayerHeroes.Length > 5 || snapshot.Draft.EnemyHeroes.Length > 5
            || snapshot.Draft.PlayerHeroes.Where((hero, slot) => InvalidHero(hero, slot) || hero.Id is null or <= 0).Any()
            || snapshot.Draft.EnemyHeroes.Where((hero, slot) => InvalidHero(hero, slot)).Any()
            || new[] { snapshot.Draft.BestEnemyBlockedSlot, snapshot.Draft.PlayerBlockedSlot, snapshot.Draft.EnemyBlockedSlot, snapshot.Draft.PlayerLeaderSlot, snapshot.Draft.EnemyLeaderSlot }
                .Any(slot => slot is not null && slot is < 0 or > 4))
            throw new InvalidDataException("Live Arena champion or slot state is invalid.");
        return snapshot;
    }

    private static bool ValidTurn(string? value) => value is null or "player" or "opponent";
    private static bool InvalidHero(LiveArenaHeroWire hero, int slot) => hero is null || hero.Slot != slot || hero.TypeId <= 0 || hero.BaseId <= 0 || string.IsNullOrWhiteSpace(hero.Name);
}

public static class ChampionFilter
{
    public static IEnumerable<ChampionRow> Apply(IEnumerable<ChampionRow> rows, string? search, string? rarity, string? affinity, string? location)
    {
        search = search?.Trim();
        return rows.Where(row =>
            (string.IsNullOrEmpty(search) || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || row.Instance.TypeId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(rarity) || rarity == "All" || row.Rarity == rarity) &&
            (string.IsNullOrEmpty(affinity) || affinity == "All" || row.Affinity == affinity) &&
            (string.IsNullOrEmpty(location) || location == "All" || row.Location == location));
    }
}
