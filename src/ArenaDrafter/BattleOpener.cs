using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace ArenaDrafter;

public sealed record BattleTargetChampionOption(int BaseId, string Name);

public sealed class BattleOpenerStepRow(
    int slot,
    int typeId,
    string name,
    int targetType,
    string targetPolicy,
    int? targetBaseId,
    IReadOnlyList<BattleTargetChampionOption> allyTargets,
    ImageSource? icon = null,
    string formLabel = "Base form",
    string cooldownLabel = "",
    string targetLabel = "",
    bool requiresTarget = false) : INotifyPropertyChanged
{
    private string targetPolicy = targetPolicy;
    private int? targetBaseId = targetBaseId;
    private ImageSource? icon = icon;
    public int Slot { get; } = slot;
    public int TypeId { get; } = typeId;
    public string Name { get; } = name;
    public int TargetType { get; } = targetType;
    public bool RequiresTarget { get; } = requiresTarget;
    public string FormLabel { get; } = formLabel;
    public string CooldownLabel { get; } = cooldownLabel;
    public string TargetLabel { get; } = targetLabel;
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
    public string TargetPolicy
    {
        get => targetPolicy;
        set
        {
            if (targetPolicy == value) return;
            targetPolicy = value;
            PropertyChanged?.Invoke(this, new(nameof(TargetPolicy)));
            PropertyChanged?.Invoke(this, new(nameof(UsesSpecificAlly)));
        }
    }
    public int? TargetBaseId
    {
        get => targetBaseId;
        set
        {
            if (targetBaseId == value) return;
            targetBaseId = value;
            PropertyChanged?.Invoke(this, new(nameof(TargetBaseId)));
        }
    }
    public string Label => $"A{Slot + 1}";
    public IReadOnlyList<string> TargetOptions => BattleTargetPolicies.Options(TargetType, RequiresTarget);
    public IReadOnlyList<BattleTargetChampionOption> AllyTargets { get; } = allyTargets;
    public bool UsesSpecificAlly => TargetPolicy == BattleTargetPolicies.SpecificAlly;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record BattleOpenerChampion(
    int BaseId,
    List<int> SkillSlots,
    List<string>? TargetPolicies = null,
    List<int>? SkillTypeIds = null,
    List<int?>? TargetBaseIds = null);

public sealed record BattleOpenerFile(int Version, List<BattleOpenerChampion> Champions)
{
    public const int CurrentVersion = 1;
    private static readonly string Path = System.IO.Path.Combine(AppPaths.Data, "live-arena-opener.json");

    public static BattleOpenerFile Load()
    {
        Directory.CreateDirectory(AppPaths.Data);
        if (!File.Exists(Path)) return new(CurrentVersion, []);
        var value = JsonSerializer.Deserialize<BattleOpenerFile>(File.ReadAllText(Path))
            ?? throw new InvalidDataException("The Live Arena opener file is empty.");
        value.Validate(true);
        var migrated = value with
        {
            Champions = value.Champions.Where(champion => champion.SkillTypeIds is not null).ToList()
        };
        migrated.Validate();
        if (migrated.Champions.Count != value.Champions.Count) migrated.Save();
        return migrated;
    }

    public void Save()
    {
        Validate();
        Directory.CreateDirectory(AppPaths.Data);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, Path, true);
    }

    public void Validate() => Validate(false);

    private void Validate(bool allowLegacySlotOnlySkills)
    {
        if (Version != CurrentVersion || Champions is null || Champions.Count > 20
            || Champions.Any(champion => champion.BaseId <= 0 || champion.SkillSlots is null || champion.SkillSlots.Count > 12
                || champion.SkillSlots.Any(slot => slot is < 0 or > 11)
                || champion.TargetPolicies is not null && (champion.TargetPolicies.Count != champion.SkillSlots.Count
                    || champion.TargetPolicies.Any(policy => !BattleTargetPolicies.IsKnown(policy)))
                || (!allowLegacySlotOnlySkills && champion.SkillTypeIds is null)
                || champion.SkillTypeIds is not null && (champion.SkillTypeIds.Count != champion.SkillSlots.Count
                    || champion.SkillTypeIds.Any(typeId => typeId <= 0))
                || champion.TargetBaseIds is not null && (champion.TargetBaseIds.Count != champion.SkillSlots.Count
                    || champion.TargetBaseIds.Any(baseId => baseId is <= 0)))
            || Champions.Select(champion => champion.BaseId).Distinct().Count() != Champions.Count)
            throw new InvalidDataException("The Live Arena opener configuration is invalid.");
    }
}

