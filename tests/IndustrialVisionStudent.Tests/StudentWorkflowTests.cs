using IndustrialVisionStudent.Camera;
using IndustrialVisionStudent.Services;
using IndustrialVisionStudent.ViewModels;

namespace IndustrialVisionStudent.Tests;

public sealed class StudentWorkflowTests
{
    [Fact]
    public void BuiltInSample_DetectsOkAndCreatesHistoryRecord()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IVS_Workflow_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Exception? failure = null;
        string? resultText = null;
        int historyCount = 0;
        var thread = new Thread(() =>
        {
            try
            {
                using var viewModel = new MainViewModel(directory);
                viewModel.LoadSampleCommand.Execute(null);
                viewModel.InspectCommand.Execute(null);
                resultText = viewModel.ResultText;
                historyCount = viewModel.HistoryRecords.Count;
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "完整演示工作流执行超时。");
        try
        {
            Assert.Null(failure);
            Assert.Equal("OK", resultText);
            Assert.Equal(1, historyCount);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SimulatedCamera_ProducesFramesThroughAcquisitionService()
    {
        using var camera = new SimulatedCamera();
        using var service = new CameraAcquisitionService(camera);
        var frames = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int received = 0;
        service.FrameReceived += _ =>
        {
            if (Interlocked.Increment(ref received) >= 3) frames.TrySetResult();
        };
        Assert.True(service.Start());
        await frames.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(service.IsRunning);
        Assert.True(received >= 3);
    }

    [Fact]
    public void AutomaticMode_LocksConfiguration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IVS_Lock_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var viewModel = new MainViewModel(directory);
            Assert.True(viewModel.IsConfigurationEditable);

            viewModel.IsAutomaticMode = true;

            Assert.False(viewModel.IsConfigurationEditable);
            Assert.Contains("参数已锁定", viewModel.StatusText);

            viewModel.IsAutomaticMode = false;
            Assert.True(viewModel.IsConfigurationEditable);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
