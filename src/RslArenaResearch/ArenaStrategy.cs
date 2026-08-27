using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace RslArenaResearch;

public sealed record ArenaStrategyCandidate(long InstanceId, int TypeId, int BaseId, string Name, ArenaRole Roles, int Priority, int LeaderPriority);

public enum ArenaDraftMode { AdaptiveDraft, PresetLineup }

public sealed record ArenaPresetCandidate(long InstanceId, int TypeId, int BaseId, string Name);

public sealed record ArenaPresetSlot(List<ArenaPresetCandidate> Candidates);

public enum ArenaChampionMatch { Any, All, None }

public enum ArenaPickRuleDraft { Any, Shared, Exclusive }

public enum ArenaPickRuleFirstTurn { Any, Player, Opponent }

public sealed record ArenaPickRule(
    Guid Id,
    string Name,
    bool Enabled,
    ArenaChampionMatch EnemyMatch,
    List<int> EnemyBaseIds,
    ArenaRole EnemyRoles,
    int MinimumEnemyRoleCount,
    ArenaChampionMatch PlayerMatch,
    List<int>? PlayerBaseIds,
    ArenaPickRuleDraft DraftRule,
    ArenaPickRuleFirstTurn FirstTurn,
    int MinimumVisibleEnemyPicks,
    int TargetSlot,
    ArenaPresetCandidate Replacement);

public static class ArenaRolePresets
{
    public static readonly string[] Names = ["Nuker", "Speed Booster", "Cleanser", "Lockout"];
    public static ArenaRole FromName(string name) => name switch
    {
        "Nuker" => ArenaRole.Damage,
        "Speed Booster" => ArenaRole.Initiative | ArenaRole.Utility,
        "Cleanser" => ArenaRole.Cleanse | ArenaRole.Sustain | ArenaRole.Utility,
        "Lockout" => ArenaRole.Opener | ArenaRole.Control,
        _ => throw new InvalidDataException("The Arena role preset is unknown.")
    };
}

