using System.Globalization;
using System.Net;
using System.Text;

namespace Tolerancing.Drawings;

/// <summary>
/// A vertical line on a chart, named in its legend: a limit, a level, the nominal value. The legend
/// reads "label value" unless <paramref name="Legend"/> gives its text; an empty one leaves the
/// marker out of the legend, as the second of a ± pair.
/// </summary>
public sealed record ChartMarker(double X, string Label, string Color, string? Legend = null)
{
    internal string? Text => Legend ?? $"{Label} {X.ToString("G4", CultureInfo.InvariantCulture)}";
}

/// <summary>Where a chart's text is anchored on its x: its start, middle or end.</summary>
public enum TextAnchor { Start, Middle, End }

/// <summary>One thing drawn on a chart, in its pixels, y downward; colors as "#rrggbb".</summary>
public abstract record ChartMark;

/// <summary>A rectangle: filled unless <paramref name="Fill"/> is null, outlined unless <paramref name="Stroke"/> is.</summary>
public sealed record ChartRect(double X, double Y, double Width, double Height, string? Fill, string? Stroke,
                               double StrokeWidth = 1.0, double FillOpacity = 1.0) : ChartMark;

/// <summary>A straight line, dashed when <paramref name="Dash"/> gives its on and off lengths.</summary>
public sealed record ChartLine(double X1, double Y1, double X2, double Y2, string Color, double Width,
                               IReadOnlyList<double>? Dash = null) : ChartMark;

/// <summary>Connected straight lines through the points.</summary>
public sealed record ChartPolyline(IReadOnlyList<(double X, double Y)> Points, string Color, double Width) : ChartMark;

/// <summary>Text with its baseline at y, turned <paramref name="Rotate"/> degrees about its anchor (negative is counterclockwise).</summary>
public sealed record ChartText(double X, double Y, string Text, double Size, TextAnchor Anchor, string Color,
                               bool Bold = false, double Rotate = 0.0) : ChartMark;

/// <summary>
/// A chart as a list of marks on a page of <see cref="Width"/> by <see cref="Height"/> pixels: written
/// as SVG here, and painted into an image by whatever can draw rectangles, lines and text.
/// </summary>
public sealed record Chart(double Width, double Height, IReadOnlyList<ChartMark> Marks)
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private static string N(double v) => v.ToString("0.##", C);

    private static string E(string text) => WebUtility.HtmlEncode(text);

    public string ToSvg()
    {
        var s = new StringBuilder();
        s.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(Width)} {N(Height)}\" width=\"{N(Width)}\" height=\"{N(Height)}\" " +
                 "font-family=\"Helvetica, Arial, sans-serif\">\n");
        foreach (var mark in Marks)
            s.Append(mark switch
            {
                ChartRect r => $"<rect x=\"{N(r.X)}\" y=\"{N(r.Y)}\" width=\"{N(r.Width)}\" height=\"{N(r.Height)}\" fill=\"{r.Fill ?? "none"}\"" +
                               (r.FillOpacity < 1.0 ? $" fill-opacity=\"{N(r.FillOpacity)}\"" : "") +
                               (r.Stroke == null ? "" : $" stroke=\"{r.Stroke}\" stroke-width=\"{N(r.StrokeWidth)}\"") + "/>\n",
                ChartLine l => $"<line x1=\"{N(l.X1)}\" y1=\"{N(l.Y1)}\" x2=\"{N(l.X2)}\" y2=\"{N(l.Y2)}\" stroke=\"{l.Color}\" stroke-width=\"{N(l.Width)}\"" +
                               (l.Dash == null ? "" : $" stroke-dasharray=\"{string.Join(" ", l.Dash.Select(N))}\"") + "/>\n",
                ChartPolyline p => $"<polyline points=\"{string.Join(" ", p.Points.Select(q => $"{N(q.X)},{N(q.Y)}"))}\" fill=\"none\" " +
                                   $"stroke=\"{p.Color}\" stroke-width=\"{N(p.Width)}\"/>\n",
                ChartText t => $"<text " + (t.Rotate == 0.0 ? $"x=\"{N(t.X)}\" y=\"{N(t.Y)}\"" : $"transform=\"translate({N(t.X)} {N(t.Y)}) rotate({N(t.Rotate)})\"") +
                               $" font-size=\"{N(t.Size)}\"" + (t.Bold ? " font-weight=\"bold\"" : "") +
                               (t.Anchor == TextAnchor.Start ? "" : $" text-anchor=\"{(t.Anchor == TextAnchor.Middle ? "middle" : "end")}\"") +
                               $" fill=\"{t.Color}\">{E(t.Text)}</text>\n",
                _ => throw new ArgumentException($"no SVG for {mark}"),
            });
        s.Append("</svg>\n");
        return s.ToString();
    }
}

