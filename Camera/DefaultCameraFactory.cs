namespace IndustrialVisionStudent.Camera;

public sealed class DefaultCameraFactory : ICameraFactory
{
    public const string SimulatedSource = "模拟摄像头";
    public const string UsbSource = "USB摄像头";

    private static readonly string[] SupportedSources =
    {
        SimulatedSource,
        UsbSource
    };

    public IReadOnlyList<string> Sources => SupportedSources;

    public ICamera Create(string source, int deviceIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return source switch
        {
            SimulatedSource => new SimulatedCamera(),
            UsbSource when deviceIndex >= 0 => new UsbCamera(deviceIndex),
            UsbSource => throw new ArgumentOutOfRangeException(
                nameof(deviceIndex), "USB摄像头设备编号不能为负数。"),
            _ => throw new NotSupportedException($"不支持的摄像头来源：{source}")
        };
    }
}
