using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.Win32;

namespace BtManage.Tray;

/// <summary>
/// Draws the tray icon: the angular B of the application mark in one colour, white on a dark
/// taskbar and black on a light one. The geometry lives on a 16-unit grid and matches
/// tools/make-icon.ps1.
/// </summary>
internal static class TrayGlyph
{
    /// <summary>
    /// The mark as one stroke that starts and ends at the middle of the stem, so the sharp
    /// corners are the four on the outside.
    /// </summary>
    private static readonly PointF[] Rune =
        [new(5.8f, 8f), new(10.2f, 5.3f), new(5.8f, 2.6f), new(5.8f, 13.4f), new(10.2f, 10.7f), new(5.8f, 8f)];

    /// <summary>Renders the mark at <paramref name="size"/> pixels. Free the icon with DestroyIcon.</summary>
    public static nint Render(int size)
    {
        var ink = TaskbarIsLight() ? Color.Black : Color.White;
        var u = size / 16f;

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(ink, 1.9f * u) { LineJoin = LineJoin.Miter, MiterLimit = 6 })
        using (var path = new GraphicsPath())
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);

            path.AddLines([.. Rune.Select(point => new PointF(point.X * u, point.Y * u))]);
            graphics.DrawPath(pen, path);
        }

        return bitmap.GetHicon();
    }

    /// <summary>The taskbar follows the "Windows mode" colour setting, not the app mode.</summary>
    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }
}
