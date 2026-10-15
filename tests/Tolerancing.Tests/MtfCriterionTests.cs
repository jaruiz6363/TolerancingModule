using Tolerancing.Core;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>
/// The geometric MTF as a criterion: W = √(1 − MTF), a floor entered as the MTF, and its spread
/// drawn from the linear spot synthesis - checked against lenses traced in full.
/// </summary>
public class MtfCriterionTests
{
    private static readonly RmsWavefrontCriterion Rms = new();

    private static (NominalLens Lens, FocusCompensator Compensator, MtfCriterion Mtf, List<ToleranceItem> Items) Setup(string spec)
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var compensator = FocusCompensator.BackFocus(lens);
        var mtf = (MtfCriterion)Criteria.Create(spec, Rms, lens, compensator);
        return (lens, compensator, mtf, DefaultTolerances.Create(lens, ShopGrade.Commercial, PowerFringes.HeNe, compensator).ToList());
    }

    [Fact]
    public void TheCriterionIsTheGeometricMtfAsWSquaredEqualToOneMinusIt()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet).Model;
        var mtf = MtfCriterion.Parse("mtf:10:20:s", Rms);
        var direct = GeometricMtf.Compute(lens, 1, new[] { 20.0 }, Rms.EffectiveOptions, new MtfOptions());
        Assert.Equal(direct.Mtf[0].Sagittal, mtf.Mtf(lens), 12);
        Assert.Equal(direct.Geometric[0].Sagittal, mtf.Mtf(lens), 12);         // no diffraction factor
        double w = mtf.Evaluate(lens);
        Assert.Equal(1.0 - mtf.Mtf(lens), w * w, 12);

        Assert.True(mtf.IsFloor);
        Assert.Equal(mtf.Mtf(lens), mtf.Quoted(w), 12);
        Assert.Equal(w, mtf.FromQuoted(mtf.Quoted(w)), 12);
        Assert.Equal(0.0, mtf.FromQuoted(1.2));                                // a floor above 1 asks for a perfect spot
        Assert.Equal("", mtf.Units);
    }

    [Theory]
    [InlineData("mtf:14:30:t", 14.0, 30.0, MtfOrientation.Tangential)]
    [InlineData("MTF:0:12.5:S", 0.0, 12.5, MtfOrientation.Sagittal)]
    [InlineData("mtf:10:40:sagittal", 10.0, 40.0, MtfOrientation.Sagittal)]
    public void ParseReadsTheFieldTheFrequencyAndTheOrientation(string text, double field, double frequency, MtfOrientation orientation)
    {
        var mtf = MtfCriterion.Parse(text, Rms);
        Assert.Equal((field, frequency, orientation), (mtf.Field, mtf.Frequency, mtf.Orientation));
        Assert.Same(Rms, mtf.Focus);
        Assert.Equal(new MtfOptions().Rings, mtf.Rings);
    }

    [Theory]
    [InlineData("mtf:14:30")]
    [InlineData("mtf:14:0:t")]
    [InlineData("mtf:a:30:t")]
    [InlineData("mtf:14:30:x")]
    [InlineData("mft:14:30:t")]
    public void ParseRefusesWhatIsNotAnMtf(string text) =>
        Assert.Throws<ArgumentException>(() => MtfCriterion.Parse(text, Rms));

    [Fact]
    public void AFieldTheLensDoesNotListIsRefusedWithTheFieldsItDoes()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var e = Assert.Throws<ArgumentException>(() => Criteria.Create("mtf:5:20:t", Rms, lens, null));
        Assert.Contains("0, 10, 14", e.Message);
    }

    [Fact]
    public void TheInterceptsAreTheRaysTheMtfSums()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet).Model;
        var mtf = MtfCriterion.Parse("mtf:14:20:t", Rms);
        var weights = mtf.InterceptWeights(lens);
        var y = mtf.Intercepts(lens);
        Assert.Equal(3 * GeometricMtf.Pupil(mtf.Rings).Length, y.Length);      // every wavelength
        Assert.Equal(GeometricMtf.Pupil(mtf.Rings).Length, weights.Sum(), 9);           // each wavelength's share, ray by ray
        var nominal = new SpotSynthesis(y, Array.Empty<double[]>(), weights, mtf.Frequency);
        Assert.Equal(mtf.Mtf(lens), nominal.Mtf(Array.Empty<double>()), 12);
    }

    /// <summary>The synthesis starts from the refocused nominal lens: with no errors it is the model's Q₀.</summary>
    [Fact]
    public void TheModelOfAnMtfIsItsSpotSynthesis()
    {
        var (lens, compensator, mtf, items) = Setup("mtf:0:20:t");
        var model = QuadraticModel.Build(lens, items, mtf, compensator);
        Assert.NotNull(model.Synthesis);
        Assert.Equal(model.Q0, model.Evaluate(new double[items.Count]), 9);

        var check = ModelCheck.Run(lens, model, StatisticsModel.Default, mtf, compensator);
        Assert.NotNull(check.Quoted);
        Assert.Equal(ModelCheck.ScatterTolerance, check.Limit);
        Assert.True(check.Passes, $"synthesis off by {check.RmsError:P1} of the scatter");
    }

    /// <summary>
    /// The triplet's commercial default set, on axis and at 10°: the synthesis's mean, 97.7 % floor
    /// and yield against 400 lenses traced and refocused. (On 2026-10-13, on axis: floor 0.222 against
    /// 0.238 traced, yield at 0.3 90.5 % against 90.7 %; at 10°, 0.065 against 0.072, 32.7 % against
    /// 31.5 % on 200.)
    /// </summary>
    [Theory]
    [InlineData("mtf:0:20:t", 0.3)]
    [InlineData("mtf:10:20:t", 0.3)]
    public void TheSynthesisPredictsTheTracedMonteCarlo(string spec, double floor)
    {
        var (lens, compensator, mtf, items) = Setup(spec);
        var model = QuadraticModel.Build(lens, items, mtf, compensator);
        var prediction = Koch.Predict(model, StatisticsModel.Default);
        var mc = MonteCarlo.Run(lens, items, StatisticsModel.Default, mtf, compensator, trials: 400, seed: 1);
        Assert.Equal(0, mc.Untraceable);

        var traced = mc.Criteria.Select(w => mtf.Quoted(w)).ToArray();
        double mean = traced.Average(), se = Math.Sqrt(traced.Average(m => (m - mean) * (m - mean)) / traced.Length);
        double predicted = 1.0 - prediction.SampledMoments.Mean;
        Assert.InRange(predicted, mean - 4 * se, mean + 4 * se);

        Assert.InRange(mtf.Quoted(prediction.ProbableWorst) - mtf.Quoted(mc.Percentile(Prediction.Yield97)), -0.04, 0.02);
        double limit = mtf.FromQuoted(floor);
        Assert.InRange(prediction.Yield(limit) - mc.Yield(limit), -0.05, 0.05);
    }

    /// <summary>The triplet allocated at least cost to an MTF of 0.3 on axis for 97.7 % of lenses, then built.</summary>
    [Fact]
    public void AnAllocationToAnMtfFloorIsMetWhenBuilt()
    {
        var (lens, compensator, mtf, items) = Setup("mtf:0:20:t");
        var goal = new AllocationGoal(mtf.FromQuoted(0.3));
        var budget = Budget.Allocate(lens, items, ShopLimits.FromGrades(lens, items), mtf, compensator,
                                     goal, StatisticsModel.Default, AllocationMethod.CostOptimal);
        Assert.True(budget.Allocation.Feasible);
        Assert.True(mtf.Quoted(budget.Allocation.Prediction.ProbableWorst) >= 0.3);

        var mc = MonteCarlo.Run(lens, budget.Allocation.Items, StatisticsModel.Default, mtf, compensator, trials: 200, seed: 4);
        Assert.InRange(mc.Yield(goal.Limit), 0.95, 1.0);
    }
}
