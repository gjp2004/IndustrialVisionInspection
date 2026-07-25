using IndustrialVisionStudent.Camera;
using OpenCvSharp;

namespace IndustrialVisionStudent.Services;

public sealed class CameraAcquisitionService : IDisposable
{
    private readonly ICamera camera;
    private readonly object lifecycleLock = new();
    private readonly object frameLock = new();
    private readonly int maximumReconnectAttempts;
    private readonly TimeSpan reconnectDelay;
    private CancellationTokenSource? cancellation;
    private Task? captureTask;
    private Mat? latestFrame;
    private bool disposed;
    private volatile bool running;

    public CameraAcquisitionService(
        ICamera camera,
        int maximumReconnectAttempts = 3,
        TimeSpan? reconnectDelay = null)
    {
        this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
        if (maximumReconnectAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumReconnectAttempts));
        this.maximumReconnectAttempts = maximumReconnectAttempts;
        this.reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(1);
    }

    public event Action<Mat>? FrameReceived;
    public event Action<int, int>? Reconnecting;
    public event Action<int>? Reconnected;
    public event Action<Exception>? CaptureFailed;
    public event Action? CaptureStopped;

    public bool IsRunning => running;
    public bool IsConnected => camera.IsOpen;

    public bool Start()
    {
        lock (lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (running) return true;
            if (!camera.Open()) return false;

            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            running = true;
            captureTask = Task.Run(() => CaptureLoopAsync(cancellation.Token));
            return true;
        }
    }

    public Mat? GetLatestFrame()
    {
        lock (frameLock)
            return latestFrame?.Clone();
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using Mat frame = camera.Capture();
                    lock (frameLock)
                    {
                        latestFrame?.Dispose();
                        latestFrame = frame.Clone();
                    }

                    // 事件中的 Mat 只在回调执行期间有效，订阅者需要立即转换或自行 Clone。
                    using Mat eventFrame = frame.Clone();
                    FrameReceived?.Invoke(eventFrame);
                    await Task.Delay(33, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    if (!await TryReconnectAsync(exception, token)) break;
                }
            }
        }
        finally
        {
            running = false;
            CaptureStopped?.Invoke();
        }
    }

    private async Task<bool> TryReconnectAsync(Exception initialError, CancellationToken token)
    {
        camera.Close();
        Exception lastError = initialError;

        for (int attempt = 1; attempt <= maximumReconnectAttempts; attempt++)
        {
            Reconnecting?.Invoke(attempt, maximumReconnectAttempts);
            await Task.Delay(reconnectDelay, token);
            try
            {
                if (camera.Open())
                {
                    Reconnected?.Invoke(attempt);
                    return true;
                }
                lastError = new InvalidOperationException($"第{attempt}次重连时摄像头拒绝打开。");
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
        }

        CaptureFailed?.Invoke(new InvalidOperationException(
            $"摄像头重连{maximumReconnectAttempts}次后仍未恢复。", lastError));
        return false;
    }

    public void Stop()
    {
        Task? task;
        lock (lifecycleLock)
        {
            cancellation?.Cancel();
            task = captureTask;
        }

        if (task is not null && Task.CurrentId != task.Id)
        {
            try { task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
        }

        lock (lifecycleLock)
        {
            captureTask = null;
            cancellation?.Dispose();
            cancellation = null;
            running = false;
            camera.Close();
        }

        lock (frameLock)
        {
            latestFrame?.Dispose();
            latestFrame = null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        camera.Dispose();
        disposed = true;
    }
}
