using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArenaDrafter;

public sealed record BugReportRequest(
    string Area,
    string Summary,
    string Expected,
    string Actual,
    string Steps,
    bool IncludeConfiguration,
    int RaidProcessId,
    IReadOnlyDictionary<string, string> RuntimeContext);

public sealed record BugReportResult(string ReportId, string ZipPath);

public static partial class DiagnosticReport
{
    private const int MaximumFiles = 40;
    private const int MaximumFileBytes = 6 * 1024 * 1024;
    private const int MaximumTotalBytes = 24 * 1024 * 1024;
    public const string GitHubIssuesUrl = "https://github.com/Nanaconda38/ArenaDrafter/issues/new";

    public static BugReportResult Create(BugReportRequest request, string? dataRoot = null, DateTime? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Area);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Summary);
        dataRoot ??= AppPaths.Data;
        var now = utcNow ?? DateTime.UtcNow;
        var reportId = $"AD-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..30];
        var reports = Path.Combine(dataRoot, "reports");
        Directory.CreateDirectory(reports);
        var destination = Path.Combine(reports, $"ArenaDrafter-BugReport-{reportId}.zip");
        var partial = destination + ".partial";
        if (File.Exists(partial)) File.Delete(partial);

        var included = new List<object>();
        var skipped = new List<string>();
        var totalBytes = 0;
        try
        {
            using (var archive = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                foreach (var file in EnumerateDiagnosticFiles(dataRoot, request.IncludeConfiguration, now).Take(MaximumFiles))
                {
                    try
                    {
                        var sanitized = Sanitize(ReadTextTail(file.FullName, MaximumFileBytes));
                        var bytes = Encoding.UTF8.GetBytes(sanitized);
                        if (totalBytes + bytes.Length > MaximumTotalBytes)
                        {
                            skipped.Add($"{file.Name}: report size limit reached");
                            continue;
                        }

                        var folder = file.DirectoryName?.Equals(Path.Combine(dataRoot, "logs"), StringComparison.OrdinalIgnoreCase) == true ? "logs" : "configuration";
                        WriteEntry(archive, $"{folder}/{file.Name}", bytes);
                        totalBytes += bytes.Length;
                        included.Add(new { path = $"{folder}/{file.Name}", bytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
                    }
                    catch (Exception exception)
                    {
                        skipped.Add($"{file.Name}: {exception.GetType().Name}");
                    }
                }

                var manifest = new
                {
                    schemaVersion = 1,
                    reportId,
                    createdUtc = now,
                    sessionId = Log.SessionId,
                    application = new
                    {
                        name = "ArenaDrafter",
                        version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                        processId = Environment.ProcessId,
                        probeSha256 = File.Exists(AppPaths.ProbeDll) ? HashFile(AppPaths.ProbeDll) : null
                    },
                    raid = new { processId = request.RaidProcessId, supportedBuild = BuildValidator.Version },
                    environment = new
                    {
                        os = RuntimeInformation.OSDescription,
                        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                        uiCulture = System.Globalization.CultureInfo.CurrentUICulture.Name,
                        culture = System.Globalization.CultureInfo.CurrentCulture.Name
                    },
                    bug = new
                    {
                        area = Sanitize(request.Area),
                        summary = Sanitize(request.Summary),
                        expected = Sanitize(request.Expected),
                        actual = Sanitize(request.Actual),
                        steps = Sanitize(request.Steps)
                    },
                    runtime = request.RuntimeContext.ToDictionary(item => item.Key, item => Sanitize(item.Value)),
                    lastError = Log.LastError is null ? null : Sanitize(Log.LastError),
                    configurationIncluded = request.IncludeConfiguration,
                    includedFiles = included,
                    skippedFiles = skipped
                };
                WriteEntry(archive, "report.json", JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }

            File.Move(partial, destination);
            Log.Info($"Diagnostic report {reportId} created at {destination}.");
            return new(reportId, destination);
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            throw;
        }
    }

    public static void OpenForSubmission(BugReportResult report, string summary)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{report.ZipPath}\"") { UseShellExecute = true });
        var title = Uri.EscapeDataString($"[Beta bug] {summary}");
        var description = Uri.EscapeDataString($"Diagnostic report ID: {report.ReportId}\n\nPlease add what happened, what you expected, and the reproduction steps.");
        var diagnostics = Uri.EscapeDataString($"Report ID: {report.ReportId}\n\nDrag the generated ZIP here after reviewing it.");
        Process.Start(new ProcessStartInfo($"{GitHubIssuesUrl}?template=beta-bug.yml&title={title}&description={description}&diagnostics={diagnostics}") { UseShellExecute = true });
    }

    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)] = "<USER_PROFILE>",
            [Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)] = "<LOCAL_APP_DATA>",
            [Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)] = "<APP_DATA>",
            [Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)] = "<TEMP>",
            [Environment.UserName] = "<USER>",
            [Environment.MachineName] = "<MACHINE>"
        };
        foreach (var replacement in replacements.Where(item => !string.IsNullOrWhiteSpace(item.Key)).OrderByDescending(item => item.Key.Length))
            text = text.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase);
        text = WindowsUserPathRegex().Replace(text, @"C:\Users\<USER>");
        text = SidRegex().Replace(text, "<WINDOWS_SID>");
        return SensitiveJsonRegex().Replace(text, match => $"{match.Groups[1].Value}\"<REDACTED>\"");
    }

    private static IEnumerable<FileInfo> EnumerateDiagnosticFiles(string dataRoot, bool includeConfiguration, DateTime now)
    {
        var logs = Path.Combine(dataRoot, "logs");
        IEnumerable<FileInfo> files = Directory.Exists(logs)
            ? new DirectoryInfo(logs).EnumerateFiles().Where(file => file.LastWriteTimeUtc >= now.AddHours(-48) && file.Extension is ".log" or ".jsonl" or ".json")
            : [];
        if (includeConfiguration)
        {
            files = files.Concat(new[] { "live-arena-strategy.json", "live-arena-opener.json" }
                .Select(name => new FileInfo(Path.Combine(dataRoot, name))).Where(file => file.Exists));
        }
        return files.OrderByDescending(file => file.LastWriteTimeUtc);
    }

    private static string ReadTextTail(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var truncated = stream.Length > maximumBytes;
        if (truncated) stream.Seek(-maximumBytes, SeekOrigin.End);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        if (truncated) _ = reader.ReadLine();
        var text = reader.ReadToEnd();
        return truncated ? $"[ArenaDrafter: beginning omitted; last {maximumBytes} bytes retained]{Environment.NewLine}{text}" : text;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] contents)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(contents);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    [GeneratedRegex("""C:\\Users\\[^\\\r\n\"]+""", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsUserPathRegex();

    [GeneratedRegex(@"S-1-5-21-(?:\d+-){2,}\d+", RegexOptions.IgnoreCase)]
    private static partial Regex SidRegex();

    [GeneratedRegex("""("(?:accountId|playerId|email|token|accessToken|refreshToken|authorization|password)"\s*:\s*)(?:"(?:\\.|[^"])*"|-?\d+|true|false|null)""", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveJsonRegex();
}

public static class CrashSession
{
    private static readonly object Sync = new();
    private static readonly string MarkerPath = Path.Combine(AppPaths.Data, "active-session.json");
    public static bool PreviousCrashDetected { get; private set; }

    public static void Start()
    {
        lock (Sync)
        {
            Directory.CreateDirectory(AppPaths.Data);
            if (File.Exists(MarkerPath))
            {
                Directory.CreateDirectory(Log.DirectoryPath);
                File.Move(MarkerPath, Path.Combine(Log.DirectoryPath, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"), true);
                PreviousCrashDetected = true;
            }
            WriteMarker("running", null, null);
        }
    }

    public static void RecordCrash(string source, Exception exception)
    {
        lock (Sync)
        {
            Log.Error($"Unhandled {source} exception.", exception);
            WriteMarker("crashed", source, exception.ToString());
        }
    }

    public static void MarkCleanExit()
    {
        lock (Sync)
        {
            if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
        }
    }

    private static void WriteMarker(string state, string? source, string? exception)
    {
        var payload = JsonSerializer.Serialize(new
        {
            utc = DateTime.UtcNow,
            state,
            source,
            exception = exception is null ? null : DiagnosticReport.Sanitize(exception),
            sessionId = Log.SessionId,
            processId = Environment.ProcessId,
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(MarkerPath, payload, new UTF8Encoding(false));
    }
}
