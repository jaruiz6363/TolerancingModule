using Tolerancing.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>
/// Compensators beside the focus (<see cref="FocusCompensator.Adjustments"/>): the joint solve
/// finds what undoes an error exactly, does better than focus alone, keeps to a travel, and a
/// focus alone is solved as it always was.
/// </summary>
public class AdjustmentTests
{
    private static readonly RmsWavefrontCriterion Rms = new();

    private static (NominalLens Lens, double Nominal) Triplet()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet, symmetric: true);
        return (lens, Sensitivity.Evaluate(lens, null, 0.0, Rms, FocusCompensator.BackFocus(lens)).Criterion);
    }

    [Fact]
    public void ADecenterOfTheElementItselfUndoesItsDecenter()
    {
        var (lens, nominal) = Triplet();
        var compensator = FocusCompensator.BackFocus(lens) with
        {
            Adjustments = new Adjustment[] { new ElementDecenterAdjustment(6, 7, Axis.X), new ElementDecenterAdjustment(6, 7, Axis.Y) },
        };
        var r = Sensitivity.Evaluate(lens, new ElementDecentre(6, 7, Axis.Y), 0.1, Rms, compensator);

        Assert.Equal(nominal, r.Criterion, 4);
        Assert.Equal(0.0, r.Adjustments![0], 4);
        Assert.Equal(-0.1, r.Adjustments[1], 4);
    }

    [Fact]
    public void ATiltOfTheElementItselfUndoesItsTilt()
    {
        var (lens, nominal) = Triplet();
        var compensator = FocusCompensator.BackFocus(lens) with
        {
            Adjustments = new Adjustment[] { new ElementTiltAdjustment(3, 4, Axis.X), new ElementTiltAdjustment(3, 4, Axis.Y) },
        };
        var r = Sensitivity.Evaluate(lens, new ElementTilt(3, 4, Axis.X), 0.1, Rms, compensator);

        // Back to the nominal lens, or a hair better: a tilt this small trades the two sides of the
        // field against each other by 1e-4 waves, so the nominal is not quite the minimum in tilt.
        Assert.InRange(r.Criterion, nominal - 1e-3, nominal + 1e-4);
        Assert.Equal(-0.1, r.Adjustments![0], 0.01);
    }

    [Fact]
    public void ATiltCompensatesAnotherElementsDecenterBetterThanFocusAlone()
    {
        var (lens, nominal) = Triplet();
        var focus = FocusCompensator.BackFocus(lens);
        var withTilt = focus with { Adjustments = new Adjustment[] { new ElementTiltAdjustment(6, 7, Axis.X), new ElementTiltAdjustment(6, 7, Axis.Y) } };
        var error = new ElementDecentre(3, 4, Axis.Y);

        double alone = Sensitivity.Evaluate(lens, error, 0.1, Rms, focus).Criterion;
        var both = Sensitivity.Evaluate(lens, error, 0.1, Rms, withTilt);

        Assert.True(both.Criterion < alone - 0.05, $"focus alone {alone}, with a tilt {both.Criterion}");
        Assert.True(both.Criterion > nominal);
    }

    [Fact]
    public void AnAdjustmentKeepsToItsTravel()
    {
        var (lens, _) = Triplet();
        var compensator = FocusCompensator.BackFocus(lens) with
        {
            Adjustments = new Adjustment[] { new ElementDecenterAdjustment(6, 7, Axis.Y) { Travel = 0.03 } },
        };
        var r = Sensitivity.Evaluate(lens, new ElementDecentre(6, 7, Axis.Y), 0.1, Rms, compensator);

        Assert.Equal(-0.03, r.Adjustments![0], 9);
        Assert.True(r.AtLimit);
    }

    [Fact]
    public void AnotherAirSpaceRefocusesWithTheBackFocus()
    {
        var (lens, _) = Triplet();
        var focus = FocusCompensator.BackFocus(lens);
        var both = focus with { Adjustments = new Adjustment[] { new SpaceAdjustment(4) } };
        var error = new ThicknessChange(2);

        double alone = Sensitivity.Evaluate(lens, error, 0.2, Rms, focus).Criterion;
        var r = Sensitivity.Evaluate(lens, error, 0.2, Rms, both);

        Assert.True(r.Criterion < alone, $"back focus alone {alone}, with space 4 {r.Criterion}");
        Assert.NotEqual(0.0, r.Adjustments![0]);
    }

    [Fact]
    public void AnImageTiltUndoesTheImageTiltAnElementTiltMakes()
    {
        // Image tilt alone, as the evaluation sees it: tilting the plane changes the off-axis fields.
        var (lens, nominal) = Triplet();
        var p = lens.Perturb();
        new ImageTiltAdjustment(Axis.X).Apply(p, 0.5);
        Assert.True(Rms.Evaluate(p.Build()) > Rms.Evaluate(lens.Model) + 0.01);

        // A tilted element tilts the image; an image tilt compensates part of it, the rest being coma.
        var focus = FocusCompensator.BackFocus(lens);
        var withImage = focus with { Adjustments = new Adjustment[] { new ImageTiltAdjustment(Axis.X), new ImageTiltAdjustment(Axis.Y) } };
        var error = new ElementTilt(6, 7, Axis.X);
        double alone = Sensitivity.Evaluate(lens, error, 0.3, Rms, focus).Criterion;
        var r = Sensitivity.Evaluate(lens, error, 0.3, Rms, withImage);
        Assert.True(r.Criterion < alone, $"focus alone {alone}, with an image tilt {r.Criterion}");
        Assert.NotEqual(0.0, r.Adjustments![0]);
    }

    [Fact]
    public void AFocusAloneIsSolvedAsItAlwaysWas()
    {
        var (lens, _) = Triplet();
        var focus = FocusCompensator.BackFocus(lens);
        var r = Sensitivity.Evaluate(lens, new ThicknessChange(4), 0.2, Rms, focus);
        Assert.Null(r.Adjustments);
    }
}

