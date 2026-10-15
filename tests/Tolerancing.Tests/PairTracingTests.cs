using Tolerancing.Core;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>
/// A pair of errors that leaves the lens untraceable at both maxima, though each traces alone, is
/// traced nearer nominal for its cross term, or left out and named - not fatal to the whole model.
/// </summary>
public class PairTracingTests
{
    private static readonly RmsWavefrontCriterion Rms = new();
    private const double T = 0.1;

    /// <summary>
    /// The RMS wavefront, but +∞ once the air spaces after surfaces 2 and 4 have both moved by more
    /// than <see cref="Gate"/>: a lens that traces with either error alone and not with both.
    /// </summary>
    private sealed record GatedRms(double Gate, double Space2, double Space4) : ICriterion
    {
        public string Name => "gated RMS";
        public string Units => "waves";
        public double Evaluate(LensModel lens) =>
            Math.Abs(lens.System.Surfaces[2].Thickness - Space2) > Gate && Math.Abs(lens.System.Surfaces[4].Thickness - Space4) > Gate
                ? double.PositiveInfinity
                : Rms.Evaluate(lens);
    }

    private static (NominalLens Lens, List<ToleranceItem> Items) Setup()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = new[] { 2, 3, 4 }.Select(s => ToleranceItem.Symmetric(new ThicknessChange(s), T)).ToList();
        return (lens, items);
    }

    private static GatedRms Gated(NominalLens lens, double gate) =>
        new(gate, lens.Model.System.Surfaces[2].Thickness, lens.Model.System.Surfaces[4].Thickness);

    [Fact]
    public void APairUntraceableAtItsMaximaIsTracedAtHalfThem()
    {
        var (lens, items) = Setup();
        var gated = Gated(lens, 0.75 * T);
        var model = QuadraticModel.Build(lens, items, gated, null, CrossTerms.All);
        Assert.Equal(new[] { (0, 2) }, model.PartlyTraced);
        Assert.Empty(model.Untraced);
        Assert.Equal(3, model.Cross.Count);

        // The cross term at half the maxima, against the model's own terms there.
        double h = 0.5 * T, q0 = model.Q0;
        double both = Sensitivity.Evaluate(lens, new[] { (items[0].Tolerance, h), (items[2].Tolerance, h) }, Rms, null).Criterion;
        double expected = (both * both - q0 - model.Terms[0].Change(h) - model.Terms[2].Change(h)) / (h * h);
        Assert.Equal(expected, model.Cross[(0, 2)], 12);

        // And, the RMS being nearly quadratic in two spacings, close to the cross term at the maxima.
        var plain = QuadraticModel.Build(lens, items, Rms, null, CrossTerms.All);
        Assert.Equal(plain.Cross[(0, 2)], model.Cross[(0, 2)], Math.Abs(plain.Cross[(0, 2)]) * 0.05);
        Assert.Equal(plain.Cross[(0, 1)], model.Cross[(0, 1)], 12);       // a pair that traces is as before
    }

    [Fact]
    public void APairThatCannotBeTracedAtAllIsLeftOutAndNamed()
    {
        var (lens, items) = Setup();
        var model = QuadraticModel.Build(lens, items, Gated(lens, 1e-9), null, CrossTerms.All);
        Assert.Equal(new[] { (0, 2) }, model.Untraced);
        Assert.Empty(model.PartlyTraced);
        Assert.False(model.Cross.ContainsKey((0, 2)));
        Assert.Equal(2, model.Cross.Count);
        Assert.True(double.IsFinite(Koch.Predict(model, StatisticsModel.Default).ProbableWorst));
    }

    /// <summary>With a compensator, the pair's cross term in the compensator's move is taken at the same half.</summary>
    [Fact]
    public void TheCompensatorsCrossTermIsTakenWhereTheCriterionsIs()
    {
        var (lens, items) = Setup();
        var compensator = FocusCompensator.BackFocus(lens);
        var model = QuadraticModel.Build(lens, items, Gated(lens, 0.75 * T), compensator, CrossTerms.All);
        var plain = QuadraticModel.Build(lens, items, Rms, compensator, CrossTerms.All);
        Assert.Equal(new[] { (0, 2) }, model.PartlyTraced);
        double c = plain.Compensator!.Cross[(0, 2)];
        Assert.Equal(c, model.Compensator!.Cross[(0, 2)], Math.Abs(c) * 0.05 + 1e-9);
    }
}
