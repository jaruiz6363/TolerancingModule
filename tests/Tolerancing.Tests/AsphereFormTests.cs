using Tolerancing.Core;
using Tolerancing.Drawings;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>
/// Rotationally invariant irregularity (TRSI), ISO 10110-5's C: what it puts on a surface and how
/// it is read, allocated and drawn; and an asphere's sag table (ISO 10110-12).
/// </summary>
public class AsphereFormTests
{
    private const double Lambda = PowerFringes.HeNe * 1e-3;       // mm

    private static readonly DrawingSettings Settings = new() { Date = "2026-10-15" };

    /// <summary>The triplet with its first surface an even asphere.</summary>
    private static NominalLens Aspheric() => ToleranceTests.Read(ToleranceTests.Triplet, s =>
    {
        s.Surfaces[1].Conic = -0.8;
        s.Surfaces[1].AsphericCoefficients[1] = -2e-5;
        s.Surfaces[1].AsphericCoefficients[2] = 1e-7;
    });

    [Fact]
    public void ItIsAZoneHalfAWaveOfSagPerFringeTheSameAtEveryAzimuth()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var p = lens.Perturb();
        new SymmetricIrregularity(3).Apply(p, 2.0);
        var s = p.System.Surfaces[3];

        Assert.Equal(PowerFringes.SemiDiameter(p, 3), s.ZernikeRadius, 12);
        Assert.Equal(2.0 * Lambda / 2.0 / 1.5, s.FringeZernike[9], 15);
        Assert.Equal(-s.FringeZernike[9], s.FringeZernike[1], 15);
        Assert.All(s.FringeZernike.Where((_, i) => i != 1 && i != 9), c => Assert.Equal(0.0, c));

