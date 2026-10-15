using Tolerancing.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>A tolerance set allocated to meet several criteria at once, each at its own limit.</summary>
public class MultiCriterionTests
{
    /// <summary>
    /// A departure such as a focal length, on <see cref="KochTests.Synthetic"/>'s tolerances: d linear
    /// in the errors with slopes g, so Q = d² has A = g², no B, and cross terms 2 g_i g_j. The tilts
    /// do not move it.
    /// </summary>
    private static QuadraticModel Focal(QuadraticModel like)
    {
        double[] g = { 0.8, 0.002, 0.0, 0.0, 150.0 };
        var cross = new Dictionary<(int, int), double>();
        for (int i = 0; i < g.Length; i++)
            for (int j = i + 1; j < g.Length; j++)
                if (g[i] * g[j] != 0.0) cross[(i, j)] = 2.0 * g[i] * g[j];
        return new QuadraticModel("focal", 0.0, like.Terms.Select((t, i) => new QuadraticTerm(t.Item, g[i] * g[i], 0.0)).ToList(), cross);
    }

    /// <summary>A departure only the tilts move, as a boresight.</summary>
    private static QuadraticModel Pointing(QuadraticModel like)
    {
        double[] g = { 0.0, 0.0, 1.2, 0.9, 0.0 };
        return new QuadraticModel("pointing", 0.0, like.Terms.Select((t, i) => new QuadraticTerm(t.Item, g[i] * g[i], 0.0)).ToList(),
                                  new Dictionary<(int, int), double> { [(2, 3)] = 2.0 * g[2] * g[3] });
    }

    private static ShopLimits[] Limits(QuadraticModel model) =>
        model.Terms.Select(t => new ShopLimits(t.Item.Max / 50.0, t.Item.Max * 50.0)).ToArray();

    private static double Level(QuadraticModel model, AllocationResult result, AllocationGoal goal) =>
        Math.Sqrt(Allocator.LevelQ(model, Koch.HalfWidths(result.Items), StatisticsModel.Default, goal));

    [Theory]
    [InlineData(AllocationMethod.EqualEffect)]
    [InlineData(AllocationMethod.CostOptimal)]
    public void AFurtherCriterionIsMetBesideTheFirst(AllocationMethod method)
    {
        var model = KochTests.Synthetic();
        var focal = Focal(model);
        var limits = Limits(model);
        var goal = new AllocationGoal(0.2, Shape: SpreadShape.Pearson);
        var alone = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, method, round: false);
        double focalLimit = 0.5 * Level(focal, alone, goal);            // half what the first criterion alone leaves

