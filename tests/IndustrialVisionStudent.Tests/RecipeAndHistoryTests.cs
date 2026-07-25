using IndustrialVisionStudent.Models;
using IndustrialVisionStudent.Services;

namespace IndustrialVisionStudent.Tests;

public sealed class RecipeAndHistoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "IVS_Tests_" + Guid.NewGuid().ToString("N"));

    public RecipeAndHistoryTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Recipe_RoundTripsAllParameters()
    {
        string path = Path.Combine(directory, "recipe.json");
        var expected = new VisionParameters(123, 456, 0.77, 2, 800, 9000, true, 10, 20, 300, 200)
        {
            MinimumAspectRatio = 0.8,
            MaximumAspectRatio = 1.2,
            ExpectedCenterX = 160,
            ExpectedCenterY = 120,
            CenterTolerance = 15,
            IsDarkObject = false,
            UseAdaptiveThreshold = true,
            AdaptiveBlockSize = 41,
            AdaptiveConstant = 7,
            PixelSizeMm = 0.025,
            RecipeName = "垫片配方",
            RecipeVersion = "2.3"
        };
        var service = new VisionRecipeService();
        service.Save(path, expected);
        Assert.Equal(expected, service.Load(path));
    }

    [Fact]
    public void Recipe_SaveUnchanged_DoesNotCreateBackup()
    {
        string path = Path.Combine(directory, "unchanged.json");
        var parameters = new VisionParameters(110, 300, 0.65, 1, 1000, 100000);
        var service = new VisionRecipeService();
        RecipeSaveResult first = service.Save(path, parameters);

        RecipeSaveResult second = service.Save(path, parameters);

        Assert.True(first.Changed);
        Assert.Null(first.BackupPath);
        Assert.False(second.Changed);
        Assert.Null(second.BackupPath);
        Assert.False(Directory.Exists(Path.Combine(directory, ".history")));
    }

    [Fact]
    public void Recipe_Overwrite_CreatesRestorableBackupOfPreviousVersion()
    {
        string path = Path.Combine(directory, "versioned.json");
        var original = new VisionParameters(110, 300, 0.65, 1, 1000, 100000)
        { RecipeVersion = "1.0" };
        VisionParameters updated = original with { Threshold = 130, RecipeVersion = "2.0" };
        var service = new VisionRecipeService();
        service.Save(path, original);

        RecipeSaveResult result = service.Save(path, updated);

        Assert.True(result.Changed);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal(original, service.Load(result.BackupPath!));
        Assert.Equal(updated, service.Load(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Recipe_BackupRetention_KeepsLatestTwentyVersions()
    {
        string path = Path.Combine(directory, "retention.json");
        var service = new VisionRecipeService();
        var parameters = new VisionParameters(100, 300, 0.65, 1, 1000, 100000);
        service.Save(path, parameters);
        for (int index = 1; index <= 25; index++)
        {
            service.Save(path, parameters with
            {
                Threshold = 100 + index,
                RecipeVersion = $"1.{index}"
            });
        }

        string historyDirectory = Path.Combine(directory, ".history");
        Assert.Equal(20, Directory.EnumerateFiles(
            historyDirectory, "retention.*.json").Count());
        VisionParameters current = service.Load(path);
        Assert.Equal(125, current.Threshold);
        Assert.Equal("1.25", current.RecipeVersion);
    }

    [Fact]
    public void History_SaveQueryAndSummary_WorkAgainstSqlite()
    {
        var service = new InspectionHistoryService(Path.Combine(directory, "inspection.db"));
        service.Initialize();
        service.Save(CreateRecord("OK", "B1"));
        service.Save(CreateRecord("NG", "B2"));

        Assert.Equal(2, service.Query().Count);
        Assert.Single(service.Query("OK"));
        Assert.Single(service.Query(batch: "B2"));
        Assert.Equal(2, service.Query(productModel: "圆形零件-A").Count);
        Assert.Empty(service.Query(productModel: "不存在的产品"));
        InspectionSummary summary = service.GetSummary();
        Assert.Equal(2, summary.Total);
        Assert.Equal(1, summary.Ok);
        Assert.Equal(1, summary.Ng);
        Assert.Equal(50, summary.OkRate);
        InspectionRecord saved = Assert.Single(service.Query("OK"));
        Assert.Equal(0.75, saved.AspectRatio);
        Assert.Equal(2.5, saved.CenterOffset);
        Assert.Equal(0.1, saved.PixelSizeMm);
        Assert.Equal(1, saved.AreaMm2);
        Assert.Equal(3, saved.WidthMm);
        Assert.Equal(4, saved.HeightMm);
        Assert.Equal("圆形零件-A", saved.ProductModel);
        Assert.Equal("测试员", saved.OperatorName);
        Assert.Equal("测试配方", saved.RecipeName);
        Assert.Equal("1.2", saved.RecipeVersion);
        Assert.Equal("PLC-1001", saved.PlcCycleId);
    }

    [Fact]
    public void CsvExport_EscapesCommaAndQuote()
    {
        string path = Path.Combine(directory, "history.csv");
        InspectionRecord record = CreateRecord("NG", "B,\"2");
        InspectionCsvExportService.Export(path, new[] { record });
        string text = File.ReadAllText(path);
        Assert.StartsWith("\ufeff", text);
        Assert.Contains("\"B,\"\"2\"", text);
    }

    [Fact]
    public void History_DateRange_ReturnsOnlyMatchingRecords()
    {
        var service = new InspectionHistoryService(Path.Combine(directory, "dates.db"));
        service.Initialize();
        service.Save(WithRecordTime(CreateRecord("OK", "OLD"), DateTimeOffset.Now.AddDays(-3)));
        service.Save(WithRecordTime(CreateRecord("OK", "TODAY"), DateTimeOffset.Now));
        IReadOnlyList<InspectionRecord> records = service.Query(
            from: DateTimeOffset.Now.AddDays(-1), toExclusive: DateTimeOffset.Now.AddDays(1));
        Assert.Single(records);
        Assert.Equal("TODAY", records[0].BatchNumber);
    }

    [Fact]
    public void NgImageStorage_WritesReadablePng()
    {
        var service = new NgImageStorageService(Path.Combine(directory, "NGImages"));
        using var image = new OpenCvSharp.Mat(
            new OpenCvSharp.Size(100, 80), OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.White);
        string path = service.Save(image, DateTimeOffset.Now);
        Assert.True(File.Exists(path));
        using OpenCvSharp.Mat loaded = OpenCvSharp.Cv2.ImRead(path);
        Assert.False(loaded.Empty());
        Assert.Equal(100, loaded.Width);
    }

    [Fact]
    public void History_CombinedFilters_ReturnOnlyExactMatches()
    {
        var service = new InspectionHistoryService(Path.Combine(directory, "combined.db"));
        service.Initialize();
        service.Save(WithRecordTime(CreateRecord("NG", "B1", "PRODUCT-A"), DateTimeOffset.Now));
        service.Save(WithRecordTime(CreateRecord("OK", "B1", "PRODUCT-B"), DateTimeOffset.Now));
        service.Save(WithRecordTime(CreateRecord("NG", "B1", "PRODUCT-A"), DateTimeOffset.Now.AddDays(-10)));

        IReadOnlyList<InspectionRecord> records = service.Query(
            resultFilter: "NG",
            batch: "B1",
            productModel: "PRODUCT-A",
            from: DateTimeOffset.Now.AddDays(-1),
            toExclusive: DateTimeOffset.Now.AddDays(1));

        InspectionRecord record = Assert.Single(records);
        Assert.Equal("PRODUCT-A", record.ProductModel);
        Assert.Equal("NG", record.Result);
    }

    [Fact]
    public void History_ReturnsDistinctReferencedNgImagePaths()
    {
        var service = new InspectionHistoryService(Path.Combine(directory, "references.db"));
        service.Initialize();
        const string evidencePath = @"C:\Evidence\ng-001.png";
        service.Save(CreateRecordWithImage(evidencePath));
        service.Save(CreateRecordWithImage(evidencePath));

        string path = Assert.Single(service.GetReferencedNgImagePaths());

        Assert.Equal(evidencePath, path);
    }

    [Fact]
    public void NgCleanup_DeletesOnlyOldUnreferencedFilesInsideManagedRoot()
    {
        string root = Path.Combine(directory, "cleanup", "NGImages");
        var service = new NgImageStorageService(root);
        using var image = new OpenCvSharp.Mat(
            new OpenCvSharp.Size(40, 30), OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.Black);
        string referenced = service.Save(image, DateTimeOffset.Now.AddDays(-10));
        string orphan = service.Save(image, DateTimeOffset.Now.AddDays(-10));
        string orphanOnlyDirectoryFile = service.Save(image, DateTimeOffset.Now.AddDays(-11));
        DateTime old = DateTime.UtcNow.AddDays(-9);
        File.SetLastWriteTimeUtc(referenced, old);
        File.SetLastWriteTimeUtc(orphan, old);
        File.SetLastWriteTimeUtc(orphanOnlyDirectoryFile, old);
        string outside = Path.Combine(directory, "outside.png");
        File.WriteAllBytes(outside, new byte[] { 1, 2, 3 });
        File.SetLastWriteTimeUtc(outside, old);

        NgImageCleanupResult result = service.CleanupOrphans(
            new[] { referenced, @"?:\invalid-path" },
            DateTimeOffset.Now.AddDays(-7));

        Assert.True(File.Exists(referenced));
        Assert.False(File.Exists(orphan));
        Assert.False(File.Exists(orphanOnlyDirectoryFile));
        Assert.True(File.Exists(outside));
        Assert.Equal(2, result.DeletedFiles);
        Assert.True(result.DeletedBytes > 0);
        Assert.Equal(1, result.DeletedDirectories);
    }

    [Fact]
    public void History_InitializeMigratesLegacyDatabase()
    {
        string path = Path.Combine(directory, "legacy.db");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE inspection_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    inspected_at TEXT NOT NULL, batch_number TEXT NOT NULL,
                    result TEXT NOT NULL, judgement_code TEXT NOT NULL,
                    judgement_reason TEXT NOT NULL, target_count INTEGER NOT NULL,
                    maximum_area REAL NOT NULL, circularity REAL NOT NULL,
                    center_x INTEGER NOT NULL, center_y INTEGER NOT NULL,
                    width INTEGER NOT NULL, height INTEGER NOT NULL,
                    processing_time_ms REAL NOT NULL, ng_image_path TEXT NULL);
                """;
            command.ExecuteNonQuery();
        }

        var service = new InspectionHistoryService(path);
        service.Initialize();
        service.Save(CreateRecord("OK", "MIGRATED"));

        InspectionRecord record = Assert.Single(service.Query());
        Assert.Equal(0.75, record.AspectRatio);
        Assert.Equal(0.1, record.PixelSizeMm);
    }

    private static InspectionRecord CreateRecord(
        string result, string batch, string productModel = "圆形零件-A") => new()
    {
        InspectedAt = DateTimeOffset.Now, BatchNumber = batch, Result = result,
        ProductModel = productModel, OperatorName = "测试员",
        RecipeName = "测试配方", RecipeVersion = "1.2", PlcCycleId = "PLC-1001",
        JudgementCode = result, JudgementReason = "测试,原因", TargetCount = 1,
        MaximumArea = 100, Circularity = 0.9, CenterX = 10, CenterY = 20,
        Width = 30, Height = 40, AspectRatio = 0.75, CenterOffset = 2.5,
        PixelSizeMm = 0.1, AreaMm2 = 1, WidthMm = 3, HeightMm = 4,
        ProcessingTimeMs = 1.5
    };

    private static InspectionRecord CreateRecordWithImage(string path) => new()
    {
        InspectedAt = DateTimeOffset.Now,
        BatchNumber = "B-NG",
        Result = "NG",
        JudgementCode = "AREA_NG",
        NgImagePath = path
    };

    private static InspectionRecord WithRecordTime(InspectionRecord source, DateTimeOffset time) => new()
    {
        InspectedAt = time, BatchNumber = source.BatchNumber,
        ProductModel = source.ProductModel, OperatorName = source.OperatorName,
        RecipeName = source.RecipeName, RecipeVersion = source.RecipeVersion,
        PlcCycleId = source.PlcCycleId, Result = source.Result,
        JudgementCode = source.JudgementCode, JudgementReason = source.JudgementReason,
        TargetCount = source.TargetCount, MaximumArea = source.MaximumArea,
        Circularity = source.Circularity, CenterX = source.CenterX, CenterY = source.CenterY,
        Width = source.Width, Height = source.Height, AspectRatio = source.AspectRatio,
        CenterOffset = source.CenterOffset, PixelSizeMm = source.PixelSizeMm,
        AreaMm2 = source.AreaMm2, WidthMm = source.WidthMm, HeightMm = source.HeightMm,
        ProcessingTimeMs = source.ProcessingTimeMs,
        NgImagePath = source.NgImagePath
    };

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
