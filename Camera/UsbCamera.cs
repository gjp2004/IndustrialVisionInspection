using OpenCvSharp;

namespace IndustrialVisionStudent.Camera;

public sealed class UsbCamera : ICamera
{
    private readonly int deviceIndex;
    private readonly int requestedWidth;
    private readonly int requestedHeight;
    private VideoCapture? capture;

    public UsbCamera(int deviceIndex = 0, int requestedWidth = 1280, int requestedHeight = 720)
    {
        if (deviceIndex < 0) throw new ArgumentOutOfRangeException(nameof(deviceIndex));
        this.deviceIndex = deviceIndex;
        this.requestedWidth = requestedWidth;
        this.requestedHeight = requestedHeight;
    }

    public bool IsOpen => capture?.IsOpened() == true;

    public bool Open()
    {
        Close();
        var newCapture = new VideoCapture(deviceIndex, VideoCaptureAPIs.DSHOW);
        if (!newCapture.IsOpened())
        {
            newCapture.Dispose();
            return false;
        }

        if (requestedWidth > 0) newCapture.Set(VideoCaptureProperties.FrameWidth, requestedWidth);
        if (requestedHeight > 0) newCapture.Set(VideoCaptureProperties.FrameHeight, requestedHeight);
        newCapture.Set(VideoCaptureProperties.BufferSize, 1);
        capture = newCapture;
        return true;
    }

    public Mat Capture()
    {
        if (!IsOpen) throw new InvalidOperationException("摄像头尚未打开。");

        var frame = new Mat();
        if (!capture!.Read(frame) || frame.Empty())
        {
            frame.Dispose();
            throw new InvalidOperationException("摄像头没有返回有效画面。");
        }

        return frame;
    }

    public void Close()
    {
        capture?.Release();
        capture?.Dispose();
        capture = null;
    }

    public void Dispose() => Close();
}
