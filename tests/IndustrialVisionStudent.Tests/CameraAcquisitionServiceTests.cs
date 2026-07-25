using IndustrialVisionStudent.Camera;
using IndustrialVisionStudent.Services;
using OpenCvSharp;

namespace IndustrialVisionStudent.Tests;

public sealed class CameraAcquisitionServiceTests
{
    [Fact]
    public async Task StartCaptureAndStop_ManageLifecycleAndFrame()
    {
        using var camera = new FakeCamera();
        using var service = new CameraAcquisitionService(camera, reconnectDelay: TimeSpan.FromMilliseconds(10));
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.FrameReceived += _ => received.TrySetResult();

        Assert.True(service.Start());
        await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using Mat? latest = service.GetLatestFrame();
        Assert.NotNull(latest);
        Assert.False(latest.Empty());
        service.Stop();
        Assert.False(service.IsRunning);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public void RepeatedStart_DoesNotCreateSecondLoop()
    {
        using var camera = new FakeCamera();
        using var service = new CameraAcquisitionService(camera);
        Assert.True(service.Start());
        Assert.True(service.Start());
        Assert.Equal(1, camera.OpenCount);
        service.Stop();
    }

    [Fact]
    public void UsbCamera_InvalidDeviceIndex_FailsGracefully()
    {
        using var camera = new UsbCamera(99);
        Assert.False(camera.Open());
        Assert.False(camera.IsOpen);
        camera.Close();
    }

    private sealed class FakeCamera : ICamera
    {
        public bool IsOpen { get; private set; }
        public int OpenCount { get; private set; }
        public bool Open() { OpenCount++; IsOpen = true; return true; }
        public Mat Capture()
        {
            if (!IsOpen) throw new InvalidOperationException();
            return new Mat(new Size(64, 48), MatType.CV_8UC3, Scalar.White);
        }
        public void Close() => IsOpen = false;
        public void Dispose() => Close();
    }
}