public static class BattleTargetPolicies
{
    public const string Automatic = "Automatic";
    public const string Self = "Self";
    public const string LowestHpAlly = "Lowest HP ally";
    public const string HighestHpAlly = "Highest HP ally";
    public const string Leader = "Leader";
    public const string FirstAlly = "First ally";
    public const string SpecificAlly = "Specific ally";
    public const string LowestHpEnemy = "Lowest HP enemy";
    public const string HighestHpEnemy = "Highest HP enemy";
    public const string FirstEnemy = "First enemy";
    public const string EnemyLeader = "Enemy leader";
    public const string ThreatPriority = "Threat priority (ban list)";
    public const string FirstDeadAlly = "First dead ally";
    public const string FirstDeadEnemy = "First dead enemy";

    private static readonly string[] All =
    [
        Automatic, Self, LowestHpAlly, HighestHpAlly, Leader, FirstAlly, SpecificAlly,
        LowestHpEnemy, HighestHpEnemy, FirstEnemy, EnemyLeader, ThreatPriority, FirstDeadAlly, FirstDeadEnemy
    ];

    public static bool IsKnown(string policy) => All.Contains(policy, StringComparer.Ordinal);
    public static bool IsSingleTarget(int targetType, bool requiresTarget) => requiresTarget && targetType is 1 or 2 or 3 or 4 or 7 or 8 or 10 or 11;

    public static IReadOnlyList<string> Options(int targetType, bool requiresTarget = true)
    {
        if (!IsSingleTarget(targetType, requiresTarget)) return [Automatic];
        return targetType switch
    {
        1 => [Automatic, Self, SpecificAlly, LowestHpAlly, HighestHpAlly, Leader, FirstAlly],
        7 => [Automatic, SpecificAlly, LowestHpAlly, HighestHpAlly, Leader, FirstAlly],
        2 or 8 => [Automatic, ThreatPriority, EnemyLeader, LowestHpEnemy, HighestHpEnemy, FirstEnemy],
        3 or 11 => [Automatic, SpecificAlly, FirstDeadAlly],
        4 => [Automatic, FirstDeadEnemy],
        10 => [Automatic, Self, SpecificAlly, LowestHpAlly, HighestHpAlly, Leader, FirstAlly,
            ThreatPriority, EnemyLeader, LowestHpEnemy, HighestHpEnemy, FirstEnemy],
        _ => [Automatic]
    };
    }

    public static string Default(int targetType, bool requiresTarget = true) => Options(targetType, requiresTarget)[0];
}

public sealed record BattleOpenerDecision(
    string Action,
    int SkillTypeId,
    int SkillSlot,
    int TargetId,
    int BaseId,
    bool ConsumesConfiguredStep,
    string Explanation)
{
    // False routes area/self skills through the visible click-and-commit path;
    // TargetId remains the validated internal completion hero for that path.
    public bool RequiresExplicitTarget { get; init; }
}

public static class BattleOpenerPlanner
{
    public static TimeSpan HudStabilizationDelay => TimeSpan.FromSeconds(1);

    public static TimeSpan VerificationTimeout(string action) =>
        TimeSpan.FromSeconds(action == "skill" ? 30 : 8);

    // A failed command is never evidence that the configured step happened. The
    // host may recover Auto mode, but the step remains pending for a later turn.
    public static int ProgressAfterFailedAction(BattleOpenerDecision decision, int currentProgress) => currentProgress;

