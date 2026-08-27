using System.IO;
using System.Numerics;

namespace RslArenaResearch;

[Flags]
public enum ArenaRole
{
    None = 0,
    Initiative = 1,
    Opener = 2,
    Damage = 4,
    Control = 8,
    Protection = 16,
    Sustain = 32,
    Cleanse = 64,
    Utility = 128,
    All = Initiative | Opener | Damage | Control | Protection | Sustain | Cleanse | Utility
}

public enum ArenaArchetype { Speed, Balanced, GoSecond }

public sealed record ArenaCandidate(long InstanceId, int TypeId, int BaseId, string Name, ArenaRole Roles, int Priority, int LeaderPriority = int.MaxValue);
public sealed record ArenaTeamEvaluation(bool ValidAfterEveryBan, int WorstScore, int AverageScore, ArenaArchetype WeakestArchetype);
public sealed record ArenaDraftRecommendation(ArenaCandidate[] Picks, ArenaCandidate[] TargetTeam, ArenaArchetype Archetype, int DenialScore, int ValidCompletionCount, int AdaptationScore);

public static class ArenaRoleDefaults
{
    public static ArenaRole FromMarker(int marker) => marker switch
    {
        200 => ArenaRole.Damage,
        201 => ArenaRole.Protection,
        202 => ArenaRole.Sustain | ArenaRole.Utility,
        203 => ArenaRole.Initiative,
        _ => ArenaRole.None
    };
}

public static class ArenaDraftPlanner
{
    private static readonly IReadOnlyDictionary<ArenaArchetype, ArenaRole[]> Requirements = new Dictionary<ArenaArchetype, ArenaRole[]>
    {
        [ArenaArchetype.Speed] = [ArenaRole.Initiative, ArenaRole.Opener | ArenaRole.Control | ArenaRole.Utility, ArenaRole.Damage, ArenaRole.Damage | ArenaRole.Cleanse | ArenaRole.Sustain | ArenaRole.Protection],
        [ArenaArchetype.Balanced] = [ArenaRole.Damage, ArenaRole.Protection | ArenaRole.Sustain, ArenaRole.Cleanse | ArenaRole.Control, ArenaRole.Initiative | ArenaRole.Opener | ArenaRole.Utility | ArenaRole.Damage],
        [ArenaArchetype.GoSecond] = [ArenaRole.Protection | ArenaRole.Sustain, ArenaRole.Cleanse | ArenaRole.Sustain, ArenaRole.Damage, ArenaRole.Control | ArenaRole.Utility | ArenaRole.Damage]
    };

    public static ArenaDraftRecommendation Recommend(
        IReadOnlyList<ArenaCandidate> pool,
        IReadOnlyCollection<int> playerBaseIds,
        IReadOnlyCollection<int> enemyBaseIds,
        int picksRequired,
        int futureEnemyPicks,
        IReadOnlyDictionary<int, ArenaRole>? knownRoles = null,
        IReadOnlyCollection<int>? observedEnemyBaseIds = null)
    {
        Validate(pool, playerBaseIds, enemyBaseIds, picksRequired, futureEnemyPicks);
        var player = playerBaseIds.Select(id => pool.Single(candidate => candidate.BaseId == id)).ToArray();
        var observed = observedEnemyBaseIds ?? enemyBaseIds;
        if (observed.Count > 5 || observed.Any(id => id <= 0) || observed.Distinct().Count() != observed.Count
            || knownRoles?.Any(item => item.Key <= 0 || item.Value == ArenaRole.None || (item.Value & ~ArenaRole.All) != 0) == true)
            throw new InvalidDataException("The observed opponent role catalog is invalid.");
        var enemyRoles = observed.Aggregate(ArenaRole.None, (roles, baseId) => roles | KnownRole(baseId));
        var unavailable = enemyBaseIds.ToHashSet();
        var available = pool.Where(candidate => !unavailable.Contains(candidate.BaseId) && !playerBaseIds.Contains(candidate.BaseId)).ToArray();
        ArenaDraftRecommendation? best = null;
        foreach (var picks in Combinations(available, picksRequired))
        {
            var locked = player.Concat(picks).ToArray();
            var future = available.Except(picks).ToArray();
            var needed = 5 - locked.Length;
            var completions = Combinations(future, needed)
                .Select(rest => (Team: locked.Concat(rest).ToArray(), Evaluation: EvaluateTeam(locked.Concat(rest).ToArray())))
                .ToArray();
            if (completions.Length == 0) continue;
            var target = completions.OrderByDescending(option => option.Evaluation.WorstScore)
                .ThenByDescending(option => option.Evaluation.AverageScore)
                .ThenByDescending(option => AdaptationScore(option.Team, enemyRoles))
                .ThenByDescending(option => PriorityScore(option.Team)).First();
            var denialScore = DenialScore(completions, future, Math.Min(futureEnemyPicks, future.Length));
            var recommendation = new ArenaDraftRecommendation(
                picks.OrderBy(candidate => candidate.Priority).ToArray(),
                target.Team,
                target.Evaluation.WeakestArchetype,
                denialScore,
                completions.Count(option => option.Evaluation.ValidAfterEveryBan),
                AdaptationScore(target.Team, enemyRoles));
            if (best is null || Better(recommendation, best)) best = recommendation;
        }
        return best ?? throw new InvalidOperationException("No legal five-champion Live Arena completion remains.");

        ArenaRole KnownRole(int baseId)
        {
            var configured = pool.FirstOrDefault(candidate => candidate.BaseId == baseId);
            if (configured is not null) return configured.Roles;
            return knownRoles?.TryGetValue(baseId, out var imported) == true ? imported : ArenaRole.None;
        }
    }

