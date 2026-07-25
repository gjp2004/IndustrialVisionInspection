using OpenCvSharp;

namespace IndustrialVisionStudent.Vision;

public static class SampleImageFactory
{
    public static Mat CreateStandardWasher()
    {
        var image = new Mat(new Size(640, 480), MatType.CV_8UC3, Scalar.White);
        Cv2.Circle(image, new Point(320, 240), 50, Scalar.Black, -1);
        Cv2.Circle(image, new Point(320, 240), 18, Scalar.White, -1);
        return image;
    }
}
