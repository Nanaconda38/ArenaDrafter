using System.IO;
using System.Text.Json;

namespace RslArenaResearch;

public sealed record LiveArenaDashboardStats(
    int Battles = 0,
    int Wins = 0,
    int Losses = 0,
    int Unknown = 0,
    int Refills = 0,
    int GemsSpent = 0)
{
    public static LiveArenaDashboardStats Empty { get; } = new();

    public LiveArenaDashboardStats AddBattle(LiveArenaBattleOutcome outcome) => outcome switch
    {
        LiveArenaBattleOutcome.Win => this with { Battles = Battles + 1, Wins = Wins + 1 },
        LiveArenaBattleOutcome.Loss => this with { Battles = Battles + 1, Losses = Losses + 1 },
        _ => this with { Battles = Battles + 1, Unknown = Unknown + 1 }
    };

    public LiveArenaDashboardStats AddRefill(int gemPrice)
    {
        if (gemPrice is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(gemPrice));
        return this with { Refills = Refills + 1, GemsSpent = checked(GemsSpent + gemPrice) };
    }

    public void Validate()
    {
        if (Battles is < 0 or > 10_000_000 || Wins is < 0 or > 10_000_000 || Losses is < 0 or > 10_000_000
            || Unknown is < 0 or > 10_000_000 || Refills is < 0 or > 10_000_000 || GemsSpent is < 0 or > 1_000_000_000
            || Wins + Losses + Unknown != Battles)
            throw new InvalidDataException("Live Arena dashboard counters are invalid.");
    }
}

public sealed record LiveArenaDashboardFile(int Version, LiveArenaDashboardStats LastRun, LiveArenaDashboardStats AllTime)
{
    public const int CurrentVersion = 1;
    private static readonly string Path = System.IO.Path.Combine(AppPaths.Data, "live-arena-dashboard.json");
    public static LiveArenaDashboardFile Empty { get; } = new(CurrentVersion, LiveArenaDashboardStats.Empty, LiveArenaDashboardStats.Empty);

    public static LiveArenaDashboardFile Load()
    {
        if (!File.Exists(Path)) return Empty;
        var value = JsonSerializer.Deserialize<LiveArenaDashboardFile>(File.ReadAllText(Path))
            ?? throw new InvalidDataException("Live Arena dashboard payload is empty.");
        value.Validate();
        return value;
    }

    public LiveArenaDashboardFile RecordBattle(LiveArenaBattleOutcome outcome) => this with { AllTime = AllTime.AddBattle(outcome) };
    public LiveArenaDashboardFile RecordRefill(int gemPrice) => this with { AllTime = AllTime.AddRefill(gemPrice) };
    public LiveArenaDashboardFile FinishRun(LiveArenaDashboardStats run) => this with { LastRun = run };

    public void Save()
    {
        Validate();
        Directory.CreateDirectory(AppPaths.Data);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, Path, true);
    }

    public void Validate()
    {
        if (Version != CurrentVersion || LastRun is null || AllTime is null)
            throw new InvalidDataException("Live Arena dashboard version is unsupported.");
        LastRun.Validate();
        AllTime.Validate();
    }
}
