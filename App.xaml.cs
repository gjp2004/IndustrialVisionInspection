using System.Windows;
using System.Windows.Threading;
using System.IO;
using IndustrialVisionStudent.Models;
using IndustrialVisionStudent.Services;

namespace IndustrialVisionStudent;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        ApplicationLogService.Info($"应用启动，版本{typeof(App).Assembly.GetName().Version}。");
        base.OnStartup(e);

        if (e.Args.Any(x => string.Equals(
                x, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(RunPublishedSelfTest());
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ApplicationLogService.Info($"应用退出，代码{e.ApplicationExitCode}。");
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ApplicationLogService.Error("界面线程发生未处理异常。", e.Exception);
        MessageBox.Show(
            $"程序遇到未处理错误，详细信息已写入日志：\n{ApplicationLogService.LogDirectory}\n\n{e.Exception.Message}",
            "工业视觉上位机", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(-1);
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        ApplicationLogService.Error("后台线程发生未处理异常。", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ApplicationLogService.Error("后台任务发生未观察异常。", e.Exception);
        e.SetObserved();
    }

    private static int RunPublishedSelfTest()
    {
        string dataRoot = Path.Combine(
            Path.GetTempPath(), "IndustrialVisionStudent", "PublishedSelfTest",
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            var history = new InspectionHistoryService(
                Path.Combine(dataRoot, "Data", "inspection.db"));
            history.Initialize();
            var diagnostics = new SystemDiagnosticsService(
                dataRoot,
                Path.Combine(AppContext.BaseDirectory, "recipes", "默认圆形零件.json"),
                history);
            SystemDiagnosticReport report = diagnostics.Run();
            ApplicationLogService.Info(
                $"发布包自检{(report.Passed ? "通过" : "失败")}。" +
                $"{Environment.NewLine}{report.ToDisplayText()}");
            return report.Passed ? 0 : 2;
        }
        catch (Exception exception)
        {
            ApplicationLogService.Error("发布包自检发生异常。", exception);
            return 3;
        }
        finally
        {
            try
            {
                if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, true);
            }
            catch (Exception exception)
            {
                ApplicationLogService.Error("发布包自检临时目录清理失败。", exception);
            }
        }
    }
}
