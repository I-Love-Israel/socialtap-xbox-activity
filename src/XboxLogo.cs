using System.Drawing;
using System.Drawing.Drawing2D;

namespace SocialTapXboxActivity;

/// <summary>
/// Draws a simple Xbox-style badge (green disc with a white X) for the idle state.
/// This is an original drawing, not Microsoft's logo file.
/// </summary>
internal static class XboxLogo
{
    public static Bitmap Create(int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var disc = new SolidBrush(Color.FromArgb(16, 124, 16));
        g.FillEllipse(disc, 0, 0, size - 1, size - 1);

        using var pen = new Pen(Color.White, size * 0.13f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        var inset = size * 0.28f;
        g.DrawLine(pen, inset, inset, size - inset, size - inset);
        g.DrawLine(pen, size - inset, inset, inset, size - inset);

        return bitmap;
    }
}
