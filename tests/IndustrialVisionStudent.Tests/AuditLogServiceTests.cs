using System.Text.Json;
using IndustrialVisionStudent.Services;

namespace IndustrialVisionStudent.Tests;

public sealed class AuditLogServiceTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "IVS_Audit_" + Guid.NewGuid().ToString("N"));

    public AuditLogServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Record_WritesValidJsonLine()
    {
        string auditDirectory = Path.Combine(directory, "Audit");
        var service = new AuditLogService(auditDirectory);

        Assert.True(service.Record("INSPECTION", "OK", "batch=B1"));

        string file = Assert.Single(Directory.EnumerateFiles(auditDirectory, "*.jsonl"));
        string line = Assert.Single(File.ReadAllLines(file));
        using JsonDocument document = JsonDocument.Parse(line);
        Assert.Equal("INSPECTION", document.RootElement.GetProperty("action").GetString());
        Assert.Equal("OK", document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("batch=B1", document.RootElement.GetProperty("details").GetString());
        Assert.True(document.RootElement.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public void Record_ConcurrentCalls_ProduceOneValidLinePerEvent()
    {
        string auditDirectory = Path.Combine(directory, "Concurrent");
        var service = new AuditLogService(auditDirectory);

        Parallel.For(0, 100, index =>
            Assert.True(service.Record("TEST", "SUCCESS", $"index={index}")));

        string file = Assert.Single(Directory.EnumerateFiles(auditDirectory, "*.jsonl"));
        string[] lines = File.ReadAllLines(file);
        Assert.Equal(100, lines.Length);
        foreach (string line in lines)
        {
            using JsonDocument document = JsonDocument.Parse(line);
            Assert.Equal("TEST", document.RootElement.GetProperty("action").GetString());
        }
    }

    [Fact]
    public void Record_WhenDirectoryCannotBeCreated_ReturnsFalseWithoutThrowing()
    {
        string file = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(file, "blocked");
        var service = new AuditLogService(Path.Combine(file, "Audit"));

        bool result = service.Record("TEST", "FAILED", "expected");

        Assert.False(result);
    }

    [Fact]
    public void Record_WithBlankAction_IsRejected()
    {
        var service = new AuditLogService(Path.Combine(directory, "Blank"));
        Assert.False(service.Record(" ", "OK", "ignored"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
