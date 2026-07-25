using OpenCvSharp;
using System.IO;

namespace IndustrialVisionStudent.Services;

public sealed class NgImageStorageService
{
    private readonly string rootDirectory;

    public NgImageStorageService(string rootDirectory)
    {
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(this.rootDirectory);
    }

    public string Save(Mat image, DateTimeOffset inspectedAt)
    {
        string directory = Path.Combine(rootDirectory, inspectedAt.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory,
            $"NG_{inspectedAt:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.png");
        if (!Cv2.ImWrite(path, image)) throw new IOException("NG证据图保存失败。");
        return path;
    }

    public NgImageCleanupResult CleanupOrphans(
        IEnumerable<string> referencedPaths,
        DateTimeOffset olderThan)
    {
        ArgumentNullException.ThrowIfNull(referencedPaths);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in referencedPaths.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            try { referenced.Add(Path.GetFullPath(path)); }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or IOException)
            {
                ApplicationLogService.Error($"忽略无效的NG图片引用路径：{path}", exception);
            }
        }
        int deletedFiles = 0;
        long deletedBytes = 0;
        int deletedDirectories = 0;
        string rootPrefix = rootDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        foreach (string directory in Directory.EnumerateDirectories(
                     rootDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var directoryInfo = new DirectoryInfo(directory);
            if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;

            foreach (string candidate in Directory.EnumerateFiles(
                         directory, "*.png", SearchOption.TopDirectoryOnly))
            {
                string fullPath = Path.GetFullPath(candidate);
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (referenced.Contains(fullPath)) continue;
                var file = new FileInfo(fullPath);
                if (file.LastWriteTimeUtc >= olderThan.UtcDateTime) continue;

                long length = file.Length;
                file.Delete();
                deletedFiles++;
                deletedBytes += length;
            }

            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
                deletedDirectories++;
            }
        }

        return new NgImageCleanupResult(deletedFiles, deletedBytes, deletedDirectories);
    }
}

public sealed record NgImageCleanupResult(
    int DeletedFiles,
    long DeletedBytes,
    int DeletedDirectories);
