using IndustrialVisionStudent.Models;
using IndustrialVisionStudent.Vision;
using OpenCvSharp;

namespace IndustrialVisionStudent.Tests;

public sealed class VisionProcessorTests
{
    [Fact]
    public void SingleDarkCircle_IsMeasuredAndAccepted()
    {
        using Mat image = CreateCircleImage(new Point(320, 240), 50);
        using VisionProcessingResult processing = VisionProcessor.Process(image, StandardParameters());
        InspectionResult result = processing.Result;

        Assert.True(result.IsOk, result.JudgementReason);
        Assert.Equal(1, result.TargetCount);
        Assert.InRange(result.MaximumArea, 7500, 8000);
        Assert.InRange(result.Circularity, 0.85, 1.0);
        Assert.InRange(result.CenterX, 318, 322);
        Assert.InRange(result.CenterY, 238, 242);
    }

    [Fact]
    public void TwoTargets_ReturnCountNg()
    {
        using Mat image = CreateCircleImage(new Point(200, 240), 50);
        Cv2.Circle(image, new Point(440, 240), 50, Scalar.Black, -1);
        using VisionProcessingResult processing = VisionProcessor.Process(image, StandardParameters());
        Assert.False(processing.Result.IsOk);
        Assert.Equal("COUNT_NG", processing.Result.JudgementCode);
        Assert.Equal(2, processing.Result.TargetCount);
    }

    [Fact]
    public void RoiCanExcludeTarget()
    {
        using Mat image = CreateCircleImage(new Point(500, 240), 50);
        VisionParameters parameters = StandardParameters() with
        { IsRoiEnabled = true, RoiX = 0, RoiY = 0, RoiWidth = 300, RoiHeight = 480 };
        using VisionProcessingResult processing = VisionProcessor.Process(image, parameters);
        Assert.Equal(0, processing.Result.TargetCount);
        Assert.Equal("COUNT_NG", processing.Result.JudgementCode);
    }

    [Fact]
    public void RoiOutsideImage_IsRejected()
    {
        using Mat image = CreateCircleImage(new Point(320, 240), 50);
        VisionParameters parameters = StandardParameters() with
        { IsRoiEnabled = true, RoiX = 500, RoiY = 400, RoiWidth = 200, RoiHeight = 200 };
        Assert.Throws<ArgumentException>(() => VisionProcessor.Process(image, parameters));
    }

