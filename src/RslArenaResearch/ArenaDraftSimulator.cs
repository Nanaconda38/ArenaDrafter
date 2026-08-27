using System.IO;

namespace RslArenaResearch;

public sealed class ArenaDraftSimulator
{
    private static readonly (string Actor, int Count)[] PickSchedule =
    [
        ("player", 1), ("opponent", 2), ("player", 2),
        ("opponent", 2), ("player", 2), ("opponent", 1)
    ];

    private readonly ArenaStrategyFile strategy;
    private readonly IReadOnlyDictionary<int, ArenaRole>? knownRoles;
    private readonly bool playerFirst;
    private int turnIndex;
    private int picksInTurn;
    private int revision;

    public ArenaDraftSimulator(ArenaStrategyFile strategy, bool playerFirst, bool allowDuplicatePicks, IReadOnlyDictionary<int, ArenaRole>? knownRoles = null)
    {
        strategy.Validate(true);
        this.strategy = strategy;
        this.knownRoles = knownRoles;
        this.playerFirst = playerFirst;
        AllowDuplicatePicks = allowDuplicatePicks;
    }

    public bool AllowDuplicatePicks { get; }
    public List<LiveArenaHeroWire> PlayerHeroes { get; } = [];
    public List<LiveArenaHeroWire> EnemyHeroes { get; } = [];
    public bool PicksComplete => turnIndex >= PickSchedule.Length;
    public string? CurrentActor => PicksComplete ? null : Actor(PickSchedule[turnIndex].Actor);
    public int PicksRemainingThisTurn => PicksComplete ? 0 : PickSchedule[turnIndex].Count - picksInTurn;

    public LiveArenaDecision RunPlayerTurn()
    {
        if (CurrentActor != "player") throw new InvalidOperationException("The simulator is waiting for opponent picks.");
        var decision = LiveArenaDecisionEngine.Decide(Snapshot("heroPick", "player"), strategy, knownRoles)
            ?? throw new InvalidOperationException("The strategy did not produce a simulated pick.");
        if (decision.Values.Length != PicksRemainingThisTurn) throw new InvalidDataException("The strategy produced the wrong simulated pick batch size.");
        foreach (var instanceId in decision.Values)
        {
            if (strategy.DraftMode == ArenaDraftMode.PresetLineup)
            {
                var candidate = strategy.PresetLineup!.SelectMany(slot => slot.Candidates)
                    .Concat((strategy.PickRules ?? []).Select(rule => rule.Replacement))
                    .DistinctBy(item => item.InstanceId)
                    .Single(item => item.InstanceId == instanceId);
                PlayerHeroes.Add(new(PlayerHeroes.Count, candidate.InstanceId, candidate.TypeId, candidate.BaseId, candidate.Name));
            }
            else
            {
                var candidate = strategy.Pool.Single(item => item.InstanceId == instanceId);
                PlayerHeroes.Add(new(PlayerHeroes.Count, candidate.InstanceId, candidate.TypeId, candidate.BaseId, candidate.Name));
            }
        }
        Advance(decision.Values.Length);
        return decision;
    }

    public void AddOpponentPick(int typeId, int baseId, string name)
    {
        if (CurrentActor != "opponent") throw new InvalidOperationException("The simulator is waiting for the bot pick.");
        if (typeId <= 0 || baseId <= 0 || string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("The simulated opponent champion is invalid.");
        if (EnemyHeroes.Any(hero => hero.BaseId == baseId)) throw new InvalidOperationException("The opponent already picked that champion.");
        if (!AllowDuplicatePicks && PlayerHeroes.Any(hero => hero.BaseId == baseId))
            throw new InvalidOperationException("That champion is already taken in this exclusive draft.");
        EnemyHeroes.Add(new(EnemyHeroes.Count, null, typeId, baseId, name));
        Advance(1);
    }

    public (LiveArenaDecision Ban, LiveArenaDecision Leader) Resolve(int playerBlockedSlot)
    {
        if (!PicksComplete || PlayerHeroes.Count != 5 || EnemyHeroes.Count != 5)
            throw new InvalidOperationException("Complete all ten simulated picks before resolving bans and leader.");
        if (playerBlockedSlot is < 0 or > 4) throw new InvalidDataException("The simulated opponent ban slot is invalid.");
        var ban = LiveArenaDecisionEngine.Decide(Snapshot("heroBan", "player"), strategy, knownRoles)
            ?? throw new InvalidOperationException("The strategy did not produce a simulated ban.");
        var leader = LiveArenaDecisionEngine.Decide(Snapshot("leaderSelection", "player", playerBlockedSlot), strategy, knownRoles)
            ?? throw new InvalidOperationException("The strategy did not produce a simulated leader.");
        return (ban, leader);
    }

    private LiveArenaSnapshotMessage Snapshot(string phase, string turn, int? playerBlockedSlot = null) => new(
        1, "liveArena", false, null,
        new(++revision, phase, playerFirst ? "player" : "opponent", turn, AllowDuplicatePicks ? 12 : 21, AllowDuplicatePicks,
            [.. PlayerHeroes], [.. EnemyHeroes], EnemyHeroes.Count == 0 ? null : 0, playerBlockedSlot, null, null, null, false),
        new(false, false, false, null, null, null, null, 0),
        new(false, false, false, false, false));

    private string Actor(string actor) => playerFirst ? actor : actor == "player" ? "opponent" : "player";

    private void Advance(int count)
    {
        picksInTurn += count;
        if (picksInTurn > PickSchedule[turnIndex].Count) throw new InvalidDataException("Too many simulated picks were added for the current turn.");
        if (picksInTurn != PickSchedule[turnIndex].Count) return;
        turnIndex++;
        picksInTurn = 0;
    }
}
