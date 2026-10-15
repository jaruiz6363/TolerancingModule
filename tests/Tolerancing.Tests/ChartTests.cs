using System.Xml.Linq;
using Tolerancing.Drawings;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>The Monte Carlo charts: well-formed SVG that holds every lens and says which it left out.</summary>
public class ChartTests
{
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    [Theory]
    [InlineData(0.0, 1.0, new[] { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0 })]
    [InlineData(1.23, 1.91, new[] { 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9 })]
    [InlineData(-0.7, 0.7, new[] { -0.6, -0.4, -0.2, 0.0, 0.2, 0.4, 0.6 })]
    public void TicksAreRoundAndInsideTheRange(double lo, double hi, double[] expected)
    {
        var ticks = Charts.Ticks(lo, hi);
        Assert.Equal(expected.Length, ticks.Count);
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], ticks[i], 9);
    }

    [Fact]
    public void AHistogramHoldsEveryTracedLensAndNotesTheOthers()
    {
        var random = new Random(3);
        var values = Enumerable.Range(0, 500).Select(_ => random.NextDouble() + random.NextDouble()).ToList();
        values.Add(double.PositiveInfinity);
        var markers = new[] { new ChartMarker(1.5, "limit", "#e9a400"), new ChartMarker(-1.0, "", "#000000", "") };
        var svg = XDocument.Parse(Charts.Histogram("t", "x", values, x => Math.Clamp(x / 2.0, 0.0, 1.0), "predicted", markers).ToSvg());

        // Each bar's height in lenses: the bars add up to the 500 traced; the untraceable one is noted.
        var texts = svg.Descendants(Ns + "text").Select(t => t.Value).ToList();
        Assert.Contains("1 of 501 lenses could not be traced and are left out", texts);
        Assert.Contains("500 traced lenses", texts);
        Assert.Contains("limit 1.5", texts);
        Assert.DoesNotContain(texts, t => t.StartsWith(" "));              // the silent marker has no legend line
        Assert.Single(svg.Descendants(Ns + "polyline"));
    }

    [Fact]
    public void ACumulativeChartStepsToTheTracedFraction()
    {
        var values = new[] { 1.0, 2.0, 3.0, double.NaN };
        var svg = XDocument.Parse(Charts.Cumulative("t", "x", values, null, "", Array.Empty<ChartMarker>(), 0.977).ToSvg());
        var points = svg.Descendants(Ns + "polyline").Single().Attribute("points")!.Value.Split(' ');
        Assert.Equal(2 + 2 * 3, points.Length);                            // from 0, two points a lens, on to the edge
        Assert.Contains(svg.Descendants(Ns + "text"), t => t.Value == "97.7 % of lenses");
    }

    [Fact]
    public void APageHoldsEachChartUnderItsHeading()
    {
        var chart = Charts.Histogram("t", "x", new[] { 1.0, 2.0 }, null, "", Array.Empty<ChartMarker>());
        string page = Charts.Page("Monte Carlo", new[] { "a <line>" }, new[] { ("first", chart), ("second", chart) });
        Assert.Contains("<h2>first</h2>", page);
        Assert.Contains("a &lt;line&gt;", page);
        Assert.Equal(2, page.Split("<svg ").Length - 1);
    }
}
