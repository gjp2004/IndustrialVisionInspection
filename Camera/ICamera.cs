using OpenCvSharp;

namespace IndustrialVisionStudent.Camera;

public interface ICamera : IDisposable
{
    bool IsOpen { get; }
    bool Open();
    Mat Capture();
    void Close();
}
