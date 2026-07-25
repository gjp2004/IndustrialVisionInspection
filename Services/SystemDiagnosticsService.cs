using System.IO;
using IndustrialVisionStudent.Models;
using OpenCvSharp;

namespace IndustrialVisionStudent.Services;

public sealed class SystemDiagnosticsService
{
    private readonly string dataRoot;
    private readonly string defaultRecipePath;
    private readonly InspectionHistoryService historyService;
    private readonly VisionRecipeService recipeService = new();

    public SystemDiagnosticsService(
        string dataRoot,
        string defaultRecipePath,
        InspectionHistoryService historyService)
    {
        this.dataRoot = Path.GetFullPath(dataRoot);
        this.defaultRecipePath = Path.GetFullPath(defaultRecipePath);
        this.historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
    }

    public SystemDiagnosticReport Run()
    {
        var checks = new List<DiagnosticCheck>
        {
            Execute("数据目录", CheckDataDirectory),
            Execute("SQLite数据库", CheckDatabase),
            Execute("OpenCV运行库", CheckOpenCv),
            Execute("默认视觉配方", CheckDefaultRecipe)
        };
        return new SystemDiagnosticReport(DateTimeOffset.Now, checks);
    }

    private string CheckDataDirectory()
    {
        Directory.CreateDirectory(dataRoot);
        string path = Path.Combine(dataRoot, $".write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(path, "IndustrialVisionStudent");
            if (new FileInfo(path).Length == 0)
                throw new IOException("测试文件写入后为空。");
            return $"可写：{dataRoot}";
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private string CheckDatabase()
    {
        InspectionSummary summary = historyService.GetSummary();
        return $"连接正常，已有{summary.Total}条检测记录。";
    }

    private static string CheckOpenCv()
    {
        using var source = new Mat(new Size(32, 32), MatType.CV_8UC3, Scalar.White);
        using var gray = new Mat();
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        if (gray.Empty() || gray.Channels() != 1)
            throw new InvalidOperationException("OpenCV灰度转换结果无效。");
        return $"OpenCvSharp {Cv2.GetVersionString()}加载正常。";
    }

    private string CheckDefaultRecipe()
    {
        VisionParameters parameters = recipeService.Load(defaultRecipePath);
        return $"“{parameters.RecipeName}” v{parameters.RecipeVersion}有效。";
    }

    private static DiagnosticCheck Execute(string name, Func<string> action)
    {
        try
        {
            return new DiagnosticCheck(name, true, action());
        }
        catch (Exception exception)
        {
            return new DiagnosticCheck(name, false, exception.Message);
        }
    }
}