    public static ArenaTeamEvaluation EvaluateTeam(IReadOnlyList<ArenaCandidate> team)
    {
        if (team.Count != 5 || team.Select(candidate => candidate.BaseId).Distinct().Count() != 5)
            throw new InvalidDataException("A Live Arena target team must contain five unique champion base identifiers.");
        var postBan = new List<(int Score, ArenaArchetype Archetype)>();
        for (var banned = 0; banned < team.Count; banned++)
        {
            var survivors = team.Where((_, index) => index != banned).ToArray();
            postBan.Add(Enum.GetValues<ArenaArchetype>()
                .Select(archetype => (Score: Score(survivors, archetype), Archetype: archetype))
                .OrderByDescending(result => result.Score).First());
        }
        var weakest = postBan.OrderBy(result => result.Score).First();
        return new(postBan.All(result => result.Score >= 40000), weakest.Score, (int)postBan.Average(result => result.Score), weakest.Archetype);
    }

    private static int DenialScore((ArenaCandidate[] Team, ArenaTeamEvaluation Evaluation)[] completions, ArenaCandidate[] future, int denialCount)
    {
        if (denialCount == 0) return completions.Max(option => option.Evaluation.WorstScore);
        var worst = int.MaxValue;
        foreach (var denied in Combinations(future, denialCount))
        {
            var ids = denied.Select(candidate => candidate.BaseId).ToHashSet();
            var score = completions.Where(option => option.Team.All(candidate => !ids.Contains(candidate.BaseId)))
                .Select(option => option.Evaluation.WorstScore).DefaultIfEmpty(int.MinValue / 2).Max();
            worst = Math.Min(worst, score);
        }
        return worst;
    }

    private static int Score(IReadOnlyList<ArenaCandidate> team, ArenaArchetype archetype)
    {
        var matched = MaximumMatching(team, Requirements[archetype], 0, 0);
        var roles = team.Aggregate(ArenaRole.None, (current, candidate) => current | candidate.Roles);
        return matched * 10000 + BitOperations.PopCount((uint)roles) * 100 + PriorityScore(team)
            + team.Where(candidate => candidate.LeaderPriority != int.MaxValue).Select(candidate => Math.Max(0, 100 - candidate.LeaderPriority)).DefaultIfEmpty().Max();
    }

    private static int MaximumMatching(IReadOnlyList<ArenaCandidate> team, IReadOnlyList<ArenaRole> requirements, int requirement, int used)
    {
        if (requirement == requirements.Count) return 0;
        var best = MaximumMatching(team, requirements, requirement + 1, used);
        for (var index = 0; index < team.Count; index++)
            if ((used & (1 << index)) == 0 && (team[index].Roles & requirements[requirement]) != 0)
                best = Math.Max(best, 1 + MaximumMatching(team, requirements, requirement + 1, used | (1 << index)));
        return best;
    }

