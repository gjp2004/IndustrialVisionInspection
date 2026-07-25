using IndustrialVisionStudent.Models;
using IndustrialVisionStudent.Services;

namespace IndustrialVisionStudent.Tests;

public sealed class InspectionStatisticsTests
{
    [Fact]
    public void Calculate_UsesOnlyProvidedFilteredRecords()
    {
        InspectionRecord[] records =
        {
            Create("OK", "OK"),
            Create("NG", "AREA_NG"),
            Create("NG", "AREA_NG"),
            Create("NG", "SIZE_NG")
        };

        InspectionStatistics result = InspectionStatisticsService.Calculate(records);

        Assert.Equal(4, result.Total);
        Assert.Equal(1, result.Ok);
        Assert.Equal(3, result.Ng);
        Assert.Equal(25, result.OkRate);
        Assert.Equal(2, result.NgCodeCounts["AREA_NG"]);
        Assert.Equal(1, result.NgCodeCounts["SIZE_NG"]);
    }

    [Fact]
    public void Calculate_EmptySet_ReturnsZeroRateAndNoCodes()
    {
        InspectionStatistics result =
            InspectionStatisticsService.Calculate(Array.Empty<InspectionRecord>());

        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.OkRate);
        Assert.Empty(result.NgCodeCounts);
    }

    private static InspectionRecord Create(string result, string code) => new()
    {
        Result = result,
        JudgementCode = code
    };
}
