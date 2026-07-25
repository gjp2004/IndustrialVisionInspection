using System.Globalization;
using System.Text;
using System.IO;
using IndustrialVisionStudent.Models;

namespace IndustrialVisionStudent.Services;

public static class InspectionCsvExportService
{
    public static void Export(string path, IEnumerable<InspectionRecord> records)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("编号,检测时间,批次,产品型号,操作员,配方名称,配方版本,PLC周期,结果,判定代码,判定原因,数量,最大面积px²,圆度,中心X,中心Y,宽度px,高度px,长宽比,中心偏移px,像素当量mm/px,面积mm²,宽度mm,高度mm,耗时ms,NG图片");
        foreach (InspectionRecord item in records)
        {
            string[] values =
            {
                item.Id.ToString(CultureInfo.InvariantCulture), item.InspectedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                item.BatchNumber, item.ProductModel, item.OperatorName, item.RecipeName,
                item.RecipeVersion, item.PlcCycleId ?? string.Empty,
                item.Result, item.JudgementCode, item.JudgementReason,
                item.TargetCount.ToString(CultureInfo.InvariantCulture), item.MaximumArea.ToString("F2", CultureInfo.InvariantCulture),
                item.Circularity.ToString("F4", CultureInfo.InvariantCulture), item.CenterX.ToString(), item.CenterY.ToString(),
                item.Width.ToString(), item.Height.ToString(),
                item.AspectRatio.ToString("F4", CultureInfo.InvariantCulture),
                item.CenterOffset.ToString("F2", CultureInfo.InvariantCulture),
                item.PixelSizeMm.ToString("F6", CultureInfo.InvariantCulture),
                item.AreaMm2.ToString("F4", CultureInfo.InvariantCulture),
                item.WidthMm.ToString("F4", CultureInfo.InvariantCulture),
                item.HeightMm.ToString("F4", CultureInfo.InvariantCulture),
                item.ProcessingTimeMs.ToString("F3", CultureInfo.InvariantCulture),
                item.NgImagePath ?? string.Empty
            };
            writer.WriteLine(string.Join(',', values.Select(Escape)));
        }
    }

    private static string Escape(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
}
