using ArenaDrafter;
using System.IO.Compression;

namespace ArenaDrafter.Tests;

[TestClass]
public sealed class DiagnosticReportTests
{
    [TestMethod]
    public void SanitizerRemovesMachinePathsSidsAndSensitiveJsonValues()
    {
        var input = $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\secret S-1-5-21-123-456-789-1001 " +
                    "{\"accountId\":12345,\"token\":\"secret\",\"heroId\":42}";

        var result = DiagnosticReport.Sanitize(input);

        StringAssert.Contains(result, "<USER_PROFILE>");
        StringAssert.Contains(result, "<WINDOWS_SID>");
        Assert.IsFalse(result.Contains("12345", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("\"secret\"", StringComparison.Ordinal));
        StringAssert.Contains(result, "\"heroId\":42");
    }

    [TestMethod]
    public void ReportContainsRecentSanitizedLogsAndOptionalConfigurationOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"arena-report-{Guid.NewGuid():N}");
        try
        {
            var logs = Directory.CreateDirectory(Path.Combine(root, "logs"));
            File.WriteAllText(Path.Combine(logs.FullName, "app.log"), $"failure in {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}");
            File.WriteAllText(Path.Combine(root, "live-arena-strategy.json"), "{\"name\":\"test\"}");
            var request = new BugReportRequest("Draft", "Pick failed", "pick", "nothing", "start", true, 1234,
                new Dictionary<string, string> { ["state"] = "draft" });

            var result = DiagnosticReport.Create(request, root, DateTime.UtcNow);

            using var archive = ZipFile.OpenRead(result.ZipPath);
            Assert.IsNotNull(archive.GetEntry("report.json"));
            Assert.IsNotNull(archive.GetEntry("logs/app.log"));
            Assert.IsNotNull(archive.GetEntry("configuration/live-arena-strategy.json"));
            using var reader = new StreamReader(archive.GetEntry("logs/app.log")!.Open());
            StringAssert.Contains(reader.ReadToEnd(), "<USER_PROFILE>");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