    [Fact]
    public void InvalidParameters_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (StandardParameters() with { Threshold = 300 }).Validate());
    }

    [Fact]
    public void WidthOutsideSpecification_ReturnsSizeNg()
    {
        using Mat image = CreateCircleImage(new Point(320, 240), 50);
        VisionParameters parameters = StandardParameters() with { MaximumWidth = 80 };
        using VisionProcessingResult processing = VisionProcessor.Process(image, parameters);
        Assert.False(processing.Result.IsOk);
        Assert.Equal("SIZE_NG", processing.Result.JudgementCode);
    }

    [Fact]
    public void IrregularTarget_ReturnsIndependentCircularityNg()
    {
        using var image = new Mat(new Size(640, 480), MatType.CV_8UC3, Scalar.White);
        Cv2.Rectangle(image, new Rect(220, 220, 200, 40), Scalar.Black, -1);
        VisionParameters parameters = StandardParameters() with
        {
            MinimumOkArea = 7000,
            MaximumOkArea = 9000,
            MinimumCircularity = 0.65
        };

        using VisionProcessingResult processing = VisionProcessor.Process(image, parameters);

        Assert.False(processing.Result.IsOk);
        Assert.Equal("CIRCULARITY_NG", processing.Result.JudgementCode);
        Assert.Equal(1, processing.Result.TargetCount);
    }

    [Fact]
    public void AspectRatioOutsideSpecification_ReturnsAspectRatioNg()
    {
        using var image = new Mat(new Size(640, 480), MatType.CV_8UC3, Scalar.White);
        Cv2.Rectangle(image, new Rect(270, 190, 100, 100), Scalar.Black, -1);
        VisionParameters parameters = StandardParameters() with
        {
            MinimumCircularity = 0,
            MinimumOkArea = 9000,
            MaximumOkArea = 11000,
            MinimumAspectRatio = 1.2,
            MaximumAspectRatio = 2
        };

        using VisionProcessingResult processing = VisionProcessor.Process(image, parameters);

        Assert.False(processing.Result.IsOk);
        Assert.Equal("ASPECT_RATIO_NG", processing.Result.JudgementCode);
    }

    [Fact]
    public void CenterOutsideTolerance_ReturnsPositionNg()
    {
        using Mat image = CreateCircleImage(new Point(400, 240), 50);
        VisionParameters parameters = StandardParameters() with
        {
            ExpectedCenterX = 320,
            ExpectedCenterY = 240,
            CenterTolerance = 20
        };

        using VisionProcessingResult processing = VisionProcessor.Process(image, parameters);

        Assert.False(processing.Result.IsOk);
        Assert.Equal("POSITION_NG", processing.Result.JudgementCode);
    }

    [Fact]
    public void InvalidCenterConfiguration_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            (StandardParameters() with { ExpectedCenterX = 320, ExpectedCenterY = -1 }).Validate());
    }

    [Fact]
    public void BrightObjectOnDarkBackground_CanBeDetected()
    {
        using var image = new Mat(new Size(640, 480), MatType.CV_8UC3, Scalar.Black);
        Cv2.Circle(image, new Point(320, 240), 50, Scalar.White, -1);
        VisionParameters parameters = StandardParameters() with { IsDarkObject = false };

        using VisionProcessingResult processing = VisionProcessor.Process(image, parameters);

        Assert.True(processing.Result.IsOk, processing.Result.JudgementReason);
    }

    [Fact]
    public void EvenAdaptiveBlockSize_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (StandardParameters() with { AdaptiveBlockSize = 10 }).Validate());
    }

    [Fact]
    public void AdaptiveThreshold_CanDetectStandardTarget()
    {
        using Mat image = CreateCircleImage(new Point(320, 240), 50);
        VisionParameters parameters = StandardParameters() with
        {
            UseAdaptiveThreshold = true,
            AdaptiveBlockSize = 31,
            AdaptiveConstant = 5
        };

        using VisionProcessingResult processing = VisionProcessor.Process(image, parameters);

        Assert.True(processing.Result.IsOk, processing.Result.JudgementReason);
    }

    [Fact]
    public void PixelCalibration_ConvertsPixelMeasurementsToMetric()
    {
        using Mat image = CreateCircleImage(new Point(320, 240), 50);
        VisionParameters parameters = StandardParameters() with { PixelSizeMm = 0.1 };

        using VisionProcessingResult processing = VisionProcessor.Process(image, parameters);

        Assert.True(processing.Result.IsOk);
        Assert.Equal(0.1, processing.Result.PixelSizeMm);
        Assert.InRange(processing.Result.WidthMm, 9.9, 10.2);
        Assert.InRange(processing.Result.HeightMm, 9.9, 10.2);
        Assert.InRange(processing.Result.AreaMm2, 75, 80);
    }

    [Fact]
    public void NegativePixelSize_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (StandardParameters() with { PixelSizeMm = -0.1 }).Validate());
    }

    [Fact]
    public void BuiltInSample_IsAcceptedByDefaultUiParameters()
    {
        using Mat image = SampleImageFactory.CreateStandardWasher();
        var parameters = new VisionParameters(110, 300, 0.65, 1, 1000, 100000);
        using VisionProcessingResult processing = VisionProcessor.Process(image, parameters);
        Assert.True(processing.Result.IsOk, processing.Result.JudgementReason);
    }

    private static Mat CreateCircleImage(Point center, int radius)
    {
        var image = new Mat(new Size(640, 480), MatType.CV_8UC3, Scalar.White);
        Cv2.Circle(image, center, radius, Scalar.Black, -1);
        return image;
    }

    private static VisionParameters StandardParameters() =>
        new(110, 300, 0.65, 1, 7000, 9000);
}