/// <summary>Each field judged on both sides of the axis (<see cref="NominalLens.MirrorFields"/>).</summary>
public class SymmetricFieldTests
{
    [Fact]
    public void EachOffAxisFieldGetsItsMirrorImageAndHalfItsWeight()
    {
        var file = ToleranceTests.Read(ToleranceTests.Triplet);
        var both = ToleranceTests.Read(ToleranceTests.Triplet, symmetric: true);
        var y = file.System.Fields.Select(f => f.Y).ToList();

        Assert.Equal(file.System.Fields.Count, both.FileFieldCount);
        Assert.Equal(y.Concat(y.Where(v => v != 0.0).Select(v => -v)), both.System.Fields.Select(f => f.Y));
        Assert.Equal(file.System.Fields.Sum(f => f.Weight), both.System.Fields.Sum(f => f.Weight), 12);
    }

    [Fact]
    public void TheNominalLensAndASymmetricErrorAreJudgedAsBefore()
    {
        var file = ToleranceTests.Read(ToleranceTests.Triplet);
        var both = ToleranceTests.Read(ToleranceTests.Triplet, symmetric: true);
        var rms = new RmsWavefrontCriterion();
        Assert.Equal(rms.Evaluate(file.Model), rms.Evaluate(both.Model), 9);

        double Thick(NominalLens lens) => Sensitivity.Evaluate(lens, new ThicknessChange(4), 0.2, rms, FocusCompensator.BackFocus(lens)).Criterion;
        Assert.Equal(Thick(file), Thick(both), 6);
    }

    [Fact]
    public void ADecenterInYCostsAlikeEitherWay()
    {
        // Judged on one side, a y decenter helps at one sign and hurts at the other; on both, it hurts either way, about alike.
        var both = ToleranceTests.Read(ToleranceTests.Triplet, symmetric: true);
        var rms = new RmsWavefrontCriterion();
        var focus = FocusCompensator.BackFocus(both);
        double nominal = Sensitivity.Evaluate(both, null, 0.0, rms, focus).Criterion;
        double up = Sensitivity.Evaluate(both, new ElementDecentre(3, 4, Axis.Y), 0.1, rms, focus).Criterion;
        double down = Sensitivity.Evaluate(both, new ElementDecentre(3, 4, Axis.Y), -0.1, rms, focus).Criterion;
        Assert.True(up > nominal && down > nominal, $"{down} {nominal} {up}");
        Assert.InRange(up / down, 0.9, 1.1);
    }
}

