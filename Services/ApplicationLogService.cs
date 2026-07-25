using System.Globalization;
using System.IO;

namespace IndustrialVisionStudent.Services;

public static class ApplicationLogService
{
    private static readonly object Sync = new();

    public static string LogDirectory { get; } = ResolveLogDirectory();

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                string path = Path.Combine(LogDirectory,
                    $"application-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path,
                    $"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} " +
                    $"[{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志写入绝不能引发第二次异常或导致应用退出。
        }
    }

    private static string ResolveLogDirectory()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
            return Path.Combine(local, "IndustrialVisionStudent", "Logs");
        return Path.Combine(Path.GetTempPath(), "IndustrialVisionStudent", "Logs");
    }
}