    private static int PriorityScore(IEnumerable<ArenaCandidate> team) => team.Sum(candidate => Math.Max(0, 100 - candidate.Priority));

    private static int AdaptationScore(IEnumerable<ArenaCandidate> team, ArenaRole enemyRoles)
    {
        var score = 0;
        foreach (var candidate in team)
        {
            if ((enemyRoles & (ArenaRole.Initiative | ArenaRole.Opener)) != 0 && (candidate.Roles & (ArenaRole.Protection | ArenaRole.Cleanse | ArenaRole.Sustain)) != 0) score += 30;
            if ((enemyRoles & (ArenaRole.Protection | ArenaRole.Sustain)) != 0 && (candidate.Roles & (ArenaRole.Damage | ArenaRole.Control)) != 0) score += 20;
            if ((enemyRoles & (ArenaRole.Damage | ArenaRole.Control)) != 0 && (candidate.Roles & (ArenaRole.Protection | ArenaRole.Cleanse | ArenaRole.Sustain)) != 0) score += 20;
        }
        return score;
    }

    private static bool Better(ArenaDraftRecommendation candidate, ArenaDraftRecommendation current) =>
        candidate.DenialScore > current.DenialScore
        || candidate.DenialScore == current.DenialScore && candidate.ValidCompletionCount > current.ValidCompletionCount
        || candidate.DenialScore == current.DenialScore && candidate.ValidCompletionCount == current.ValidCompletionCount
            && candidate.AdaptationScore > current.AdaptationScore
        || candidate.DenialScore == current.DenialScore && candidate.ValidCompletionCount == current.ValidCompletionCount
            && candidate.AdaptationScore == current.AdaptationScore
            && PriorityScore(candidate.Picks) > PriorityScore(current.Picks);

    private static void Validate(IReadOnlyList<ArenaCandidate> pool, IReadOnlyCollection<int> player, IReadOnlyCollection<int> enemy, int picksRequired, int futureEnemyPicks)
    {
        if (pool.Count is < 5 or > 20) throw new InvalidDataException("A Live Arena pool must contain between 5 and 20 champions.");
        if (pool.Any(candidate => candidate.InstanceId <= 0 || candidate.TypeId <= 0 || candidate.BaseId <= 0 || string.IsNullOrWhiteSpace(candidate.Name)
            || candidate.Roles == ArenaRole.None || candidate.Priority < 0 || candidate.LeaderPriority < 0))
            throw new InvalidDataException("A Live Arena pool candidate is invalid or has no role.");
        if (pool.Select(candidate => candidate.InstanceId).Distinct().Count() != pool.Count || pool.Select(candidate => candidate.BaseId).Distinct().Count() != pool.Count)
            throw new InvalidDataException("A Live Arena pool cannot contain duplicate instances or champion base identifiers.");
        if (player.Count > 5 || player.Count != player.Distinct().Count() || enemy.Count > 5 || enemy.Count != enemy.Distinct().Count()
            || player.Any(id => pool.All(candidate => candidate.BaseId != id)) || player.Intersect(enemy).Any())
            throw new InvalidDataException("The current Live Arena draft state is invalid.");
        if (picksRequired is < 1 or > 2 || player.Count + picksRequired > 5 || futureEnemyPicks is < 0 or > 2)
            throw new InvalidDataException("The requested Live Arena pick batch is invalid.");
    }

    private static IEnumerable<T[]> Combinations<T>(IReadOnlyList<T> values, int count)
    {
        if (count == 0) { yield return []; yield break; }
        var buffer = new T[count];
        foreach (var result in Choose(0, 0)) yield return result;

        IEnumerable<T[]> Choose(int start, int depth)
        {
            if (depth == count) { yield return [.. buffer]; yield break; }
            for (var index = start; index <= values.Count - (count - depth); index++)
            {
                buffer[depth] = values[index];
                foreach (var result in Choose(index + 1, depth + 1)) yield return result;
            }
        }
    }
}