/// <summary>
/// Fields on the x axis, traced on the lens turned a quarter turn (<see cref="QuarterTurn"/>). The
/// sign conventions are pinned by a ray: a skew ray through a lens decentered, tilted and figured
/// lands where the turned ray through the turned lens lands, turned back. And with fields on both
/// axes the set is itself turned into itself by a quarter turn, so an error in x costs what the same
/// error in y does.
/// </summary>
public class XyFieldTests
{
    private static NominalLens Xy()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var system = SystemCopy.Of(lens.System);
        NominalLens.MirrorFields(system, xy: true);
        return new NominalLens(system, lens.Catalog);
    }

    [Fact]
    public void TheTurnedLensTracesTheTurnedRay()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var p = lens.Perturb();
        new SurfaceDecentre(3, Axis.X).Apply(p, 0.03);
        new SurfaceDecentre(3, Axis.Y).Apply(p, -0.02);
        new SurfaceTilt(4, Axis.X).Apply(p, 0.2);
        new SurfaceTilt(4, Axis.Y).Apply(p, -0.1);
        new ElementTilt(6, 7, Axis.Y).Apply(p, 0.15);
        new SurfaceIrregularity(2).Apply(p, 3.0);
        var model = p.Build();
        model.System.Surfaces[2].FringeZernike[6] = 2e-4;              // oblique astigmatism too
        model.System.Surfaces[2].FringeZernike[7] = 1e-4;              // and coma
        model = new WavefrontErrorCalculator.Core.LensModel(model.System, lens.Catalog, w => p.Indices[w]);
        var turned = QuarterTurn.Turn(model, Array.Empty<int>());
        int w0 = model.PrimaryWavelength;

        foreach (var (py, pz) in new[] { (0.5, 0.3), (-0.7, 0.2), (0.1, -0.8), (0.0, 0.9) })
        {
            var a = AberrationCalculator.Core.RayTrace.RealRayTrace.Trace(model.System, model.Indices(w0), model.Paraxial, 0.0, py, pz, false);
            // The pupil point (x, y) = (pz, py) turned through +90° is (−py, pz).
            var b = AberrationCalculator.Core.RayTrace.RealRayTrace.Trace(turned.System, turned.Indices(w0), turned.Paraxial, 0.0, pz, -py, false);
            Assert.True(a.Ok && b.Ok);
            // The landing (x, y) = (a.Z, a.Y) turned is (−a.Y, a.Z) = (b.Z, b.Y): to 1e-6 mm, the product of
            // the doubly tilted surface's two tilts, which a quarter turn takes in the other order. A sign
            // the wrong way would miss by a hundredth of a millimeter.
            Assert.Equal(-a.Y, b.Z, 1e-6);
            Assert.Equal(a.Z, b.Y, 1e-6);
        }
    }

    [Fact]
    public void FieldsOnTheXAxisAreAddedAndShareTheWeight()
    {
        var lens = Xy();
        var file = ToleranceTests.Read(ToleranceTests.Triplet);
        Assert.Equal(file.System.Fields.Count + 3 * file.System.Fields.Count(f => f.Y != 0.0), lens.System.Fields.Count);
        Assert.Equal(file.System.Fields.Sum(f => f.Weight), lens.System.Fields.Sum(f => f.Weight), 12);
        var rms = new RmsWavefrontCriterion();
        Assert.Equal(rms.Evaluate(file.Model), rms.Evaluate(lens.Model), 9);
    }

    [Theory]
    [InlineData("TEDX:3-4:0.07", "TEDY:3-4:0.07")]
    [InlineData("TETX:6-7:0.2", "TETY:6-7:0.2")]
    [InlineData("TSTX:4:0.1", "TSTY:4:0.1")]
    [InlineData("TSDX:2:0.05", "TSDY:2:0.05")]
    public void AnErrorInXCostsWhatTheSameErrorInYDoes(string x, string y)
    {
        var lens = Xy();
        var rms = new RmsWavefrontCriterion();
        var focus = FocusCompensator.BackFocus(lens);
        double Cost(string spec)
        {
            var (t, a) = ToleranceSpec.Parse(spec);
            return Sensitivity.Evaluate(lens, t, a, rms, focus).Criterion;
        }
        Assert.Equal(Cost(y), Cost(x), 6);
        Assert.Equal(Cost(y.Replace(":0.", ":-0.")), Cost(x.Replace(":0.", ":-0.")), 6);
    }

    [Fact]
    public void AnImageTiltInXCostsWhatOneInYDoes()
    {
        var lens = Xy();
        var rms = new RmsWavefrontCriterion();
        double Cost(Axis axis)
        {
            var p = lens.Perturb();
            new ImageTiltAdjustment(axis).Apply(p, 2.0);
            return rms.Evaluate(p.Build());
        }
        Assert.True(Cost(Axis.X) > rms.Evaluate(lens.Model) + 0.01);
        Assert.Equal(Cost(Axis.X), Cost(Axis.Y), 6);
    }
}
