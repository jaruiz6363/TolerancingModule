using Tolerancing.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>Focal length, back focus and boresight as criteria: departures from the refocused nominal lens.</summary>
public class CriteriaTests
{
    private static readonly RmsWavefrontCriterion Rms = new();

    private static NominalLens Triplet() => ToleranceTests.Read(ToleranceTests.Triplet);

    [Fact]
    public void TheFocalLengthsChangeIsTheParaxialOnes()
    {
        var lens = Triplet();
        var efl = (EflCriterion)Criteria.Create("efl", Rms, lens, null);
        Assert.Equal((double)lens.Model.Paraxial.Efl, efl.Reference[0], 12);

        var perturbed = lens.Perturb();
        new ThicknessChange(3).Apply(perturbed, 0.1);
        double moved = (double)perturbed.Build().Paraxial.Efl - efl.Reference[0];
        var r = Sensitivity.Evaluate(lens, new ThicknessChange(3), 0.1, efl, null);
        Assert.Equal(Math.Abs(moved), r.Criterion, 12);
        Assert.Equal(moved, r.Deviation![0], 12);                  // signed, so cross terms can be taken of it
    }

    [Fact]
    public void TheBackFocusChangeIsTheParaxialOnes()
    {
        var lens = Triplet();
        var bfl = Criteria.Create("bfl", Rms, lens, null);
        var perturbed = lens.Perturb();
        new IndexChange(1).Apply(perturbed, 0.002);
        double moved = (double)perturbed.Build().Paraxial.Bfl - (double)lens.Model.Paraxial.Bfl;
        Assert.Equal(Math.Abs(moved), Sensitivity.Evaluate(lens, new IndexChange(1), 0.002, bfl, null).Criterion, 12);
    }

    [Fact]
    public void MovingTheWholeLensMovesTheAxialImageAsFar()
    {
        // Object at infinity, image plane fixed: the lens and its stop moved 0.05 mm across take the
        // image with them, exactly.
        var lens = Triplet();
        var boresight = Criteria.Create("boresight-mm", Rms, lens, null);
        var whole = new ElementDecentre(1, lens.BackFocusSurface, Axis.Y);
        var r = Sensitivity.Evaluate(lens, whole, 0.05, boresight, null);
        Assert.Equal(0.0, r.Deviation![0], 9);
        Assert.Equal(0.05, r.Deviation[1], 6);
    }

    /// <summary>
    /// By default boresight is the line of sight's angle: the same 0.05 mm at the image of an object
    /// at infinity, over the focal length, in degrees.
    /// </summary>
    [Fact]
    public void BoresightIsTheLineOfSightsAngleInDegrees()
    {
        var lens = Triplet();
        var boresight = (BoresightCriterion)Criteria.Create("boresight", Rms, lens, null);
        Assert.Equal("deg", boresight.Units);
        Assert.Equal((double)lens.Model.Paraxial.Efl, Math.Abs(BoresightCriterion.ImageHeightPerAngle(lens.Model)), 9);

        var whole = new ElementDecentre(1, lens.BackFocusSurface, Axis.Y);
        var r = Sensitivity.Evaluate(lens, whole, 0.05, boresight, null);
        Assert.Equal(0.0, r.Deviation![0], 9);
        Assert.Equal(0.05 / (double)lens.Model.Paraxial.Efl * 180.0 / Math.PI, r.Deviation[1], 6);
    }

    [Fact]
    public void ACompensatorRefocusesForTheSharpestImageNotForTheDeparture()
    {
        var lens = Triplet();
        var compensator = FocusCompensator.BackFocus(lens);
        var efl = Criteria.Create("efl", Rms, lens, compensator);
        var byEfl = Sensitivity.Evaluate(lens, new ThicknessChange(3), 0.1, efl, compensator);
        var byRms = Sensitivity.Evaluate(lens, new ThicknessChange(3), 0.1, Rms, compensator);
        Assert.Equal(byRms.Change, byEfl.Change, 9);
    }

    [Fact]
    public void CrossTermsFromTheSlopesAreThoseOfTwoErrorsTracedTogether()
    {
        var lens = Triplet();
        var compensator = FocusCompensator.BackFocus(lens);
        var boresight = Criteria.Create("boresight", Rms, lens, compensator);
        var items = new[]
        {
            ToleranceItem.Symmetric(new ElementDecentre(1, 2, Axis.Y), 0.05),
            ToleranceItem.Symmetric(new ElementDecentre(3, 4, Axis.Y), 0.05),
            ToleranceItem.Symmetric(new ElementTilt(6, 7, Axis.X), 0.05),
        };
        var table = Sensitivity.Run(lens, items, boresight, compensator);
        var derived = QuadraticModel.DeviationCross(table);
        for (int i = 0; i < items.Length; i++)
            for (int j = i + 1; j < items.Length; j++)
            {
                var a = table.Rows[i];
                var b = table.Rows[j];
                double both = Sensitivity.Evaluate(lens, new[] { (a.Item.Tolerance, a.Item.Max), (b.Item.Tolerance, b.Item.Max) },
                                                   boresight, compensator).Criterion;
                double traced = (both * both - a.MaxCriterion * a.MaxCriterion - b.MaxCriterion * b.MaxCriterion
                                 + table.NominalCompensated * table.NominalCompensated) / (a.Item.Max * b.Item.Max);
                Assert.Equal(traced, derived[(i, j)], Math.Abs(traced) * 0.02 + 1e-9);
            }
    }

    [Fact]
    public void TheModelOfAFocalLengthFollowsTheLensItself()
    {
        var lens = Triplet();
        var efl = Criteria.Create("efl", Rms, lens, null);
        var items = DefaultTolerances.Create(lens).Where(i => i.Tolerance is ThicknessChange or IndexChange or PowerFringes).ToList();
        var model = QuadraticModel.Build(lens, items, efl, null);
        Assert.Equal(items.Count * (items.Count - 1) / 2, model.Cross.Count);    // every pair, none traced
        var check = ModelCheck.Run(lens, model, StatisticsModel.Default, efl, null);
        Assert.True(check.RelativeToSpread);
        Assert.True(check.Passes, $"model off by {check.RmsError:P1}");
    }

    [Fact]
    public void BoresightNeedsAnAxialField()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet, s => s.Fields.RemoveAt(0));
        Assert.Throws<ArgumentException>(() => Criteria.Create("boresight", Rms, lens, null));
        Assert.Throws<ArgumentException>(() => Criteria.Create("mtf", Rms, lens, null));
    }
}
