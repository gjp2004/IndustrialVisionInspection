using System.Diagnostics;
using IndustrialVisionStudent.Models;
using OpenCvSharp;

namespace IndustrialVisionStudent.Vision;

public static class VisionProcessor
{
    public static VisionProcessingResult Process(Mat source, VisionParameters parameters)
    {
        if (source.Empty())
            throw new ArgumentException("待检测图像为空。", nameof(source));

        parameters.Validate();
        var stopwatch = Stopwatch.StartNew();
        var gray = new Mat();
        var binary = Mat.Zeros(source.Rows, source.Cols, MatType.CV_8UC1).ToMat();
        var annotated = source.Clone();

        try
        {
            if (source.Channels() == 1)
                source.CopyTo(gray);
            else
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);
            Rect region = CreateProcessingRegion(source, parameters);
            using (var grayRegion = new Mat(gray, region))
            using (var binaryRegion = new Mat(binary, region))
            using (var temporaryBinary = new Mat())
            using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3)))
            {
                ThresholdTypes thresholdType = parameters.IsDarkObject
                    ? ThresholdTypes.BinaryInv
                    : ThresholdTypes.Binary;
                if (parameters.UseAdaptiveThreshold)
                {
                    Cv2.AdaptiveThreshold(grayRegion, temporaryBinary, 255,
                        AdaptiveThresholdTypes.GaussianC, thresholdType,
                        parameters.AdaptiveBlockSize, parameters.AdaptiveConstant);
                }
                else
                {
                    Cv2.Threshold(grayRegion, temporaryBinary, parameters.Threshold, 255,
                        thresholdType);
                }
                Cv2.MorphologyEx(temporaryBinary, temporaryBinary, MorphTypes.Open, kernel);
                Cv2.MorphologyEx(temporaryBinary, temporaryBinary, MorphTypes.Close, kernel);
                temporaryBinary.CopyTo(binaryRegion);
            }

            if (parameters.IsRoiEnabled)
                Cv2.Rectangle(annotated, region, new Scalar(255, 255, 0), 2);

            Cv2.FindContours(binary, out Point[][] contours, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            // 数量统计只按面积去除噪点。圆度属于产品规格，必须在判定阶段
            // 给出独立、可解释的 CIRCULARITY_NG，不能静默删除后误报 COUNT_NG。
            var valid = contours
                .Select(CreateMeasurement)
                .Where(x => x.Area >= parameters.MinimumContourArea)
                .OrderByDescending(x => x.Area)
                .ToArray();

            foreach (ContourMeasurement measurement in valid)
            {
                Cv2.DrawContours(annotated, new[] { measurement.Contour }, -1,
                    new Scalar(0, 200, 0), 2);
                Cv2.Rectangle(annotated, measurement.Bounds, new Scalar(255, 120, 0), 2);
                Cv2.DrawMarker(annotated, measurement.Center, new Scalar(0, 0, 255),
                    MarkerTypes.Cross, 18, 2);
            }

            InspectionResult result = BuildResult(valid, parameters, stopwatch.Elapsed.TotalMilliseconds);
            Scalar color = result.IsOk ? new Scalar(0, 200, 0) : new Scalar(0, 0, 255);
            Cv2.PutText(annotated, result.IsOk ? "OK" : "NG", new Point(18, 40),
                HersheyFonts.HersheySimplex, 1.2, color, 3);

            return new VisionProcessingResult(result, gray, binary, annotated);
        }
        catch
        {
            gray.Dispose();
            binary.Dispose();
            annotated.Dispose();
            throw;
        }
    }

    private static Rect CreateProcessingRegion(Mat source, VisionParameters parameters)
    {
        if (!parameters.IsRoiEnabled) return new Rect(0, 0, source.Width, source.Height);
        long right = (long)parameters.RoiX + parameters.RoiWidth;
        long bottom = (long)parameters.RoiY + parameters.RoiHeight;
        if (right > source.Width || bottom > source.Height)
            throw new ArgumentException(
                $"ROI({parameters.RoiX},{parameters.RoiY},{parameters.RoiWidth},{parameters.RoiHeight})" +
                $"超出图像范围{source.Width}×{source.Height}。 ");
        return new Rect(parameters.RoiX, parameters.RoiY, parameters.RoiWidth, parameters.RoiHeight);
    }

    private static ContourMeasurement CreateMeasurement(Point[] contour)
    {
        double area = Cv2.ContourArea(contour);
        double perimeter = Cv2.ArcLength(contour, true);
        double circularity = perimeter <= double.Epsilon
            ? 0
            : 4 * Math.PI * area / (perimeter * perimeter);
        Rect bounds = Cv2.BoundingRect(contour);
        Moments moments = Cv2.Moments(contour);
        Point center = Math.Abs(moments.M00) <= double.Epsilon
            ? new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2)
            : new Point((int)(moments.M10 / moments.M00), (int)(moments.M01 / moments.M00));
        return new ContourMeasurement(contour, area, circularity, bounds, center);
    }

    private static InspectionResult BuildResult(
        IReadOnlyList<ContourMeasurement> valid,
        VisionParameters parameters,
        double elapsedMs)
    {
        ContourMeasurement? largest = valid.Count > 0 ? valid[0] : null;

        if (valid.Count != parameters.ExpectedCount)
            return Create(false, "COUNT_NG",
                $"目标数量不符：期望{parameters.ExpectedCount}个，实际{valid.Count}个。",
                valid.Count, largest, elapsedMs, parameters);

        if (largest is null)
            return Create(false, "NO_TARGET", "没有找到有效目标。", 0, null, elapsedMs, parameters);

        if (largest.Area < parameters.MinimumOkArea || largest.Area > parameters.MaximumOkArea)
            return Create(false, "AREA_NG",
                $"最大目标面积{largest.Area:F0}px²，超出合格范围。",
                valid.Count, largest, elapsedMs, parameters);

        if (largest.Circularity < parameters.MinimumCircularity)
            return Create(false, "CIRCULARITY_NG",
                $"目标圆度{largest.Circularity:F3}，低于下限{parameters.MinimumCircularity:F3}。",
                valid.Count, largest, elapsedMs, parameters);

        if (largest.Bounds.Width < parameters.MinimumWidth || largest.Bounds.Width > parameters.MaximumWidth ||
            largest.Bounds.Height < parameters.MinimumHeight || largest.Bounds.Height > parameters.MaximumHeight)
            return Create(false, "SIZE_NG",
                $"目标尺寸{largest.Bounds.Width}×{largest.Bounds.Height}px，超出宽高合格范围。",
                valid.Count, largest, elapsedMs, parameters);

        double aspectRatio = largest.Bounds.Height == 0
            ? 0
            : (double)largest.Bounds.Width / largest.Bounds.Height;
        if (aspectRatio < parameters.MinimumAspectRatio || aspectRatio > parameters.MaximumAspectRatio)
            return Create(false, "ASPECT_RATIO_NG",
                $"目标长宽比{aspectRatio:F3}，超出范围" +
                $"{parameters.MinimumAspectRatio:F3}～{parameters.MaximumAspectRatio:F3}。",
                valid.Count, largest, elapsedMs, parameters);

        if (parameters.IsCenterCheckEnabled)
        {
            double offset = Math.Sqrt(
                Math.Pow(largest.Center.X - parameters.ExpectedCenterX, 2) +
                Math.Pow(largest.Center.Y - parameters.ExpectedCenterY, 2));
            if (offset > parameters.CenterTolerance)
                return Create(false, "POSITION_NG",
                    $"目标中心偏移{offset:F1}px，超过容差{parameters.CenterTolerance}px。",
                    valid.Count, largest, elapsedMs, parameters);
        }

        return Create(true, "OK", "目标数量、面积、圆度、尺寸、长宽比和位置均符合当前参数。",
            valid.Count, largest, elapsedMs, parameters);
    }

    private static InspectionResult Create(
        bool isOk, string code, string reason, int count,
        ContourMeasurement? measurement, double elapsedMs, VisionParameters parameters) => new()
    {
        IsOk = isOk,
        JudgementCode = code,
        JudgementReason = reason,
        TargetCount = count,
        MaximumArea = measurement?.Area ?? 0,
        Circularity = measurement?.Circularity ?? 0,
        CenterX = measurement?.Center.X ?? 0,
        CenterY = measurement?.Center.Y ?? 0,
        Width = measurement?.Bounds.Width ?? 0,
        Height = measurement?.Bounds.Height ?? 0,
        AspectRatio = measurement is null || measurement.Bounds.Height == 0
            ? 0
            : (double)measurement.Bounds.Width / measurement.Bounds.Height,
        CenterOffset = measurement is null || !parameters.IsCenterCheckEnabled
            ? 0
            : Math.Sqrt(
                Math.Pow(measurement.Center.X - parameters.ExpectedCenterX, 2) +
                Math.Pow(measurement.Center.Y - parameters.ExpectedCenterY, 2)),
        PixelSizeMm = parameters.PixelSizeMm,
        AreaMm2 = measurement?.Area * parameters.PixelSizeMm * parameters.PixelSizeMm ?? 0,
        WidthMm = measurement?.Bounds.Width * parameters.PixelSizeMm ?? 0,
        HeightMm = measurement?.Bounds.Height * parameters.PixelSizeMm ?? 0,
        ProcessingTimeMs = elapsedMs
    };

    private sealed record ContourMeasurement(
        Point[] Contour, double Area, double Circularity, Rect Bounds, Point Center);
}
