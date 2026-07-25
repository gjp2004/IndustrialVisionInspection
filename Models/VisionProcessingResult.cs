using OpenCvSharp;

namespace IndustrialVisionStudent.Models;

public sealed class VisionProcessingResult : IDisposable
{
    public VisionProcessingResult(InspectionResult result, Mat gray, Mat binary, Mat annotated)
    {
        Result = result;
        Gray = gray;
        Binary = binary;
        Annotated = annotated;
    }

    public InspectionResult Result { get; }
    public Mat Gray { get; }
    public Mat Binary { get; }
    public Mat Annotated { get; }

    public void Dispose()
    {
        Gray.Dispose();
        Binary.Dispose();
        Annotated.Dispose();
    }
}
