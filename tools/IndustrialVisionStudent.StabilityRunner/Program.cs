using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using IndustrialVisionStudent.Communication;
using IndustrialVisionStudent.Models;
using IndustrialVisionStudent.Services;
using IndustrialVisionStudent.Vision;

int durationSeconds = args.Length > 0 && int.TryParse(args[0], out int parsed) ? parsed : 1800;
string reportPath = args.Length > 1 ? Path.GetFullPath(args[1]) :
    Path.GetFullPath("stability-report.json");
if (durationSeconds < 1) throw new ArgumentOutOfRangeException(nameof(durationSeconds));

string tempDirectory = Path.Combine(Path.GetTempPath(), "IVS_LongStability_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDirectory);
var report = new StabilityReport { RequestedDurationSeconds = durationSeconds, StartedAt = DateTimeOffset.Now };
var stopwatch = Stopwatch.StartNew();
var statusTimer = Stopwatch.StartNew();
var process = Process.GetCurrentProcess();

try
{
    var history = new InspectionHistoryService(Path.Combine(tempDirectory, "stability.db"));
    history.Initialize();
    using var source = SampleImageFactory.CreateStandardWasher();
    var parameters = new VisionParameters(110, 300, 0.65, 1, 1000, 100000);

    int port = GetFreePort();
    using var server = new SimulatedPlcServer();
    using var client = new TcpPlcClient();
    var starts = Channel.CreateUnbounded<string>();
    client.StartRequested += id => starts.Writer.TryWrite(id);
    server.Start(port);
    await client.ConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(3));
    await WaitUntilAsync(() => server.HasClient, TimeSpan.FromSeconds(3));

    DateTime deadline = DateTime.UtcNow.AddSeconds(durationSeconds);
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            using VisionProcessingResult processing = VisionProcessor.Process(source, parameters);
            if (!processing.Result.IsOk) throw new InvalidOperationException(processing.Result.JudgementReason);
            report.VisionCycles++;
            report.TotalVisionMilliseconds += processing.Result.ProcessingTimeMs;

            if (report.VisionCycles % 20 == 0)
            {
                string response = await client.SendRequestAsync("PING", TimeSpan.FromSeconds(2));
                if (response != "PONG") throw new IOException("PING response: " + response);
                report.PingRequests++;
            }

            if (report.VisionCycles % 100 == 0)
            {
                string cycleId = report.VisionCycles.ToString();
                await server.TriggerInspectionAsync(cycleId);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                string receivedId = await starts.Reader.ReadAsync(timeout.Token);
                if (receivedId != cycleId) throw new IOException("Cycle mismatch");
                if (await client.SendRequestAsync($"BUSY {cycleId}", TimeSpan.FromSeconds(2)) != $"ACK BUSY {cycleId}")
                    throw new IOException("BUSY handshake failed");
                if (await client.SendRequestAsync($"RESULT {cycleId} OK", TimeSpan.FromSeconds(2)) != $"ACK RESULT {cycleId}")
                    throw new IOException("RESULT handshake failed");
                report.PlcInspectionCycles++;
            }

            if (report.VisionCycles % 50 == 0)
            {
                history.Save(new InspectionRecord
                {
                    InspectedAt = DateTimeOffset.Now, BatchNumber = "LONG-STABILITY", Result = "OK",
                    JudgementCode = "OK", JudgementReason = "长稳循环", TargetCount = 1,
                    MaximumArea = processing.Result.MaximumArea,
                    Circularity = processing.Result.Circularity,
                    ProcessingTimeMs = processing.Result.ProcessingTimeMs
                });
                report.DatabaseWrites++;
            }

            process.Refresh();
            report.MaximumWorkingSetBytes = Math.Max(report.MaximumWorkingSetBytes, process.WorkingSet64);
            await Task.Delay(10);
        }
        catch (Exception exception)
        {
            report.Errors.Add($"{DateTimeOffset.Now:O} {exception}");
            await Task.Delay(100);
        }

        if (statusTimer.Elapsed >= TimeSpan.FromSeconds(30))
        {
            Console.WriteLine($"elapsed={stopwatch.Elapsed:hh\\:mm\\:ss} cycles={report.VisionCycles} " +
                $"db={report.DatabaseWrites} plc={report.PlcInspectionCycles} errors={report.Errors.Count} " +
                $"workingMB={report.MaximumWorkingSetBytes / 1024.0 / 1024.0:F1}");
            statusTimer.Restart();
        }
    }

    report.DatabaseRowsAtEnd = history.GetSummary().Total;
}
finally
{
    stopwatch.Stop();
    report.ActualDurationSeconds = stopwatch.Elapsed.TotalSeconds;
    report.CompletedAt = DateTimeOffset.Now;
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    File.WriteAllText(reportPath, JsonSerializer.Serialize(report,
        new JsonSerializerOptions { WriteIndented = true }));
    try { Directory.Delete(tempDirectory, true); } catch { }
}

Console.WriteLine($"completed cycles={report.VisionCycles} errors={report.Errors.Count} report={reportPath}");
return report.Errors.Count == 0 ? 0 : 1;

static int GetFreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
{
    DateTime deadline = DateTime.UtcNow + timeout;
    while (!condition())
    {
        if (DateTime.UtcNow >= deadline) throw new TimeoutException();
        await Task.Delay(10);
    }
}

internal sealed class StabilityReport
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public int RequestedDurationSeconds { get; set; }
    public double ActualDurationSeconds { get; set; }
    public long VisionCycles { get; set; }
    public long PingRequests { get; set; }
    public long PlcInspectionCycles { get; set; }
    public long DatabaseWrites { get; set; }
    public int DatabaseRowsAtEnd { get; set; }
    public double TotalVisionMilliseconds { get; set; }
    public long MaximumWorkingSetBytes { get; set; }
    public List<string> Errors { get; set; } = new();
}
