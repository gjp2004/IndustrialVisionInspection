using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IndustrialVisionStudent.ViewModels;

namespace IndustrialVisionStudent.Tests;

public sealed class UiSmokeTests
{
    [Fact]
    public void MainWindow_CanInitializeMeasureAndCloseOnStaThread()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IVS_Ui_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? requestedPath = Environment.GetEnvironmentVariable("IVS_SCREENSHOT_PATH");
        bool preserveSnapshot = !string.IsNullOrWhiteSpace(requestedPath);
        string snapshotPath = preserveSnapshot
            ? Path.GetFullPath(requestedPath!)
            : Path.Combine(Path.GetTempPath(), "IVS_Snapshot_" + Guid.NewGuid().ToString("N") + ".png");
        Exception? failure = null;
        double actualWidth = 0;
        double actualHeight = 0;
        bool receivedCameraFrame = false;
        int pixelWidth = 0;
        int pixelHeight = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application();
                var window = new MainWindow(directory);
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                actualWidth = window.ActualWidth;
                actualHeight = window.ActualHeight;
                var viewModel = Assert.IsType<MainViewModel>(window.DataContext);
                viewModel.SelectedCameraSource = "模拟摄像头";
                viewModel.StartCameraCommand.Execute(null);
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
                    (_, _) => frame.Continue = false, window.Dispatcher);
                timer.Start();
                Dispatcher.PushFrame(frame);
                timer.Stop();
                receivedCameraFrame = viewModel.DisplayImage is not null;
                viewModel.StopCameraCommand.Execute(null);
                viewModel.LoadSampleCommand.Execute(null);
                viewModel.InspectCommand.Execute(null);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
                pixelWidth = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
                pixelHeight = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
                var bitmap = new RenderTargetBitmap(
                    pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream stream = File.Create(snapshotPath))
                    encoder.Save(stream);
                window.Close();
                application.Shutdown();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF界面初始化超时。");
        try
        {
            Assert.Null(failure);
            Assert.True(actualWidth >= 1050);
            Assert.True(actualHeight >= 680);
            Assert.True(receivedCameraFrame, "模拟摄像头未能把实时帧更新到WPF界面。");
            var file = new FileInfo(snapshotPath);
            Assert.True(file.Exists);
            Assert.True(file.Length > 10_000, $"界面快照文件过小：{file.Length}字节");
            using FileStream stream = File.OpenRead(snapshotPath);
            var decoder = new PngBitmapDecoder(
                stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(pixelWidth, decoder.Frames[0].PixelWidth);
            Assert.Equal(pixelHeight, decoder.Frames[0].PixelHeight);
        }
        finally
        {
            if (!preserveSnapshot && File.Exists(snapshotPath)) File.Delete(snapshotPath);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