    public static bool IsHudTransitionPending(BattleSnapshotMessage snapshot) =>
        snapshot.HudVisible && (!snapshot.SkillSelectionAvailable || snapshot.HudSkillCount == 0 || snapshot.HudSkills.Length == 0);

    public static string? TerminalFailureReason(
        BattleOpenerDecision decision,
        BattleSnapshotMessage before,
        BattleSnapshotMessage after)
    {
        if (IsActionApplied(decision, before, after)) return null;
        if (decision.Action == "skill" && (after.Turn != before.Turn || after.ActiveHeroId != before.ActiveHeroId))
            return "RAID advanced to another turn without applying the configured skill.";
        return null;
    }

    public static bool IsActionApplied(
        BattleOpenerDecision decision,
        BattleSnapshotMessage before,
        BattleSnapshotMessage after)
    {
        if (decision.Action == "auto") return after.AutoMode;
        if (decision.Action == "manual") return !after.AutoMode;
        if (decision.Action != "skill") return false;

        var beforeHero = before.Heroes.FirstOrDefault(hero => hero.Id == before.ActiveHeroId && hero.BaseId == decision.BaseId);
        var beforeSkill = beforeHero?.Skills.FirstOrDefault(skill => skill.TypeId == decision.SkillTypeId);
        if (beforeHero is null || beforeSkill is null) return false;

        var afterHero = after.Heroes.FirstOrDefault(hero => hero.Id == beforeHero.Id);
        if (afterHero is null) return false;
        var afterSkill = afterHero.Skills.FirstOrDefault(skill => skill.TypeId == decision.SkillTypeId);
        if (afterSkill is null) return true;
        if (beforeSkill.MaxCooldown > 0) return afterSkill.Cooldown > beforeSkill.Cooldown;
        return after.Turn != before.Turn || after.ActiveHeroId != before.ActiveHeroId;
    }

    public static BattleOpenerDecision? Decide(
        BattleSnapshotMessage snapshot,
        BattleOpenerFile configuration,
        IReadOnlyDictionary<int, int> progress,
        int? playerLeaderSlot = null,
        bool initialAutoVerified = true,
        int? enemyLeaderSlot = null,
        IReadOnlyList<int>? enemyThreatPriority = null)
    {
        configuration.Validate();
        if (!snapshot.Active || snapshot.Finished || snapshot.Kind != 6) return null;
        if (!initialAutoVerified)
            return snapshot.ModeChangeAvailable ? Auto("Battle started. Enabling Auto mode before any configured opening step.") : null;
        var aliveAllies = snapshot.Heroes.Where(hero => hero.Team == "Ally" && !hero.Dead).ToArray();
        var remaining = configuration.Champions.Where(configured =>
            aliveAllies.Any(hero => hero.BaseId == configured.BaseId)
            && progress.GetValueOrDefault(configured.BaseId) < configured.SkillSlots.Count).ToArray();
        if (remaining.Length == 0)
            return snapshot.AutoMode || !snapshot.ModeChangeAvailable ? null : Auto("Opening sequence complete. Enabling Auto mode.");

        var active = snapshot.Heroes.FirstOrDefault(hero => hero.Id == snapshot.ActiveHeroId);
        if (active is null || active.Team != "Ally" || active.Dead)
            return snapshot.AutoMode || !snapshot.ModeChangeAvailable ? null : Auto("Auto mode handles turns until a configured allied champion becomes active.");

        var activeConfiguration = remaining.FirstOrDefault(configured => configured.BaseId == active.BaseId);
        if (activeConfiguration is null)
            return snapshot.AutoMode || !snapshot.ModeChangeAvailable ? null : Auto($"Auto mode handles {active.Name}'s turn because no opening step is pending for this champion.");

        if (snapshot.AutoMode)
            return snapshot.ModeChangeAvailable ? new("manual", 0, -1, 0, active.BaseId, false,
                $"Pausing Auto mode for {active.Name}'s configured opening step.")
                : null;

        // Mythical form changes briefly expose a valid Manual HUD with no skill
        // collection. Waiting here preserves the same turn and prevents an
        // Auto/Manual recovery loop while RAID rebuilds the alternate form.
        if (IsHudTransitionPending(snapshot)) return null;

        var step = progress.GetValueOrDefault(active.BaseId);
        var slot = activeConfiguration.SkillSlots[step];
        var configuredTypeId = activeConfiguration.SkillTypeIds?.ElementAtOrDefault(step) ?? 0;
        var skill = configuredTypeId > 0
            ? active.Skills.FirstOrDefault(candidate => candidate.TypeId == configuredTypeId)
            : active.Skills.FirstOrDefault(candidate => candidate.Slot == slot);
        if (skill is null || skill.Disabled || skill.Cooldown != 0)
            return snapshot.ModeChangeAvailable
                ? Auto($"{active.Name}'s configured skill (A{slot + 1}, ID {configuredTypeId}) is unavailable in the current form. Resuming Auto mode and retrying on a later turn.")
                : null;
        if (!snapshot.SkillSelectionAvailable || snapshot.HudSkillCount <= skill.Slot) return null;

        var policy = activeConfiguration.TargetPolicies?.ElementAtOrDefault(step) ?? BattleTargetPolicies.Default(skill.Target, skill.RequiresTarget);
        var targetBaseId = activeConfiguration.TargetBaseIds?.ElementAtOrDefault(step);
        var target = SelectTarget(snapshot, active, skill.Target, policy, targetBaseId,
            playerLeaderSlot, enemyLeaderSlot, enemyThreatPriority);
        if (target == 0)
            return snapshot.ModeChangeAvailable
                ? Auto($"{active.Name}'s configured {skill.Name} has no legal completion target. Resuming Auto mode.")
                : null;
        var targetHero = snapshot.Heroes.FirstOrDefault(hero => hero.Id == target);
        var targetText = skill.RequiresTarget
            ? $" Target: {targetHero?.Name ?? "none"} ({policy})."
            : string.Empty;
        return new("skill", skill.TypeId, skill.Slot, target, active.BaseId, true,
            $"Opening step {step + 1}: {active.Name} uses {skill.Name} (A{slot + 1}).{targetText}")
        {
            RequiresExplicitTarget = skill.RequiresTarget
        };
    }

