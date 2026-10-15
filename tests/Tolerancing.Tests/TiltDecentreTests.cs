using AberrationCalculator.Core.IO;
using AberrationCalculator.Core.RayTrace;
using Tolerancing.Core;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace Tolerancing.Tests;

public class PlacementTests
{
    private static void AssertSame(Placement a, Placement b, int digits = 13)
    {
        for (int i = 0; i < 9; i++) Assert.Equal(a.R[i], b.R[i], digits);
        Assert.Equal(a.Dx, b.Dx, digits);
        Assert.Equal(a.Dy, b.Dy, digits);
        Assert.Equal(a.Dz, b.Dz, digits);
    }

    [Fact]
    public void TheIdentityChangesNothingEitherWayRound()
    {
        var m = Placement.TiltAbout(0.01, -0.02, 3.0).Within(Placement.Decentre(0.1, -0.2, 0.05));
        AssertSame(m, m.Within(Placement.Identity));
        AssertSame(m, Placement.Identity.Within(m));
    }

    [Fact]
    public void ATiltAboutTheVertexIsATiltAboutAPivotAtTheVertex()
    {
        AssertSame(Placement.Tilt(0.03, 0.01), Placement.TiltAbout(0.03, 0.01, 0.0));
    }

    [Fact]
    public void TiltsAboutOnePivotAddLikeAnglesInOnePlane()
    {
        // Two turns about the same pivot and axis are one turn by the sum - the pivot's
        // construction and the composition have to agree for this to hold exactly.
        var twice = Placement.TiltAbout(0.02, 0.0, 7.0).Within(Placement.TiltAbout(0.03, 0.0, 7.0));
        AssertSame(Placement.TiltAbout(0.05, 0.0, 7.0), twice);
    }

    [Fact]
    public void AMoveAndItsInverseCancel()
    {
        var move = Placement.TiltAbout(0.02, -0.01, 4.0).Within(Placement.Decentre(0.3, 0.1, -0.2));
        // The inverse of p ↦ R(p − d) is p ↦ Rᵀ(p + R d).
        var r = move.R;
        var rt = new[] { r[0], r[3], r[6], r[1], r[4], r[7], r[2], r[5], r[8] };
        var inverse = new Placement(rt,
            -(r[0] * move.Dx + r[1] * move.Dy + r[2] * move.Dz),
            -(r[3] * move.Dx + r[4] * move.Dy + r[5] * move.Dz),
            -(r[6] * move.Dx + r[7] * move.Dy + r[8] * move.Dz));
        AssertSame(Placement.Identity, inverse.Within(move), 12);
    }

    [Fact]
    public void ASurfaceGetsTheTiltsThatGiveItsAxis()
    {
        var surface = new AberrationCalculator.Core.Models.Surface();
        Placement.Tilt(0.012, -0.034).ApplyTo(surface);
        Assert.Equal(0.012, surface.TiltX, 14);
        Assert.Equal(-0.034, surface.TiltY, 14);
    }
}

public class TiltDecentreTests
{
    private static (double X, double Y) Chief(LensModel model, int field)
    {
        var system = model.System;
        int w = model.PrimaryWavelength;
        var hit = RealRayTrace.Trace(system, model.Indices(w), model.Paraxial, system.Fields[field].Y, 0.0, 0.0,
                                     atParaxialFocus: false);
        Assert.True(hit.Ok);
        return (hit.Z, hit.Y);
    }

    private static PerturbedLens Apply(NominalLens lens, params string[] specs)
    {
        var perturbed = lens.Perturb();
        foreach (var s in specs)
        {
            var (t, amount) = ToleranceSpec.Parse(s);
            t.Apply(perturbed, amount);
        }
        return perturbed;
    }