public sealed record ArenaStrategyFile(
    int Version,
    List<ArenaStrategyCandidate> Pool,
    List<int> BanPriority,
    ArenaDraftMode DraftMode = ArenaDraftMode.AdaptiveDraft,
    List<ArenaPresetSlot>? PresetLineup = null,
    List<int>? LeaderPriority = null,
    List<ArenaPickRule>? PickRules = null)
{
    public const int CurrentVersion = 3;
    private static readonly string Path = System.IO.Path.Combine(AppPaths.Data, "live-arena-strategy.json");
    public bool LeaderPriorityReviewed { get; init; }

    public static ArenaStrategyFile Load()
    {
        Directory.CreateDirectory(AppPaths.Data);
        if (!File.Exists(Path)) return new(CurrentVersion, [], [], ArenaDraftMode.AdaptiveDraft, EmptyPresetLineup(), [], []);
        return Parse(File.ReadAllText(Path));
    }

    public static ArenaStrategyFile Parse(string json)
    {
        var strategy = JsonSerializer.Deserialize<ArenaStrategyFile>(json)
            ?? throw new InvalidDataException("The Live Arena strategy file is empty.");
        if (strategy.Version == 1)
            strategy = new(CurrentVersion, strategy.Pool, strategy.BanPriority, ArenaDraftMode.AdaptiveDraft, EmptyPresetLineup(),
                strategy.Pool.OrderBy(candidate => candidate.LeaderPriority).Select(candidate => candidate.BaseId).ToList(), []);
        else if (strategy.Version == 2)
            strategy = strategy with
            {
                Version = CurrentVersion,
                PresetLineup = strategy.PresetLineup ?? EmptyPresetLineup(),
                LeaderPriority = strategy.LeaderPriority ?? strategy.Pool.OrderBy(candidate => candidate.LeaderPriority).Select(candidate => candidate.BaseId).ToList(),
                PickRules = []
            };
        else
            strategy = strategy with
            {
                PresetLineup = strategy.PresetLineup ?? EmptyPresetLineup(),
                LeaderPriority = strategy.LeaderPriority ?? strategy.Pool.OrderBy(candidate => candidate.LeaderPriority).Select(candidate => candidate.BaseId).ToList(),
                PickRules = strategy.PickRules ?? []
            };
        strategy.Validate(false);
        return strategy;
    }

    public void Save()
    {
        Validate(false);
        Directory.CreateDirectory(AppPaths.Data);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, Path, true);
    }

    public void Validate(bool requireReady)
    {
        if (Version != CurrentVersion || Pool is null || BanPriority is null || !Enum.IsDefined(DraftMode))
            throw new InvalidDataException("The Live Arena strategy version is unsupported.");
        var presetLineup = PresetLineup ?? EmptyPresetLineup();
        var leaderPriority = LeaderPriority ?? Pool.OrderBy(candidate => candidate.LeaderPriority).Select(candidate => candidate.BaseId).ToList();
        var pickRules = PickRules ?? [];
        if (Pool.Count > 20 || requireReady && DraftMode == ArenaDraftMode.AdaptiveDraft && Pool.Count < 5)
            throw new InvalidDataException(requireReady ? "Add between 5 and 20 champions before starting Live Arena strategy." : "A Live Arena pool cannot exceed 20 champions.");
        if (Pool.Any(candidate => candidate.InstanceId <= 0 || candidate.InstanceId > int.MaxValue || candidate.TypeId <= 0 || candidate.BaseId <= 0 || string.IsNullOrWhiteSpace(candidate.Name)
            || candidate.Roles == ArenaRole.None || (candidate.Roles & ~ArenaRole.All) != 0 || candidate.Priority < 0 || candidate.LeaderPriority < 0)
            || Pool.Select(candidate => candidate.InstanceId).Distinct().Count() != Pool.Count
            || Pool.Select(candidate => candidate.BaseId).Distinct().Count() != Pool.Count)
            throw new InvalidDataException("The Live Arena pool contains an invalid or duplicate champion.");
        if (BanPriority.Any(id => id <= 0) || BanPriority.Distinct().Count() != BanPriority.Count)
            throw new InvalidDataException("The Live Arena ban priority contains an invalid or duplicate base identifier.");
        if (leaderPriority.Any(id => id <= 0) || leaderPriority.Distinct().Count() != leaderPriority.Count)
            throw new InvalidDataException("The Live Arena leader priority contains an invalid or duplicate base identifier.");
        if (presetLineup.Count != 5 || presetLineup.Any(slot => slot?.Candidates is null))
            throw new InvalidDataException("Preset Lineup must contain exactly five slots.");
        var preset = presetLineup.SelectMany(slot => slot.Candidates).ToArray();
        if (preset.Length > 20 || preset.Any(candidate => candidate.InstanceId <= 0 || candidate.InstanceId > int.MaxValue || candidate.TypeId <= 0
            || candidate.BaseId <= 0 || string.IsNullOrWhiteSpace(candidate.Name))
            || preset.Select(candidate => candidate.InstanceId).Distinct().Count() != preset.Length
            || preset.Select(candidate => candidate.BaseId).Distinct().Count() != preset.Length)
            throw new InvalidDataException("Preset Lineup contains an invalid or duplicate champion.");
        if (requireReady && DraftMode == ArenaDraftMode.PresetLineup && presetLineup.Any(slot => slot.Candidates.Count == 0))
            throw new InvalidDataException("Add a primary champion to all five Preset Lineup slots before starting Live Arena.");
        if (pickRules.Count > 50 || pickRules.Any(rule => rule.Id == Guid.Empty || string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 80
            || !Enum.IsDefined(rule.EnemyMatch) || rule.EnemyBaseIds is null || rule.EnemyBaseIds.Count is < 1 or > 20
            || rule.EnemyBaseIds.Any(id => id <= 0) || rule.EnemyBaseIds.Distinct().Count() != rule.EnemyBaseIds.Count
            || rule.EnemyRoles < ArenaRole.None || (rule.EnemyRoles & ~ArenaRole.All) != 0 || rule.MinimumEnemyRoleCount is < 0 or > 5
            || (rule.MinimumEnemyRoleCount == 0) != (rule.EnemyRoles == ArenaRole.None)
            || !Enum.IsDefined(rule.PlayerMatch) || rule.PlayerBaseIds is null || rule.PlayerBaseIds.Count > 20
            || rule.PlayerBaseIds.Any(id => id <= 0) || rule.PlayerBaseIds.Distinct().Count() != rule.PlayerBaseIds.Count
            || !Enum.IsDefined(rule.DraftRule) || !Enum.IsDefined(rule.FirstTurn) || rule.MinimumVisibleEnemyPicks is < 0 or > 5
            || rule.TargetSlot is < 0 or > 4 || rule.Replacement is null || rule.Replacement.InstanceId <= 0
            || rule.Replacement.InstanceId > int.MaxValue || rule.Replacement.TypeId <= 0 || rule.Replacement.BaseId <= 0
            || string.IsNullOrWhiteSpace(rule.Replacement.Name))
            || pickRules.Select(rule => rule.Id).Distinct().Count() != pickRules.Count)
            throw new InvalidDataException("Preset Lineup pick rules contain an invalid value or duplicate identifier.");
    }

    public static List<ArenaPresetSlot> EmptyPresetLineup() => Enumerable.Range(0, 5).Select(_ => new ArenaPresetSlot([])).ToList();
}

public sealed class ArenaPoolRow : INotifyPropertyChanged
{
    private ChampionRow champion = null!;
    private ArenaRole roles;
    private int priority;
    private int leaderPriority;

