using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.Win32;

namespace BtManage.Tray;

/// <summary>
/// Draws the tray icon: the headphones mark in one colour, white on a dark taskbar and black
/// on a light one. The geometry lives on a 16-unit grid and matches tools/make-icon.ps1.
/// </summary>
internal static class TrayGlyph
{
    /// <summary>Renders the mark at <paramref name="size"/> pixels. Free the icon with DestroyIcon.</summary>
    public static nint Render(int size)
    {
        var ink = TaskbarIsLight() ? Color.Black : Color.White;
        var u = size / 16f;

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(ink, 1.5f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var brush = new SolidBrush(ink))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);

            // Headband, then the two ear cups.
            graphics.DrawArc(pen, 3.5f * u, 2.5f * u, 9f * u, 9f * u, 180, 180);
            graphics.DrawLine(pen, 3.5f * u, 7f * u, 3.5f * u, 8.5f * u);
            graphics.DrawLine(pen, 12.5f * u, 7f * u, 12.5f * u, 8.5f * u);
            FillRoundedRectangle(graphics, brush, 2f * u, 8.5f * u, 3f * u, 5.5f * u, 1.2f * u);
            FillRoundedRectangle(graphics, brush, 11f * u, 8.5f * u, 3f * u, 5.5f * u, 1.2f * u);
        }

        return bitmap.GetHicon();
    }

    private static void FillRoundedRectangle(Graphics graphics, Brush brush, float x, float y, float width, float height, float radius)
    {
        var diameter = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }

    /// <summary>The taskbar follows the "Windows mode" colour setting, not the app mode.</summary>
    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }
}
