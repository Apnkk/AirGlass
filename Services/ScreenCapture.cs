using System.IO;

namespace AirGlass.Services;

public static class ScreenCapture
{
    /// <summary>Saves a screen region (device pixels) as PNG in Pictures\AirGlass and returns the file path.</summary>
    public static string SaveRegion(int x, int y, int width, int height)
    {
        if (width < 2 || height < 2)
            throw new ArgumentException("Zone de capture vide.");

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AirGlass");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"AirGlass-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }
}