    private static BattleOpenerDecision Auto(string explanation) => new("auto", 0, -1, 0, 0, false, explanation);

    private static int SelectTarget(
        BattleSnapshotMessage snapshot,
        BattleHeroWire active,
        int targetType,
        string policy,
        int? targetBaseId,
        int? playerLeaderSlot,
        int? enemyLeaderSlot,
        IReadOnlyList<int>? enemyThreatPriority)
    {
        IEnumerable<BattleHeroWire> candidates = targetType switch
        {
            0 => snapshot.Heroes.Where(hero => hero.Id == active.Id && !hero.Dead),
            2 or 6 or 8 => snapshot.Heroes.Where(hero => hero.Team == "Enemy" && !hero.Dead),
            3 or 11 => snapshot.Heroes.Where(hero => hero.Team == "Ally" && hero.Dead),
            4 => snapshot.Heroes.Where(hero => hero.Team == "Enemy" && hero.Dead),
            7 or 9 => snapshot.Heroes.Where(hero => hero.Team == "Ally" && !hero.Dead && hero.Id != active.Id),
            10 => snapshot.Heroes.Where(hero => !hero.Dead),
            _ => snapshot.Heroes.Where(hero => hero.Team == "Ally" && !hero.Dead)
        };
        var legal = candidates.ToArray();
        BattleHeroWire? target = policy switch
        {
            BattleTargetPolicies.Self => legal.FirstOrDefault(hero => hero.Team == "Ally" && hero.Id == active.Id),
            BattleTargetPolicies.SpecificAlly => legal.FirstOrDefault(hero => hero.Team == "Ally" && hero.BaseId == targetBaseId),
            BattleTargetPolicies.Leader => legal.FirstOrDefault(hero => hero.Team == "Ally" && hero.Slot == playerLeaderSlot + 1),
            BattleTargetPolicies.EnemyLeader => legal.FirstOrDefault(hero => hero.Team == "Enemy" && hero.Slot == enemyLeaderSlot + 1),
            BattleTargetPolicies.ThreatPriority => SelectThreatPriority(legal.Where(hero => hero.Team == "Enemy").ToArray(), enemyThreatPriority),
            BattleTargetPolicies.HighestHpAlly => legal.Where(hero => hero.Team == "Ally")
                .OrderByDescending(HealthRatio).ThenBy(hero => hero.Slot).FirstOrDefault(),
            BattleTargetPolicies.HighestHpEnemy => legal.Where(hero => hero.Team == "Enemy")
                .OrderByDescending(HealthRatio).ThenBy(hero => hero.Slot).FirstOrDefault(),
            BattleTargetPolicies.LowestHpAlly => legal.Where(hero => hero.Team == "Ally")
                .OrderBy(HealthRatio).ThenBy(hero => hero.Slot).FirstOrDefault(),
            BattleTargetPolicies.LowestHpEnemy => legal.Where(hero => hero.Team == "Enemy")
                .OrderBy(HealthRatio).ThenBy(hero => hero.Slot).FirstOrDefault(),
            BattleTargetPolicies.FirstAlly or BattleTargetPolicies.FirstDeadAlly => legal
                .Where(hero => hero.Team == "Ally").OrderBy(hero => hero.Slot).FirstOrDefault(),
            BattleTargetPolicies.FirstEnemy or BattleTargetPolicies.FirstDeadEnemy => legal
                .Where(hero => hero.Team == "Enemy").OrderBy(hero => hero.Slot).FirstOrDefault(),
            _ when targetType is 0 or 10 => legal.FirstOrDefault(hero => hero.Id == active.Id),
            _ => legal.OrderBy(HealthRatio).ThenBy(hero => hero.Slot).FirstOrDefault()
        };
        return target?.Id ?? 0;
    }

