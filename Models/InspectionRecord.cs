namespace IndustrialVisionStudent.Models;

public sealed class InspectionRecord
{
    public long Id { get; init; }
    public DateTimeOffset InspectedAt { get; init; }
    public string BatchNumber { get; init; } = "DEFAULT";
    public string ProductModel { get; init; } = "DEFAULT";
    public string OperatorName { get; init; } = "未填写";
    public string RecipeName { get; init; } = "默认配方";
    public string RecipeVersion { get; init; } = "1.0";
    public string? PlcCycleId { get; init; }
    public string Result { get; init; } = "NG";
    public string JudgementCode { get; init; } = "NO_RESULT";
    public string JudgementReason { get; init; } = string.Empty;
    public int TargetCount { get; init; }
    public double MaximumArea { get; init; }
    public double Circularity { get; init; }
    public int CenterX { get; init; }
    public int CenterY { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double AspectRatio { get; init; }
    public double CenterOffset { get; init; }
    public double PixelSizeMm { get; init; }
    public double AreaMm2 { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
    public double ProcessingTimeMs { get; init; }
    public string? NgImagePath { get; init; }
}

public sealed record InspectionSummary(int Total, int Ok, int Ng)
{
    public double OkRate => Total == 0 ? 0 : Ok * 100.0 / Total;
}
