using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ArenaDrafter;

public static class AppPaths
{
    public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArenaDrafter");
    public static readonly string RaidRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlariumPlay", "StandAloneApps", "raid-shadow-legends");
    public static readonly string Build = Path.Combine(RaidRoot, "build");
    public static readonly string RaidExe = Path.Combine(Build, "Raid.exe");
    public static readonly string GameAssembly = Path.Combine(Build, "GameAssembly.dll");
    public static readonly string Metadata = Path.Combine(Build, "Raid_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
    public static string ProbeDll => Path.Combine(AppContext.BaseDirectory, "RslArenaProbe.dll");
}

public static class BuildValidator
{
    public const string Version = "11.71.0";
    private static readonly IReadOnlyDictionary<string, string> Expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [AppPaths.RaidExe] = "45F41A9199400AABA7B7C44B8862C2C6F7F3BC2BBCBC3CE23E26A70013A4AF8F",
        [AppPaths.GameAssembly] = "37294C7F2B7F70B0F949BE67A07B977ECBF489172EFD06B0807F83995A2B87D6",
        [AppPaths.Metadata] = "1711C7F5865713F3437ED578006E3FAD7480324076ABDF68A462B9EEAAE016CA"
    };

    public static void Validate(Process process)
    {
        Log.Info($"Validating RAID PID {process.Id}.");
        var actualPath = process.MainModule?.FileName ?? throw new InvalidDataException("RAID process path is unavailable.");
        if (!IsExpectedProcessPath(actualPath))
            throw new InvalidDataException("RAID is running from an unexpected path.");
        foreach (var item in Expected)
        {
            if (!File.Exists(item.Key)) throw new FileNotFoundException("A required RAID file is missing.", item.Key);
            ValidateHash(item.Key, item.Value);
            Log.Info($"Validated SHA-256 for {Path.GetFileName(item.Key)}.");
        }

        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(AppPaths.RaidExe));
            if (!certificate.Subject.Contains("Plarium", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RAID executable is not signed by Plarium.");
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("RAID executable has no readable Authenticode signature.", exception);
        }
        Log.Info("RAID build validation completed.");
    }

    public static bool IsExpectedProcessPath(string path) =>
        Path.GetFullPath(path).Equals(Path.GetFullPath(AppPaths.RaidExe), StringComparison.OrdinalIgnoreCase);

    public static void ValidateHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported build: {Path.GetFileName(path)} has an unexpected SHA-256 hash.");
    }
}

public static class GameLauncher
{
    public static bool IsPlariumPlayRunning() => Process.GetProcessesByName("PlariumPlay").Length > 0 || Process.GetProcessesByName("PlariumPlayClient").Length > 0;

    public static Process? FindRaid() => Process.GetProcessesByName("Raid").SingleOrDefault(process =>
    {
        try { return string.Equals(process.MainModule?.FileName, AppPaths.RaidExe, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    });

    public static void Launch()
    {
        var executable = ResolvePlariumPlay();
        Process.Start(new ProcessStartInfo(executable, "--args -gameid=101 -tray-start") { UseShellExecute = true });
    }

    private static string ResolvePlariumPlay()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"Software\Classes\plariumplay\shell\open\command");
            var command = key?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(command))
            {
                var candidate = command.Trim().TrimStart('"').Split('"')[0];
                if (File.Exists(candidate)) return candidate;
            }
        }

        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlariumPlay", "PlariumPlay.exe");
        return File.Exists(fallback) ? fallback : throw new FileNotFoundException("Plarium Play could not be resolved from the registry or Local AppData.");
    }
}

public static class NativeInjector
{
    private const uint ProcessCreateThread = 0x0002, ProcessQueryInformation = 0x0400, ProcessVmOperation = 0x0008, ProcessVmWrite = 0x0020, ProcessVmRead = 0x0010;
    private const uint CommitReserve = 0x3000, Release = 0x8000, ReadWrite = 0x04, Infinite = 0xFFFFFFFF;

    public static void Inject(Process process, string dllPath)
    {
        Log.Info($"Starting LoadLibraryW injection into RAID PID {process.Id} using {dllPath}.");
        if (!File.Exists(dllPath)) throw new FileNotFoundException("Native probe DLL is missing.", dllPath);
        var pathBytes = System.Text.Encoding.Unicode.GetBytes(Path.GetFullPath(dllPath) + '\0');
        using var handle = OpenProcess(ProcessCreateThread | ProcessQueryInformation | ProcessVmOperation | ProcessVmWrite | ProcessVmRead, false, process.Id);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the RAID process.");
        var remote = VirtualAllocEx(handle, IntPtr.Zero, (nuint)pathBytes.Length, CommitReserve, ReadWrite);
        if (remote == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not allocate the DLL path in RAID.");
        try
        {
            if (!WriteProcessMemory(handle, remote, pathBytes, (nuint)pathBytes.Length, out var written) || written != (nuint)pathBytes.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not write the DLL path to RAID.");
            var loadLibrary = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
            using var thread = CreateRemoteThread(handle, IntPtr.Zero, 0, loadLibrary, remote, 0, out _);
            if (thread.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start LoadLibraryW in RAID.");
            if (WaitForSingleObject(thread, Infinite) != 0 || !GetExitCodeThread(thread, out var loaded) || loaded == 0)
                throw new InvalidOperationException("RAID rejected the native probe DLL.");
            Log.Info($"LoadLibraryW completed with result 0x{loaded:X8}.");
        }
        finally { VirtualFreeEx(handle, remote, 0, Release); }
    }

    [DllImport("kernel32", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32", SetLastError = true)] private static extern IntPtr VirtualAllocEx(SafeProcessHandle process, IntPtr address, nuint size, uint allocationType, uint protect);
    [DllImport("kernel32", SetLastError = true)] private static extern bool VirtualFreeEx(SafeProcessHandle process, IntPtr address, nuint size, uint freeType);
    [DllImport("kernel32", SetLastError = true)] private static extern bool WriteProcessMemory(SafeProcessHandle process, IntPtr address, byte[] buffer, nuint size, out nuint written);
    [DllImport("kernel32", SetLastError = true)] private static extern SafeWaitHandle CreateRemoteThread(SafeProcessHandle process, IntPtr attributes, nuint stackSize, IntPtr startAddress, IntPtr parameter, uint flags, out uint threadId);
    [DllImport("kernel32", SetLastError = true)] private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
    [DllImport("kernel32", SetLastError = true)] private static extern bool GetExitCodeThread(SafeWaitHandle thread, out uint exitCode);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}

public sealed class SafeProcessHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeProcessHandle() : base(true) { }
    protected override bool ReleaseHandle() => CloseHandle(handle);
    [DllImport("kernel32", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}

public sealed class SafeWaitHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeWaitHandle() : base(true) { }
    protected override bool ReleaseHandle() => CloseHandle(handle);
    [DllImport("kernel32", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}
