using System.Text.Json;
using System.IO;
using IndustrialVisionStudent.Models;

namespace IndustrialVisionStudent.Services;

public sealed class VisionRecipeService
{
    private const int MaximumBackupsPerRecipe = 20;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RecipeSaveResult Save(string path, VisionParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        parameters.Validate();
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(parameters, Options);
        string? backupPath = null;
        bool changed = true;
        if (File.Exists(fullPath))
        {
            string current = File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
            changed = !string.Equals(current, json, StringComparison.Ordinal);
            if (!changed) return new RecipeSaveResult(fullPath, false, null);
            backupPath = CreateBackup(fullPath);
        }

        string temporaryPath = fullPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json, new System.Text.UTF8Encoding(false));
            File.Move(temporaryPath, fullPath, true);
            TrimBackups(fullPath);
            return new RecipeSaveResult(fullPath, true, backupPath);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }
    }

    public VisionParameters Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        VisionParameters? parameters = JsonSerializer.Deserialize<VisionParameters>(
            File.ReadAllText(path), Options);
        if (parameters is null) throw new InvalidDataException("配方文件内容为空或格式无效。");
        parameters.Validate();
        return parameters;
    }

    private static string CreateBackup(string fullPath)
    {
        string historyDirectory = Path.Combine(
            Path.GetDirectoryName(fullPath)!, ".history");
        Directory.CreateDirectory(historyDirectory);
        string baseName = Path.GetFileNameWithoutExtension(fullPath);
        string backupPath = Path.Combine(
            historyDirectory,
            $"{baseName}.{DateTime.Now:yyyyMMdd-HHmmss-fff}.{Guid.NewGuid():N}.json");
        File.Copy(fullPath, backupPath, false);
        return backupPath;
    }

    private static void TrimBackups(string fullPath)
    {
        string historyDirectory = Path.Combine(
            Path.GetDirectoryName(fullPath)!, ".history");
        if (!Directory.Exists(historyDirectory)) return;
        string baseName = Path.GetFileNameWithoutExtension(fullPath);
        FileInfo[] backups = new DirectoryInfo(historyDirectory)
            .EnumerateFiles($"{baseName}.*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .ThenByDescending(x => x.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (FileInfo backup in backups.Skip(MaximumBackupsPerRecipe))
            backup.Delete();
    }
}

public sealed record RecipeSaveResult(
    string Path,
    bool Changed,
    string? BackupPath);
