using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/// <summary>Renders screenshot elements and saves their PNG output.</summary>
internal static class ScreenshotRenderer
{
    public static RenderTargetBitmap Render(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(
            (int)(width * 1.5),
            (int)(height * 1.5),
            144,
            144,
            PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    public static void CompositeDialog(string backgroundPath, BitmapSource dialog, string outputPath)
    {
        var decoder = new PngBitmapDecoder(
            new Uri(Path.GetFullPath(backgroundPath)),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var background = decoder.Frames[0];
        const double scale = 1.5;
        var logicalWidth = background.PixelWidth / scale;
        var logicalHeight = background.PixelHeight / scale;
        var visual = new DrawingVisual();

        using (var drawingContext = visual.RenderOpen())
        {
            drawingContext.DrawImage(background, new Rect(0, 0, logicalWidth, logicalHeight));
            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(105, 0, 0, 0)),
                null,
                new Rect(0, 0, logicalWidth, logicalHeight));

            var dialogWidth = dialog.PixelWidth / scale;
            var dialogHeight = dialog.PixelHeight / scale;
            var x = (logicalWidth - dialogWidth) / 2.0;
            var y = (logicalHeight - dialogHeight) / 2.0;
            drawingContext.DrawImage(dialog, new Rect(x, y, dialogWidth, dialogHeight));
        }

        var bitmap = new RenderTargetBitmap(
            background.PixelWidth,
            background.PixelHeight,
            144,
            144,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Save(bitmap, outputPath);
    }

    public static void Save(BitmapSource bitmap, string path)
    {
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }
}
