using System.Reflection;
using AberrationCalculator.Core.Models;
using Tolerancing.Core;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace Tolerancing.Tests;

public class SystemCopyTests
{
    [Fact]
    public void TheCopySharesNoObjectWithTheOriginal()
    {
        var original = ToleranceTests.Read(ToleranceTests.Triplet).System;
        var copy = SystemCopy.Of(original);

        Assert.NotSame(original, copy);
        Assert.NotSame(original.Surfaces, copy.Surfaces);
        Assert.NotSame(original.Wavelengths, copy.Wavelengths);
        Assert.NotSame(original.Fields, copy.Fields);
        Assert.NotSame(original.Aperture, copy.Aperture);
        for (int s = 0; s < original.Surfaces.Count; s++)
        {
            Assert.NotSame(original.Surfaces[s], copy.Surfaces[s]);
            Assert.NotSame(original.Surfaces[s].AsphericCoefficients, copy.Surfaces[s].AsphericCoefficients);
            Assert.NotSame(original.Surfaces[s].Parameters, copy.Surfaces[s].Parameters);
            Assert.NotSame(original.Surfaces[s].Settings, copy.Surfaces[s].Settings);
        }
    }

    [Fact]
    public void TheCopyHasEveryValueOfTheOriginal()
    {
        var original = ToleranceTests.Read(ToleranceTests.Triplet).System;
        var copy = SystemCopy.Of(original);

        AssertSameValues(original, copy);
        for (int s = 0; s < original.Surfaces.Count; s++)
        {
            AssertSameValues(original.Surfaces[s], copy.Surfaces[s]);
            Assert.Equal(original.Surfaces[s].AsphericCoefficients, copy.Surfaces[s].AsphericCoefficients);
        }
        for (int w = 0; w < original.Wavelengths.Count; w++) AssertSameValues(original.Wavelengths[w], copy.Wavelengths[w]);
        for (int f = 0; f < original.Fields.Count; f++) AssertSameValues(original.Fields[f], copy.Fields[f]);
    }

    [Fact]
    public void ChangingTheCopyLeavesTheOriginalAlone()
    {
        var original = ToleranceTests.Read(ToleranceTests.Triplet).System;
        double thickness = original.Surfaces[2].Thickness, coefficient = original.Surfaces[1].AsphericCoefficients[0];
        var copy = SystemCopy.Of(original);

        copy.Surfaces[2].Thickness += 1.0;
        copy.Surfaces[1].AsphericCoefficients[0] = 1e-5;
        copy.Surfaces[1].Parameters[0] = 3.0;
        copy.Fields.Add(new Field(20.0));

        Assert.Equal(thickness, original.Surfaces[2].Thickness);
        Assert.Equal(coefficient, original.Surfaces[1].AsphericCoefficients[0]);
        Assert.Equal(0.0, original.Surfaces[1].Parameters[0]);
        Assert.Equal(3, original.Fields.Count);
    }

    /// <summary>Every public readable property of a value type or string is equal.</summary>
    private static void AssertSameValues(object expected, object actual)
    {
        foreach (var p in expected.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (!p.PropertyType.IsValueType && p.PropertyType != typeof(string)) continue;
            Assert.True(Equals(p.GetValue(expected), p.GetValue(actual)), $"{expected.GetType().Name}.{p.Name} differs");
        }
    }
}

public class CompensatorTests
{
    [Fact]
    public void BrentFindsTheMinimumOfAParabola()
    {
        var (x, f) = Brent.Minimize(t => (t - 0.3) * (t - 0.3) + 2.0, -1.0, 4.0, tolerance: 1e-10);
        Assert.Equal(0.3, x, 1e-7);       // a minimum is located to √ε, where f is flat to rounding
        Assert.Equal(2.0, f, 12);
    }

    [Fact]
    public void RefocusingFollowsTheParaxialFocusAndNeverMakesTheLensWorse()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var criterion = new RmsWavefrontCriterion();
        var compensator = FocusCompensator.BackFocus(lens);
        var reference = Sensitivity.Evaluate(lens, null, 0.0, criterion, compensator);

        var perturbed = lens.Perturb();
        new IndexChange(6).Apply(perturbed, 0.002);
        double uncompensated = criterion.Evaluate(perturbed.Build());
        double paraxialShift = perturbed.Build().Paraxial.Bfl - lens.Model.Paraxial.Bfl;
        var compensated = compensator.Solve(perturbed, criterion);

        Assert.True(compensated.Criterion < uncompensated);
        // Best focus follows the paraxial focus, to the 0.02 mm this lens's colour and field
        // curvature move the best compromise away from it.
        Assert.Equal(paraxialShift, compensated.Change - reference.Change, 0.03);
        Assert.Equal(lens.System.Surfaces[7].Thickness + compensated.Change, perturbed.System.Surfaces[7].Thickness, 12);
    }

    [Fact]
    public void AThicknessToleranceOnTheCompensatorItselfIsUndoneByIt()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var criterion = new RmsWavefrontCriterion { Polychromatic = false };
        var compensator = FocusCompensator.BackFocus(lens);
        var reference = Sensitivity.Evaluate(lens, null, 0.0, criterion, compensator);

        var moved = Sensitivity.Evaluate(lens, new ThicknessChange(7), 0.3, criterion, compensator);

        Assert.Equal(reference.Change, moved.Change, 5);
        Assert.Equal(reference.Criterion, moved.Criterion, 6);
    }
}

public class SensitivityTests
{
    [Fact]
    public void TheDefaultSetHasPowerOnEveryRefractingSurfaceThicknessesAndGlass()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = DefaultTolerances.Create(lens, ShopGrade.Precision);

