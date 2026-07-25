using System.Globalization;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndustrialVisionStudent.Commands;
using IndustrialVisionStudent.Camera;
using IndustrialVisionStudent.Communication;
using IndustrialVisionStudent.Models;
using IndustrialVisionStudent.Services;
using IndustrialVisionStudent.Utils;
using IndustrialVisionStudent.Vision;
using Microsoft.Win32;
using OpenCvSharp;

namespace IndustrialVisionStudent.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private Mat? sourceImage;
    private VisionProcessingResult? processingResult;
    private CameraAcquisitionService? acquisitionService;
    private BitmapImage? displayImage;
    private VisionDebugView selectedDebugView = VisionDebugView.标注图;
    private string resultText = "等待检测";
    private string judgementReason = "请先选择图片。";
    private string measurementSummary = "暂无测量数据";
    private string statusText = "就绪";
    private Brush resultBrush = Brushes.SlateGray;
    private readonly VisionRecipeService recipeService = new();
    private readonly InspectionHistoryService historyService;
    private readonly NgImageStorageService ngImageStorageService;
    private readonly SystemDiagnosticsService diagnosticsService;
    private readonly ICameraFactory cameraFactory;
    private readonly AuditLogService auditLog;
    private bool isRoiEnabled;
    private string roiXText = "0";
    private string roiYText = "0";
    private string roiWidthText = "100";
    private string roiHeightText = "100";
    private string thresholdText = "110";
    private string minimumContourAreaText = "300";
    private string minimumCircularityText = "0.65";
    private string expectedCountText = "1";
    private string minimumOkAreaText = "1000";
    private string maximumOkAreaText = "100000";
    private string minimumWidthText = "0";
    private string maximumWidthText = "100000";
    private string minimumHeightText = "0";
    private string maximumHeightText = "100000";
    private string minimumAspectRatioText = "0";
    private string maximumAspectRatioText = "100000";
    private string expectedCenterXText = "-1";
    private string expectedCenterYText = "-1";
    private string centerToleranceText = "0";
    private bool isDarkObject = true;
    private bool useAdaptiveThreshold;
    private string adaptiveBlockSizeText = "31";
    private string adaptiveConstantText = "5";
    private string pixelSizeMmText = "0";
    private string recipeName = "默认配方";
    private string recipeVersion = "1.0";
    private string productModel = "圆形零件-A";
    private string operatorName = "演示操作员";
    private string batchNumber = "BATCH-001";
    private string historyResultFilter = "全部";
    private string historyBatchFilter = string.Empty;
    private string historyProductFilter = string.Empty;
    private string historySummary = "暂无历史数据";
    private DateTime? historyStartDate;
    private DateTime? historyEndDate;
    private InspectionRecord? selectedHistoryRecord;
    private readonly SimulatedPlcServer plcServer = new();
    private readonly IPlcClient plcClient = new TcpPlcClient();
    private readonly PlcHeartbeatMonitor heartbeatMonitor;
    private readonly SemaphoreSlim plcCycleLock = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private string plcPortText = "1502";
    private string plcStatus = "PLC：未启动";
    private string plcLog = string.Empty;
    private bool isAutomaticMode;
    private int nextCycleId = 1000;
    private bool? lastInspectionOk;
    private CancellationTokenSource? plcReconnectCancellation;
    private bool allowPlcAutoReconnect;
    private bool isPlcConnecting;
    private int frameUpdatePending;
    private string cameraIndexText = "0";
    private string selectedCameraSource = DefaultCameraFactory.SimulatedSource;
    private string? activePlcCycleId;

    public MainViewModel(
        string? dataRootOverride = null,
        ICameraFactory? cameraFactory = null)
    {
        this.cameraFactory = cameraFactory ?? new DefaultCameraFactory();
        string dataRoot = dataRootOverride ?? ResolveDefaultDataRoot();
        historyService = new InspectionHistoryService(Path.Combine(dataRoot, "Data", "inspection.db"));
        ngImageStorageService = new NgImageStorageService(Path.Combine(dataRoot, "NGImages"));
        auditLog = new AuditLogService(Path.Combine(dataRoot, "Logs", "Audit"));
        historyService.Initialize();
        diagnosticsService = new SystemDiagnosticsService(
            dataRoot,
            Path.Combine(AppContext.BaseDirectory, "recipes", "默认圆形零件.json"),
            historyService);
        heartbeatMonitor = new PlcHeartbeatMonitor(plcClient);
        plcServer.LogReceived += AppendPlcLog;
        plcClient.StartRequested += OnPlcStartRequested;
        plcClient.ConnectionLost += OnPlcConnectionLost;
        heartbeatMonitor.Succeeded += elapsed => UpdatePlcStatus($"PLC：在线，心跳{elapsed.TotalMilliseconds:F0}ms");
        heartbeatMonitor.Failed += exception =>
        {
            UpdatePlcStatus($"PLC心跳失败：{exception.Message}");
            BeginPlcReconnect();
        };
        LoadImageCommand = new RelayCommand(LoadImage);
        LoadSampleCommand = new RelayCommand(LoadSampleImage);
        StartCameraCommand = new RelayCommand(StartCamera, () => acquisitionService?.IsRunning != true);
        StopCameraCommand = new RelayCommand(StopCamera, () => acquisitionService?.IsRunning == true);
        InspectCommand = new RelayCommand(Inspect, HasInspectionImage);
        SaveRecipeCommand = new RelayCommand(SaveRecipe);
        LoadRecipeCommand = new RelayCommand(LoadRecipe);
        RefreshHistoryCommand = new RelayCommand(RefreshHistory);
        ExportHistoryCommand = new RelayCommand(ExportHistory, () => HistoryRecords.Count > 0);
        StartPlcServerCommand = new RelayCommand(StartPlcServer, () => !plcServer.IsRunning);
        StopPlcServerCommand = new RelayCommand(StopPlcServer, () => plcServer.IsRunning);
        ConnectPlcCommand = new RelayCommand(ConnectPlc, () => !plcClient.IsConnected && !isPlcConnecting);
        DisconnectPlcCommand = new RelayCommand(DisconnectPlc, () => plcClient.IsConnected);
        PingPlcCommand = new RelayCommand(PingPlc, () => plcClient.IsConnected);
        TriggerPlcCommand = new RelayCommand(TriggerPlc, () => plcServer.HasClient);
        OpenNgImageCommand = new RelayCommand(OpenNgImage,
            () => !string.IsNullOrWhiteSpace(SelectedHistoryRecord?.NgImagePath));
        CleanupNgImagesCommand = new RelayCommand(CleanupNgImages);
        RunDiagnosticsCommand = new RelayCommand(RunDiagnostics);
        RefreshHistory();
    }

    private static string ResolveDefaultDataRoot()
    {
        string preferred = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IndustrialVisionStudent");
        try
        {
            Directory.CreateDirectory(preferred);
            return preferred;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            string fallback = Path.Combine(
                Path.GetTempPath(), "IndustrialVisionStudent", Environment.UserName);
            Directory.CreateDirectory(fallback);
            ApplicationLogService.Error(
                $"无法使用默认数据目录“{preferred}”，已切换到临时目录“{fallback}”。", exception);
            return fallback;
        }
    }

    public RelayCommand LoadImageCommand { get; }
    public RelayCommand LoadSampleCommand { get; }
    public RelayCommand StartCameraCommand { get; }
    public RelayCommand StopCameraCommand { get; }
    public RelayCommand InspectCommand { get; }
    public RelayCommand SaveRecipeCommand { get; }
    public RelayCommand LoadRecipeCommand { get; }
    public RelayCommand RefreshHistoryCommand { get; }
    public RelayCommand ExportHistoryCommand { get; }
    public RelayCommand StartPlcServerCommand { get; }
    public RelayCommand StopPlcServerCommand { get; }
    public RelayCommand ConnectPlcCommand { get; }
    public RelayCommand DisconnectPlcCommand { get; }
    public RelayCommand PingPlcCommand { get; }
    public RelayCommand TriggerPlcCommand { get; }
    public RelayCommand OpenNgImageCommand { get; }
    public RelayCommand CleanupNgImagesCommand { get; }
    public RelayCommand RunDiagnosticsCommand { get; }
    public ObservableCollection<InspectionRecord> HistoryRecords { get; } = new();
    public string[] HistoryResultFilters { get; } = { "全部", "OK", "NG" };
    public Array DebugViews { get; } = Enum.GetValues(typeof(VisionDebugView));
    public IReadOnlyList<string> CameraSources => cameraFactory.Sources;

    public string CameraIndexText { get => cameraIndexText; set => SetProperty(ref cameraIndexText, value); }
    public string SelectedCameraSource
    {
        get => selectedCameraSource;
        set => SetProperty(ref selectedCameraSource, value);
    }
    public string ThresholdText { get => thresholdText; set => SetProperty(ref thresholdText, value); }
    public string MinimumContourAreaText { get => minimumContourAreaText; set => SetProperty(ref minimumContourAreaText, value); }
    public string MinimumCircularityText { get => minimumCircularityText; set => SetProperty(ref minimumCircularityText, value); }
    public string ExpectedCountText { get => expectedCountText; set => SetProperty(ref expectedCountText, value); }
    public string MinimumOkAreaText { get => minimumOkAreaText; set => SetProperty(ref minimumOkAreaText, value); }
    public string MaximumOkAreaText { get => maximumOkAreaText; set => SetProperty(ref maximumOkAreaText, value); }
    public string MinimumWidthText { get => minimumWidthText; set => SetProperty(ref minimumWidthText, value); }
    public string MaximumWidthText { get => maximumWidthText; set => SetProperty(ref maximumWidthText, value); }
    public string MinimumHeightText { get => minimumHeightText; set => SetProperty(ref minimumHeightText, value); }
    public string MaximumHeightText { get => maximumHeightText; set => SetProperty(ref maximumHeightText, value); }
    public string MinimumAspectRatioText { get => minimumAspectRatioText; set => SetProperty(ref minimumAspectRatioText, value); }
    public string MaximumAspectRatioText { get => maximumAspectRatioText; set => SetProperty(ref maximumAspectRatioText, value); }
    public string ExpectedCenterXText { get => expectedCenterXText; set => SetProperty(ref expectedCenterXText, value); }
    public string ExpectedCenterYText { get => expectedCenterYText; set => SetProperty(ref expectedCenterYText, value); }
    public string CenterToleranceText { get => centerToleranceText; set => SetProperty(ref centerToleranceText, value); }
    public bool IsDarkObject { get => isDarkObject; set => SetProperty(ref isDarkObject, value); }
    public bool UseAdaptiveThreshold { get => useAdaptiveThreshold; set => SetProperty(ref useAdaptiveThreshold, value); }
    public string AdaptiveBlockSizeText { get => adaptiveBlockSizeText; set => SetProperty(ref adaptiveBlockSizeText, value); }
    public string AdaptiveConstantText { get => adaptiveConstantText; set => SetProperty(ref adaptiveConstantText, value); }
    public string PixelSizeMmText { get => pixelSizeMmText; set => SetProperty(ref pixelSizeMmText, value); }
    public string RecipeName { get => recipeName; set => SetProperty(ref recipeName, value); }
    public string RecipeVersion { get => recipeVersion; set => SetProperty(ref recipeVersion, value); }
    public string ProductModel { get => productModel; set => SetProperty(ref productModel, value); }
    public string OperatorName { get => operatorName; set => SetProperty(ref operatorName, value); }
    public bool IsRoiEnabled { get => isRoiEnabled; set => SetProperty(ref isRoiEnabled, value); }
    public string RoiXText { get => roiXText; set => SetProperty(ref roiXText, value); }
    public string RoiYText { get => roiYText; set => SetProperty(ref roiYText, value); }
    public string RoiWidthText { get => roiWidthText; set => SetProperty(ref roiWidthText, value); }
    public string RoiHeightText { get => roiHeightText; set => SetProperty(ref roiHeightText, value); }
    public string BatchNumber { get => batchNumber; set => SetProperty(ref batchNumber, value); }
    public string HistoryResultFilter
    {
        get => historyResultFilter;
        set { if (SetProperty(ref historyResultFilter, value)) RefreshHistory(); }
    }
    public string HistoryBatchFilter { get => historyBatchFilter; set => SetProperty(ref historyBatchFilter, value); }
    public string HistoryProductFilter { get => historyProductFilter; set => SetProperty(ref historyProductFilter, value); }
    public string HistorySummary { get => historySummary; private set => SetProperty(ref historySummary, value); }
    public DateTime? HistoryStartDate { get => historyStartDate; set => SetProperty(ref historyStartDate, value); }
    public DateTime? HistoryEndDate { get => historyEndDate; set => SetProperty(ref historyEndDate, value); }
    public InspectionRecord? SelectedHistoryRecord
    {
        get => selectedHistoryRecord;
        set
        {
            if (SetProperty(ref selectedHistoryRecord, value)) OpenNgImageCommand.RaiseCanExecuteChanged();
        }
    }
    public string PlcPortText { get => plcPortText; set => SetProperty(ref plcPortText, value); }
    public string PlcStatus { get => plcStatus; private set => SetProperty(ref plcStatus, value); }
    public string PlcLog { get => plcLog; private set => SetProperty(ref plcLog, value); }
    public bool IsAutomaticMode
    {
        get => isAutomaticMode;
        set
        {
            if (SetProperty(ref isAutomaticMode, value))
            {
                OnPropertyChanged(nameof(IsConfigurationEditable));
                StatusText = value
                    ? "已进入自动模式，检测参数已锁定。"
                    : "已退出自动模式，可以修改检测参数。";
            }
        }
    }
    public bool IsConfigurationEditable => !IsAutomaticMode;

    public BitmapImage? DisplayImage
    {
        get => displayImage;
        private set
        {
            if (SetProperty(ref displayImage, value))
                OnPropertyChanged(nameof(EmptyHintVisibility));
        }
    }

    public Visibility EmptyHintVisibility => DisplayImage is null ? Visibility.Visible : Visibility.Collapsed;

    public VisionDebugView SelectedDebugView
    {
        get => selectedDebugView;
        set
        {
            if (SetProperty(ref selectedDebugView, value)) RefreshDisplayImage();
        }
    }

    public string ResultText { get => resultText; private set => SetProperty(ref resultText, value); }
    public string JudgementReason { get => judgementReason; private set => SetProperty(ref judgementReason, value); }
    public string MeasurementSummary { get => measurementSummary; private set => SetProperty(ref measurementSummary, value); }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public Brush ResultBrush { get => resultBrush; private set => SetProperty(ref resultBrush, value); }

    private void LoadImage()
    {
        StopCamera();
        var dialog = new OpenFileDialog
        {
            Title = "选择待检测图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        Mat loaded = Cv2.ImRead(dialog.FileName, ImreadModes.Color);
        if (loaded.Empty())
        {
            loaded.Dispose();
            StatusText = "图片读取失败。";
            return;
        }

        processingResult?.Dispose();
        processingResult = null;
        sourceImage?.Dispose();
        sourceImage = loaded;
        DisplayImage = BitmapConverter.ToBitmapImage(sourceImage);
        ResultText = "等待检测";
        ResultBrush = Brushes.SlateGray;
        JudgementReason = "图片已加载，请调整参数后执行检测。";
        MeasurementSummary = $"图像尺寸：{sourceImage.Width} × {sourceImage.Height}px";
        StatusText = $"已加载：{dialog.SafeFileName}";
        InspectCommand.RaiseCanExecuteChanged();
    }

    private void LoadSampleImage()
    {
        StopCamera();
        processingResult?.Dispose(); processingResult = null;
        sourceImage?.Dispose();
        sourceImage = SampleImageFactory.CreateStandardWasher();
        DisplayImage = BitmapConverter.ToBitmapImage(sourceImage);
        ResultText = "等待检测"; ResultBrush = Brushes.SlateGray;
        JudgementReason = "已生成标准垫片示例图，可直接执行检测。";
        MeasurementSummary = "图像尺寸：640 × 480px";
        StatusText = "标准示例图已加载。";
        InspectCommand.RaiseCanExecuteChanged();
    }

    private void StartCamera()
    {
        if (!int.TryParse(CameraIndexText, out int cameraIndex) || cameraIndex < 0)
        {
            StatusText = "摄像头编号必须是大于或等于0的整数。";
            return;
        }

        StopCamera();
        sourceImage?.Dispose();
        sourceImage = null;
        processingResult?.Dispose();
        processingResult = null;

        ICamera camera;
        try
        {
            camera = cameraFactory.Create(SelectedCameraSource, cameraIndex);
        }
        catch (Exception exception)
        {
            StatusText = $"创建摄像头失败：{exception.Message}";
            return;
        }
        acquisitionService = new CameraAcquisitionService(camera);
        acquisitionService.FrameReceived += OnCameraFrameReceived;
        acquisitionService.Reconnecting += OnCameraReconnecting;
        acquisitionService.Reconnected += OnCameraReconnected;
        acquisitionService.CaptureFailed += OnCameraFailed;
        acquisitionService.CaptureStopped += OnCameraStopped;

        try
        {
            if (!acquisitionService.Start())
            {
                StatusText = $"无法打开编号为{cameraIndex}的摄像头。";
                ReleaseCameraService();
                return;
            }

            ResultText = "实时画面";
            ResultBrush = Brushes.DodgerBlue;
            JudgementReason = "摄像头已打开，可对当前最新帧执行检测。";
            StatusText = SelectedCameraSource == DefaultCameraFactory.UsbSource
                ? $"USB摄像头{cameraIndex}采集中。"
                : "模拟摄像头采集中。";
        }
        catch (Exception exception)
        {
            StatusText = $"打开摄像头失败：{exception.Message}";
            ReleaseCameraService();
        }

        RefreshCommands();
    }

    private void StopCamera()
    {
        if (acquisitionService is null) return;
        ReleaseCameraService();
        StatusText = "摄像头已停止。";
        RefreshCommands();
    }

    private void OnCameraFrameReceived(Mat frame)
    {
        if (Interlocked.Exchange(ref frameUpdatePending, 1) == 1) return;
        BitmapImage bitmap;
        try { bitmap = BitmapConverter.ToBitmapImage(frame); }
        catch { Interlocked.Exchange(ref frameUpdatePending, 0); return; }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (acquisitionService?.IsRunning == true) DisplayImage = bitmap;
            }
            finally { Interlocked.Exchange(ref frameUpdatePending, 0); }
        });
    }

    private void OnCameraReconnecting(int attempt, int maximum) =>
        UpdateStatusOnUiThread($"摄像头断线，正在进行第{attempt}/{maximum}次重连……");

    private void OnCameraReconnected(int attempt) =>
        UpdateStatusOnUiThread($"摄像头已在第{attempt}次尝试后恢复。 ");

    private void OnCameraFailed(Exception exception) =>
        UpdateStatusOnUiThread($"摄像头采集失败：{exception.Message}");

    private void OnCameraStopped() => Application.Current.Dispatcher.BeginInvoke(RefreshCommands);

    private void UpdateStatusOnUiThread(string text) =>
        Application.Current.Dispatcher.BeginInvoke(() => StatusText = text);

    private void ReleaseCameraService()
    {
        CameraAcquisitionService? service = acquisitionService;
        acquisitionService = null;
        if (service is null) return;
        service.FrameReceived -= OnCameraFrameReceived;
        service.Reconnecting -= OnCameraReconnecting;
        service.Reconnected -= OnCameraReconnected;
        service.CaptureFailed -= OnCameraFailed;
        service.CaptureStopped -= OnCameraStopped;
        service.Dispose();
    }

    private void Inspect()
    {
        using Mat? cameraFrame = acquisitionService?.GetLatestFrame();
        Mat? inspectionImage = cameraFrame ?? sourceImage;
        if (inspectionImage is null) return;

        try
        {
            VisionParameters parameters = ReadParameters();
            processingResult?.Dispose();
            processingResult = VisionProcessor.Process(inspectionImage, parameters);
            InspectionResult result = processingResult.Result;
            lastInspectionOk = result.IsOk;
            ResultText = result.IsOk ? "OK" : "NG";
            ResultBrush = result.IsOk ? Brushes.SeaGreen : Brushes.Crimson;
            JudgementReason = $"{result.JudgementCode}：{result.JudgementReason}";
            MeasurementSummary =
                $"有效目标：{result.TargetCount}\n" +
                $"最大面积：{result.MaximumArea:F1}px²\n" +
                $"圆度：{result.Circularity:F3}\n" +
                $"长宽比：{result.AspectRatio:F3}\n" +
                $"中心：({result.CenterX}, {result.CenterY})px\n" +
                $"中心偏移：{result.CenterOffset:F1}px\n" +
                $"外接尺寸：{result.Width} × {result.Height}px\n" +
                (result.PixelSizeMm > 0
                    ? $"标定测量：{result.WidthMm:F3} × {result.HeightMm:F3}mm，" +
                      $"面积{result.AreaMm2:F3}mm²\n"
                    : "标定测量：未启用（像素当量为0）\n") +
                $"处理耗时：{result.ProcessingTimeMs:F2}ms";
            StatusText = "检测完成。";
            SaveInspectionHistory(result, processingResult.Annotated);
            RefreshDisplayImage();
        }
        catch (Exception exception)
        {
            ResultText = "错误";
            ResultBrush = Brushes.DarkOrange;
            JudgementReason = exception.Message;
            StatusText = "检测未执行，请检查参数。";
        }
    }

    private bool TryReadPlcPort(out int port)
    {
        if (int.TryParse(PlcPortText, out port) && port is >= 1 and <= 65535) return true;
        PlcStatus = "PLC端口必须在1～65535之间。";
        return false;
    }

    private void StartPlcServer()
    {
        if (!TryReadPlcPort(out int port)) return;
        try { plcServer.Start(port); PlcStatus = $"PLC模拟器：127.0.0.1:{port}"; }
        catch (Exception exception) { PlcStatus = $"启动PLC模拟器失败：{exception.Message}"; }
        RefreshPlcCommands();
    }

    private void StopPlcServer()
    {
        DisconnectPlc();
        plcServer.Stop(); PlcStatus = "PLC：未启动"; RefreshPlcCommands();
    }

    private async void ConnectPlc()
    {
        if (!TryReadPlcPort(out int port)) return;
        isPlcConnecting = true;
        allowPlcAutoReconnect = true;
        plcReconnectCancellation?.Cancel();
        RefreshPlcCommands();
        try
        {
            await plcClient.ConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(3));
            heartbeatMonitor.Start(TimeSpan.FromSeconds(2));
            PlcStatus = "PLC：已连接";
        }
        catch (Exception exception) { PlcStatus = $"PLC连接失败：{exception.Message}"; }
        finally { isPlcConnecting = false; }
        RefreshPlcCommands();
    }

    private async void DisconnectPlc()
    {
        allowPlcAutoReconnect = false;
        plcReconnectCancellation?.Cancel();
        heartbeatMonitor.Stop();
        await plcClient.DisconnectAsync();
        PlcStatus = plcServer.IsRunning ? "PLC模拟器已启动，客户端未连接" : "PLC：未启动";
        RefreshPlcCommands();
    }

    private void OnPlcConnectionLost(Exception exception)
    {
        UpdatePlcStatus($"PLC连接断开：{exception.Message}");
        BeginPlcReconnect();
    }

    private void BeginPlcReconnect()
    {
        if (!allowPlcAutoReconnect || !TryReadPlcPort(out int port)) return;
        CancellationTokenSource? previous = Interlocked.Exchange(
            ref plcReconnectCancellation, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();
        CancellationToken token = plcReconnectCancellation.Token;

        _ = Task.Run(async () =>
        {
            for (int attempt = 1; attempt <= 4 && !token.IsCancellationRequested; attempt++)
            {
                try
                {
                    UpdatePlcStatus($"PLC正在自动重连：第{attempt}/4次");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(4, attempt)), token);
                    await plcClient.ConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(2), token);
                    heartbeatMonitor.Start(TimeSpan.FromSeconds(2));
                    UpdatePlcStatus($"PLC已在第{attempt}次尝试后重新连接");
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    if (attempt == 4) UpdatePlcStatus($"PLC自动重连失败：{exception.Message}");
                }
            }
        }, token);
    }

    private async void PingPlc()
    {
        try
        {
            string response = await plcClient.SendRequestAsync("PING", TimeSpan.FromSeconds(2));
            PlcStatus = response == "PONG" ? "PLC：PING正常" : $"PLC响应异常：{response}";
        }
        catch (Exception exception) { PlcStatus = $"PING失败：{exception.Message}"; }
    }

    private async void TriggerPlc()
    {
        try
        {
            await plcServer.TriggerInspectionAsync(Interlocked.Increment(ref nextCycleId).ToString());
            PlcStatus = "PLC已发送检测请求。";
        }
        catch (Exception exception) { PlcStatus = $"PLC触发失败：{exception.Message}"; }
    }

    private void OnPlcStartRequested(string cycleId)
    {
        _ = Task.Run(async () =>
        {
            bool lockAcquired = false;
            try
            {
                await plcCycleLock.WaitAsync(lifetimeCancellation.Token);
                lockAcquired = true;
                bool automaticMode = await Application.Current.Dispatcher.InvokeAsync(() => IsAutomaticMode);
                if (!automaticMode)
                {
                    AppendPlcLog($"忽略START {cycleId}：当前不是自动模式");
                    return;
                }
                string busy = await plcClient.SendRequestAsync(
                    $"BUSY {cycleId}", TimeSpan.FromSeconds(2), lifetimeCancellation.Token);
                if (busy != $"ACK BUSY {cycleId}") throw new IOException($"BUSY握手异常：{busy}");
                lastInspectionOk = null;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    activePlcCycleId = cycleId;
                    try { Inspect(); }
                    finally { activePlcCycleId = null; }
                });
                string result = lastInspectionOk == true ? "OK" : "NG";
                string ack = await plcClient.SendRequestAsync(
                    $"RESULT {cycleId} {result}", TimeSpan.FromSeconds(2), lifetimeCancellation.Token);
                if (ack != $"ACK RESULT {cycleId}") throw new IOException($"RESULT握手异常：{ack}");
                auditLog.Record(
                    "PLC_CYCLE", "SUCCESS",
                    $"cycle={cycleId}; result={result}");
                UpdatePlcStatus($"周期{cycleId}完成：{result}");
            }
            catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                auditLog.Record(
                    "PLC_CYCLE", "FAILED",
                    $"cycle={cycleId}; error={exception.Message}");
                UpdatePlcStatus($"周期{cycleId}失败：{exception.Message}");
            }
            finally { if (lockAcquired) plcCycleLock.Release(); }
        });
    }

    private void AppendPlcLog(string message) => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        PlcLog += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        if (PlcLog.Length > 10000) PlcLog = PlcLog[^8000..];
        RefreshPlcCommands();
    });

    private void UpdatePlcStatus(string text) =>
        Application.Current.Dispatcher.BeginInvoke(() => { PlcStatus = text; RefreshPlcCommands(); });

    private void RefreshPlcCommands()
    {
        StartPlcServerCommand.RaiseCanExecuteChanged(); StopPlcServerCommand.RaiseCanExecuteChanged();
        ConnectPlcCommand.RaiseCanExecuteChanged(); DisconnectPlcCommand.RaiseCanExecuteChanged();
        PingPlcCommand.RaiseCanExecuteChanged(); TriggerPlcCommand.RaiseCanExecuteChanged();
    }

    private void SaveInspectionHistory(InspectionResult result, Mat annotated)
    {
        try
        {
            DateTimeOffset now = DateTimeOffset.Now;
            string? ngPath = result.IsOk ? null : ngImageStorageService.Save(annotated, now);
            historyService.Save(new InspectionRecord
            {
                InspectedAt = now,
                BatchNumber = string.IsNullOrWhiteSpace(BatchNumber) ? "DEFAULT" : BatchNumber.Trim(),
                ProductModel = string.IsNullOrWhiteSpace(ProductModel) ? "DEFAULT" : ProductModel.Trim(),
                OperatorName = string.IsNullOrWhiteSpace(OperatorName) ? "未填写" : OperatorName.Trim(),
                RecipeName = string.IsNullOrWhiteSpace(RecipeName) ? "默认配方" : RecipeName.Trim(),
                RecipeVersion = string.IsNullOrWhiteSpace(RecipeVersion) ? "1.0" : RecipeVersion.Trim(),
                PlcCycleId = activePlcCycleId,
                Result = result.IsOk ? "OK" : "NG",
                JudgementCode = result.JudgementCode,
                JudgementReason = result.JudgementReason,
                TargetCount = result.TargetCount,
                MaximumArea = result.MaximumArea,
                Circularity = result.Circularity,
                CenterX = result.CenterX, CenterY = result.CenterY,
                Width = result.Width, Height = result.Height,
                AspectRatio = result.AspectRatio,
                CenterOffset = result.CenterOffset,
                PixelSizeMm = result.PixelSizeMm,
                AreaMm2 = result.AreaMm2,
                WidthMm = result.WidthMm,
                HeightMm = result.HeightMm,
                ProcessingTimeMs = result.ProcessingTimeMs,
                NgImagePath = ngPath
            });
            auditLog.Record(
                "INSPECTION",
                result.IsOk ? "OK" : "NG",
                $"batch={BatchNumber}; product={ProductModel}; recipe={RecipeName} v{RecipeVersion}; " +
                $"cycle={activePlcCycleId ?? "MANUAL"}; code={result.JudgementCode}");
            RefreshHistory();
        }
        catch (Exception exception)
        {
            StatusText = $"检测完成，但历史记录保存失败：{exception.Message}";
        }
    }

    private void RefreshHistory()
    {
        try
        {
            string? filter = HistoryResultFilter is "OK" or "NG" ? HistoryResultFilter : null;
            DateTimeOffset? from = HistoryStartDate.HasValue
                ? new DateTimeOffset(HistoryStartDate.Value.Date) : null;
            DateTimeOffset? to = HistoryEndDate.HasValue
                ? new DateTimeOffset(HistoryEndDate.Value.Date.AddDays(1)) : null;
            if (from.HasValue && to.HasValue && from >= to)
                throw new ArgumentException("历史查询的开始日期不能晚于结束日期。");
            IReadOnlyList<InspectionRecord> records = historyService.Query(
                filter, HistoryBatchFilter, HistoryProductFilter, from: from, toExclusive: to);
            HistoryRecords.Clear();
            foreach (InspectionRecord record in records) HistoryRecords.Add(record);
            InspectionStatistics summary = InspectionStatisticsService.Calculate(records);
            string ngBreakdown = summary.NgCodeCounts.Count == 0
                ? "无NG"
                : string.Join("，", summary.NgCodeCounts.Select(x => $"{x.Key} {x.Value}"));
            HistorySummary =
                $"当前筛选：总数 {summary.Total}　OK {summary.Ok}　NG {summary.Ng}　" +
                $"合格率 {summary.OkRate:F1}%　NG分布：{ngBreakdown}";
            ExportHistoryCommand?.RaiseCanExecuteChanged();
        }
        catch (Exception exception)
        {
            StatusText = $"查询历史失败：{exception.Message}";
        }
    }

    private void OpenNgImage()
    {
        string? path = SelectedHistoryRecord?.NgImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "所选记录没有可用的NG证据图。";
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception exception) { StatusText = $"打开NG图片失败：{exception.Message}"; }
    }

    private void CleanupNgImages()
    {
        MessageBoxResult confirmation = MessageBox.Show(
            "将永久删除超过7天、且数据库没有引用的孤立NG图片。\n" +
            "数据库仍在使用的证据图不会删除。是否继续？",
            "清理NG图片", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            IReadOnlyList<string> referenced = historyService.GetReferencedNgImagePaths();
            NgImageCleanupResult result = ngImageStorageService.CleanupOrphans(
                referenced, DateTimeOffset.Now.AddDays(-7));
            auditLog.Record(
                "NG_IMAGE_CLEANUP",
                "SUCCESS",
                $"files={result.DeletedFiles}; bytes={result.DeletedBytes}; " +
                $"directories={result.DeletedDirectories}");
            StatusText =
                $"NG图片清理完成：删除{result.DeletedFiles}个孤立文件，" +
                $"释放{result.DeletedBytes / 1024.0 / 1024.0:F2}MB，" +
                $"移除{result.DeletedDirectories}个空目录。";
        }
        catch (Exception exception)
        {
            StatusText = $"NG图片清理失败：{exception.Message}";
            ApplicationLogService.Error("NG图片清理失败。", exception);
        }
    }

    private void RunDiagnostics()
    {
        SystemDiagnosticReport report = diagnosticsService.Run();
        string title = report.Passed ? "系统自检通过" : "系统自检发现问题";
        StatusText = report.Passed
            ? "系统自检通过：数据目录、数据库、OpenCV和默认配方正常。"
            : "系统自检未全部通过，请查看报告并检查日志。";
        ApplicationLogService.Info(
            $"{title}{Environment.NewLine}{report.ToDisplayText()}");
        MessageBox.Show(
            report.ToDisplayText(),
            title,
            MessageBoxButton.OK,
            report.Passed ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ExportHistory()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出检测历史", Filter = "CSV文件|*.csv", DefaultExt = ".csv",
            FileName = $"检测历史_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            InspectionCsvExportService.Export(dialog.FileName, HistoryRecords);
            StatusText = $"已导出{HistoryRecords.Count}条记录。";
        }
        catch (Exception exception)
        {
            StatusText = $"导出失败：{exception.Message}";
        }
    }

    private VisionParameters ReadParameters()
    {
        CultureInfo culture = CultureInfo.InvariantCulture;
        if (!int.TryParse(ThresholdText, out int threshold) ||
            !double.TryParse(MinimumContourAreaText, NumberStyles.Float, culture, out double minimumContourArea) ||
            !double.TryParse(MinimumCircularityText, NumberStyles.Float, culture, out double minimumCircularity) ||
            !int.TryParse(ExpectedCountText, out int expectedCount) ||
            !double.TryParse(MinimumOkAreaText, NumberStyles.Float, culture, out double minimumOkArea) ||
            !double.TryParse(MaximumOkAreaText, NumberStyles.Float, culture, out double maximumOkArea) ||
            !int.TryParse(RoiXText, out int roiX) || !int.TryParse(RoiYText, out int roiY) ||
            !int.TryParse(RoiWidthText, out int roiWidth) || !int.TryParse(RoiHeightText, out int roiHeight))
            throw new FormatException("检测参数格式错误，请使用数字；小数使用英文句点。 ");

        if (!int.TryParse(MinimumWidthText, out int minimumWidth) ||
            !int.TryParse(MaximumWidthText, out int maximumWidth) ||
            !int.TryParse(MinimumHeightText, out int minimumHeight) ||
            !int.TryParse(MaximumHeightText, out int maximumHeight) ||
            !double.TryParse(MinimumAspectRatioText, NumberStyles.Float, culture, out double minimumAspectRatio) ||
            !double.TryParse(MaximumAspectRatioText, NumberStyles.Float, culture, out double maximumAspectRatio) ||
            !int.TryParse(ExpectedCenterXText, out int expectedCenterX) ||
            !int.TryParse(ExpectedCenterYText, out int expectedCenterY) ||
            !int.TryParse(CenterToleranceText, out int centerTolerance) ||
            !int.TryParse(AdaptiveBlockSizeText, out int adaptiveBlockSize) ||
            !double.TryParse(AdaptiveConstantText, NumberStyles.Float, culture, out double adaptiveConstant) ||
            !double.TryParse(PixelSizeMmText, NumberStyles.Float, culture, out double pixelSizeMm))
            throw new FormatException("宽高、长宽比或中心位置参数格式错误。");

        return new VisionParameters(threshold, minimumContourArea, minimumCircularity,
            expectedCount, minimumOkArea, maximumOkArea,
            IsRoiEnabled, roiX, roiY, roiWidth, roiHeight,
            minimumWidth, maximumWidth, minimumHeight, maximumHeight,
            minimumAspectRatio, maximumAspectRatio,
            expectedCenterX, expectedCenterY, centerTolerance,
            IsDarkObject, UseAdaptiveThreshold, adaptiveBlockSize, adaptiveConstant,
            pixelSizeMm, RecipeName, RecipeVersion);
    }

    public void SetRoi(int x, int y, int width, int height)
    {
        RoiXText = x.ToString(CultureInfo.InvariantCulture);
        RoiYText = y.ToString(CultureInfo.InvariantCulture);
        RoiWidthText = width.ToString(CultureInfo.InvariantCulture);
        RoiHeightText = height.ToString(CultureInfo.InvariantCulture);
        IsRoiEnabled = true;
        StatusText = $"ROI已设置：({x}, {y}, {width}, {height})";
    }

    private void SaveRecipe()
    {
        try
        {
            VisionParameters parameters = ReadParameters();
            var dialog = new SaveFileDialog
            {
                Title = "保存视觉配方",
                Filter = "JSON配方|*.json",
                DefaultExt = ".json",
                FileName = "零件检测配方.json"
            };
            if (dialog.ShowDialog() != true) return;
            RecipeSaveResult saveResult = recipeService.Save(dialog.FileName, parameters);
            auditLog.Record(
                "RECIPE_SAVE", "SUCCESS",
                $"name={parameters.RecipeName}; version={parameters.RecipeVersion}; " +
                $"path={dialog.FileName}; changed={saveResult.Changed}; " +
                $"backup={saveResult.BackupPath ?? "NONE"}");
            StatusText = saveResult switch
            {
                { Changed: false } => $"配方内容未变化：{dialog.SafeFileName}",
                { BackupPath: not null } =>
                    $"配方已保存并备份旧版本：{dialog.SafeFileName}",
                _ => $"配方已保存：{dialog.SafeFileName}"
            };
        }
        catch (Exception exception)
        {
            StatusText = $"保存配方失败：{exception.Message}";
        }
    }

    private void LoadRecipe()
    {
        var dialog = new OpenFileDialog { Title = "加载视觉配方", Filter = "JSON配方|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            VisionParameters parameters = recipeService.Load(dialog.FileName);
            ApplyParameters(parameters);
            auditLog.Record(
                "RECIPE_LOAD", "SUCCESS",
                $"name={parameters.RecipeName}; version={parameters.RecipeVersion}; " +
                $"path={dialog.FileName}");
            StatusText = $"配方已加载：{dialog.SafeFileName}";
        }
        catch (Exception exception)
        {
            StatusText = $"加载配方失败：{exception.Message}";
        }
    }

    private void ApplyParameters(VisionParameters value)
    {
        ThresholdText = value.Threshold.ToString(CultureInfo.InvariantCulture);
        MinimumContourAreaText = value.MinimumContourArea.ToString(CultureInfo.InvariantCulture);
        MinimumCircularityText = value.MinimumCircularity.ToString(CultureInfo.InvariantCulture);
        ExpectedCountText = value.ExpectedCount.ToString(CultureInfo.InvariantCulture);
        MinimumOkAreaText = value.MinimumOkArea.ToString(CultureInfo.InvariantCulture);
        MaximumOkAreaText = value.MaximumOkArea.ToString(CultureInfo.InvariantCulture);
        MinimumWidthText = value.MinimumWidth.ToString(CultureInfo.InvariantCulture);
        MaximumWidthText = value.MaximumWidth.ToString(CultureInfo.InvariantCulture);
        MinimumHeightText = value.MinimumHeight.ToString(CultureInfo.InvariantCulture);
        MaximumHeightText = value.MaximumHeight.ToString(CultureInfo.InvariantCulture);
        MinimumAspectRatioText = value.MinimumAspectRatio.ToString(CultureInfo.InvariantCulture);
        MaximumAspectRatioText = value.MaximumAspectRatio.ToString(CultureInfo.InvariantCulture);
        ExpectedCenterXText = value.ExpectedCenterX.ToString(CultureInfo.InvariantCulture);
        ExpectedCenterYText = value.ExpectedCenterY.ToString(CultureInfo.InvariantCulture);
        CenterToleranceText = value.CenterTolerance.ToString(CultureInfo.InvariantCulture);
        IsDarkObject = value.IsDarkObject;
        UseAdaptiveThreshold = value.UseAdaptiveThreshold;
        AdaptiveBlockSizeText = value.AdaptiveBlockSize.ToString(CultureInfo.InvariantCulture);
        AdaptiveConstantText = value.AdaptiveConstant.ToString(CultureInfo.InvariantCulture);
        PixelSizeMmText = value.PixelSizeMm.ToString(CultureInfo.InvariantCulture);
        RecipeName = value.RecipeName;
        RecipeVersion = value.RecipeVersion;
        IsRoiEnabled = value.IsRoiEnabled;
        RoiXText = value.RoiX.ToString(CultureInfo.InvariantCulture);
        RoiYText = value.RoiY.ToString(CultureInfo.InvariantCulture);
        RoiWidthText = value.RoiWidth.ToString(CultureInfo.InvariantCulture);
        RoiHeightText = value.RoiHeight.ToString(CultureInfo.InvariantCulture);
    }

    private void RefreshDisplayImage()
    {
        Mat? liveFrame = null;
        Mat? mat = SelectedDebugView switch
        {
            VisionDebugView.灰度图 when processingResult is not null => processingResult.Gray,
            VisionDebugView.二值图 when processingResult is not null => processingResult.Binary,
            VisionDebugView.标注图 when processingResult is not null => processingResult.Annotated,
            _ => sourceImage
        };

        if (mat is null)
        {
            liveFrame = acquisitionService?.GetLatestFrame();
            mat = liveFrame;
        }

        try
        {
            if (mat is not null) DisplayImage = BitmapConverter.ToBitmapImage(mat);
        }
        finally
        {
            liveFrame?.Dispose();
        }
    }

    private bool HasInspectionImage() =>
        sourceImage is not null || acquisitionService?.IsRunning == true;

    private void RefreshCommands()
    {
        StartCameraCommand.RaiseCanExecuteChanged();
        StopCameraCommand.RaiseCanExecuteChanged();
        InspectCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        lifetimeCancellation.Cancel();
        allowPlcAutoReconnect = false;
        plcReconnectCancellation?.Cancel();
        plcReconnectCancellation?.Dispose();
        heartbeatMonitor.Dispose();
        plcClient.Dispose();
        plcServer.Dispose();
        lifetimeCancellation.Dispose();
        ReleaseCameraService();
        processingResult?.Dispose();
        sourceImage?.Dispose();
    }
}