    public required ChampionRow Champion
    {
        get => champion;
        init
        {
            champion = value ?? throw new ArgumentNullException(nameof(value));
            champion.PropertyChanged += Champion_PropertyChanged;
        }
    }
    public long InstanceId => Champion.Instance.Id;
    public int TypeId => Champion.Instance.TypeId;
    public int BaseId => Champion.Instance.BaseId;
    public string Name => Champion.Name;
    public string Marker => Champion.Marker;
    public ImageSource? Portrait => Champion.Portrait;
    public string LeaderPriorityLabel => LeaderPriority == 0 ? "PREFERRED LEADER" : $"FALLBACK {LeaderPriority}";
    public string LeaderPriorityIcon => LeaderPriority == 0 ? "♛" : "↳";
    public int Priority { get => priority; set { value = Math.Max(0, value); if (priority == value) return; priority = value; PropertyChanged?.Invoke(this, new(nameof(Priority))); } }
    public int LeaderPriority
    {
        get => leaderPriority;
        set
        {
            value = Math.Max(0, value);
            if (leaderPriority == value) return;
            leaderPriority = value;
            PropertyChanged?.Invoke(this, new(nameof(LeaderPriority)));
            PropertyChanged?.Invoke(this, new(nameof(LeaderPriorityLabel)));
            PropertyChanged?.Invoke(this, new(nameof(LeaderPriorityIcon)));
        }
    }
    public ArenaRole Roles { get => roles; set { if (roles == value) return; roles = value; PropertyChanged?.Invoke(this, new(nameof(Roles))); RaiseRoleProperties(); } }
    public bool Initiative { get => Has(ArenaRole.Initiative); set => SetRole(ArenaRole.Initiative, value); }
    public bool Opener { get => Has(ArenaRole.Opener); set => SetRole(ArenaRole.Opener, value); }
    public bool Damage { get => Has(ArenaRole.Damage); set => SetRole(ArenaRole.Damage, value); }
    public bool Control { get => Has(ArenaRole.Control); set => SetRole(ArenaRole.Control, value); }
    public bool Protection { get => Has(ArenaRole.Protection); set => SetRole(ArenaRole.Protection, value); }
    public bool Sustain { get => Has(ArenaRole.Sustain); set => SetRole(ArenaRole.Sustain, value); }
    public bool Cleanse { get => Has(ArenaRole.Cleanse); set => SetRole(ArenaRole.Cleanse, value); }
    public bool Utility { get => Has(ArenaRole.Utility); set => SetRole(ArenaRole.Utility, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ArenaStrategyCandidate ToCandidate() => new(InstanceId, TypeId, BaseId, Name, Roles, Priority, LeaderPriority);

    private void Champion_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChampionRow.Portrait)) PropertyChanged?.Invoke(this, new(nameof(Portrait)));
    }

    private bool Has(ArenaRole role) => (Roles & role) != 0;
    private void SetRole(ArenaRole role, bool enabled)
    {
        var updated = enabled ? Roles | role : Roles & ~role;
        if (updated != ArenaRole.None) Roles = updated;
    }
    private void RaiseRoleProperties()
    {
        foreach (var name in new[] { nameof(Initiative), nameof(Opener), nameof(Damage), nameof(Control), nameof(Protection), nameof(Sustain), nameof(Cleanse), nameof(Utility) })
            PropertyChanged?.Invoke(this, new(name));
    }
}