        Assert.Equal(new[] { 1, 2, 3, 4, 6, 7 }, items.Where(i => i.Tolerance is PowerFringes).Select(i => i.Tolerance.Surface));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, items.Where(i => i.Tolerance is ThicknessChange).Select(i => i.Tolerance.Surface));
        Assert.Equal(new[] { 1, 3, 6 }, items.Where(i => i.Tolerance is IndexChange).Select(i => i.Tolerance.Surface));
        Assert.Equal(new[] { 1, 3, 6 }, items.Where(i => i.Tolerance is AbbeChange).Select(i => i.Tolerance.Surface));
        Assert.All(items.Where(i => i.Tolerance is PowerFringes), i => Assert.Equal(5.0, i.Max));
        Assert.All(items.Where(i => i.Tolerance is ThicknessChange), i => Assert.Equal(0.1, i.Max));
        Assert.All(items.Where(i => i.Tolerance is IndexChange), i => Assert.Equal(0.001, i.Max));
    }

    [Fact]
    public void ATableRowIsTheCriterionAtEachEndAfterRefocusing()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var criterion = new RmsWavefrontCriterion();
        var compensator = FocusCompensator.BackFocus(lens);
        var items = new[]
        {
            ToleranceItem.Symmetric(new PowerFringes(3), 10.0),
            ToleranceItem.Symmetric(new IndexChange(6), 0.0),       // no range: no change
        };

        var table = Sensitivity.Run(lens, items, criterion, compensator);

        var reference = Sensitivity.Evaluate(lens, null, 0.0, criterion, compensator);
        var plus = Sensitivity.Evaluate(lens, new PowerFringes(3), 10.0, criterion, compensator);
        Assert.Equal(reference.Criterion, table.NominalCompensated, 12);
        Assert.Equal(criterion.Evaluate(lens.Model), table.Nominal, 12);
        Assert.Equal(plus.Criterion, table.Rows[0].MaxCriterion, 12);
        Assert.Equal(plus.Change - reference.Change, table.Rows[0].MaxCompensator, 12);
        Assert.Equal(0.0, table.Rows[1].MinChange, 9);
        Assert.Equal(0.0, table.Rows[1].MaxChange, 9);
        Assert.Equal(table.Rows[0].WorstIncrease, table.RssIncrease, 12);
    }
}

/// <summary>
/// The test triplet perturbed - 10 fringes on surface 3 at 0.6328 µm, its air space 0.2 mm
/// longer - and the same lens computed independently in OpticStudio on 2026-10-12, this side with
/// WavefrontErrorCalculator's OpticStudio presets, written to check OpticStudio's results, so both
/// compute the same quantity. The radius was
/// set in OpticStudio to the one <see cref="PowerFringes"/> gives, and the back focus optimized by DLS
/// on the merit function its wizard builds for RMS wavefront referred to the chief ray,
/// polychromatic, Gaussian quadrature with 6 rings and 12 arms. Neither program is taken as right:
/// two independent computations agreeing is the check, and a change in the tracing breaks it.
/// </summary>
public class OpticStudioComparisonTests
{
    private static PerturbedLens Perturbed(out NominalLens lens)
    {
        lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var perturbed = lens.Perturb();
        new PowerFringes(3, 0.6328).Apply(perturbed, 10.0);
        new ThicknessChange(4).Apply(perturbed, 0.2);
        return perturbed;
    }

    [Fact]
    public void TheFringesGiveTheRadiusSetInOpticStudio()
    {
        var perturbed = Perturbed(out _);
        Assert.Equal(-20.35065973, perturbed.System.Surfaces[3].Radius, 7);
    }

    [Fact]
    public void ThePolychromaticCriterionAgreesWithOpticStudiosMerit()
    {
        var perturbed = Perturbed(out _);
        var criterion = new RmsWavefrontCriterion { Options = WavefrontOptions.Zemax };

        Assert.Equal(1.5393338, criterion.Evaluate(perturbed.Build()), 6);       // OpticStudio 1.5393337617
    }

    [Fact]
    public void RefocusingAgreesWithOpticStudiosBackFocus()
    {
        var perturbed = Perturbed(out _);
        var criterion = new RmsWavefrontCriterion { Options = WavefrontOptions.Zemax };

        var c = new FocusCompensator(7).Solve(perturbed, criterion);

        Assert.Equal(43.160286518, perturbed.System.Surfaces[7].Thickness, 1e-7); // OpticStudio's value
        Assert.Equal(1.1921957, c.Criterion, 6);                                 // OpticStudio 1.1921956775
    }

    [Theory]
    [InlineData(0, 1, 0.693538)]       // field 1, d: OpticStudio Zernike, 1024 × 1024, RMS about the mean
    [InlineData(1, 0, 1.879718)]       // field 2, F
    [InlineData(2, 2, 1.740746)]       // field 3, C
    public void EachFieldAndWavelengthAgreesWithOpticStudiosRms(int field, int wave, double opticStudio)
    {
        var perturbed = Perturbed(out _);
        var criterion = new RmsWavefrontCriterion { Options = WavefrontOptions.ZemaxZernike };

        var s = WavefrontCalculator.Compute(perturbed.Build(), field, wave, criterion.EffectiveOptions, criterion.Sampling).Statistics;

        // OpticStudio's grid converges on the quadrature: 1.99344 at 256², 1.99611 at 1024² for one
        // field the quadrature puts at 1.99641. 2e-4 relative is what its 1024² grid leaves.
        Assert.Equal(opticStudio, s.RmsStandardDeviation, opticStudio * 2e-4);
    }
}
