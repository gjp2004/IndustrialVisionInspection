namespace IndustrialVisionStudent.Camera;

public interface ICameraFactory
{
    IReadOnlyList<string> Sources { get; }
    ICamera Create(string source, int deviceIndex);
}
