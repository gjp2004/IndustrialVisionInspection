using IndustrialVisionStudent.Models;
using IndustrialVisionStudent.Services;

namespace IndustrialVisionStudent.Tests;

public sealed class SystemDiagnosticsTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "IVS_Diagnostics_" + Guid.NewGuid().ToString("N"));

    public SystemDiagnosticsTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Run_WithHealthyDependencies_PassesEveryCheck()
    {
        string databasePath = Path.Combine(directory, "Data", "inspection.db");
        string recipePath = Path.Combine(directory, "recipes", "default.json");
        var history = new InspectionHistoryService(databasePath);
        history.Initialize();
        new VisionRecipeService().Save(
            recipePath,
            new VisionParameters(110, 300, 0.65, 1, 1000, 100000)
            {
                RecipeName = "诊断配方",
                RecipeVersion = "1.0"
            });
        var service = new SystemDiagnosticsService(directory, recipePath, history);

        SystemDiagnosticReport report = service.Run();

        Assert.True(report.Passed, report.ToDisplayText());
        Assert.Equal(4, report.Checks.Count);
        Assert.All(report.Checks, check => Assert.True(check.Passed, check.Message));
        Assert.Empty(Directory.EnumerateFiles(directory, ".write-test-*.tmp"));
    }

    [Fact]
    public void Run_WhenRecipeMissing_ReturnsReadableFailureWithoutThrowing()
    {
        var history = new InspectionHistoryService(
            Path.Combine(directory, "Data", "inspection.db"));
        history.Initialize();
        var service = new SystemDiagnosticsService(
            directory, Path.Combine(directory, "missing.json"), history);

        SystemDiagnosticReport report = service.Run();

        Assert.False(report.Passed);
        DiagnosticCheck recipe = Assert.Single(
            report.Checks, x => x.Name == "默认视觉配方");
        Assert.False(recipe.Passed);
        Assert.Contains("missing.json", recipe.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, report.Checks.Count(x => x.Passed));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
