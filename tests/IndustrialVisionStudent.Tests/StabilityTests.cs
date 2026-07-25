using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using IndustrialVisionStudent.Camera;
using IndustrialVisionStudent.Communication;
using IndustrialVisionStudent.Models;
using IndustrialVisionStudent.Services;
using IndustrialVisionStudent.Vision;
using OpenCvSharp;

namespace IndustrialVisionStudent.Tests;

[Collection("Performance")]
public sealed class StabilityTests
{
    [Fact]
    public void VisionProcessing_FiveHundredCycles_CompletesWithoutFailure()
    {
        using var image = new Mat(new Size(640, 480), MatType.CV_8UC3, Scalar.White);
        Cv2.Circle(image, new Point(320, 240), 50, Scalar.Black, -1);
        var parameters = new VisionParameters(110, 300, 0.65, 1, 7000, 9000);
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < 500; index++)
        {
            using VisionProcessingResult result = VisionProcessor.Process(image, parameters);
            Assert.True(result.Result.IsOk);
        }
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"处理耗时{stopwatch.Elapsed}");
    }

    [Fact]
    public void VisionProcessing_OneThousandDisposedCycles_DoesNotRetainSignificantMemory()
    {
        using var image = new Mat(new Size(640, 480), MatType.CV_8UC3, Scalar.White);
        Cv2.Circle(image, new Point(320, 240), 50, Scalar.Black, -1);
        var parameters = new VisionParameters(110, 300, 0.65, 1, 7000, 9000);

        for (int index = 0; index < 100; index++)
        {
            using VisionProcessingResult warmup = VisionProcessor.Process(image, parameters);
        }
        ForceCollection();
        long managedBefore = GC.GetTotalMemory(true);
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        long privateBefore = process.PrivateMemorySize64;

        for (int index = 0; index < 1000; index++)
        {
            using VisionProcessingResult result = VisionProcessor.Process(image, parameters);
            Assert.True(result.Result.IsOk);
        }

        ForceCollection();
        long managedAfter = GC.GetTotalMemory(true);
        process.Refresh();
        long privateAfter = process.PrivateMemorySize64;
        long managedGrowth = Math.Max(0, managedAfter - managedBefore);
        long privateGrowth = Math.Max(0, privateAfter - privateBefore);

        Assert.True(managedGrowth < 8 * 1024 * 1024,
            $"托管内存增长{managedGrowth / 1024.0 / 1024.0:F1}MB");
        Assert.True(privateGrowth < 32 * 1024 * 1024,
            $"私有内存增长{privateGrowth / 1024.0 / 1024.0:F1}MB");
    }

    [Fact]
    public void Sqlite_TwoHundredWritesAndQuery_AreConsistent()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IVS_Stability_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new InspectionHistoryService(Path.Combine(directory, "history.db"));
            service.Initialize();
            for (int index = 0; index < 200; index++)
                service.Save(new InspectionRecord
                {
                    InspectedAt = DateTimeOffset.Now, BatchNumber = "STABLE",
                    Result = index % 2 == 0 ? "OK" : "NG", JudgementCode = "TEST",
                    JudgementReason = "稳定性测试", TargetCount = 1
                });
            InspectionSummary summary = service.GetSummary();
            Assert.Equal(200, summary.Total);
            Assert.Equal(100, summary.Ok);
            Assert.Equal(100, summary.Ng);
            Assert.Equal(200, service.Query(limit: 500).Count);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Tcp_FiftySequentialRequests_AllMatch()
    {
        int port = GetFreePort();
        using var server = new SimulatedPlcServer();
        using var client = new TcpPlcClient();
        server.Start(port);
        await client.ConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => server.HasClient, TimeSpan.FromSeconds(2));
        for (int index = 0; index < 50; index++)
            Assert.Equal("PONG", await client.SendRequestAsync("PING", TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task CameraCaptureFailure_ReconnectsAndProducesFrame()
    {
        using var camera = new RecoveringCamera();
        using var service = new CameraAcquisitionService(
            camera, maximumReconnectAttempts: 2, reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Reconnected += attempt => reconnected.TrySetResult(attempt);
        service.FrameReceived += _ => frame.TrySetResult();
        Assert.True(service.Start());
        Assert.Equal(1, await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        await frame.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(service.IsRunning);
    }

    private sealed class RecoveringCamera : ICamera
    {
        private bool failNextCapture = true;
        public bool IsOpen { get; private set; }
        public bool Open() { IsOpen = true; return true; }
        public Mat Capture()
        {
            if (!IsOpen) throw new InvalidOperationException();
            if (failNextCapture)
            {
                failNextCapture = false;
                throw new IOException("模拟断线");
            }
            return new Mat(new Size(64, 48), MatType.CV_8UC3, Scalar.White);
        }
        public void Close() => IsOpen = false;
        public void Dispose() => Close();
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
