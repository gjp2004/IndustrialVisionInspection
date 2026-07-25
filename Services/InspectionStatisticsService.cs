using IndustrialVisionStudent.Models;

namespace IndustrialVisionStudent.Services;

public static class InspectionStatisticsService
{
    public static InspectionStatistics Calculate(IEnumerable<InspectionRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        InspectionRecord[] snapshot = records.ToArray();
        int ok = snapshot.Count(x => x.Result == "OK");
        int ng = snapshot.Count(x => x.Result == "NG");
        IReadOnlyDictionary<string, int> codes = snapshot
            .Where(x => x.Result == "NG")
            .GroupBy(x => string.IsNullOrWhiteSpace(x.JudgementCode)
                ? "UNKNOWN"
                : x.JudgementCode)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        return new InspectionStatistics(snapshot.Length, ok, ng, codes);
    }
}
