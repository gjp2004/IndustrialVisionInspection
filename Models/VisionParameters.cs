namespace IndustrialVisionStudent.Models;

public sealed record VisionParameters(
    int Threshold,
    double MinimumContourArea,
    double MinimumCircularity,
    int ExpectedCount,
    double MinimumOkArea,
    double MaximumOkArea,
    bool IsRoiEnabled = false,
    int RoiX = 0,
    int RoiY = 0,
    int RoiWidth = 0,
    int RoiHeight = 0,
    int MinimumWidth = 0,
    int MaximumWidth = 100000,
    int MinimumHeight = 0,
    int MaximumHeight = 100000,
    double MinimumAspectRatio = 0,
    double MaximumAspectRatio = 100000,
    int ExpectedCenterX = -1,
    int ExpectedCenterY = -1,
    int CenterTolerance = 0,
    bool IsDarkObject = true,
    bool UseAdaptiveThreshold = false,
    int AdaptiveBlockSize = 31,
    double AdaptiveConstant = 5,
    double PixelSizeMm = 0,
    string RecipeName = "默认配方",
    string RecipeVersion = "1.0")
{
    public void Validate()
    {
        if (Threshold is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(Threshold), "阈值必须在0～255之间。");
        if (MinimumContourArea < 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumContourArea), "最小轮廓面积不能为负数。");
        if (MinimumCircularity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumCircularity), "圆度必须在0～1之间。");
        if (ExpectedCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(ExpectedCount), "期望目标数量必须大于0。");
        if (MinimumOkArea <= 0 || MaximumOkArea < MinimumOkArea)
            throw new ArgumentOutOfRangeException(nameof(MaximumOkArea), "合格面积范围无效。");
        if (IsRoiEnabled && (RoiX < 0 || RoiY < 0 || RoiWidth <= 0 || RoiHeight <= 0))
            throw new ArgumentOutOfRangeException(nameof(RoiWidth), "启用ROI后，坐标不能为负且宽高必须大于0。");
        if (MinimumWidth < 0 || MaximumWidth < MinimumWidth)
            throw new ArgumentOutOfRangeException(nameof(MaximumWidth), "宽度合格范围无效。");
        if (MinimumHeight < 0 || MaximumHeight < MinimumHeight)
            throw new ArgumentOutOfRangeException(nameof(MaximumHeight), "高度合格范围无效。");
        if (MinimumAspectRatio < 0 || MaximumAspectRatio < MinimumAspectRatio)
            throw new ArgumentOutOfRangeException(nameof(MaximumAspectRatio), "长宽比合格范围无效。");
        if ((ExpectedCenterX < -1) || (ExpectedCenterY < -1))
            throw new ArgumentOutOfRangeException(nameof(ExpectedCenterX), "期望中心坐标只能为-1（禁用）或非负整数。");
        if ((ExpectedCenterX == -1) != (ExpectedCenterY == -1))
            throw new ArgumentException("期望中心X和Y必须同时启用或同时禁用。");
        if (CenterTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(CenterTolerance), "中心位置容差不能为负数。");
        if (AdaptiveBlockSize < 3 || AdaptiveBlockSize % 2 == 0)
            throw new ArgumentOutOfRangeException(nameof(AdaptiveBlockSize), "自适应阈值窗口必须是大于或等于3的奇数。");
        if (!double.IsFinite(AdaptiveConstant))
            throw new ArgumentOutOfRangeException(nameof(AdaptiveConstant), "自适应阈值常数必须是有限数字。");
        if (!double.IsFinite(PixelSizeMm) || PixelSizeMm < 0)
            throw new ArgumentOutOfRangeException(nameof(PixelSizeMm), "像素当量必须是大于或等于0的有限数字。");
        if (string.IsNullOrWhiteSpace(RecipeName) || RecipeName.Trim().Length > 80)
            throw new ArgumentException("配方名称不能为空且不能超过80个字符。", nameof(RecipeName));
        if (string.IsNullOrWhiteSpace(RecipeVersion) || RecipeVersion.Trim().Length > 30)
            throw new ArgumentException("配方版本不能为空且不能超过30个字符。", nameof(RecipeVersion));
    }

    public bool IsCenterCheckEnabled => ExpectedCenterX >= 0 && ExpectedCenterY >= 0;
    public bool IsCalibrationEnabled => PixelSizeMm > 0;
}
