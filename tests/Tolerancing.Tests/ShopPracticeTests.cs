using Tolerancing.Core;
using Tolerancing.Drawings;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>Burge's rules of thumb (OPTI 521): the base past which looser saves nothing, glass grades, bevels.</summary>
public class ShopPracticeTests
{
    [Fact]
    public void NoToleranceIsAllowedLooserThanBurgesBase()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = DefaultTolerances.Create(lens);
        var limits = ShopLimits.FromGrades(lens, items);
        ShopLimits Of<T>(Func<T, bool> match) where T : Tolerance =>
            limits[items.FindIndex(i => i.Tolerance is T t && match(t))];

        Assert.Equal(0.001, Of<IndexChange>(_ => true).Loosest, 12);                    // the low-cost grade's 0.002 buys nothing
        Assert.Equal(0.2, Of<ThicknessChange>(t => lens.System.Surfaces[t.Surface].Thickness >= 1.0).Loosest, 12);
        // A surface tilt is held to 6' of deviation: Δn·θ at an air-glass surface.
        int s = 1;
        double dn = lens.Nd[s] - lens.Nd[s - 1];
        Assert.Equal(DefaultTolerances.BaseDeviationDeg / dn, Of<SurfaceTilt>(t => t.Surface == s).Loosest, 12);
        // A base never comes below what the shop can hold at best.
        Assert.All(limits, l => Assert.True(l.Loosest >= l.Tightest));
    }

    [Fact]
    public void GlassTolerancesRoundDownToTheGradesItIsSoldIn()
    {
        var index = new ShopLimits(0.0002, 0.001) { Steps = DefaultTolerances.IndexGrades };
        Assert.Equal(0.001, index.Round(0.001));
        Assert.Equal(0.0005, index.Round(0.0009));        // a preferred number would give 0.0008, which no one sells
        Assert.Equal(0.0003, index.Round(0.00045));
        Assert.Equal(0.0002, index.Round(0.00021));
        Assert.Equal(0.0002, index.Round(0.0001));        // never tighter than the tightest
        var any = new ShopLimits(0.01, 1.0);
        Assert.Equal(0.8, any.Round(0.9));                 // other kinds keep the preferred numbers
    }

    [Theory]
    [InlineData(12.0, 0.3)]
    [InlineData(25.0, 0.3)]
    [InlineData(60.0, 0.5)]
    [InlineData(150.0, 1.0)]
    [InlineData(500.0, 2.0)]
    public void ABevelGrowsWithTheLens(double diameter, double least)
    {
        var (min, max) = new DrawingSettings().Chamfer(diameter);
        Assert.Equal(least, min);
        Assert.Equal(2.0 * least, max);
    }

    [Fact]
    public void ABevelTheSettingsGiveIsKept()
    {
        Assert.Equal((null, 0.3), new DrawingSettings { ChamferMax = 0.3 }.Chamfer(100.0));
        Assert.Equal((0.2, 0.4), new DrawingSettings { ChamferMin = 0.2 }.Chamfer(100.0));
        Assert.Equal((0.2, 0.25), new DrawingSettings { ChamferMin = 0.2, ChamferMax = 0.25 }.Chamfer(100.0));
    }

    [Fact]
    public void TheDrawingStatesTheBevelRangeAndWarnsWhenItWouldEatTheEdge()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var settings = new DrawingSettings { Date = "2026-10-12" };
        var d = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), settings)[0];
        var text = string.Join("\n", Layout.Draw(d, settings).Marks.OfType<Label>().Select(l => l.Text));
        Assert.Contains("Prot. chamfer 0.3-0.6", text);

        var wide = settings with { ChamferMin = 3.0 };
        var w = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), wide)[0];
        Assert.Contains(w.Warnings, m => m.Contains("whole") && m.Contains("edge"));
    }
}
