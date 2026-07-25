namespace IndustrialVisionStudent.Models;

public sealed class InspectionResult
{
    public bool IsOk { get; init; }
    public string JudgementCode { get; init; } = "NO_RESULT";
    public string JudgementReason { get; init; } = "尚未执行检测";
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
}
