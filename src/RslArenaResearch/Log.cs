using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RslArenaResearch;

public static class Log
{
    private static readonly object Sync = new();
    public static readonly string DirectoryPath = Path.Combine(AppPaths.Data, "logs");
    public static readonly string FilePath = Path.Combine(DirectoryPath, $"app-{DateTime.UtcNow:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", exception is null ? message : $"{message} | {exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(FilePath, $"{DateTime.UtcNow:O} [{level}] [PID {Environment.ProcessId}] [TID {Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void OpenDirectory()
    {
        Directory.CreateDirectory(DirectoryPath);
        Process.Start(new ProcessStartInfo("explorer.exe", DirectoryPath) { UseShellExecute = true });
    }
}

public sealed class BattleDiagnosticRecorder : IDisposable
{
    private readonly object sync = new();
    private readonly string directoryPath;
    private bool recording;

    public BattleDiagnosticRecorder(string? directoryPath = null) =>
        this.directoryPath = directoryPath ?? Log.DirectoryPath;

    public string? FilePath { get; private set; }
    public bool ManualReferenceActive { get; private set; }
    public bool ManualReferenceSawBattle { get; private set; }

    public string Start(int raidProcessId)
    {
        lock (sync)
        {
            StopCore("restarted");
            Directory.CreateDirectory(directoryPath);
            FilePath = Path.Combine(directoryPath, $"battle-debug-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-raid-{raidProcessId}.jsonl");
            File.WriteAllText(FilePath, string.Empty, new UTF8Encoding(false));
            recording = true;
            WriteCore(new { utc = DateTime.UtcNow, eventType = "session-start", raidProcessId });
            return FilePath;
        }
    }

    public void BeginManualReference()
    {
        lock (sync)
        {
            ManualReferenceActive = true;
            ManualReferenceSawBattle = false;
            WriteCore(new { utc = DateTime.UtcNow, eventType = "manual-reference-start" });
        }
    }

    public void EndManualReference(string reason)
    {
        lock (sync)
        {
            if (!ManualReferenceActive) return;
            WriteCore(new { utc = DateTime.UtcNow, eventType = "manual-reference-stop", reason });
            ManualReferenceActive = false;
        }
    }

    public void RecordSnapshot(BattleSnapshotMessage snapshot, long? actionId)
    {
        lock (sync)
        {
            if (!recording) return;
            ManualReferenceSawBattle |= ManualReferenceActive && snapshot.Active;
            var active = snapshot.Heroes.FirstOrDefault(hero => hero.Id == snapshot.ActiveHeroId);
            WriteCore(new
            {
                utc = DateTime.UtcNow,
                eventType = "snapshot",
                actionId,
                snapshot.Revision,
                snapshot.Active,
                snapshot.Kind,
                snapshot.StageId,
                snapshot.Round,
                snapshot.Turn,
                snapshot.ActiveHeroId,
                snapshot.Finished,
                snapshot.AutoMode,
                snapshot.HudVisible,
                snapshot.ModeChangeAvailable,
                snapshot.SkillSelectionAvailable,
                snapshot.HudSkillCount,
                snapshot.HudSkills,
                activeHero = active,
                heroes = snapshot.Heroes.Select(hero => new
                {
                    hero.Id,
                    hero.TypeId,
                    hero.BaseId,
                    hero.Name,
                    hero.Team,
                    hero.Slot,
                    hero.Health,
                    hero.MaxHealth,
                    hero.Dead,
                    hero.Effects
                })
            });
        }
    }

    public void RecordAction(long actionId, string phase, BattleOpenerDecision decision, BattleSnapshotMessage snapshot, string method, string expected)
    {
        lock (sync)
        {
            WriteCore(new
            {
                utc = DateTime.UtcNow,
                eventType = "action",
                actionId,
                phase,
                decision.Action,
                decision.SkillTypeId,
                decision.SkillSlot,
                decision.TargetId,
                decision.BaseId,
                decision.Explanation,
                method,
                expected,
                beforeRevision = snapshot.Revision,
                beforeTurn = snapshot.Turn,
                beforeActiveHeroId = snapshot.ActiveHeroId,
                beforeAutoMode = snapshot.AutoMode,
                beforeHudSkills = snapshot.HudSkills
            });
        }
    }

    public void RecordAutomation(AutomationMessage message, long? actionId)
    {
        lock (sync)
            WriteCore(new { utc = DateTime.UtcNow, eventType = "probe-automation", actionId, message.State, message.Message });
    }

    public void RecordMarker(string marker, string message, long? actionId = null)
    {
        lock (sync)
            WriteCore(new { utc = DateTime.UtcNow, eventType = "marker", actionId, marker, message });
    }

    public void Dispose()
    {
        lock (sync) StopCore("session-ended");
    }

    private void StopCore(string reason)
    {
        if (!recording) return;
        WriteCore(new { utc = DateTime.UtcNow, eventType = "session-stop", reason });
        recording = false;
        ManualReferenceActive = false;
        ManualReferenceSawBattle = false;
    }

    private void WriteCore(object value)
    {
        if (!recording || FilePath is null) return;
        try { File.AppendAllText(FilePath, JsonSerializer.Serialize(value) + Environment.NewLine, new UTF8Encoding(false)); }
        catch (Exception exception) { Log.Error("Battle diagnostic write failed.", exception); }
    }
}

public class ContextDiagnosticRecorder : IDisposable
{
    private readonly object sync = new();
    private readonly string directoryPath;
    private readonly string filePrefix;
    private readonly string payloadType;
    private readonly string displayName;
    private bool recording;

    protected ContextDiagnosticRecorder(string filePrefix, string payloadType, string displayName, string? directoryPath = null)
    {
        this.directoryPath = directoryPath ?? Log.DirectoryPath;
        this.filePrefix = filePrefix;
        this.payloadType = payloadType;
        this.displayName = displayName;
    }

    public string? FilePath { get; private set; }
    public bool IsRecording => recording;

    public string Start(int raidProcessId)
    {
        lock (sync)
        {
            StopCore("restarted");
            Directory.CreateDirectory(directoryPath);
            FilePath = Path.Combine(directoryPath, $"{filePrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-raid-{raidProcessId}.jsonl");
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, eventType = "session-start", raidProcessId }) + Environment.NewLine, new UTF8Encoding(false));
            recording = true;
            return FilePath;
        }
    }

    public void Record(string payload)
    {
        lock (sync)
        {
            if (!recording || FilePath is null) return;
            if (payload.Length > 512 * 1024) throw new InvalidDataException($"{displayName} diagnostic payload exceeds the size limit.");
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.GetProperty("type").GetString() != payloadType) throw new InvalidDataException($"{displayName} diagnostic payload type is invalid.");
            File.AppendAllText(FilePath, payload + Environment.NewLine, new UTF8Encoding(false));
        }
    }

    public void Stop(string reason)
    {
        lock (sync) StopCore(reason);
    }

    public void Dispose()
    {
        lock (sync) StopCore("session-ended");
    }

    private void StopCore(string reason)
    {
        if (!recording || FilePath is null) return;
        File.AppendAllText(FilePath, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, eventType = "session-stop", reason }) + Environment.NewLine, new UTF8Encoding(false));
        recording = false;
    }
}

public sealed class RewardDiagnosticRecorder(string? directoryPath = null)
    : ContextDiagnosticRecorder("reward-debug", "rewardTrace", "Reward", directoryPath);

public sealed class MythicalClickTraceRecorder(string? directoryPath = null)
    : ContextDiagnosticRecorder("mythical-click-path", "mythicalClickTrace", "Mythical click-path", directoryPath);
