using System.Globalization;
using System.IO;
using IndustrialVisionStudent.Models;
using Microsoft.Data.Sqlite;

namespace IndustrialVisionStudent.Services;

public sealed class InspectionHistoryService
{
    private readonly string connectionString;
    private readonly object databaseLock = new();

    public InspectionHistoryService(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        string fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Pooling = false
        }.ToString();
    }

    public void Initialize()
    {
        lock (databaseLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS inspection_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    inspected_at TEXT NOT NULL,
                    batch_number TEXT NOT NULL,
                    product_model TEXT NOT NULL DEFAULT 'DEFAULT',
                    operator_name TEXT NOT NULL DEFAULT '未填写',
                    recipe_name TEXT NOT NULL DEFAULT '默认配方',
                    recipe_version TEXT NOT NULL DEFAULT '1.0',
                    plc_cycle_id TEXT NULL,
                    result TEXT NOT NULL CHECK(result IN ('OK','NG')),
                    judgement_code TEXT NOT NULL,
                    judgement_reason TEXT NOT NULL,
                    target_count INTEGER NOT NULL,
                    maximum_area REAL NOT NULL,
                    circularity REAL NOT NULL,
                    center_x INTEGER NOT NULL,
                    center_y INTEGER NOT NULL,
                    width INTEGER NOT NULL,
                    height INTEGER NOT NULL,
                    aspect_ratio REAL NOT NULL DEFAULT 0,
                    center_offset REAL NOT NULL DEFAULT 0,
                    pixel_size_mm REAL NOT NULL DEFAULT 0,
                    area_mm2 REAL NOT NULL DEFAULT 0,
                    width_mm REAL NOT NULL DEFAULT 0,
                    height_mm REAL NOT NULL DEFAULT 0,
                    processing_time_ms REAL NOT NULL,
                    ng_image_path TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_inspection_time
                    ON inspection_records(inspected_at DESC);
                CREATE INDEX IF NOT EXISTS idx_inspection_result
                    ON inspection_records(result);
                CREATE INDEX IF NOT EXISTS idx_inspection_batch
                    ON inspection_records(batch_number);
                """;
            command.ExecuteNonQuery();
            EnsureColumn(connection, "aspect_ratio", "REAL NOT NULL DEFAULT 0");
            EnsureColumn(connection, "center_offset", "REAL NOT NULL DEFAULT 0");
            EnsureColumn(connection, "pixel_size_mm", "REAL NOT NULL DEFAULT 0");
            EnsureColumn(connection, "area_mm2", "REAL NOT NULL DEFAULT 0");
            EnsureColumn(connection, "width_mm", "REAL NOT NULL DEFAULT 0");
            EnsureColumn(connection, "height_mm", "REAL NOT NULL DEFAULT 0");
            EnsureColumn(connection, "product_model", "TEXT NOT NULL DEFAULT 'DEFAULT'");
            EnsureColumn(connection, "operator_name", "TEXT NOT NULL DEFAULT '未填写'");
            EnsureColumn(connection, "recipe_name", "TEXT NOT NULL DEFAULT '默认配方'");
            EnsureColumn(connection, "recipe_version", "TEXT NOT NULL DEFAULT '1.0'");
            EnsureColumn(connection, "plc_cycle_id", "TEXT NULL");
        }
    }

    public long Save(InspectionRecord record)
    {
        lock (databaseLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO inspection_records
                (inspected_at,batch_number,product_model,operator_name,recipe_name,
                 recipe_version,plc_cycle_id,result,judgement_code,judgement_reason,
                 target_count,maximum_area,circularity,center_x,center_y,width,height,
                 aspect_ratio,center_offset,pixel_size_mm,area_mm2,width_mm,height_mm,
                 processing_time_ms,ng_image_path)
                VALUES
                ($time,$batch,$product,$operator,$recipeName,$recipeVersion,$cycle,
                 $result,$code,$reason,$count,$area,$circularity,$centerX,
                 $centerY,$width,$height,$aspectRatio,$centerOffset,$pixelSizeMm,$areaMm2,
                 $widthMm,$heightMm,$elapsed,$image);
                SELECT last_insert_rowid();
                """;
            AddParameters(command, record);
            return (long)(command.ExecuteScalar() ?? 0L);
        }
    }

    public IReadOnlyList<InspectionRecord> Query(
        string? resultFilter = null,
        string? batch = null,
        string? productModel = null,
        int limit = 500,
        DateTimeOffset? from = null,
        DateTimeOffset? toExclusive = null)
    {
        if (limit is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(limit));
        lock (databaseLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            var conditions = new List<string>();
            if (resultFilter is "OK" or "NG")
            {
                conditions.Add("result = $result");
                command.Parameters.AddWithValue("$result", resultFilter);
            }
            if (!string.IsNullOrWhiteSpace(batch))
            {
                conditions.Add("batch_number = $batch");
                command.Parameters.AddWithValue("$batch", batch.Trim());
            }
            if (!string.IsNullOrWhiteSpace(productModel))
            {
                conditions.Add("product_model = $product");
                command.Parameters.AddWithValue("$product", productModel.Trim());
            }
            if (from.HasValue)
            {
                conditions.Add("inspected_at >= $from");
                command.Parameters.AddWithValue("$from", from.Value.ToUniversalTime().ToString("O"));
            }
            if (toExclusive.HasValue)
            {
                conditions.Add("inspected_at < $to");
                command.Parameters.AddWithValue("$to", toExclusive.Value.ToUniversalTime().ToString("O"));
            }
            string where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
            command.CommandText = $"""
                SELECT id,inspected_at,batch_number,product_model,operator_name,recipe_name,
                       recipe_version,plc_cycle_id,result,judgement_code,judgement_reason,
                       target_count,maximum_area,circularity,center_x,center_y,width,height,
                       aspect_ratio,center_offset,pixel_size_mm,area_mm2,width_mm,height_mm,
                       processing_time_ms,ng_image_path
                FROM inspection_records {where}
                ORDER BY id DESC LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            using SqliteDataReader reader = command.ExecuteReader();
            var records = new List<InspectionRecord>();
            while (reader.Read()) records.Add(ReadRecord(reader));
            return records;
        }
    }

    public InspectionSummary GetSummary()
    {
        lock (databaseLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*),
                       COALESCE(SUM(CASE WHEN result='OK' THEN 1 ELSE 0 END),0),
                       COALESCE(SUM(CASE WHEN result='NG' THEN 1 ELSE 0 END),0)
                FROM inspection_records;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            reader.Read();
            return new InspectionSummary(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        }
    }

    public IReadOnlyList<string> GetReferencedNgImagePaths()
    {
        lock (databaseLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT DISTINCT ng_image_path
                FROM inspection_records
                WHERE ng_image_path IS NOT NULL AND TRIM(ng_image_path) <> '';
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            var paths = new List<string>();
            while (reader.Read()) paths.Add(reader.GetString(0));
            return paths;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static void AddParameters(SqliteCommand command, InspectionRecord record)
    {
        command.Parameters.AddWithValue("$time", record.InspectedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$batch", record.BatchNumber);
        command.Parameters.AddWithValue("$product", record.ProductModel);
        command.Parameters.AddWithValue("$operator", record.OperatorName);
        command.Parameters.AddWithValue("$recipeName", record.RecipeName);
        command.Parameters.AddWithValue("$recipeVersion", record.RecipeVersion);
        command.Parameters.AddWithValue("$cycle", (object?)record.PlcCycleId ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", record.Result);
        command.Parameters.AddWithValue("$code", record.JudgementCode);
        command.Parameters.AddWithValue("$reason", record.JudgementReason);
        command.Parameters.AddWithValue("$count", record.TargetCount);
        command.Parameters.AddWithValue("$area", record.MaximumArea);
        command.Parameters.AddWithValue("$circularity", record.Circularity);
        command.Parameters.AddWithValue("$centerX", record.CenterX);
        command.Parameters.AddWithValue("$centerY", record.CenterY);
        command.Parameters.AddWithValue("$width", record.Width);
        command.Parameters.AddWithValue("$height", record.Height);
        command.Parameters.AddWithValue("$aspectRatio", record.AspectRatio);
        command.Parameters.AddWithValue("$centerOffset", record.CenterOffset);
        command.Parameters.AddWithValue("$pixelSizeMm", record.PixelSizeMm);
        command.Parameters.AddWithValue("$areaMm2", record.AreaMm2);
        command.Parameters.AddWithValue("$widthMm", record.WidthMm);
        command.Parameters.AddWithValue("$heightMm", record.HeightMm);
        command.Parameters.AddWithValue("$elapsed", record.ProcessingTimeMs);
        command.Parameters.AddWithValue("$image", (object?)record.NgImagePath ?? DBNull.Value);
    }

    private static InspectionRecord ReadRecord(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        InspectedAt = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
        BatchNumber = reader.GetString(2), ProductModel = reader.GetString(3),
        OperatorName = reader.GetString(4), RecipeName = reader.GetString(5),
        RecipeVersion = reader.GetString(6),
        PlcCycleId = reader.IsDBNull(7) ? null : reader.GetString(7),
        Result = reader.GetString(8),
        JudgementCode = reader.GetString(9), JudgementReason = reader.GetString(10),
        TargetCount = reader.GetInt32(11), MaximumArea = reader.GetDouble(12),
        Circularity = reader.GetDouble(13), CenterX = reader.GetInt32(14),
        CenterY = reader.GetInt32(15), Width = reader.GetInt32(16), Height = reader.GetInt32(17),
        AspectRatio = reader.GetDouble(18), CenterOffset = reader.GetDouble(19),
        PixelSizeMm = reader.GetDouble(20), AreaMm2 = reader.GetDouble(21),
        WidthMm = reader.GetDouble(22), HeightMm = reader.GetDouble(23),
        ProcessingTimeMs = reader.GetDouble(24),
        NgImagePath = reader.IsDBNull(25) ? null : reader.GetString(25)
    };

    private static void EnsureColumn(SqliteConnection connection, string name, string definition)
    {
        using SqliteCommand list = connection.CreateCommand();
        list.CommandText = "PRAGMA table_info(inspection_records);";
        using SqliteDataReader reader = list.ExecuteReader();
        bool exists = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), name, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }
        reader.Close();
        if (exists) return;

        using SqliteCommand alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE inspection_records ADD COLUMN {name} {definition};";
        alter.ExecuteNonQuery();
    }
}
