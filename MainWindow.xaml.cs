using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IndustrialVisionStudent.ViewModels;

namespace IndustrialVisionStudent;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private Point roiStart;
    private Rectangle? roiRectangle;
    private bool isSelectingRoi;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? dataRootOverride)
    {
        InitializeComponent();
        viewModel = new MainViewModel(dataRootOverride);
        DataContext = viewModel;
    }

    private void RoiCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (viewModel.DisplayImage is null) return;
        roiStart = ClampToRenderedImage(e.GetPosition(RoiCanvas));
        isSelectingRoi = true;
        RoiCanvas.CaptureMouse();
        roiRectangle ??= new Rectangle
        {
            Stroke = Brushes.Cyan,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(35, 0, 255, 255))
        };
        if (!RoiCanvas.Children.Contains(roiRectangle)) RoiCanvas.Children.Add(roiRectangle);
        UpdateRoiRectangle(roiStart);
    }

    private void RoiCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (isSelectingRoi) UpdateRoiRectangle(ClampToRenderedImage(e.GetPosition(RoiCanvas)));
    }

    private void RoiCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isSelectingRoi || viewModel.DisplayImage is null) return;
        Point end = ClampToRenderedImage(e.GetPosition(RoiCanvas));
        isSelectingRoi = false;
        RoiCanvas.ReleaseMouseCapture();

        Rect rendered = GetRenderedImageRect();
        double left = Math.Min(roiStart.X, end.X);
        double top = Math.Min(roiStart.Y, end.Y);
        double width = Math.Abs(end.X - roiStart.X);
        double height = Math.Abs(end.Y - roiStart.Y);
        if (width < 3 || height < 3 || rendered.Width <= 0 || rendered.Height <= 0) return;

        int x = (int)Math.Round((left - rendered.X) / rendered.Width * viewModel.DisplayImage.PixelWidth);
        int y = (int)Math.Round((top - rendered.Y) / rendered.Height * viewModel.DisplayImage.PixelHeight);
        int pixelWidth = Math.Max(1, (int)Math.Round(width / rendered.Width * viewModel.DisplayImage.PixelWidth));
        int pixelHeight = Math.Max(1, (int)Math.Round(height / rendered.Height * viewModel.DisplayImage.PixelHeight));
        pixelWidth = Math.Min(pixelWidth, viewModel.DisplayImage.PixelWidth - x);
        pixelHeight = Math.Min(pixelHeight, viewModel.DisplayImage.PixelHeight - y);
        viewModel.SetRoi(x, y, pixelWidth, pixelHeight);
    }

    private void UpdateRoiRectangle(Point current)
    {
        if (roiRectangle is null) return;
        double left = Math.Min(roiStart.X, current.X);
        double top = Math.Min(roiStart.Y, current.Y);
        Canvas.SetLeft(roiRectangle, left);
        Canvas.SetTop(roiRectangle, top);
        roiRectangle.Width = Math.Abs(current.X - roiStart.X);
        roiRectangle.Height = Math.Abs(current.Y - roiStart.Y);
    }

    private Point ClampToRenderedImage(Point point)
    {
        Rect rect = GetRenderedImageRect();
        return new Point(
            Math.Clamp(point.X, rect.Left, rect.Right),
            Math.Clamp(point.Y, rect.Top, rect.Bottom));
    }

    private Rect GetRenderedImageRect()
    {
        if (viewModel.DisplayImage is null || RoiCanvas.ActualWidth <= 0 || RoiCanvas.ActualHeight <= 0)
            return Rect.Empty;
        double scale = Math.Min(
            RoiCanvas.ActualWidth / viewModel.DisplayImage.PixelWidth,
            RoiCanvas.ActualHeight / viewModel.DisplayImage.PixelHeight);
        double width = viewModel.DisplayImage.PixelWidth * scale;
        double height = viewModel.DisplayImage.PixelHeight * scale;
        return new Rect((RoiCanvas.ActualWidth - width) / 2, (RoiCanvas.ActualHeight - height) / 2,
            width, height);
    }

    protected override void OnClosed(EventArgs e)
    {
        viewModel.Dispose();
        base.OnClosed(e);
    }
}