    /// <summary>
    /// A surface turned about its own centre of curvature is the same sphere, so an element "tilt"
    /// of that one surface with the pivot there changes nothing at all.
    /// </summary>
    [Theory]
    [InlineData(3, Axis.X, 0.4)]
    [InlineData(6, Axis.Y, -0.7)]
    public void ASphereTurnedAboutItsCentreOfCurvatureChangesNothing(int surface, Axis axis, double degrees)
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        double radius = lens.System.Surfaces[surface].Radius;
        var perturbed = lens.Perturb();

        new ElementTilt(surface, surface, axis, PivotOffset: radius).Apply(perturbed, degrees);

        var criterion = new RmsWavefrontCriterion();
        Assert.True(perturbed.Build().System.Surfaces[surface].IsPerturbed);
        Assert.Equal(criterion.Evaluate(lens.Model), criterion.Evaluate(perturbed.Build()), 9);
    }

    [Fact]
    public void TheWholeLensMovedOverMovesTheImageOver()
    {
        // Every surface, the stop included, decentred by d, and the image plane not: the pupil the
        // rays are launched at moves with the lens, so this is checked on rays launched there.
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var moved = Apply(lens, "TEDY:1-7:0.05").Build();
        var nominal = lens.Model;
        int w = nominal.PrimaryWavelength;
        foreach (double py in new[] { 0.0, 0.5, -0.8 })
        {
            var a = RealRayTrace.TraceFrom(nominal.System, nominal.Indices(w), nominal.Paraxial, 0.0, py * 5.0, 0.0, 0.0, 1.0, false);
            var b = RealRayTrace.TraceFrom(moved.System, moved.Indices(w), moved.Paraxial, 0.0, py * 5.0 + 0.05, 0.0, 0.0, 1.0, false);
            Assert.Equal(a.Y + 0.05, b.Y, 10);
            Assert.Equal(a.Z, b.Z, 10);
        }
    }

    [Fact]
    public void AnXDecentreOfASymmetricLensIsTheSameEitherWay()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var criterion = new RmsWavefrontCriterion();
        Assert.Equal(criterion.Evaluate(Apply(lens, "TEDX:3-4:0.07").Build()),
                     criterion.Evaluate(Apply(lens, "TEDX:3-4:-0.07").Build()), 10);
        Assert.Equal(criterion.Evaluate(Apply(lens, "TSTY:2:0.1").Build()),
                     criterion.Evaluate(Apply(lens, "TSTY:2:-0.1").Build()), 10);
    }

    [Fact]
    public void TheDefaultSetTiltsAndDecentresEveryElementAndWedgesEverySurface()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        Assert.Equal(new[] { (1, 2), (3, 4), (6, 7) }, DefaultTolerances.Elements(lens));

        var items = DefaultTolerances.Create(lens, ShopGrade.Commercial);
        Assert.Equal(12, items.Count(i => i.Tolerance is SurfaceTilt));
        Assert.Equal(0, items.Count(i => i.Tolerance is SurfaceDecentre));       // all spheres
        Assert.Equal(6, items.Count(i => i.Tolerance is ElementDecentre));
        Assert.Equal(6, items.Count(i => i.Tolerance is ElementTilt));
        Assert.All(items.Where(i => i.Tolerance is SurfaceTilt), i => Assert.Equal(0.001 * 180.0 / Math.PI, i.Max, 12));
    }

    [Theory]
    [InlineData("TFRN:3:10", typeof(PowerFringes), 3, 10.0)]
    [InlineData("tsty:2:-0.05", typeof(SurfaceTilt), 2, -0.05)]
    [InlineData("TETX:6-7:0.1", typeof(ElementTilt), 6, 0.1)]
    public void ASpecReadsAsItsTolerance(string text, Type type, int surface, double amount)
    {
        var (t, a) = ToleranceSpec.Parse(text);
        Assert.IsType(type, t);
        Assert.Equal(surface, t.Surface);
        Assert.Equal(amount, a);
    }

    [Theory]
    [InlineData("TFRN:3")]
    [InlineData("TFRN:3-4:1")]
    [InlineData("TETX:7-6:0.1")]
    [InlineData("XXXX:1:1")]
    public void ABadSpecIsRefused(string text) => Assert.Throws<FormatException>(() => ToleranceSpec.Parse(text));

    [Fact]
    public void TheExportOfTheUnperturbedLensReadsBackAsTheSameLens()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        string path = Path.Combine(Path.GetTempPath(), $"tol-export-{Guid.NewGuid():N}.zmx");
        try
        {
            ZemaxExport.Write(lens.Perturb(), path);
            var back = new NominalLens(LensFile.Read(path, lens.Catalog), lens.Catalog);
            var criterion = new RmsWavefrontCriterion();
            Assert.Equal(criterion.Evaluate(lens.Model), criterion.Evaluate(back.Model), 12);
            Assert.Equal(lens.Model.Paraxial.Efl, back.Model.Paraxial.Efl, 12);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnIndexChangeCannotBeExported()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var perturbed = Apply(lens, "TIND:1:0.001");
        Assert.Throws<NotSupportedException>(() => ZemaxExport.ToZmx(perturbed));
    }

    /// <summary>
    /// The test triplet with a wedge in x and y on surface 2, surface 6 decentred both ways, the
    /// first element tilted about its first vertex, the last decentred and tilted - surface 2 moved
    /// both on its element and with it - exported with <see cref="ZemaxExport"/> and traced in
    /// OpticStudio on 2026-10-12: the chief ray launched at the paraxial pupil centre, primary λ.
    /// </summary>
    private static readonly string[] Everything =
        { "TSTX:2:0.3", "TSTY:2:0.5", "TSDX:6:0.04", "TSDY:6:-0.03", "TETX:1-2:0.2", "TEDY:6-7:0.05", "TETY:6-7:-0.15" };

    [Fact]
    public void TheAxialChiefRayAgreesWithOpticStudio()
    {
        var model = Apply(ToleranceTests.Read(ToleranceTests.Triplet), Everything).Build();
        var (x, y) = Chief(model, 0);
        Assert.Equal(-0.26104043769594576, x, 1e-10);
        Assert.Equal(0.23379205715548057, y, 1e-10);
    }

    [Fact]
    public void TheEdgeOfTheFieldAgreesWithOpticStudio()
    {
        var model = Apply(ToleranceTests.Read(ToleranceTests.Triplet), Everything).Build();
        // 3e-8 and 2.5e-7 mm off, where the perturbations moved the ray 0.28 mm: OpticStudio's
        // paraxial pupil for the perturbed lens, against the nominal one this traces from.
        var (x, y) = Chief(model, 2);
        Assert.Equal(-0.28413340700964512, x, 1e-7);
        Assert.Equal(12.834491830601108, y, 1e-6);
    }

    [Fact]
    public void ATiltAboutXHasOpticStudiosSign()
    {
        // TSTX on surface 2 alone: exact to the last digit OpticStudio prints.
        var model = Apply(ToleranceTests.Read(ToleranceTests.Triplet), "TSTX:2:0.3").Build();
        Assert.Equal(12.736087702005491, Chief(model, 2).Y, 1e-10);
    }

    [Theory]
    [InlineData(2, 2, 2.4396504)]      // field 3, C: OpticStudio Zernike, 1024², RMS about the mean
    [InlineData(1, 0, 2.6106124)]      // field 2, F
    public void TheWavefrontOfEachFieldAgreesWithOpticStudio(int field, int wave, double opticStudio)
    {
        var model = Apply(ToleranceTests.Read(ToleranceTests.Triplet), Everything).Build();
        var criterion = new RmsWavefrontCriterion { Options = WavefrontOptions.ZemaxZernike };

        var s = WavefrontCalculator.Compute(model, field, wave, criterion.EffectiveOptions, criterion.Sampling).Statistics;

        Assert.Equal(opticStudio, s.RmsStandardDeviation, opticStudio * 2e-4);
    }
}