public sealed class ArenaCatalogRow(ChampionCatalogWire champion) : INotifyPropertyChanged
{
    private ImageSource? portrait;
    public int TypeId { get; } = champion.TypeId;
    public int BaseId { get; } = champion.BaseId;
    public string Name { get; } = champion.Name;
    public int Rarity { get; } = champion.Rarity;
    public IReadOnlyList<ChampionSkillCatalogWire> Skills { get; } = champion.Skills;
    public ImageSource? Portrait { get => portrait; set { if (portrait == value) return; portrait = value; PropertyChanged?.Invoke(this, new(nameof(Portrait))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ArenaBanPriorityRow(int baseId, string name, ImageSource? portrait = null) : INotifyPropertyChanged
{
    private string name = name;
    private ImageSource? portrait = portrait;
    private int order;
    public int BaseId { get; } = baseId;
    public int Order { get => order; set { if (order == value) return; order = Math.Max(0, value); PropertyChanged?.Invoke(this, new(nameof(Order))); PropertyChanged?.Invoke(this, new(nameof(OrderLabel))); } }
    public string OrderLabel => Order == 0 ? "FIRST BAN" : $"BAN PRIORITY {Order + 1}";
    public string Name { get => name; set { if (name == value) return; name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    public ImageSource? Portrait { get => portrait; set { if (portrait == value) return; portrait = value; PropertyChanged?.Invoke(this, new(nameof(Portrait))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class PresetLineupCandidateRow : INotifyPropertyChanged
{
    private readonly ChampionRow champion;
    private int order;
    public PresetLineupCandidateRow(ChampionRow champion)
    {
        this.champion = champion ?? throw new ArgumentNullException(nameof(champion));
        this.champion.PropertyChanged += Champion_PropertyChanged;
    }

    public ChampionRow Champion => champion;
    public long InstanceId => Champion.Instance.Id;
    public int TypeId => Champion.Instance.TypeId;
    public int BaseId => Champion.Instance.BaseId;
    public string Name => Champion.Name;
    public ImageSource? Portrait => Champion.Portrait;
    public int Order { get => order; set { if (order == value) return; order = value; PropertyChanged?.Invoke(this, new(nameof(Order))); PropertyChanged?.Invoke(this, new(nameof(PositionLabel))); } }
    public string PositionLabel => Order == 0 ? "PRIMARY" : $"SUBSTITUTE {Order}";
    public ArenaPresetCandidate ToCandidate() => new(InstanceId, TypeId, BaseId, Name);
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Champion_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChampionRow.Portrait)) PropertyChanged?.Invoke(this, new(nameof(Portrait)));
    }
}

public sealed class PresetLineupSlotRow : INotifyPropertyChanged
{
    public PresetLineupSlotRow(int slot)
    {
        Slot = slot;
        Candidates.CollectionChanged += (_, _) =>
        {
            PropertyChanged?.Invoke(this, new(nameof(Primary)));
            PropertyChanged?.Invoke(this, new(nameof(Substitutes)));
            PropertyChanged?.Invoke(this, new(nameof(HasPrimary)));
            PropertyChanged?.Invoke(this, new(nameof(RemovePrimaryLabel)));
        };
    }

    public int Slot { get; }
    public string Label => $"SLOT {Slot + 1}";
    public string OrderLabel => Slot switch { 0 => "I", 1 => "II", 2 => "III", 3 => "IV", _ => "V" };
    public ObservableCollection<PresetLineupCandidateRow> Candidates { get; } = [];
    public PresetLineupCandidateRow? Primary => Candidates.FirstOrDefault();
    public IEnumerable<PresetLineupCandidateRow> Substitutes => Candidates.Skip(1);
    public bool HasPrimary => Candidates.Count > 0;
    public string RemovePrimaryLabel => Candidates.Count > 1 ? "Remove primary → promote" : "Remove primary";
    public override string ToString() => $"Slot {Slot + 1}";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class LiveArenaDisplayRow(int slot, int baseId, int typeId, string name, ImageSource? portrait = null) : INotifyPropertyChanged
{
    private ImageSource? portrait = portrait;
    public int Slot { get; } = slot;
    public int BaseId { get; } = baseId;
    public int TypeId { get; } = typeId;
    public string Name { get; } = string.IsNullOrWhiteSpace(name) ? "Unavailable champion" : name;
    public string SlotLabel => $"SLOT {Slot + 1}";
    public ImageSource? Portrait
    {
        get => portrait;
        set { if (portrait == value) return; portrait = value; PropertyChanged?.Invoke(this, new(nameof(Portrait))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ArenaPickRuleRow(ArenaPickRule rule, string summary, ImageSource? portrait) : INotifyPropertyChanged
{
    private ArenaPickRule rule = rule;
    private string summary = summary;
    private ImageSource? portrait = portrait;

    public Guid Id => rule.Id;
    public string Name => rule.Name;
    public string ReplacementName => rule.Replacement.Name;
    public string TargetLabel => $"SLOT {rule.TargetSlot + 1}";
    public string Summary { get => summary; private set { summary = value; PropertyChanged?.Invoke(this, new(nameof(Summary))); } }
    public ImageSource? Portrait { get => portrait; private set { portrait = value; PropertyChanged?.Invoke(this, new(nameof(Portrait))); } }
    public bool Enabled
    {
        get => rule.Enabled;
        set
        {
            if (rule.Enabled == value) return;
            rule = rule with { Enabled = value };
            PropertyChanged?.Invoke(this, new(nameof(Enabled)));
        }
    }

    public ArenaPickRule ToRule() => rule;

    public void UpdatePortrait(ImageSource? value) => Portrait = value;

    public void Update(ArenaPickRule value, string valueSummary, ImageSource? valuePortrait)
    {
        rule = value;
        Summary = valueSummary;
        Portrait = valuePortrait;
        foreach (var property in new[] { nameof(Name), nameof(ReplacementName), nameof(TargetLabel), nameof(Enabled) })
            PropertyChanged?.Invoke(this, new(property));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record ArenaPickRuleEvaluation(Guid RuleId, string RuleName, bool Matched, bool Applied, string Explanation);

public sealed record PresetLineupResolution(
    ArenaPresetCandidate[] Team,
    ArenaPresetCandidate[] Picks,
    string Explanation,
    ArenaPickRuleEvaluation[] RuleEvaluations);

public static class PresetLineupResolver
{
    public static PresetLineupResolution Resolve(
        IReadOnlyList<ArenaPresetSlot> slots,
        IReadOnlyCollection<int> playerBaseIds,
        IReadOnlyCollection<int> enemyBaseIds,
        bool allowDuplicatePicks,
        int picksRequired,
        IReadOnlyList<ArenaPickRule>? rules = null,
        string? firstTurn = null,
        IReadOnlyDictionary<int, ArenaRole>? knownRoles = null,
        bool draftRuleKnown = true)
    {
        if (slots.Count != 5 || picksRequired is < 1 or > 2) throw new InvalidDataException("Preset Lineup draft input is invalid.");
        var player = playerBaseIds.ToHashSet();
        var blocked = allowDuplicatePicks ? new HashSet<int>() : enemyBaseIds.ToHashSet();
        var orderedRules = rules ?? [];
        var evaluations = new List<ArenaPickRuleEvaluation>(orderedRules.Count);
        var matches = orderedRules.ToDictionary(rule => rule.Id, EvaluateConditions);
        var locked = new Dictionary<int, ArenaPresetCandidate>();
        var assignedPlayer = new HashSet<int>();

        // A rule condition such as "none of" may stop matching after its replacement was already accepted.
        // Persist the accepted champion in its configured target slot for the rest of this draft.
        foreach (var rule in orderedRules.Where(rule => rule.Enabled))
        {
            if (player.Contains(rule.Replacement.BaseId) && !assignedPlayer.Contains(rule.Replacement.BaseId) && !locked.ContainsKey(rule.TargetSlot))
            {
                locked[rule.TargetSlot] = rule.Replacement;
                assignedPlayer.Add(rule.Replacement.BaseId);
            }
        }
        for (var index = 0; index < slots.Count; index++)
        {
            if (locked.ContainsKey(index)) continue;
            var selected = slots[index].Candidates.FirstOrDefault(candidate => player.Contains(candidate.BaseId) && !assignedPlayer.Contains(candidate.BaseId));
            if (selected is null) continue;
            locked[index] = selected;
            assignedPlayer.Add(selected.BaseId);
        }
        if (assignedPlayer.Count != player.Count)
            throw new InvalidDataException("RAID's current picks do not match the configured Preset Lineup or an active pick rule.");

        var activeBySlot = new Dictionary<int, ArenaPickRule>();
        var reserved = new Dictionary<int, int>();
        foreach (var rule in orderedRules)
        {
            if (!rule.Enabled)
            {
                evaluations.Add(new(rule.Id, rule.Name, false, false, $"{rule.Name}: disabled."));
                continue;
            }
            var condition = matches[rule.Id];
            if (!condition.Matched)
            {
                evaluations.Add(new(rule.Id, rule.Name, false, false, $"{rule.Name}: {condition.Details}"));
                continue;
            }
            if (locked.ContainsKey(rule.TargetSlot))
            {
                evaluations.Add(new(rule.Id, rule.Name, true, false, $"{rule.Name}: {condition.Details}; matched after Slot {rule.TargetSlot + 1} was locked."));
                continue;
            }
            if (activeBySlot.ContainsKey(rule.TargetSlot))
            {
                evaluations.Add(new(rule.Id, rule.Name, true, false, $"{rule.Name}: {condition.Details}; a higher-priority rule already controls Slot {rule.TargetSlot + 1}."));
                continue;
            }
            if (blocked.Contains(rule.Replacement.BaseId) || reserved.ContainsKey(rule.Replacement.BaseId) || player.Contains(rule.Replacement.BaseId))
            {
                evaluations.Add(new(rule.Id, rule.Name, true, false, $"{rule.Name}: {condition.Details}; {rule.Replacement.Name} is unavailable; Slot {rule.TargetSlot + 1} uses its base substitutes."));
                activeBySlot[rule.TargetSlot] = rule;
                continue;
            }
            activeBySlot[rule.TargetSlot] = rule;
            reserved[rule.Replacement.BaseId] = rule.TargetSlot;
            evaluations.Add(new(rule.Id, rule.Name, true, true, $"{rule.Name}: {condition.Details}; use {rule.Replacement.Name} in Slot {rule.TargetSlot + 1}."));
        }

        var used = new HashSet<int>();
        var team = new List<ArenaPresetCandidate>(5);
        var reasons = new List<string>(5);
        for (var index = 0; index < slots.Count; index++)
        {
            var candidates = slots[index].Candidates;
            ArenaPresetCandidate? selected;
            if (locked.TryGetValue(index, out var accepted)) selected = accepted;
            else
            {
                var rule = activeBySlot.GetValueOrDefault(index);
                var preferred = rule is not null && reserved.TryGetValue(rule.Replacement.BaseId, out var target) && target == index
                    ? rule.Replacement
                    : null;
                selected = preferred is not null && !used.Contains(preferred.BaseId) && !blocked.Contains(preferred.BaseId)
                    ? preferred
                    : candidates.FirstOrDefault(candidate => !used.Contains(candidate.BaseId) && !blocked.Contains(candidate.BaseId)
                        && !player.Contains(candidate.BaseId)
                        && (!reserved.TryGetValue(candidate.BaseId, out var reservedSlot) || reservedSlot == index));
            }
            if (selected is null) throw new InvalidOperationException($"Slot {index + 1} has no available champion. Add another substitute.");
            used.Add(selected.BaseId);
            team.Add(selected);
            var position = candidates.IndexOf(selected);
            reasons.Add(activeBySlot.TryGetValue(index, out var applied) && applied.Replacement.BaseId == selected.BaseId
                ? $"Slot {index + 1}: {selected.Name} (rule {applied.Name})"
                : position == 0 ? $"Slot {index + 1}: {selected.Name} (primary)"
                : $"Slot {index + 1}: {selected.Name} (substitute {position})");
        }
        var picks = team.Where(candidate => !player.Contains(candidate.BaseId)).Take(picksRequired).ToArray();
        if (picks.Length != picksRequired) throw new InvalidOperationException("Preset Lineup cannot supply the requested pick batch.");
        return new([.. team], picks, string.Join("; ", reasons), [.. evaluations]);

        (bool Matched, string Details) EvaluateConditions(ArenaPickRule rule)
        {
            var checks = new List<(string Name, bool Result)>
            {
                ($"WHEN enemy {rule.EnemyMatch.ToString().ToLowerInvariant()} of {rule.EnemyBaseIds.Count}", ChampionMatch(rule.EnemyMatch, rule.EnemyBaseIds, enemyBaseIds))
            };
            if (rule.PlayerBaseIds is { Count: > 0 } playerCondition)
                checks.Add(($"our picks {rule.PlayerMatch.ToString().ToLowerInvariant()} of {playerCondition.Count}", ChampionMatch(rule.PlayerMatch, playerCondition, playerBaseIds)));
            if (rule.MinimumEnemyRoleCount > 0)
            {
                var count = enemyBaseIds.Count(baseId => knownRoles?.TryGetValue(baseId, out var roles) == true && (roles & rule.EnemyRoles) != 0);
                checks.Add(($"enemy role count {count}/{rule.MinimumEnemyRoleCount}", count >= rule.MinimumEnemyRoleCount));
            }
            if (rule.DraftRule != ArenaPickRuleDraft.Any)
                checks.Add(($"{rule.DraftRule.ToString().ToLowerInvariant()} draft", draftRuleKnown
                    && (rule.DraftRule == ArenaPickRuleDraft.Shared ? allowDuplicatePicks : !allowDuplicatePicks)));
            if (rule.FirstTurn != ArenaPickRuleFirstTurn.Any)
                checks.Add(($"{rule.FirstTurn.ToString().ToLowerInvariant()} first", firstTurn == rule.FirstTurn.ToString().ToLowerInvariant()));
            if (rule.MinimumVisibleEnemyPicks > 0)
                checks.Add(($"enemy picks {enemyBaseIds.Count}/{rule.MinimumVisibleEnemyPicks}", enemyBaseIds.Count >= rule.MinimumVisibleEnemyPicks));
            return (checks.All(check => check.Result), string.Join("; ", checks.Select(check => $"{check.Name}={(check.Result ? "true" : "false")}")));
        }
    }

    private static bool ChampionMatch(ArenaChampionMatch match, IReadOnlyCollection<int> expected, IReadOnlyCollection<int> actual) => match switch
    {
        ArenaChampionMatch.Any => expected.Any(actual.Contains),
        ArenaChampionMatch.All => expected.All(actual.Contains),
        ArenaChampionMatch.None => expected.All(id => !actual.Contains(id)),
        _ => false
    };
}

public enum LiveArenaAutomationMode { Off, DryRun, Armed }

public sealed record LiveArenaDecision(
    string Key,
    string Action,
    int[] Values,
    string Explanation,
    ArenaPickRuleEvaluation[]? RuleEvaluations = null);

public sealed record LiveArenaSessionDecision(string Action, string Explanation, int BeforeValue = 0);
public enum LiveArenaBattleOutcome { Unknown, Win, Loss }

public static class LiveArenaSessionPlanner
{
    public const string DeferredReturnMessage = "The traced Live Arena result dialog is not visibly active.";
    public const int DeferredReturnMaxAttempts = 20;
    public static readonly TimeSpan DeferredReturnRetryDelay = TimeSpan.FromMilliseconds(500);

    public static bool IsDeferredReturn(string action, string state, string message) =>
        action == "return" && state == "live-deferred" && message == DeferredReturnMessage;

    public static bool LimitReached(int completedBattles, int battleLimit)
    {
        if (completedBattles < 0) throw new ArgumentOutOfRangeException(nameof(completedBattles));
        if (battleLimit is < 1 or > 999) throw new ArgumentOutOfRangeException(nameof(battleLimit));
        return completedBattles >= battleLimit;
    }

    public static LiveArenaBattleOutcome Outcome(BattleSnapshotMessage snapshot)
    {
        if (!snapshot.Active || snapshot.Kind != 6 || !snapshot.Finished) return LiveArenaBattleOutcome.Unknown;
        var allyAlive = snapshot.Heroes.Any(hero => hero.Team == "Ally" && !hero.Dead && hero.Health > 0);
        var enemyAlive = snapshot.Heroes.Any(hero => hero.Team == "Enemy" && !hero.Dead && hero.Health > 0);
        if (allyAlive && !enemyAlive) return LiveArenaBattleOutcome.Win;
        if (!allyAlive && enemyAlive) return LiveArenaBattleOutcome.Loss;
        return LiveArenaBattleOutcome.Unknown;
    }

    public static LiveArenaSessionDecision? Decide(LiveArenaSnapshotMessage snapshot, bool autoRefill, bool rewardBatchInProgress = false, bool rewardClaimWaiting = false)
    {
        if (snapshot.Ui.FinishVisible) return new("return", "Close the result screen and return to Live Arena.");
        if (snapshot.Ui.RewardOverlayVisible) return new("reward-close", "Close the blocking Live Arena reward overlay.");
        if (rewardClaimWaiting) return null;
        if (snapshot.Ui.DailyBattleRefillReady)
            return new("reward-refill", "Claim the completed five-battle reward containing a free Live Arena refill.", snapshot.Ui.RewardClaimableCount);
        if (snapshot.Ui.RewardBatchReady || (rewardBatchInProgress && snapshot.Ui.RewardClaimableCount > 0))
            return new("reward-claim", $"Claim the next reward in the complete Live Arena daily batch ({snapshot.Ui.RewardClaimableCount} remaining).", snapshot.Ui.RewardClaimableCount);
        if (snapshot.Ui.RefillVisible)
            return autoRefill && snapshot.Ui.RefillCanConfirm
                ? new("refill", snapshot.Ui.RefillGemPrice == 0
                    ? "Consume the visible free Live Arena token refill."
                    : $"Confirm the visible Live Arena token refill for {snapshot.Ui.RefillGemPrice} Gems.", snapshot.Ui.RefillGemPrice)
                : null;
        if (snapshot.Ui.MenuVisible && snapshot.Ui.QueueAvailable && !snapshot.Matchmaking
            && snapshot.Draft.Phase is null && !snapshot.Transport.Active)
            return new("queue", "Start the next Live Arena opponent search.");
        return null;
    }
}

public static class LiveArenaDecisionEngine
{
    public static LiveArenaDecision? Decide(LiveArenaSnapshotMessage snapshot, ArenaStrategyFile strategy, IReadOnlyDictionary<int, ArenaRole>? knownRoles = null)
    {
        strategy.Validate(true);
        if (snapshot.Draft.Turn != "player") return null;
        return snapshot.Draft.Phase switch
        {
            "heroPick" => Pick(snapshot, strategy, knownRoles),
            "heroBan" => Ban(snapshot, strategy, knownRoles),
            "leaderSelection" => Leader(snapshot, strategy),
            _ => null
        };
    }

    private static LiveArenaDecision Pick(LiveArenaSnapshotMessage snapshot, ArenaStrategyFile strategy, IReadOnlyDictionary<int, ArenaRole>? knownRoles)
    {
        var player = snapshot.Draft.PlayerHeroes.Select(hero => hero.BaseId).ToArray();
        var enemy = snapshot.Draft.EnemyHeroes.Select(hero => hero.BaseId).ToArray();
        var duplicatePicksAllowed = snapshot.Draft.AllowDuplicatePicks == true;
        var required = PicksRequired(snapshot);
        if (strategy.DraftMode == ArenaDraftMode.PresetLineup)
        {
            var resolution = PresetLineupResolver.Resolve(strategy.PresetLineup!, player, enemy, duplicatePicksAllowed, required,
                strategy.PickRules, snapshot.Draft.FirstTurn, knownRoles, snapshot.Draft.AllowDuplicatePicks is not null);
            var presetNames = string.Join(" + ", resolution.Picks.Select(candidate => candidate.Name));
            var ruleDetails = resolution.RuleEvaluations.Where(result => result.Matched).Select(result => result.Explanation).ToArray();
            var ruleExplanation = ruleDetails.Length == 0 ? "No pick rule matched." : string.Join(" ", ruleDetails);
            return new(Key(snapshot), "pick", resolution.Picks.Select(candidate => checked((int)candidate.InstanceId)).ToArray(),
                $"Pick {presetNames} from Preset Lineup. {ruleExplanation} {resolution.Explanation}.", resolution.RuleEvaluations);
        }
        var unavailableEnemy = duplicatePicksAllowed ? Array.Empty<int>() : enemy;
        var futureEnemyPicks = duplicatePicksAllowed ? 0 : Math.Min(2, 5 - enemy.Length);
        var recommendation = ArenaDraftPlanner.Recommend(strategy.Pool.Select(candidate =>
            new ArenaCandidate(candidate.InstanceId, candidate.TypeId, candidate.BaseId, candidate.Name, candidate.Roles, candidate.Priority, candidate.LeaderPriority)).ToArray(),
            player, unavailableEnemy, required, futureEnemyPicks, knownRoles, enemy);
        var names = string.Join(" + ", recommendation.Picks.Select(candidate => candidate.Name));
        var target = string.Join(", ", recommendation.TargetTeam.Select(candidate => candidate.Name));
        return new(Key(snapshot), "pick", recommendation.Picks.Select(candidate => checked((int)candidate.InstanceId)).ToArray(),
            $"Pick {names}. {recommendation.Archetype} target: {target}. {recommendation.ValidCompletionCount} ban-safe completion(s); denial score {recommendation.DenialScore}; opponent adaptation {recommendation.AdaptationScore}.");
    }

    private static LiveArenaDecision Ban(LiveArenaSnapshotMessage snapshot, ArenaStrategyFile strategy, IReadOnlyDictionary<int, ArenaRole>? knownRoles)
    {
        var ordered = strategy.BanPriority.Select((baseId, priority) => (baseId, priority)).ToDictionary(item => item.baseId, item => item.priority);
        var configured = strategy.Pool.ToDictionary(candidate => candidate.BaseId);
        var target = snapshot.Draft.EnemyHeroes
            .OrderBy(hero => ordered.TryGetValue(hero.BaseId, out var priority) ? priority : int.MaxValue)
            .ThenByDescending(hero => ThreatScore(RolesFor(hero.BaseId)))
            .ThenBy(hero => hero.Slot == snapshot.Draft.BestEnemyBlockedSlot ? 0 : 1)
            .ThenBy(hero => hero.Slot)
            .FirstOrDefault() ?? throw new InvalidOperationException("RAID exposes no opponent champion that can be banned.");
        var reason = ordered.ContainsKey(target.BaseId) ? "explicit ban priority"
            : ThreatScore(RolesFor(target.BaseId)) > 0 ? "known role threat"
            : target.Slot == snapshot.Draft.BestEnemyBlockedSlot ? "RAID threat fallback" : "first legal fallback";
        return new(Key(snapshot), "ban", [target.Slot], $"Ban {target.Name} in slot {target.Slot + 1}: {reason}.");

        ArenaRole RolesFor(int baseId)
        {
            if (configured.TryGetValue(baseId, out var candidate)) return candidate.Roles;
            return knownRoles?.TryGetValue(baseId, out var imported) == true ? imported : ArenaRole.None;
        }
    }

    private static int ThreatScore(ArenaRole roles)
    {
        var score = 0;
        if ((roles & ArenaRole.Initiative) != 0) score += 50;
        if ((roles & ArenaRole.Opener) != 0) score += 40;
        if ((roles & ArenaRole.Control) != 0) score += 30;
        if ((roles & ArenaRole.Sustain) != 0) score += 25;
        if ((roles & ArenaRole.Damage) != 0) score += 20;
        return score;
    }

    private static LiveArenaDecision Leader(LiveArenaSnapshotMessage snapshot, ArenaStrategyFile strategy)
    {
        var banned = snapshot.Draft.PlayerBlockedSlot;
        var configured = strategy.Pool.ToDictionary(candidate => candidate.BaseId);
        var leaderPriority = strategy.LeaderPriority ?? strategy.Pool.OrderBy(candidate => candidate.LeaderPriority).Select(candidate => candidate.BaseId).ToList();
        var ordered = leaderPriority.Select((baseId, priority) => (baseId, priority)).ToDictionary(item => item.baseId, item => item.priority);
        var target = snapshot.Draft.PlayerHeroes.Where(hero => hero.Slot != banned)
            .OrderBy(hero => ordered.TryGetValue(hero.BaseId, out var priority) ? priority : int.MaxValue)
            .ThenBy(hero => configured.TryGetValue(hero.BaseId, out var candidate) && candidate.Roles.HasFlag(ArenaRole.Initiative) ? 0 : 1)
            .ThenBy(hero => hero.Slot)
            .FirstOrDefault() ?? throw new InvalidOperationException("RAID exposes no surviving champion that can be the leader.");
        return new(Key(snapshot), "leader", [target.Slot], $"Select {target.Name} in slot {target.Slot + 1} as leader using configured leader priority.");
    }

    private static int PicksRequired(LiveArenaSnapshotMessage snapshot)
    {
        var player = snapshot.Draft.PlayerHeroes.Length;
        var enemy = snapshot.Draft.EnemyHeroes.Length;
        if (player >= 5) throw new InvalidDataException("RAID requested a pick after five player champions were already selected.");
        if (player == 0 && enemy == 0) return 1;
        return Math.Min(2, 5 - player);
    }

    private static string Key(LiveArenaSnapshotMessage snapshot) => snapshot.Draft.Revision is int revision
        ? $"{revision}:{snapshot.Draft.Phase}:{snapshot.Draft.Turn}"
        : throw new InvalidDataException("RAID did not expose the current Live Arena turn revision.");
}
