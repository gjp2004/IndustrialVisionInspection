using IndustrialVisionStudent.Camera;

namespace IndustrialVisionStudent.Tests;

public sealed class CameraFactoryTests
{
    private readonly DefaultCameraFactory factory = new();

    [Fact]
    public void Sources_ExposeSimulatedAndUsbOptions()
    {
        Assert.Contains(DefaultCameraFactory.SimulatedSource, factory.Sources);
        Assert.Contains(DefaultCameraFactory.UsbSource, factory.Sources);
    }

    [Fact]
    public void CreateSimulated_ReturnsSimulatedCamera()
    {
        using ICamera camera = factory.Create(DefaultCameraFactory.SimulatedSource, 0);
        Assert.IsType<SimulatedCamera>(camera);
    }

    [Fact]
    public void CreateUsb_ReturnsUsbCameraWithoutOpeningHardware()
    {
        using ICamera camera = factory.Create(DefaultCameraFactory.UsbSource, 0);
        Assert.IsType<UsbCamera>(camera);
        Assert.False(camera.IsOpen);
    }

    [Fact]
    public void CreateUsb_WithNegativeIndex_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            factory.Create(DefaultCameraFactory.UsbSource, -1));
    }

    [Fact]
    public void CreateUnknownSource_IsRejected()
    {
        Assert.Throws<NotSupportedException>(() =>
            factory.Create("未知工业相机", 0));
    }
}
