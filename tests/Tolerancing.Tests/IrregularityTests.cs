using Tolerancing.Core;
using Tolerancing.Drawings;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>
/// Irregularity (TIRR): what it puts on a surface, that the default set carries it, and how a
/// drawing states it and a power too loose for a test plate.
/// </summary>
public class IrregularityTests
{
    private const double Lambda = PowerFringes.HeNe * 1e-3;       // mm

    [Fact]
    public void HalfIsAstigmatismAndHalfSphericalEachWithAQuarterWaveOfSagPerFringe()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var p = lens.Perturb();
        new SurfaceIrregularity(3).Apply(p, 2.0);
        var s = p.System.Surfaces[3];

        Assert.Equal(PowerFringes.SemiDiameter(p, 3), s.ZernikeRadius, 12);
        Assert.Equal(2.0 * Lambda / 4.0 / 2.0, s.FringeZernike[5], 15);    // ρ² cos 2θ: peak to valley 2
        Assert.Equal(2.0 * Lambda / 4.0 / 1.5, s.FringeZernike[9], 15);    // 6ρ⁴ − 6ρ² + 1: peak to valley 1.5
        Assert.Equal(-s.FringeZernike[9], s.FringeZernike[1], 15);         // less its constant

        // Each half, sampled over the aperture, has a peak to valley of half the irregularity's sag.
        double a = s.ZernikeRadius;
        var astigmatism = new AberrationCalculator.Core.Models.Surface { ZernikeRadius = a, FringeZernike = new double[] { 0, 0, 0, 0, 0, s.FringeZernike[5] } };
        var spherical = new AberrationCalculator.Core.Models.Surface { ZernikeRadius = a, FringeZernike = new double[] { 0, s.FringeZernike[1], 0, 0, 0, 0, 0, 0, 0, s.FringeZernike[9] } };
        foreach (var part in new[] { astigmatism, spherical })
        {
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = 0; i <= 200; i++)
                for (int j = 0; j <= 200; j++)
                {
                    double x = a * (2.0 * i / 200 - 1), y = a * (2.0 * j / 200 - 1);
                    if (x * x + y * y > a * a) continue;
                    double z = part.ZernikeSag(x, y, out _, out _);
                    (lo, hi) = (Math.Min(lo, z), Math.Max(hi, z));
                }
            Assert.Equal(2.0 * Lambda / 4.0, hi - lo, 1e-9);
        }
        Assert.Equal(0.0, spherical.ZernikeSag(0.0, 0.0, out _, out _), 15);       // the vertex stays
    }

    [Fact]
    public void ItLeavesTheParaxialLensAlone()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var p = lens.Perturb();
        foreach (int s in new[] { 1, 2, 3, 4, 6, 7 }) new SurfaceIrregularity(s).Apply(p, 5.0);
        Assert.Equal((double)lens.Model.Paraxial.Efl, (double)p.Build().Paraxial.Efl, 12);
    }

    [Fact]
    public void ItChangesTheTracedWavefront()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var criterion = new RmsWavefrontCriterion();
        var compensator = FocusCompensator.BackFocus(lens);
        double nominal = Sensitivity.Evaluate(lens, null, 0.0, criterion, compensator).Criterion;
        double up = Sensitivity.Evaluate(lens, new SurfaceIrregularity(3), 2.0, criterion, compensator).Criterion;
        double down = Sensitivity.Evaluate(lens, new SurfaceIrregularity(3), -2.0, criterion, compensator).Criterion;
        Assert.True(Math.Abs(up - nominal) > 0.01 && Math.Abs(down - nominal) > 0.01, $"{down} {nominal} {up}");
    }

    [Fact]
    public void TheDefaultSetCarriesItAtSmithsGrades()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        foreach (var (grade, fringes) in new[] { (ShopGrade.LowCost, 10.0), (ShopGrade.Commercial, 2.0), (ShopGrade.Precision, 1.0), (ShopGrade.ExtraPrecise, 0.25) })
        {
            var items = DefaultTolerances.Create(lens, grade).Where(i => i.Tolerance is SurfaceIrregularity).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 6, 7 }, items.Select(i => i.Tolerance.Surface));
            Assert.All(items, i => Assert.Equal(fringes, i.Max));
        }
        var set = DefaultTolerances.Create(lens);
        var limits = ShopLimits.FromGrades(lens, set);
        int k = set.FindIndex(i => i.Tolerance is SurfaceIrregularity);
        Assert.Equal((0.25, 10.0), (limits[k].Tightest, limits[k].Loosest));
    }

    [Fact]
    public void ItIsReadAndWrittenAsTirr()
    {
        var (t, amount) = ToleranceSpec.Parse("TIRR:4:1.5");
        Assert.Equal(new SurfaceIrregularity(4), t);
        Assert.Equal(1.5, amount);
        var set = ToleranceSpec.ParseSet("TIRR 2 0.5\n");
        Assert.Equal("TIRR", set[0].Tolerance.Code);
        Assert.Contains("TIRR 2 0.5", ToleranceSpec.FormatSet(set));
    }

    [Fact]
    public void AZemaxFileCannotCarryIt()
    {
        var p = ToleranceTests.Read(ToleranceTests.Triplet).Perturb();
        new SurfaceIrregularity(3).Apply(p, 1.0);
        Assert.Throws<NotSupportedException>(() => ZemaxExport.ToZmx(p));
    }

    // --- Drawings ----------------------------------------------------------------------

    private static readonly DrawingSettings Settings = new() { Date = "2026-10-14" };

    [Fact]
    public void ADrawingStatesTheAllocatedIrregularity()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = ToleranceSpec.ParseSet("TFRN 3 4\nTIRR 3 0.6\nTFRN 4 4\n");
        var d = ElementDrawings.Build(lens, items, Settings)[1];
        Assert.Equal(0.6, d.Front.IrregularityFringes, 12);
        Assert.Equal(Settings.DefaultIrregularity, d.Back.IrregularityFringes);
        Assert.Contains(d.Warnings, w => w.Contains("surface 4: irregularity not toleranced"));
        Assert.Null(d.Front.RadiusTolerance);
    }

    [Fact]
    public void PowerPastWhatATestPlateReadsBecomesARadiusTolerance()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = ToleranceSpec.ParseSet("TFRN 1 30\nTIRR 1 2\nTFRN 2 8\nTIRR 2 2\n");
        var d = ElementDrawings.Build(lens, items, Settings)[0];

        // Surface 1: 30 fringes over its aperture, a sag of 30 λ/2 at its edge - as a radius, about 2R²Δs/h².
        double r = lens.System.Surfaces[1].Radius, h = PowerFringes.SemiDiameter(lens.Perturb(), 1);
        double approx = 2.0 * r * r * (30.0 * Lambda / 2.0) / (h * h);
        Assert.NotNull(d.Front.RadiusTolerance);
        Assert.InRange(d.Front.RadiusTolerance!.Value, 0.7 * approx, approx);
        Assert.Null(d.Back.RadiusTolerance);                        // 8 fringes stay fringes

        var iso = string.Join("\n", Layout.Draw(d, Settings).Marks.OfType<Label>().Select(l => l.Text));
        Assert.Contains($"±{d.Front.RadiusTolerance.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} CX", iso);
        Assert.Contains("3/-(2)", iso);
        Assert.Contains("3/8(2)", iso);

        var asme = Settings with { Standard = DrawingStandard.Asme };
        var us = ElementDrawings.Build(lens, items, asme)[0];
        var cells = Layout.Draw(us, asme).Marks.OfType<Label>().Select(l => l.Text).ToList();
        Assert.Contains("-", cells);                                // no power for surface 1
        Assert.Contains("8 FR", cells);
    }
}
