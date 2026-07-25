using System.Text;
using System.Text.Json;
using System.IO;
using IndustrialVisionStudent.Models;

namespace IndustrialVisionStudent.Services;

public sealed class AuditLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object sync = new();
    private readonly string directory;

    public AuditLogService(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
    }

    public bool Record(string action, string outcome, string details)
    {
        if (string.IsNullOrWhiteSpace(action)) return false;
        try
        {
            var entry = new AuditEvent(
                DateTimeOffset.Now,
                action.Trim(),
                string.IsNullOrWhiteSpace(outcome) ? "UNKNOWN" : outcome.Trim(),
                details?.Trim() ?? string.Empty);
            string line = JsonSerializer.Serialize(entry, JsonOptions);
            lock (sync)
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"audit-{DateTime.Now:yyyy-MM-dd}.jsonl");
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
            return true;
        }
        catch (Exception exception)
        {
            ApplicationLogService.Error("操作审计日志写入失败。", exception);
            return false;
        }
    }
}
