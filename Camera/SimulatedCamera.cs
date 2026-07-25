using OpenCvSharp;

namespace IndustrialVisionStudent.Camera;

public sealed class SimulatedCamera : ICamera
{
    private readonly object stateLock = new();
    private int frameNumber;
    private bool isOpen;

    public bool IsOpen { get { lock (stateLock) return isOpen; } }

    public bool Open()
    {
        lock (stateLock)
        {
            isOpen = true;
            frameNumber = 0;
            return true;
        }
    }

    public Mat Capture()
    {
        int currentFrame;
        lock (stateLock)
        {
            if (!isOpen) throw new InvalidOperationException("模拟摄像头尚未打开。");
            currentFrame = frameNumber++;
        }

        var image = new Mat(new Size(640, 480), MatType.CV_8UC3, Scalar.White);
        int centerX = 320 + (int)Math.Round(Math.Sin(currentFrame * 0.04) * 80);
        Cv2.Circle(image, new Point(centerX, 240), 50, Scalar.Black, -1);
        Cv2.Circle(image, new Point(centerX, 240), 18, Scalar.White, -1);
        Cv2.PutText(image, $"SIM {currentFrame}", new Point(15, 30),
            HersheyFonts.HersheySimplex, 0.7, new Scalar(70, 70, 70), 2);
        return image;
    }

    public void Close() { lock (stateLock) isOpen = false; }
    public void Dispose() => Close();
}