/// <summary>
/// Charts of a Monte Carlo run: a histogram of the traced lenses with the count the prediction
/// expects in each bin, and their cumulative fraction against the prediction's. Markers and the
/// prediction are named in a legend, not on the plot, so nothing overlaps however close they fall.
/// </summary>
public static class Charts
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private const double Width = 760, Height = 440, Left = 74, Right = 24, Top = 56, Bottom = 64;
    private const string Bar = "#9ec5e8", BarEdge = "#5b8fc7", Predicted = "#c8102e", Axis = "#333333", Grid = "#e4e4e4";

    /// <summary>
    /// A histogram of <paramref name="values"/>, the non-finite ones left out and counted in a note,
    /// with the count <paramref name="predictedCdf"/> expects in each bin drawn over it.
    /// </summary>
    public static Chart Histogram(string title, string xLabel, IReadOnlyList<double> values,
                                  Func<double, double>? predictedCdf, string predictedLabel,
                                  IReadOnlyList<ChartMarker> markers)
    {
        var finite = values.Where(double.IsFinite).ToArray();
        if (finite.Length == 0) return Empty(title, "no lens could be traced");
        var (lo, hi) = Range(finite, markers);
        int bins = Math.Clamp((int)Math.Ceiling(2.0 * Math.Cbrt(finite.Length)), 15, 60);
        double w = (hi - lo) / bins;
        var counts = new int[bins];
        foreach (double v in finite) counts[Math.Clamp((int)((v - lo) / w), 0, bins - 1)]++;
        var expected = predictedCdf == null ? null
            : Enumerable.Range(0, bins).Select(b => values.Count * (predictedCdf(lo + (b + 1) * w) - predictedCdf(lo + b * w))).ToArray();
        double top = Math.Max(counts.Max(), expected?.Max() ?? 0.0) * 1.08;
        if (top <= 0.0) top = 1.0;

        var p = new Plot(title, Note(values.Count, finite.Length));
        p.Frame(lo, hi, 0.0, top, xLabel, "lenses");
        for (int b = 0; b < bins; b++)
            if (counts[b] > 0) p.Column(lo + b * w, lo + (b + 1) * w, counts[b]);
        var legend = new List<(string, string, bool)> { (Bar, $"{finite.Length} traced lenses", false) };
        if (expected != null)
        {
            p.Curve(Enumerable.Range(0, bins).Select(b => (lo + (b + 0.5) * w, expected[b])).ToList(), Predicted);
            legend.Add((Predicted, predictedLabel, true));
        }
        foreach (var m in markers) p.Marker(m.X, m.Color);
        legend.AddRange(markers.Where(m => m.Text!.Length > 0).Select(m => (m.Color, m.Text!, true)));
        p.Legend(legend);
        return p.ToChart();
    }

    /// <summary>
    /// The fraction of lenses at or below each value - the traced lenses as a step, the prediction
    /// as a curve - with a horizontal line at <paramref name="fraction"/> when given.
    /// </summary>
    public static Chart Cumulative(string title, string xLabel, IReadOnlyList<double> values,
                                   Func<double, double>? predictedCdf, string predictedLabel,
                                   IReadOnlyList<ChartMarker> markers, double? fraction = null)
    {
        var finite = values.Where(double.IsFinite).OrderBy(v => v).ToArray();
        if (finite.Length == 0) return Empty(title, "no lens could be traced");
        var (lo, hi) = Range(finite, markers);

        var p = new Plot(title, Note(values.Count, finite.Length));
        p.Frame(lo, hi, 0.0, 1.0, xLabel, "fraction of lenses at or below");
        if (fraction is double f) p.Horizontal(f, "#888888");
        // Untraceable lenses fail every limit, so the traced fraction stops short of 1.
        var steps = new List<(double, double)> { (lo, 0.0) };
        for (int i = 0; i < finite.Length; i++)
        {
            steps.Add((finite[i], i / (double)values.Count));
            steps.Add((finite[i], (i + 1) / (double)values.Count));
        }
        steps.Add((hi, finite.Length / (double)values.Count));
        p.Curve(steps, BarEdge);
        var legend = new List<(string, string, bool)> { (BarEdge, $"{finite.Length} traced lenses", true) };
        if (predictedCdf != null)
        {
            p.Curve(Enumerable.Range(0, 201).Select(k => lo + (hi - lo) * k / 200.0).Select(x => (x, predictedCdf(x))).ToList(), Predicted);
            legend.Add((Predicted, predictedLabel, true));
        }
        foreach (var m in markers) p.Marker(m.X, m.Color);
        legend.AddRange(markers.Where(m => m.Text!.Length > 0).Select(m => (m.Color, m.Text!, true)));
        if (fraction is double f2) legend.Add(("#888888", $"{f2 * 100.0:0.#} % of lenses", true));
        p.Legend(legend, bottom: true);
        return p.ToChart();
    }

    /// <summary>One HTML page holding the charts as SVG, each under its heading, after a few lines of text.</summary>
    public static string Page(string title, IEnumerable<string> intro, IEnumerable<(string Heading, Chart Chart)> charts)
    {
        var s = new StringBuilder();
        s.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">\n");
        s.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        s.Append($"<title>{E(title)}</title>\n");
        s.Append("<style>body{font-family:Helvetica,Arial,sans-serif;background:#fff;color:#222;margin:24px auto;max-width:800px;padding:0 16px}" +
                 "h1{font-size:20px}h2{font-size:16px;margin-top:28px}p{margin:4px 0;font-size:14px}svg{max-width:100%;height:auto}</style>\n");
        s.Append($"</head><body>\n<h1>{E(title)}</h1>\n");
        foreach (var line in intro) s.Append($"<p>{E(line)}</p>\n");
        foreach (var (heading, chart) in charts) s.Append($"<h2>{E(heading)}</h2>\n{chart.ToSvg()}\n");
        s.Append("</body></html>\n");
        return s.ToString();
    }

    private static string Note(int all, int traced) => all == traced ? "" : $"{all - traced} of {all} lenses could not be traced and are left out";

    private static Chart Empty(string title, string note) => new Plot(title, note).ToChart();

    /// <summary>The values' and markers' span, widened a little either side; a single value gets a span of its own.</summary>
    private static (double Lo, double Hi) Range(IReadOnlyList<double> values, IReadOnlyList<ChartMarker> markers)
    {
        var all = values.Concat(markers.Select(m => m.X)).Where(double.IsFinite).ToArray();
        double lo = all.Min(), hi = all.Max();
        if (hi - lo < 1e-12 * Math.Max(1.0, Math.Abs(hi))) { lo -= 0.5 * Math.Max(1e-9, Math.Abs(lo)); hi += 0.5 * Math.Max(1e-9, Math.Abs(hi)); }
        double pad = 0.04 * (hi - lo);
        return (lo - pad, hi + pad);
    }

    /// <summary>Round tick values, about <paramref name="count"/> of them, across [lo, hi].</summary>
    internal static List<double> Ticks(double lo, double hi, int count = 6)
    {
        double raw = (hi - lo) / count, decade = Math.Pow(10.0, Math.Floor(Math.Log10(raw)));
        double step = new[] { 1.0, 2.0, 2.5, 5.0, 10.0 }.Select(m => m * decade).First(s => s >= 0.7 * raw);
        var ticks = new List<double>();
        for (double t = Math.Ceiling(lo / step) * step; t <= hi + 1e-9 * step; t += step) ticks.Add(Math.Abs(t) < 1e-12 * step ? 0.0 : t);
        return ticks;
    }

    private static string G(double v) => v.ToString("G4", C);

    private static string E(string text) => WebUtility.HtmlEncode(text);

    /// <summary>One chart being laid out: its plot area maps data to pixels.</summary>
    private sealed class Plot
    {
        private readonly List<ChartMark> _marks = new();
        private double _x0, _x1, _y0, _y1;

        public Plot(string title, string note)
        {
            _marks.Add(new ChartRect(0, 0, Width, Height, "#ffffff", null));
            _marks.Add(new ChartText(Left, 24, title, 15, TextAnchor.Start, Axis, Bold: true));
            if (note.Length > 0) _marks.Add(new ChartText(Left, 42, note, 11, TextAnchor.Start, "#666666"));
        }

        private double X(double v) => Left + (v - _x0) / (_x1 - _x0) * (Width - Left - Right);
        private double Y(double v) => Height - Bottom - (v - _y0) / (_y1 - _y0) * (Height - Top - Bottom);

        public void Frame(double x0, double x1, double y0, double y1, string xLabel, string yLabel)
        {
            (_x0, _x1, _y0, _y1) = (x0, x1, y0, y1);
            foreach (double t in Ticks(y0, y1, 5))
            {
                _marks.Add(new ChartLine(Left, Y(t), Width - Right, Y(t), Grid, 1.0));
                _marks.Add(new ChartText(Left - 6, Y(t) + 4, G(t), 11, TextAnchor.End, Axis));
            }
            foreach (double t in Ticks(x0, x1))
            {
                _marks.Add(new ChartLine(X(t), Height - Bottom, X(t), Height - Bottom + 5, Axis, 1.0));
                _marks.Add(new ChartText(X(t), Height - Bottom + 18, G(t), 11, TextAnchor.Middle, Axis));
            }
            _marks.Add(new ChartRect(Left, Top, Width - Left - Right, Height - Top - Bottom, null, Axis));
            _marks.Add(new ChartText((Left + Width - Right) / 2, Height - 22, xLabel, 12, TextAnchor.Middle, Axis));
            _marks.Add(new ChartText(18, (Top + Height - Bottom) / 2, yLabel, 12, TextAnchor.Middle, Axis, Rotate: -90));
        }

        public void Column(double x0, double x1, double count) =>
            _marks.Add(new ChartRect(X(x0), Y(count), X(x1) - X(x0), Y(0.0) - Y(count), Bar, BarEdge, 0.6));

        public void Curve(IReadOnlyList<(double X, double Y)> points, string color) =>
            _marks.Add(new ChartPolyline(points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y))
                                               .Select(p => (X(p.X), Y(Math.Clamp(p.Y, _y0, _y1)))).ToList(), color, 2.0));

        public void Marker(double x, string color)
        {
            if (double.IsFinite(x)) _marks.Add(new ChartLine(X(x), Top, X(x), Height - Bottom, color, 1.5, new[] { 6.0, 4.0 }));
        }

        public void Horizontal(double y, string color) =>
            _marks.Add(new ChartLine(Left, Y(y), Width - Right, Y(y), color, 1.0, new[] { 3.0, 3.0 }));

        /// <summary>The legend in the plot's upper right, or lower right: a swatch, or a line for a curve, and its text.</summary>
        public void Legend(IReadOnlyList<(string Color, string Text, bool Line)> entries, bool bottom = false)
        {
            const double row = 16;
            double width = entries.Max(e => Helvetica.Width(e.Text, 11 * 0.718)) + 42;      // font-size 11: capitals 0.718 of it
            double x = Width - Right - width - 8, y = bottom ? Height - Bottom - entries.Count * row - 16 : Top + 8;
            _marks.Add(new ChartRect(x, y, width, entries.Count * row + 8, "#ffffff", "#cccccc", 1.0, 0.9));
            for (int i = 0; i < entries.Count; i++)
            {
                var (color, text, line) = entries[i];
                double cy = y + 12 + i * row;
                _marks.Add(line ? new ChartLine(x + 8, cy, x + 28, cy, color, 2.0) : new ChartRect(x + 8, cy - 5, 20, 10, color, BarEdge, 0.6));
                _marks.Add(new ChartText(x + 34, cy + 4, text, 11, TextAnchor.Start, Axis));
            }
        }

        public Chart ToChart() => new(Width, Height, _marks.ToList());
    }
}