        double a = s.ZernikeRadius, lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i <= 400; i++)
        {
            double h = a * i / 400;
            double z = s.ZernikeSag(h, 0.0, out _, out _);
            Assert.Equal(z, s.ZernikeSag(0.0, h, out _, out _), 15);
            Assert.Equal(z, s.ZernikeSag(h * Math.Sqrt(0.5), h * Math.Sqrt(0.5), out _, out _), 15);
            Assert.Equal(2.0 * Lambda / 2.0 * SymmetricIrregularity.Shape(h, a), z, 15);
            (lo, hi) = (Math.Min(lo, z), Math.Max(hi, z));
        }
        Assert.Equal(2.0 * Lambda / 2.0, hi - lo, 1e-9);
        Assert.Equal(0.0, s.ZernikeSag(0.0, 0.0, out _, out _), 15);     // the vertex stays
    }

    [Fact]
    public void ItChangesTheTracedWavefrontButNotTheParaxialLens()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var p = lens.Perturb();
        foreach (int s in new[] { 1, 2, 3, 4, 6, 7 }) new SymmetricIrregularity(s).Apply(p, 5.0);
        Assert.Equal((double)lens.Model.Paraxial.Efl, (double)p.Build().Paraxial.Efl, 12);

        var criterion = new RmsWavefrontCriterion();
        var compensator = FocusCompensator.BackFocus(lens);
        double nominal = Sensitivity.Evaluate(lens, null, 0.0, criterion, compensator).Criterion;
        double up = Sensitivity.Evaluate(lens, new SymmetricIrregularity(3), 2.0, criterion, compensator).Criterion;
        double down = Sensitivity.Evaluate(lens, new SymmetricIrregularity(3), -2.0, criterion, compensator).Criterion;
        Assert.True(Math.Abs(up - nominal) > 0.01 && Math.Abs(down - nominal) > 0.01, $"{down} {nominal} {up}");
    }

    [Fact]
    public void ItIsReadAndWrittenAsTrsiAllocatedWithinTheIrregularityGradesAndNotInTheDefaultSet()
    {
        var items = ToleranceSpec.ParseSet("TRSI 1 0.5\nTRSI 7 -0.2 0.3\n");
        Assert.Equal(new SymmetricIrregularity(1), items[0].Tolerance);
        Assert.Equal(items, ToleranceSpec.ParseSet(ToleranceSpec.FormatSet(items)));
        Assert.Contains("TRSI 1 0.5\n", ToleranceSpec.FormatSet(items));

        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var limits = ShopLimits.FromGrades(lens, items);
        Assert.Equal((0.25, 10.0), (limits[0].Tightest, limits[0].Loosest));
        Assert.DoesNotContain(DefaultTolerances.Create(lens), i => i.Tolerance is SymmetricIrregularity);

        var p = lens.Perturb();
        new SymmetricIrregularity(1).Apply(p, 0.5);
        var e = Assert.Throws<NotSupportedException>(() => ZemaxExport.ToZmx(p));
        Assert.Contains("TRSI", e.Message);
    }

    [Fact]
    public void ItIsDrawnAsCInTheFormIndicationAndWarnedWhenMoreThanTheIrregularity()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = ToleranceSpec.ParseSet("TFRN 3 4\nTIRR 3 1\nTRSI 3 0.5\nTFRN 4 4\nTIRR 4 0.5\nTRSI 4 1\n");
        var d = ElementDrawings.Build(lens, items, Settings)[1];
        Assert.Equal(0.5, d.Front.SymmetricIrregularityFringes);
        Assert.Null(ElementDrawings.Build(lens, items.Take(2).ToList(), Settings)[1].Front.SymmetricIrregularityFringes);

        string Text(DrawingStandard standard) =>
            string.Join(" ", Layout.Draw(d, Settings with { Standard = standard }).Marks.OfType<Label>().Select(l => l.Text));
        Assert.Contains("3/4(1/0.5)", Text(DrawingStandard.Iso10110));
        Assert.Contains("3/4(0.5/1)", Text(DrawingStandard.Iso10110));
        Assert.Contains("ROTATIONALLY SYMMETRIC IRREGULARITY 0.5 FR MAX", Text(DrawingStandard.Asme));
        Assert.Contains(d.Warnings, w => w.StartsWith("surface 4: rotationally invariant irregularity 1 fringes is more than the irregularity"));
        Assert.DoesNotContain(d.Warnings, w => w.StartsWith("surface 3: rotationally"));
    }

    /// <summary>ISO 10110-6 writes an asphere's centering as 4/σ(L); the US form keeps its note.</summary>
    [Fact]
    public void AnAspheresDecenterIsLIn4SigmaL()
    {
        var lens = Aspheric();
        var items = ToleranceSpec.ParseSet("TSTX 1 0.05\nTSDX 1 0.01\n");
        var d = ElementDrawings.Build(lens, items, Settings)[0];
        string Text(DrawingStandard standard) =>
            string.Join(" ", Layout.Draw(d, Settings with { Standard = standard }).Marks.OfType<Label>().Select(l => l.Text));

        var iso = Text(DrawingStandard.Iso10110);
        Assert.Contains("4/3'(0.010)", iso);
        Assert.DoesNotContain(" L 0.010", iso);
        Assert.DoesNotContain("decenter 0.010", iso);
        Assert.Contains("4/3'", iso.Replace("4/3'(0.010)", ""));            // the spherical back surface: no L
        Assert.Contains("SURFACE 1 DECENTER 0.010 MAX TO THE DATUM AXIS", Text(DrawingStandard.Asme));
    }

    /// <summary>
    /// Power past what a test plate reads is drawn as a radius tolerance, rounded down; the table
    /// follows the radius as drawn, so it allows no more than the radius callout does.
    /// </summary>
    [Fact]
    public void WherePowerIsARadiusToleranceTheTableFollowsTheRadius()
    {
        var lens = Aspheric();
        var items = ToleranceSpec.ParseSet("TFRN 1 30\nTIRR 1 1\n");
        var front = ElementDrawings.Build(lens, items, Settings)[0].Front;
        double dr = front.RadiusTolerance!.Value;

        var nominal = lens.System.Surfaces[1];
        double h = front.SagTable[^1].H, power = 0.0;
        foreach (int sign in new[] { -1, 1 })
        {
            var p = lens.Perturb();
            p.System.Surfaces[1].Curvature = 1.0 / (nominal.Radius + sign * dr);
            power = Math.Max(power, Math.Abs(p.System.Surfaces[1].Sag(h) - nominal.Sag(h)));
        }
        Assert.Equal((power + Lambda / 8.0) * 1000.0, front.SagTable[^1].DeltaZUm, 1e-9);
        Assert.True(power < 30.0 * Lambda / 2.0, $"{power} against {30.0 * Lambda / 2.0}");     // rounded down
    }

    /// <summary>The settings' slope is a note on each aspheric surface, warned of when tighter than the table allows.</summary>
    [Fact]
    public void ASlopeSettingIsANoteOnEachAsphere()
    {
        var lens = Aspheric();
        var items = ToleranceSpec.ParseSet("TFRN 1 3\nTIRR 1 1\nTRSI 1 0.5\n");
        string Text(DrawingSettings settings, DrawingStandard standard)
        {
            var d = ElementDrawings.Build(lens, items, settings)[0];
            return string.Join(" ", Layout.Draw(d, settings with { Standard = standard }).Marks.OfType<Label>().Select(l => l.Text));
        }

        var loose = Settings with { AsphereSlopeMrad = 1.0, AsphereSlopeSamplingMm = 1.0 };
        Assert.Contains("Surface 1 slope deviation max 1 mrad over a sampling length of 1 mm (ISO 10110-5).", Text(loose, DrawingStandard.Iso10110));
        Assert.Contains("SURFACE 1 SLOPE DEVIATION MAX 1 MRAD", Text(loose, DrawingStandard.Asme));
        Assert.DoesNotContain("Surface 2 slope", Text(loose, DrawingStandard.Iso10110));       // a sphere
        Assert.DoesNotContain("slope deviation", Text(Settings, DrawingStandard.Iso10110));
        Assert.Contains("max 0.005 mrad", Text(Settings with { AsphereSlopeMrad = 0.005 }, DrawingStandard.Iso10110));

        var tableMax = ElementDrawings.Build(lens, items, Settings)[0].Front.SagTable.Max(r => r.DeltaSlopeMrad);
        Assert.DoesNotContain(ElementDrawings.Build(lens, items, loose)[0].Warnings, w => w.Contains("slope"));
        var tight = ElementDrawings.Build(lens, items, Settings with { AsphereSlopeMrad = tableMax / 2 })[0];
        Assert.Contains(tight.Warnings, w => w.StartsWith("surface 1: a slope deviation of"));
    }

    [Theory]
    [InlineData(7.0, new[] { 0.0, 1, 2, 3, 4, 5, 6, 7 })]
    [InlineData(4.5, new[] { 0.0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5 })]
    [InlineData(10.0, new[] { 0.0, 2, 4, 6, 8, 10 })]
    [InlineData(0.9, new[] { 0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9 })]
    public void TheTablesHeightsAreRoundStepsToTheClearAperture(double sd, double[] heights)
    {
        var got = ElementDrawings.Heights(sd);
        Assert.Equal(heights.Length, got.Count);
        for (int i = 0; i < heights.Length; i++) Assert.Equal(heights[i], got[i], 12);
        Assert.True(got.Count <= ElementDrawings.SagRows);
    }

    [Fact]
    public void AnAsphereHasASagTableAndASphereNone()
    {
        var lens = Aspheric();
        var d = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), Settings)[0];
        var table = d.Front.SagTable;
        Assert.Equal(ElementDrawings.Heights(7.0), table.Select(r => r.H));
        Assert.All(table, r => Assert.Equal(lens.System.Surfaces[1].Sag(r.H), r.Z, 15));
        Assert.Equal(0.0, table[0].DeltaZUm);
        Assert.Empty(d.Back.SagTable);

        var text = string.Join(" ", Layout.Draw(d, Settings).Marks.OfType<Label>().Select(l => l.Text));
        Assert.Contains("Surface 1 sag", text);
        Assert.Contains("±Δz µm", text);
        Assert.Contains("Sag tables:", text);
        Assert.DoesNotContain("Surface 2 sag", text);
    }

    /// <summary>
    /// Δz is what the analysis traced: of every sign of power, irregularity and its rotationally
    /// invariant part, each at its full tolerance, the most the sag moves at each height - along x
    /// or y, where the astigmatic half is largest - is Δz, neither more nor less.
    /// </summary>
    [Fact]
    public void DeltaZIsTheMostTheTracedFormErrorsMoveTheSag()
    {
        var lens = Aspheric();
        var items = ToleranceSpec.ParseSet("TFRN 1 3\nTIRR 1 1\nTRSI 1 0.5\n");
        var table = ElementDrawings.Build(lens, items, Settings)[0].Front.SagTable;
        var nominal = lens.System.Surfaces[1];

        var most = new double[table.Count];
        foreach (int a in new[] { -1, 1 })
            foreach (int b in new[] { -1, 1 })
                foreach (int c in new[] { -1, 1 })
                {
                    var p = lens.Perturb();
                    new PowerFringes(1).Apply(p, 3.0 * a);
                    new SurfaceIrregularity(1).Apply(p, 1.0 * b);
                    new SymmetricIrregularity(1).Apply(p, 0.5 * c);
                    var s = p.System.Surfaces[1];
                    for (int i = 0; i < table.Count; i++)
                    {
                        double h = table[i].H;
                        foreach (var (x, y) in new[] { (h, 0.0), (0.0, h) })
                        {
                            double dz = s.Sag(h) + s.ZernikeSag(x, y, out _, out _) - nominal.Sag(h);
                            most[i] = Math.Max(most[i], Math.Abs(dz) * 1000.0);
                        }
                    }
                }
        for (int i = 0; i < table.Count; i++) Assert.Equal(most[i], table[i].DeltaZUm, 1e-9);

        // Δs likewise: the steepest the same surfaces tilt, by their gradient on the x and y axes.
        var steepest = new double[table.Count];
        const double e = 1e-4;
        foreach (int a in new[] { -1, 1 })
            foreach (int b in new[] { -1, 1 })
                foreach (int c in new[] { -1, 1 })
                {
                    var p = lens.Perturb();
                    new PowerFringes(1).Apply(p, 3.0 * a);
                    new SurfaceIrregularity(1).Apply(p, 1.0 * b);
                    new SymmetricIrregularity(1).Apply(p, 0.5 * c);
                    var s = p.System.Surfaces[1];
                    double D(double x, double y)
                    {
                        double r = Math.Sqrt(x * x + y * y);
                        return s.Sag(r) + s.ZernikeSag(x, y, out _, out _) - nominal.Sag(r);
                    }
                    for (int i = 0; i < table.Count; i++)
                    {
                        double h = table[i].H;
                        foreach (var (x, y) in new[] { (h, 0.0), (0.0, h) })
                        {
                            double gx = (D(x + e, y) - D(x - e, y)) / (2 * e), gy = (D(x, y + e) - D(x, y - e)) / (2 * e);
                            steepest[i] = Math.Max(steepest[i], Math.Sqrt(gx * gx + gy * gy) * 1000.0);
                        }
                    }
                }
        for (int i = 0; i < table.Count; i++) Assert.Equal(steepest[i], table[i].DeltaSlopeMrad, 1e-6);

        // At the edge the zone has no sag: power's half wave a fringe, and the astigmatic half's λ/8.
        Assert.Equal((3.0 * Lambda / 2.0 + Lambda / 8.0) * 1000.0, table[^1].DeltaZUm, 1e-6);
    }
}