        var both = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, method, round: false,
                                      new[] { new Requirement(focal, focalLimit) });

        Assert.True(both.Feasible);
        Assert.True(both.Prediction.ProbableWorst <= goal.Limit * (1.0 + 1e-9));
        Assert.Single(both.Also);
        Assert.Equal(Level(focal, both, goal), both.Also[0].Quantile(goal.Yield), 9);
        Assert.True(both.Also[0].Quantile(goal.Yield) <= focalLimit * (1.0 + 1e-9));
        Assert.True(both.Also[0].Quantile(goal.Yield) >= focalLimit * 0.99, "the focal length binds");
        Assert.True(both.Cost > alone.Cost);
        Assert.NotNull(both.JointYield);
        Assert.Null(alone.JointYield);
    }

    [Fact]
    public void TheSampledSpreadIsCorrectedForEachCriterion()
    {
        var model = KochTests.Synthetic();
        var focal = Focal(model);
        var goal = new AllocationGoal(0.2);
        var pearson = Allocator.Allocate(model, Limits(model), goal with { Shape = SpreadShape.Pearson }, StatisticsModel.Default,
                                         AllocationMethod.EqualEffect, round: false);
        double focalLimit = 0.5 * Level(focal, pearson, goal);
        var result = Allocator.Allocate(model, Limits(model), goal, StatisticsModel.Default, AllocationMethod.EqualEffect, round: false,
                                        new[] { new Requirement(focal, focalLimit) });
        Assert.True(result.Feasible);
        Assert.True(result.Prediction.ProbableWorst <= goal.Limit * (1.0 + 1e-9));
        double level = result.Also[0].Quantile(goal.Yield);
        Assert.InRange(level, 0.99 * focalLimit, focalLimit * (1.0 + 1e-9));
    }

    [Fact]
    public void WithThreeCriteriaCostOptimalMeetsEachAndCostsNoMoreThanEqualEffect()
    {
        var model = KochTests.Synthetic();
        var limits = Limits(model);
        var goal = new AllocationGoal(0.2, Shape: SpreadShape.Pearson);
        var alone = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, AllocationMethod.CostOptimal, round: false);
        var also = new[]
        {
            new Requirement(Focal(model), 0.6 * Level(Focal(model), alone, goal)),
            new Requirement(Pointing(model), 0.6 * Level(Pointing(model), alone, goal)),
        };

        var equal = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, AllocationMethod.EqualEffect, round: false, also);
        var cheap = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, AllocationMethod.CostOptimal, round: false, also);

        foreach (var r in new[] { equal, cheap })
        {
            Assert.True(r.Feasible);
            Assert.True(r.Prediction.ProbableWorst <= goal.Limit * (1.0 + 1e-9));
            for (int c = 0; c < also.Length; c++)
                Assert.True(r.Also[c].Quantile(goal.Yield) <= also[c].Limit * (1.0 + 1e-9), $"{r.Method} criterion {c}");
        }
        Assert.True(cheap.Cost <= equal.Cost * (1.0 + 1e-6), $"cost {cheap.Cost} against equal effect's {equal.Cost}");
    }

    [Theory]
    [InlineData(AllocationMethod.EqualEffect, SpreadShape.Sampled)]
    [InlineData(AllocationMethod.CostOptimal, SpreadShape.Pearson)]
    public void AJointGoalHasItsYieldWithinEveryLimitAtOnce(AllocationMethod method, SpreadShape shape)
    {
        var model = KochTests.Synthetic();
        var limits = Limits(model);
        var goal = new AllocationGoal(0.2, Shape: shape);
        var alone = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, method, round: false);
        var also = new[]
        {
            new Requirement(Focal(model), 0.6 * Level(Focal(model), alone, goal)),
            new Requirement(Pointing(model), 0.6 * Level(Pointing(model), alone, goal)),
        };
        var each = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, method, round: false, also);
        var joint = Allocator.Allocate(model, limits, goal with { Joint = true }, StatisticsModel.Default, method, round: false, also);

        Assert.Equal(goal.Yield, each.MarginalYield);
        Assert.True(joint.Feasible);
        Assert.True(joint.Goal.Joint);
        Assert.True(joint.JointYield >= goal.Yield, $"joint yield {joint.JointYield:P3}");
        Assert.InRange(joint.MarginalYield, goal.Yield, 1.0 - (1.0 - goal.Yield) / 3.0 + 1e-12);
        if (each.JointYield >= goal.Yield)
        {
            // Equal effect can leave all but one criterion inside its limit: then the goal is met already.
            Assert.Equal(Koch.HalfWidths(each.Items), Koch.HalfWidths(joint.Items));
            Assert.Equal(AllocationMethod.EqualEffect, method);
            return;
        }
        Assert.True(joint.JointYield <= goal.Yield + 0.02 * (1.0 - goal.Yield) + 1e-12, $"joint yield {joint.JointYield:P3}: tighter than it need be");
        Assert.True(joint.Cost > each.Cost);
        // Each criterion is held to the higher yield.
        Assert.True(joint.Prediction.Quantile(joint.MarginalYield) <= goal.Limit * (1.0 + 1e-9));
        for (int c = 0; c < also.Length; c++)
            Assert.True(joint.Also[c].Quantile(joint.MarginalYield) <= also[c].Limit * (1.0 + 1e-9));
    }

    [Fact]
    public void AJointGoalWithOneCriterionIsTheOrdinaryOne()
    {
        var model = KochTests.Synthetic();
        var goal = new AllocationGoal(0.2, Shape: SpreadShape.Pearson);
        var plain = Allocator.Allocate(model, Limits(model), goal, StatisticsModel.Default, AllocationMethod.EqualEffect);
        var joint = Allocator.Allocate(model, Limits(model), goal with { Joint = true }, StatisticsModel.Default, AllocationMethod.EqualEffect);
        Assert.Equal(Koch.HalfWidths(plain.Items), Koch.HalfWidths(joint.Items));
        Assert.Null(joint.JointYield);
    }

    [Fact]
    public void ACriterionNoToleranceCanMeetMakesTheGoalInfeasible()
    {
        var model = KochTests.Synthetic();
        var limits = Limits(model);
        var goal = new AllocationGoal(10.0, Shape: SpreadShape.Pearson);
        var tightest = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, AllocationMethod.EqualEffect, round: false,
                                          new[] { new Requirement(Focal(model), 1e-9) });
        Assert.False(tightest.Feasible);
        Assert.True(Enumerable.Range(0, model.Terms.Count).All(tightest.AtTightest));
    }

    [Fact]
    public void ARequirementMustBeOfTheSameTolerances()
    {
        var model = KochTests.Synthetic();
        var other = KochTests.Synthetic() with { Terms = KochTests.Synthetic().Terms.Take(4).ToList() };
        Assert.Throws<ArgumentException>(() => Allocator.Allocate(model, Limits(model), new AllocationGoal(0.2), StatisticsModel.Default,
                                                                  AllocationMethod.EqualEffect, also: new[] { new Requirement(other, 0.1) }));
    }

    [Fact]
    public void TheJointYieldIsWithinTheBoundsOfTheSeparateOnes()
    {
        var model = KochTests.Synthetic();
        var focal = Focal(model);
        var shape = Koch.Predict(model, StatisticsModel.Default);
        var focalShape = Koch.Predict(focal, StatisticsModel.Default);
        double limit = shape.Quantile(0.9), focalLimit = focalShape.Quantile(0.9);
        double joint = Koch.JointYield(new[] { model, focal }, new[] { limit, focalLimit }, StatisticsModel.Default);
        double a = shape.Yield(limit), b = focalShape.Yield(focalLimit);
        Assert.InRange(joint, a + b - 1.0 - 1e-9, Math.Min(a, b) + 1e-9);
        // One criterion alone: its own yield, on the same draws.
        Assert.Equal(a, Koch.JointYield(new[] { model }, new[] { limit }, StatisticsModel.Default), 12);
    }

    private static (NominalLens Lens, FocusCompensator Compensator, ICriterion[] Criteria, ToleranceItem[] Items) TripletCase()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var compensator = FocusCompensator.BackFocus(lens);
        var rms = new RmsWavefrontCriterion();
        var criteria = new[] { rms, Criteria.Create("efl", rms, lens, compensator), Criteria.Create("boresight", rms, lens, compensator) };
        var items = new[]
        {
            ToleranceItem.Symmetric(new ThicknessChange(3), 0.1),
            ToleranceItem.Symmetric(new IndexChange(3), 0.001),
            ToleranceItem.Symmetric(new ElementDecentre(3, 4, Axis.X), 0.03),
            ToleranceItem.Symmetric(new ElementDecentre(3, 4, Axis.Y), 0.03),
        };
        return (lens, compensator, criteria, items);
    }

    [Fact]
    public void OneTraceForEveryCriterionGivesEachTheTableAndModelItWouldHaveAlone()
    {
        var (lens, compensator, criteria, items) = TripletCase();

        var tables = Sensitivity.Run(lens, items, criteria, compensator);
        var models = QuadraticModel.Build(lens, items, criteria, compensator, CrossTerms.All);

        for (int m = 0; m < criteria.Length; m++)
        {
            var alone = Sensitivity.Run(lens, items, criteria[m], compensator);
            Assert.Equal(alone.NominalCompensated, tables[m].NominalCompensated, 12);
            for (int i = 0; i < items.Length; i++)
            {
                Assert.Equal(alone.Rows[i].MinCriterion, tables[m].Rows[i].MinCriterion, 12);
                Assert.Equal(alone.Rows[i].MaxCriterion, tables[m].Rows[i].MaxCriterion, 12);
                Assert.Equal(alone.Rows[i].MaxCompensator, tables[m].Rows[i].MaxCompensator, 12);
            }
            var model = QuadraticModel.Build(lens, items, criteria[m], compensator, CrossTerms.All);
            Assert.Equal(model.Cross.Count, models[m].Cross.Count);
            foreach (var (pair, c) in model.Cross) Assert.Equal(c, models[m].Cross[pair], 9);
            Assert.Equal(model.Compensator!.Cross.Count, models[m].Compensator!.Cross.Count);
        }
    }

    [Fact]
    public void CriteriaThatRefocusDifferentlyAreTracedApart()
    {
        // A departure refocused by an image quality of its own: its lens is not the first criterion's.
        var (lens, compensator, criteria, items) = TripletCase();
        var other = new RmsWavefrontCriterion { Polychromatic = false };
        var efl = Criteria.Create("efl", other, lens, compensator);
        var applied = new (Tolerance, double)[] { (items[0].Tolerance, 0.1), (items[2].Tolerance, 0.03) };

        var both = Sensitivity.Evaluate(lens, applied, new[] { criteria[0], efl }, compensator);

        Assert.Equal(Sensitivity.Evaluate(lens, applied, efl, compensator).Criterion, both[1].Criterion, 12);
        Assert.Equal(Sensitivity.Evaluate(lens, applied, efl, compensator).Change, both[1].Change, 12);
    }

    [Fact]
    public void TheModelCheckOfEveryCriterionIsOnTheSameLenses()
    {
        var (lens, compensator, criteria, items) = TripletCase();
        var models = QuadraticModel.Build(lens, items, criteria, compensator, CrossTerms.All);
        var checks = ModelCheck.Run(lens, models, StatisticsModel.Default, criteria, compensator, lenses: 4);
        for (int m = 0; m < criteria.Length; m++)
        {
            var alone = ModelCheck.Run(lens, models[m], StatisticsModel.Default, criteria[m], compensator, lenses: 4);
            Assert.Equal(alone.Lenses.Select(l => l.Traced), checks[m].Lenses.Select(l => l.Traced));
            Assert.Equal(criteria[m] is DeviationCriterion, checks[m].RelativeToSpread);
            Assert.True(checks[m].Passes, $"{criteria[m].Name}: model off by {checks[m].RmsError:P1}");
        }
    }

    [Fact]
    public void FurtherCriteriaAreMeasuredOnTheLensAsTheCompensatorLeftIt()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var compensator = FocusCompensator.BackFocus(lens);
        var rms = new RmsWavefrontCriterion();
        var efl = Criteria.Create("efl", rms, lens, compensator);
        var boresight = Criteria.Create("boresight", rms, lens, compensator);
        var applied = new (Tolerance, double)[] { (new ThicknessChange(3), 0.1), (new ElementDecentre(3, 4, Axis.Y), 0.03) };

        var (result, also) = Sensitivity.Evaluate(lens, applied, rms, new[] { efl, boresight }, compensator);

        Assert.Equal(Sensitivity.Evaluate(lens, applied, rms, compensator).Criterion, result.Criterion, 12);
        Assert.Equal(Sensitivity.Evaluate(lens, applied, efl, compensator).Criterion, also[0], 9);
        Assert.Equal(Sensitivity.Evaluate(lens, applied, boresight, compensator).Criterion, also[1], 9);
    }
}
