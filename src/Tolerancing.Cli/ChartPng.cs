using SkiaSharp;
using Tolerancing.Drawings;

namespace Tolerancing.Cli;

/// <summary>
/// A chart painted into a PNG by SkiaSharp, mark by mark, at <c>scale</c> times its size in pixels:
/// the same layout as its SVG, for slides and documents that take no SVG.
/// </summary>
internal static class ChartPng
{
    public static void Write(Chart chart, string path, float scale = 2f)
    {
        var info = new SKImageInfo((int)Math.Ceiling(chart.Width * scale), (int)Math.Ceiling(chart.Height * scale));
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);
        foreach (var mark in chart.Marks) Draw(canvas, mark);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
    }

    private static SKColor Color(string hex, double opacity = 1.0) =>
        SKColor.Parse(hex).WithAlpha((byte)Math.Round(255 * Math.Clamp(opacity, 0.0, 1.0)));

    private static SKPaint Stroke(string color, double width, IReadOnlyList<double>? dash = null) => new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        Color = Color(color),
        StrokeWidth = (float)width,
        StrokeJoin = SKStrokeJoin.Round,
        PathEffect = dash == null ? null : SKPathEffect.CreateDash(dash.Select(d => (float)d).ToArray(), 0f),
    };

    /// <summary>Helvetica where there is one, else Arial, else the system's sans serif - DejaVu Sans on most Linux.</summary>
    private static SKTypeface Face(bool bold)
    {
        var style = bold ? SKFontStyle.Bold : SKFontStyle.Normal;
        foreach (var family in new[] { "Helvetica", "Arial", "Liberation Sans", "DejaVu Sans" })
        {
            var face = SKTypeface.FromFamilyName(family, style);
            if (face != null && face.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase)) return face;
        }
        return SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
    }

    private static void Draw(SKCanvas canvas, ChartMark mark)
    {
        switch (mark)
        {
            case ChartRect r:
            {
                var rect = SKRect.Create((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
                if (r.Fill != null)
                {
                    using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Color(r.Fill, r.FillOpacity) };
                    canvas.DrawRect(rect, fill);
                }
                if (r.Stroke != null)
                {
                    using var stroke = Stroke(r.Stroke, r.StrokeWidth);
                    canvas.DrawRect(rect, stroke);
                }
                break;
            }
            case ChartLine l:
            {
                using var paint = Stroke(l.Color, l.Width, l.Dash);
                canvas.DrawLine((float)l.X1, (float)l.Y1, (float)l.X2, (float)l.Y2, paint);
                break;
            }
            case ChartPolyline p when p.Points.Count > 1:
            {
                using var path = new SKPath();
                path.MoveTo((float)p.Points[0].X, (float)p.Points[0].Y);
                foreach (var (x, y) in p.Points.Skip(1)) path.LineTo((float)x, (float)y);
                using var paint = Stroke(p.Color, p.Width);
                canvas.DrawPath(path, paint);
                break;
            }
            case ChartText t:
            {
                using var face = Face(t.Bold);
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Color = Color(t.Color),
                    Typeface = face,
                    TextSize = (float)t.Size,
                    TextAlign = t.Anchor switch { TextAnchor.Middle => SKTextAlign.Center, TextAnchor.End => SKTextAlign.Right, _ => SKTextAlign.Left },
                };
                canvas.Save();
                canvas.Translate((float)t.X, (float)t.Y);
                if (t.Rotate != 0.0) canvas.RotateDegrees((float)t.Rotate);
                canvas.DrawText(t.Text, 0f, 0f, paint);
                canvas.Restore();
                break;
            }
        }
    }
}
