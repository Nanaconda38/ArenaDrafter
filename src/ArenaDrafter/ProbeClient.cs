using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ArenaDrafter;

public sealed class ProbeClient : IAsyncDisposable
{
    private readonly NamedPipeServerStream pipe;
    private StreamReader? reader;
    private StreamWriter? writer;
    private CancellationTokenSource? reading;

    public event Action<SnapshotMessage>? SnapshotReceived;
    public event Action<CatalogMessage>? CatalogReceived;
    public event Action<BattleSnapshotMessage>? BattleReceived;
    public event Action<LiveArenaSnapshotMessage>? LiveArenaReceived;
    public event Action<AutomationMessage>? AutomationReceived;
    public event Action<string>? RewardTraceReceived;
    public event Action<string>? MythicalClickTraceReceived;
    public event Action<string>? ErrorReceived;

    public ProbeClient(int processId)
    {
        Log.Info($"Creating named pipe server for RAID PID {processId}.");
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm($"D:P(A;;GA;;;{user.Value})", AccessControlSections.Access);
        pipe = NamedPipeServerStreamAcl.Create($"ArenaDrafter-{processId}", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
        SetMediumIntegrity(pipe);
        Log.Info($"Named pipe ACL limited to SID {user.Value} with medium integrity access.");
    }

    private static void SetMediumIntegrity(NamedPipeServerStream server)
    {
        var sid = new SecurityIdentifier("S-1-16-8192");
        var sidBytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(sidBytes, 0);
        var aceSize = 8 + sidBytes.Length;
        var acl = new byte[8 + aceSize];
        acl[0] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(2), (ushort)acl.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(4), 1);
        acl[8] = 0x11;
        BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(10), (ushort)aceSize);
        BinaryPrimitives.WriteUInt32LittleEndian(acl.AsSpan(12), 1);
        sidBytes.CopyTo(acl, 16);
        var pinned = GCHandle.Alloc(acl, GCHandleType.Pinned);
        try
        {
            var error = SetSecurityInfo(server.SafePipeHandle.DangerousGetHandle(), 6, 0x10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, pinned.AddrOfPinnedObject());
            if (error != 0) throw new Win32Exception((int)error, "Could not set medium integrity on the named pipe.");
        }
        finally { pinned.Free(); }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint SetSecurityInfo(IntPtr handle, int objectType, uint securityInformation, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        Log.Info("Waiting for the native probe to connect to the named pipe.");
        await pipe.WaitForConnectionAsync(cancellationToken);
        Log.Info("Native probe connected to the named pipe.");
        reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        await writer.WriteLineAsync("INIT 1");
        reading = new CancellationTokenSource();
        _ = ReadLoopAsync(reading.Token);
    }

    public Task WatchAsync() => SendAsync("WATCH");
    public Task PickLiveArenaAsync(IEnumerable<int> instanceIds) => SendAsync(LiveArenaCommands.Pick(instanceIds));
    public Task BanLiveArenaAsync(int slot) => SendAsync(LiveArenaCommands.Ban(slot));
    public Task SelectLiveArenaLeaderAsync(int slot) => SendAsync(LiveArenaCommands.Leader(slot));
    public Task QueueLiveArenaAsync() => SendAsync(LiveArenaCommands.Queue);
    public Task RefillLiveArenaAsync(int gemPrice) => SendAsync(LiveArenaCommands.Refill(gemPrice));
    public Task ReturnToLiveArenaAsync() => SendAsync(LiveArenaCommands.Return);
    public Task ClaimLiveArenaRewardAsync(int claimableCount) => SendAsync(LiveArenaCommands.ClaimReward(claimableCount));
    public Task CloseLiveArenaRewardOverlayAsync() => SendAsync(LiveArenaCommands.CloseRewardOverlay);
    public Task EnableBattleAutoAsync() => SendAsync(BattleCommands.Auto);
    public Task EnableBattleManualAsync() => SendAsync(BattleCommands.Manual);
    public Task UseBattleSkillAsync(int skillTypeId, int skillSlot, int targetId) => SendAsync(BattleCommands.Skill(skillTypeId, skillSlot, targetId));
    public Task DiagnosticClickBattleSkillAsync(int skillTypeId, int skillSlot, int targetId) => SendAsync(BattleCommands.DiagnosticClick(skillTypeId, skillSlot, targetId));
    public Task StartBattleDiagnosticsAsync() => SendAsync(BattleCommands.StartDiagnostics);
    public Task StopBattleDiagnosticsAsync() => SendAsync(BattleCommands.StopDiagnostics);
    public Task StartMythicalClickTraceAsync() => SendAsync(BattleCommands.StartMythicalClickTrace);
    public Task StopMythicalClickTraceAsync() => SendAsync(BattleCommands.StopMythicalClickTrace);
    public Task StartRewardDiagnosticsAsync() => SendAsync(RewardDiagnosticCommands.Start);
    public Task StopRewardDiagnosticsAsync() => SendAsync(RewardDiagnosticCommands.Stop);
    public Task StopAsync() => SendAsync("STOP");

