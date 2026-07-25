using System.IO;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace IndustrialVisionStudent.Utils;

public static class BitmapConverter
{
    public static BitmapImage ToBitmapImage(Mat mat)
    {
        Cv2.ImEncode(".png", mat, out byte[] bytes);
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
