namespace IndustrialVisionStudent.Models;

public sealed record DiagnosticCheck(
    string Name,
    bool Passed,
    string Message);

public sealed record SystemDiagnosticReport(
    DateTimeOffset CheckedAt,
    IReadOnlyList<DiagnosticCheck> Checks)
{
    public bool Passed => Checks.Count > 0 && Checks.All(x => x.Passed);

    public string ToDisplayText() => string.Join(
        Environment.NewLine,
        Checks.Select(x => $"{(x.Passed ? "通过" : "失败")}｜{x.Name}：{x.Message}"));
}