    private async Task SendAsync(string command)
    {
        if (writer is null) throw new InvalidOperationException("The probe is not connected.");
        Log.Info($"Sending probe command: {command}.");
        await writer.WriteLineAsync(command);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        long lastRevision = -1;
        long lastBattleRevision = -1;
        try
        {
            while (!cancellationToken.IsCancellationRequested && await reader!.ReadLineAsync(cancellationToken) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = root.GetProperty("type").GetString();
                Log.Info($"Received probe message: {type ?? "missing type"}.");
                if (root.GetProperty("protocol").GetInt32() != 1) throw new InvalidDataException("Unsupported probe protocol.");
                switch (type)
                {
                    case "hello": break;
                    case "snapshot":
                        var snapshot = SnapshotParser.Parse(line, lastRevision);
                        lastRevision = snapshot.Revision;
                        Dispatch(SnapshotReceived, snapshot, "snapshot");
                        break;
                    case "catalog": Dispatch(CatalogReceived, CatalogParser.Parse(line), "catalog"); break;
                    case "battle":
                        var battle = BattleSnapshotParser.Parse(line, lastBattleRevision);
                        lastBattleRevision = battle.Revision;
                        Dispatch(BattleReceived, battle, "battle");
                        break;
                    case "liveArena":
                        Dispatch(LiveArenaReceived, LiveArenaSnapshotParser.Parse(line), "Live Arena");
                        break;
                    case "automation":
                        var automationState = root.GetProperty("state").GetString() ?? throw new InvalidDataException("Automation state is missing.");
                        var automationMessage = root.GetProperty("message").GetString() ?? throw new InvalidDataException("Automation message is missing.");
                        Dispatch(AutomationReceived, new AutomationMessage(automationState, automationMessage), "automation");
                        break;
                    case "rewardTrace":
                        if (line.Length > 512 * 1024) throw new InvalidDataException("Reward diagnostic message exceeds the size limit.");
                        if (root.GetProperty("contexts").ValueKind != JsonValueKind.Array) throw new InvalidDataException("Reward diagnostic contexts are invalid.");
                        Dispatch(RewardTraceReceived, line, "reward diagnostic");
                        break;
                    case "mythicalClickTrace":
                        if (line.Length > 128 * 1024) throw new InvalidDataException("Mythical click-path diagnostic message exceeds the size limit.");
                        if (root.GetProperty("sample").ValueKind != JsonValueKind.Object) throw new InvalidDataException("Mythical click-path diagnostic sample is invalid.");
                        Dispatch(MythicalClickTraceReceived, line, "Mythical click-path diagnostic");
                        break;
                    case "error": ReportError(root.GetProperty("message").GetString() ?? "Native probe error."); break;
                    default: throw new InvalidDataException("Unknown probe message type.");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Log.Error("Probe read loop stopped.", exception); ReportError(exception.Message); }
    }

    private void Dispatch<T>(Action<T>? handler, T message, string messageType)
    {
        if (handler is null) return;
        try { handler(message); }
        catch (Exception exception)
        {
            Log.Error($"Probe {messageType} handler failed; the connection remains active.", exception);
            ReportError($"The {messageType} update could not be applied: {exception.Message}");
        }
    }

    private void ReportError(string message)
    {
        try { ErrorReceived?.Invoke(message); }
        catch (Exception exception) { Log.Error("Probe error handler failed.", exception); }
    }

    public async ValueTask DisposeAsync()
    {
        Log.Info("Disposing probe connection.");
        reading?.Cancel();
        if (pipe.IsConnected && writer is not null)
        {
            try { await StopAsync(); } catch { }
        }
        pipe.Dispose();
        reading?.Dispose();
        Log.Info("Probe connection disposed.");
    }
}
