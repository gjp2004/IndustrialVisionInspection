namespace IndustrialVisionStudent.Models;

public sealed record InspectionStatistics(
    int Total,
    int Ok,
    int Ng,
    IReadOnlyDictionary<string, int> NgCodeCounts)
{
    public double OkRate => Total == 0 ? 0 : Ok * 100.0 / Total;
}