    private static BattleHeroWire? SelectThreatPriority(BattleHeroWire[] legal, IReadOnlyList<int>? priority)
    {
        foreach (var baseId in priority ?? [])
        {
            var target = legal.FirstOrDefault(hero => hero.BaseId == baseId);
            if (target is not null) return target;
        }
        return legal.OrderBy(HealthRatio).ThenBy(hero => hero.Slot).FirstOrDefault();
    }

    private static double HealthRatio(BattleHeroWire hero) => hero.MaxHealth == 0 ? 1d : (double)hero.Health / hero.MaxHealth;
}

public static class BattleCommands
{
    public const string Auto = "BATTLE_AUTO";
    public const string Manual = "BATTLE_MANUAL";
    public const string StartDiagnostics = "BATTLE_DIAGNOSTICS START";
    public const string StopDiagnostics = "BATTLE_DIAGNOSTICS STOP";
    public const string StartMythicalClickTrace = "MYTHICAL_CLICK_TRACE START";
    public const string StopMythicalClickTrace = "MYTHICAL_CLICK_TRACE STOP";

    public static string DiagnosticClick(int skillTypeId, int skillSlot, int targetId)
    {
        if (skillTypeId <= 0 || skillSlot is < 0 or > 11 || targetId < 0)
            throw new ArgumentOutOfRangeException(nameof(skillTypeId), "Battle skill identifiers must be positive, slots must be supported, and targets cannot be negative.");
        return $"BATTLE_SKILL_CLICK {skillTypeId},{skillSlot},{targetId}";
    }

    public static string Skill(int skillTypeId, int skillSlot, int targetId)
    {
        if (skillTypeId <= 0 || skillSlot is < 0 or > 11 || targetId < 0)
            throw new ArgumentOutOfRangeException(nameof(skillTypeId), "Battle skill identifiers must be positive, slots must be supported, and targets cannot be negative.");
        return $"BATTLE_SKILL {skillTypeId},{skillSlot},{targetId}";
    }
}

public static class RewardDiagnosticCommands
{
    public const string Start = "REWARD_DIAGNOSTICS START";
    public const string Stop = "REWARD_DIAGNOSTICS STOP";
}
