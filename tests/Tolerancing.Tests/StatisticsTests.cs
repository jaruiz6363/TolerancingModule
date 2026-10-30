using Tolerancing.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>Each distribution's moments, against numerical integration of its density and against its own draws.</summary>
public class DistributionTests
{
    /// <summary>∫ f over [a, b] by Simpson's rule on n intervals.</summary>
    private static double Integrate(Func<double, double> f, double a, double b, int n = 20000)
    {
        double h = (b - a) / n, sum = f(a) + f(b);
        for (int i = 1; i < n; i++) sum += f(a + i * h) * (i % 2 == 0 ? 2.0 : 4.0);
        return sum * h / 3.0;
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(3.0)]
    [InlineData(1.0)]
    public void ATruncatedNormalsMomentsAreItsDensitys(double k)
    {
        // The normal with σ = 1/k, kept within ±1.
        double sigma = 1.0 / k;
        double Density(double x) => Math.Exp(-0.5 * x * x / (sigma * sigma));
        double mass = Integrate(Density, -1.0, 1.0);
        var d = Distribution.Normal(k);
        Assert.Equal(Integrate(x => x * x * Density(x), -1.0, 1.0) / mass, d.Variance, 6);
        Assert.Equal(Integrate(x => Math.Pow(x, 4) * Density(x), -1.0, 1.0) / mass, d.Fourth, 6);
        Assert.Equal(Integrate(x => Math.Pow(x, 6) * Density(x), -1.0, 1.0) / mass, d.Sixth, 6);
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(1.5)]
    [InlineData(3.0)]
    public void ATruncatedRayleighsMomentsAreItsDensitys(double k)
    {
        double sigma = 1.0 / k;
        double Density(double r) => r * Math.Exp(-0.5 * r * r / (sigma * sigma));
        double mass = Integrate(Density, 0.0, 1.0);
        var d = RadialDistribution.Rayleigh(k);
        Assert.Equal(Integrate(r => r * r * Density(r), 0.0, 1.0) / mass, d.Second, 7);
        Assert.Equal(Integrate(r => Math.Pow(r, 4) * Density(r), 0.0, 1.0) / mass, d.Fourth, 7);
        for (int n = 0; n <= 6; n++)
            Assert.Equal(Integrate(r => Math.Pow(r, n) * Density(r), 0.0, 1.0) / mass, d.Moment(n), 9);
    }

    [Fact]
    public void TheUniformAndEndPointSixthMomentsAreOneSeventhAndOne()
    {
        Assert.Equal(Integrate(x => Math.Pow(x, 6) / 2.0, -1.0, 1.0), Distribution.Uniform.Sixth, 12);
        Assert.Equal(1.0, Distribution.EndPoint.Sixth);
        for (int n = 0; n <= 6; n++)
        {
            Assert.Equal(Integrate(r => Math.Pow(r, n) * 2.0 * r, 0.0, 1.0), RadialDistribution.Disc.Moment(n), 10);
            Assert.Equal(1.0, RadialDistribution.Ring.Moment(n));
        }
    }

    [Fact]
    public void TheDiscIsTheTriangularRadiusOfKochsTable2()
    {
        // Uniform over the disc: density 2ρ on [0, 1].
        Assert.Equal(Integrate(r => r * r * 2.0 * r, 0.0, 1.0), RadialDistribution.Disc.Second, 10);
        Assert.Equal(Integrate(r => Math.Pow(r, 4) * 2.0 * r, 0.0, 1.0), RadialDistribution.Disc.Fourth, 10);
    }

    public static IEnumerable<object[]> AxisDistributions() => new[]
    {
        new object[] { Distribution.Uniform }, new object[] { Distribution.EndPoint }, new object[] { Distribution.Normal(2.0) },
    };

    public static IEnumerable<object[]> RadialDistributions() => new[]
    {
        new object[] { RadialDistribution.Disc }, new object[] { RadialDistribution.Ring }, new object[] { RadialDistribution.Rayleigh(2.0) },
    };

    [Theory]
    [MemberData(nameof(AxisDistributions))]
    public void DrawsHaveTheMomentsTheDistributionStates(Distribution d)
    {
        var random = new Random(5);
        const int n = 400_000;
        double s2 = 0.0, s4 = 0.0, max = 0.0;
        for (int i = 0; i < n; i++)
        {
            double x = d.Sample(random);
            s2 += x * x;
            s4 += x * x * x * x;
            max = Math.Max(max, Math.Abs(x));
        }
        Assert.True(max <= 1.0);
        Assert.Equal(d.Variance, s2 / n, 2);
        Assert.Equal(d.Fourth, s4 / n, 2);
    }

    [Theory]
    [MemberData(nameof(RadialDistributions))]
    public void RadiiDrawnHaveTheMomentsTheDistributionStates(RadialDistribution d)
    {
        var random = new Random(6);
        const int n = 400_000;
        double s2 = 0.0, s4 = 0.0;
        for (int i = 0; i < n; i++)
        {
            double r = d.SampleRadius(random);
            Assert.InRange(r, 0.0, 1.0);
            s2 += r * r;
            s4 += r * r * r * r;
        }
        Assert.Equal(d.Second, s2 / n, 2);
        Assert.Equal(d.Fourth, s4 / n, 2);
    }
}

public class SpecialFunctionTests
{
    [Theory]
    [InlineData(1.0, 0.5)]
    [InlineData(1.0, 3.0)]
    [InlineData(1.0, 20.0)]
    public void GammaPOfOneIsTheExponentialsDistribution(double a, double x) =>
        Assert.Equal(1.0 - Math.Exp(-x), Tolerancing.Core.Special.GammaP(a, x), 13);

    [Theory]
    [InlineData(2.0, 1.0)]
    [InlineData(2.0, 6.0)]
    public void GammaPOfTwoIsInClosedForm(double a, double x) =>
        Assert.Equal(1.0 - Math.Exp(-x) * (1.0 + x), Tolerancing.Core.Special.GammaP(a, x), 13);

    [Theory]
    [InlineData(0.5, 0.1)]
    [InlineData(2.7, 0.977)]
    [InlineData(40.0, 0.5)]
    public void TheGammaQuantileInvertsGammaP(double a, double p) =>
        Assert.Equal(p, Tolerancing.Core.Special.GammaP(a, Tolerancing.Core.Special.GammaQuantile(a, p)), 10);

    [Theory]
    [InlineData(0.977)]
    [InlineData(0.5)]
    [InlineData(0.1)]
    public void WilsonHilfertyTakesOverSmoothlyFromTheSeries(double p)
    {
        // The quantile just below the switch, by bisection on the series, and just above, by the
        // cube root: a shape 0.002 % apart, so they must agree to about that.
        double below = Tolerancing.Core.Special.GammaQuantile(999.99, p);
        double above = Tolerancing.Core.Special.GammaQuantile(1000.01, p);
        Assert.Equal(1.0, above / below, 4);
    }

    [Fact]
    public void LogGammaIsTheFactorial()
    {
        Assert.Equal(Math.Log(24.0), Tolerancing.Core.Special.LogGamma(5.0), 13);
        Assert.Equal(0.5 * Math.Log(Math.PI), Tolerancing.Core.Special.LogGamma(0.5), 13);
    }

    [Fact]
    public void TheNormalQuantileOf977PercentIsTwo() =>
        Assert.Equal(2.0, Tolerancing.Core.Special.NormalQuantile(Prediction.Yield97), 6);
}

/// <summary>Koch's moment algebra, against draws from the very model it describes.</summary>
public class KochTests
{
    /// <summary>
    /// A model with all the parts Koch's formulas have: a lone error with a linear term, a lone
    /// error that is purely quadratic, an x-y pair unlike in x and y and with a cross term between
    /// them, and a cross term between two errors of different kinds.
    /// </summary>
    internal static QuadraticModel Synthetic(double scale = 1.0)
    {
        var items = new[]
        {
            ToleranceItem.Symmetric(new ThicknessChange(2), 0.1 * scale),
            ToleranceItem.Symmetric(new PowerFringes(3), 4.0 * scale),
            ToleranceItem.Symmetric(new SurfaceTilt(4, Axis.X), 0.05 * scale),
            ToleranceItem.Symmetric(new SurfaceTilt(4, Axis.Y), 0.05 * scale),
            ToleranceItem.Symmetric(new IndexChange(1), 0.001 * scale),
        };
        var terms = new[]
        {
            new QuadraticTerm(items[0], A: 3.0, B: 0.4),
            new QuadraticTerm(items[1], A: 2e-3, B: 0.0),
            new QuadraticTerm(items[2], A: 90.0, B: 1.5),
            new QuadraticTerm(items[3], A: 40.0, B: -0.7),
            new QuadraticTerm(items[4], A: 1e5, B: 20.0),
        };
        var cross = new Dictionary<(int, int), double> { [(2, 3)] = 30.0, [(0, 4)] = 400.0, [(1, 2)] = -0.5 };
        return new QuadraticModel("test", 0.01, terms, cross);
    }

    public static IEnumerable<object[]> Models() => new[]
    {
        new object[] { new StatisticsModel(Distribution.Uniform) },
        new object[] { new StatisticsModel(Distribution.EndPoint) },
        new object[] { new StatisticsModel(Distribution.Normal(2.0)) },
        new object[] { new StatisticsModel(Distribution.Uniform, RadialDistribution.Disc) },
        new object[] { new StatisticsModel(Distribution.Normal(3.0), RadialDistribution.Rayleigh(2.5)) },
        new object[] { new StatisticsModel(Distribution.EndPoint, RadialDistribution.Ring) },
    };

    /// <summary>The synthetic model with biased ranges and some errors of their own distribution.</summary>
    internal static QuadraticModel Biased()
    {
        var model = Synthetic();
        var terms = model.Terms.ToArray();
        terms[0] = terms[0] with { Item = new ToleranceItem(terms[0].Item.Tolerance, -0.02, 0.15) };
        terms[1] = terms[1] with { Item = terms[1].Item with { Distribution = Distribution.Normal(3.0) } };
        terms[4] = terms[4] with { Item = new ToleranceItem(terms[4].Item.Tolerance, -0.0005, 0.0015) { Distribution = Distribution.EndPoint } };
        return model with { Terms = terms };
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void TheMeanVarianceAndThirdMomentAreThoseOfTheModelsOwnDraws(StatisticsModel statistics)
    {
        foreach (var model in new[] { Synthetic(), Biased() })
        {
            var prediction = Koch.Predict(model, statistics);
            var mc = MonteCarlo.RunModel(model, statistics, trials: 400_000, seed: 3);

            // Four standard errors of the sampled mean; the higher moments to their sampling noise.
            double seMean = Math.Sqrt(prediction.VarianceQ / mc.Trials);
            Assert.InRange(mc.MeanQ, prediction.MeanQ - 4 * seMean, prediction.MeanQ + 4 * seMean);
            Assert.InRange(mc.VarianceQ / prediction.VarianceQ, 0.98, 1.02);
            double skewMc = mc.ThirdQ / Math.Pow(mc.VarianceQ, 1.5);
            Assert.InRange(skewMc - prediction.Skewness, -0.03, 0.03);
        }
    }

    [Fact]
    public void ABiasAloneMovesTheMeanAsTheModelSays()
    {
        // Every range made almost nothing about its centre: Q is the model at the centres.
        var model = Biased();
        var centres = model.Terms.Select(t => t.Item.Center).ToArray();
        var pinned = model with
        {
            Terms = model.Terms.Select((t, i) => t with { Item = t.Item with { Min = centres[i] - 1e-9, Max = centres[i] + 1e-9 } }).ToList(),
        };
        var p = Koch.Predict(pinned, StatisticsModel.Default);
        Assert.Equal(model.Evaluate(centres), p.MeanQ, 10);
        Assert.Equal(p.MeanQ - p.Q0, p.BiasShift, 10);
        Assert.True(p.BiasShift > 0.0);
    }

    [Fact]
    public void AHalfWidthChangeKeepsTheBiasInProportion()
    {
        var item = new ToleranceItem(new ThicknessChange(1), -0.02, 0.1) { Distribution = Distribution.Normal(3.0) };
        var half = item.WithHalfWidth(0.03);
        Assert.Equal(0.03, half.HalfWidth, 14);
        Assert.Equal(item.Center / item.HalfWidth, half.Center / half.HalfWidth, 14);
        Assert.Equal(item.Distribution, half.Distribution);
    }

    [Theory]
    [InlineData(SpreadShape.Normal)]
    [InlineData(SpreadShape.Gamma)]
    [InlineData(SpreadShape.Pearson)]
    [InlineData(SpreadShape.Sampled)]
    public void EachShapesYieldAndQuantileAreInverse(SpreadShape shape)
    {
        var p = Koch.Predict(Biased(), StatisticsModel.Default) with { Shape = shape };
        double w = p.Quantile(0.9);
        Assert.Equal(0.9, p.Yield(w), shape == SpreadShape.Sampled ? 3 : 9);
        Assert.True(p.ProbableWorst > p.Quantile(0.841));
    }

    [Fact]
    public void PearsonWithAGammasSkewnessIsThatGamma()
    {
        // A gamma of shape k has skewness 2/√k and starts at zero: Pearson's shift is then nothing.
        var p = Koch.Predict(Synthetic(), StatisticsModel.Default);
        double k = p.MeanQ * p.MeanQ / p.VarianceQ;
        var gamma = p with { Shape = SpreadShape.Gamma };
        var pearson = p with { Shape = SpreadShape.Pearson, ThirdQ = 2.0 / Math.Sqrt(k) * Math.Pow(p.SigmaQ, 3) };
        foreach (double f in new[] { 0.1, 0.5, 0.977 })
            Assert.Equal(gamma.QuantileQ(f), pearson.QuantileQ(f), 9);
    }

    [Fact]
    public void TheSampledShapeIsTheModelsOwnMonteCarlo()
    {
        var model = Biased();
        var p = Koch.Predict(model, StatisticsModel.Default) with { Shape = SpreadShape.Sampled };
        var mc = MonteCarlo.RunModel(model, StatisticsModel.Default, trials: 200_000, seed: 9);
        foreach (double f in new[] { 0.5, 0.841, 0.977 })
            Assert.InRange(p.Quantile(f) / mc.Percentile(f), 0.98, 1.02);     // 20 000 draws against 200 000
    }

    [Fact]
    public void AnErrorAtItsEndPointsWithoutALinearTermDoesNotSpread()
    {
        // x² is t² at either end: the mean moves and the variance is nothing.
        var item = ToleranceItem.Symmetric(new ThicknessChange(1), 0.2);
        var model = new QuadraticModel("test", 1.0, new[] { new QuadraticTerm(item, 5.0, 0.0) }, new Dictionary<(int, int), double>());
        var p = Koch.Predict(model, new StatisticsModel(Distribution.EndPoint));
        Assert.Equal(1.0 + 5.0 * 0.04, p.MeanQ, 14);
        Assert.Equal(0.0, p.VarianceQ, 14);
    }

    [Fact]
    public void APolarPairOfEqualSensitivitiesIsTheSameAsTheRadiusAlone()
    {
        // A_x = A_y and no linear or cross term: Q's change is A ρ², whatever θ is.
        var model = Synthetic() with
        {
            Terms = Synthetic().Terms.Select((t, i) => i == 3 ? t with { A = 90.0, B = 0.0 } : i == 2 ? t with { B = 0.0 } : t with { A = 0.0, B = 0.0 }).ToList(),
            Cross = new Dictionary<(int, int), double>(),
        };
        var p = Koch.Predict(model, new StatisticsModel(Distribution.Uniform, RadialDistribution.Disc));
        double t = 0.05, a = 90.0;
        Assert.Equal(0.01 + a * t * t / 2.0, p.MeanQ, 14);                        // ν₂ = t²/2
        Assert.Equal(a * a * Math.Pow(t, 4) * (1.0 / 3.0 - 1.0 / 4.0), p.VarianceQ, 14); // ν₄ − ν₂²
    }


    [Fact]
    public void AParabolaIsFittedThroughBothEndsAndNominal()
    {
        // Q = 0.04 + 3x² − 0.5x, its ends at −0.1 and +0.3 - not symmetric.
        double Q(double x) => 0.04 + 3.0 * x * x - 0.5 * x;
        var row = new SensitivityRow(new ToleranceItem(new ThicknessChange(1), -0.1, 0.3),
                                     Math.Sqrt(Q(-0.1)), 0.0, Math.Sqrt(Q(0.3)), 0.0, 0.2);
        var term = QuadraticModel.FitTerm(row, 0.04);
        Assert.Equal(3.0, term.A, 12);
        Assert.Equal(-0.5, term.B, 12);
    }

    [Fact]
    public void TheXAndYOfEachDecentreAndTiltArePaired()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = DefaultTolerances.Create(lens);
        var pairs = QuadraticModel.AxisPairs(items);
        Assert.Equal(12, pairs.Count);           // six wedges, three decentres, three tilts
        Assert.All(pairs, p => Assert.Equal(((IAxisTolerance)items[p.X].Tolerance).WithAxis(Axis.Y), items[p.Y].Tolerance));
    }
}

/// <summary>Allocation: Adams's closed forms are optima; the allocators meet their goals within the shop's limits.</summary>
public class AllocationTests
{
    private static double Cost(IReadOnlyList<double> t, IReadOnlyList<double> tight) => t.Select((v, k) => tight[k] / v).Sum();

    public static IEnumerable<object[]> Problems() => new[]
    {
        new object[] { new[] { 1.0, 4.0, 0.3 }, new[] { 0.01, 0.02, 0.05 }, 0.2 },
        new object[] { new[] { 20.0, 0.5, 3.0, 7.0 }, new[] { 0.001, 0.1, 0.01, 0.003 }, 0.05 },
    };

    /// <summary>
    /// Adams's worst-case form meets its constraint, and no other point on the constraint costs less -
    /// tried at random and by small moves along it, either way.
    /// </summary>
    [Theory]
    [MemberData(nameof(Problems))]
    public void AdamsWorstCaseFormIsTheLeastCost(double[] s, double[] tight, double c)
    {
        var t = Adams.WorstCase(s, tight, c);
        Assert.Equal(c, t.Select((v, k) => Math.Abs(s[k]) * v).Sum(), 12);
        double best = Cost(t, tight);
        var random = new Random(1);
        for (int trial = 0; trial < 20000; trial++)
        {
            var u = s.Select(_ => random.NextDouble() + 1e-3).ToArray();
            double scale = c / u.Select((v, k) => Math.Abs(s[k]) * v).Sum();
            Assert.True(Cost(u.Select(v => v * scale).ToArray(), tight) >= best * (1.0 - 1e-12));
        }
        AssertLocalMinimum(t, tight, v => v.Select((x, k) => Math.Abs(s[k]) * x).Sum(), c);
    }

    [Theory]
    [MemberData(nameof(Problems))]
    public void AdamsRssFormIsTheLeastCost(double[] s, double[] tight, double c)
    {
        var t = Adams.Rss(s, tight, c);
        Assert.Equal(c, Math.Sqrt(t.Select((v, k) => s[k] * s[k] * v * v).Sum()), 12);
        double best = Cost(t, tight);
        var random = new Random(2);
        for (int trial = 0; trial < 20000; trial++)
        {
            var u = s.Select(_ => random.NextDouble() + 1e-3).ToArray();
            double scale = c / Math.Sqrt(u.Select((v, k) => s[k] * s[k] * v * v).Sum());
            Assert.True(Cost(u.Select(v => v * scale).ToArray(), tight) >= best * (1.0 - 1e-12));
        }
        AssertLocalMinimum(t, tight, v => Math.Sqrt(v.Select((x, k) => s[k] * s[k] * x * x).Sum()), c);
    }

    /// <summary>Moving two tolerances against each other, rescaled back onto the constraint, never costs less.</summary>
    private static void AssertLocalMinimum(double[] t, double[] tight, Func<double[], double> constraint, double c)
    {
        double best = Cost(t, tight);
        for (int i = 0; i < t.Length; i++)
            for (int j = 0; j < t.Length; j++)
            {
                if (i == j) continue;
                foreach (double step in new[] { 1e-3, -1e-3 })
                {
                    var u = (double[])t.Clone();
                    u[i] *= 1.0 + step;
                    u[j] *= 1.0 - step;
                    double scale = c / constraint(u);
                    Assert.True(Cost(u.Select(v => v * scale).ToArray(), tight) > best);
                }
            }
    }

    /// <summary>
    /// With errors at their end points and no linear or cross terms, Q never spreads and its mean is
    /// Q₀ + Σ A t²: the goal is Adams's RSS constraint with s² = A, and the cost-optimal allocation
    /// must be his closed form.
    /// </summary>
    [Fact]
    public void CostOptimalIsAdamsRssFormWhenTheMeanIsAllThereIs()
    {
        var model = KochTests.Synthetic();
        model = model with
        {
            Terms = model.Terms.Select(t => t with { B = 0.0 }).ToList(),
            Cross = new Dictionary<(int, int), double>(),
        };
        var limits = model.Terms.Select(t => new ShopLimits(t.Item.Max / 50.0, t.Item.Max * 50.0)).ToArray();
        var goal = new AllocationGoal(0.15);
        var statistics = new StatisticsModel(Distribution.EndPoint);

        var result = Allocator.Allocate(model, limits, goal, statistics, AllocationMethod.CostOptimal, round: false);

        // The x and y of the tilt share one value: one tolerance of A_x + A_y and one tightest.
        var groups = Allocator.AllocationGroups(model.Terms.Select(t => t.Item).ToList());
        var s = groups.Select(g => Math.Sqrt(g.Sum(i => model.Terms[i].A))).ToArray();
        var tight = groups.Select(g => limits[g[0]].Tightest).ToArray();
        var adams = Adams.Rss(s, tight, Math.Sqrt(goal.TargetQ - model.Q0));
        Assert.True(result.Feasible);
        for (int k = 0; k < groups.Count; k++)
            Assert.Equal(1.0, result.Items[groups[k][0]].Max / adams[k], 6);
        Assert.Equal(goal.TargetQ, result.Prediction.MeanQ, 9);
    }

    [Fact]
    public void EqualEffectGivesEveryFreeToleranceTheSameIncreaseAndMeetsTheGoal()
    {
        var model = KochTests.Synthetic();
        var limits = model.Terms.Select(t => new ShopLimits(t.Item.Max / 50.0, t.Item.Max * 50.0)).ToArray();
        var goal = new AllocationGoal(0.2, Shape: SpreadShape.Pearson);
        var result = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, AllocationMethod.EqualEffect, round: false);

        Assert.True(result.Feasible);
        Assert.Equal(goal.Limit, result.Prediction.ProbableWorst, 6);
        var groups = Allocator.AllocationGroups(result.Items);
        var effects = groups.Select(g => g.Sum(i => model.Terms[i].WorseIncrease(result.Items[i].Max))).ToArray();
        Assert.All(effects, e => Assert.Equal(1.0, e / effects[0], 6));
    }

    [Fact]
    public void CostOptimalCostsNoMoreThanEqualEffect()
    {
        var model = KochTests.Synthetic();
        var limits = model.Terms.Select(t => new ShopLimits(t.Item.Max / 50.0, t.Item.Max * 50.0)).ToArray();
        var goal = new AllocationGoal(0.2, Shape: SpreadShape.Pearson);
        var equal = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, AllocationMethod.EqualEffect, round: false);
        var cheap = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, AllocationMethod.CostOptimal, round: false);
        Assert.Equal(goal.Limit, cheap.Prediction.ProbableWorst, 6);
        Assert.True(cheap.Cost <= equal.Cost);
    }

    [Fact]
    public void AGoalTheTightestCannotMeetIsReportedAndHeldAtTheTightest()
    {
        var model = KochTests.Synthetic();
        var limits = model.Terms.Select(t => new ShopLimits(t.Item.Max, t.Item.Max * 10.0)).ToArray();
        var result = Allocator.Allocate(model, limits, new AllocationGoal(0.1001), StatisticsModel.Default, AllocationMethod.EqualEffect);
        Assert.False(result.Feasible);
        Assert.All(Enumerable.Range(0, result.Items.Count), i => Assert.True(result.AtTightest(i)));
    }

    [Fact]
    public void AnEasyGoalLeavesEverythingAtItsLoosest()
    {
        var model = KochTests.Synthetic();
        var limits = model.Terms.Select(t => new ShopLimits(t.Item.Max / 10.0, t.Item.Max)).ToArray();
        var result = Allocator.Allocate(model, limits, new AllocationGoal(10.0), StatisticsModel.Default, AllocationMethod.CostOptimal);
        Assert.True(result.Feasible);
        Assert.All(Enumerable.Range(0, result.Items.Count), i => Assert.True(result.AtLoosest(i)));
    }

    [Theory]
    [InlineData(0.0173, 0.015)]
    [InlineData(0.25, 0.25)]
    [InlineData(7.9, 6.0)]
    [InlineData(1.19e-4, 1.0e-4)]
    [InlineData(0.0999999, 0.08)]
    [InlineData(37.0, 30.0)]
    public void ValuesRoundDownToPreferredNumbers(double value, double rounded) =>
        Assert.Equal(rounded, Allocator.RoundDown(value), 12);

    [Fact]
    public void RoundingKeepsTheGoal()
    {
        var model = KochTests.Synthetic();
        var limits = model.Terms.Select(t => new ShopLimits(t.Item.Max / 50.0, t.Item.Max * 50.0)).ToArray();
        var goal = new AllocationGoal(0.2);
        var result = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, AllocationMethod.CostOptimal);
        Assert.True(result.Prediction.ProbableWorst <= goal.Limit);
    }

    /// <summary>
    /// The sampled goal is solved on Pearson's shape and corrected: the answer's sampled 97.7 % level
    /// is the limit to the half-percent of Q the correction stops at.
    /// </summary>
    [Theory]
    [InlineData(AllocationMethod.EqualEffect)]
    [InlineData(AllocationMethod.CostOptimal)]
    public void ASampledGoalIsMetToTheHalfPercent(AllocationMethod method)
    {
        var model = KochTests.Biased();
        var limits = model.Terms.Select(t => new ShopLimits(t.Item.HalfWidth / 50.0, t.Item.HalfWidth * 50.0)).ToArray();
        var goal = new AllocationGoal(0.2);
        var result = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, method, round: false);
        Assert.Equal(SpreadShape.Sampled, result.Prediction.Shape);
        double q = result.Prediction.QuantileQ(goal.Yield);
        Assert.InRange(q / goal.TargetQ, 0.995, 1.0);
    }

    /// <summary>The synthetic model with a compensator that every error moves, the tilts most.</summary>
    private static QuadraticModel WithCompensator(QuadraticModel model)
    {
        double[] slope = { 2.0, 0.01, 4.0, 3.0, 50.0 };
        var move = new QuadraticModel("move", 0.0,
                                      model.Terms.Select((t, i) => new QuadraticTerm(t.Item, 0.0, slope[i])).ToList(),
                                      new Dictionary<(int, int), double>());
        return model with { Compensator = move };
    }

    [Theory]
    [InlineData(AllocationMethod.EqualEffect)]
    [InlineData(AllocationMethod.CostOptimal)]
    public void ATravelGoalKeepsTheCompensatorWithinIt(AllocationMethod method)
    {
        var model = WithCompensator(KochTests.Synthetic());
        var limits = model.Terms.Select(t => new ShopLimits(t.Item.Max / 50.0, t.Item.Max * 50.0)).ToArray();
        var free = new AllocationGoal(0.2, Shape: SpreadShape.Pearson);
        var loose = Allocator.Allocate(model, limits, free, StatisticsModel.Default, method, round: false);
        double travel = 0.5 * loose.Travel!.Needed(free.Yield);        // half what the criterion alone would need

        var bound = Allocator.Allocate(model, limits, free with { Travel = travel }, StatisticsModel.Default, method, round: false);

        Assert.True(bound.Feasible);
        Assert.True(bound.Travel!.Needed(free.Yield) <= travel * (1.0 + 1e-9));
        Assert.True(bound.Prediction.ProbableWorst <= free.Limit * (1.0 + 1e-9));
        Assert.True(bound.Cost > loose.Cost);
    }

    [Fact]
    public void WithTwoConstraintsCostOptimalStillCostsNoMoreThanEqualEffect()
    {
        var model = WithCompensator(KochTests.Synthetic());
        var limits = model.Terms.Select(t => new ShopLimits(t.Item.Max / 50.0, t.Item.Max * 50.0)).ToArray();
        var goal = new AllocationGoal(0.2, Shape: SpreadShape.Pearson) { Travel = 0.05 };
        var equal = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, AllocationMethod.EqualEffect, round: false);
        var cheap = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, AllocationMethod.CostOptimal, round: false);
        Assert.True(cheap.Travel!.Needed(goal.Yield) <= 0.05 * (1.0 + 1e-6));
        Assert.True(cheap.Cost <= equal.Cost * (1.0 + 1e-9));
    }
}

/// <summary>Sinclair's budget trees: they hold every tolerance once and add up to Koch's totals.</summary>
public class BudgetTreeTests
{
    [Fact]
    public void TheRootOfEitherTreeIsKochsTotal()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = DefaultTolerances.Create(lens);
        var model = KochTests.Biased();          // any prediction with cross terms and a bias will do
        var p = Koch.Predict(model, StatisticsModel.Default);
        var root = BudgetTree.Evaluate(BudgetTree.ByKind(model.Terms.Select(t => t.Item).ToList()), p);
        Assert.Equal(p.MeanQ - p.Q0, root.Mean, 12);
        Assert.Equal(p.SigmaQ, root.Sigma, 12);
        Assert.Equal(Math.Sqrt(Math.Max(0.0, root.Mean + 2.0 * root.Sigma)), root.Equivalent, 14);
    }

    [Fact]
    public void ByElementHoldsEveryDefaultToleranceOnceAndNothingIsLeftOver()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = DefaultTolerances.Create(lens);
        var tree = BudgetTree.ByElement(lens, items);
        var leaves = Leaves(tree).ToList();
        var held = leaves.SelectMany(l => l.Terms).ToList();
        Assert.Equal(Enumerable.Range(0, items.Count), held.OrderBy(i => i));
        Assert.DoesNotContain(leaves, l => l.Name == "Other");
        // The middle element's lens maker holds its two surfaces' power, irregularity and wedge, its thickness and glass.
        var maker = tree.Children.Single(c => c.Name == "Element 3-4").Children.Single(c => c.Name == "Lens maker");
        Assert.Equal(2 * 4 + 1 + 2, maker.Terms.Count);
    }

    [Fact]
    public void ALinearNodeAddsItsChildrensSpreads()
    {
        var p = Koch.Predict(KochTests.Synthetic(), StatisticsModel.Default);
        var rss = BudgetTree.Evaluate(BudgetNode.Branch("r", new[] { BudgetNode.Leaf("a", new[] { 0 }), BudgetNode.Leaf("b", new[] { 1 }) }), p);
        var linear = BudgetTree.Evaluate(BudgetNode.Branch("l", new[]
        {
            BudgetNode.Branch("both", new[] { BudgetNode.Leaf("a", new[] { 0 }), BudgetNode.Leaf("b", new[] { 1 }) }, Combination.Linear),
        }), p);
        var a = rss.Children[0];
        var b = rss.Children[1];
        Assert.Equal(a.Sigma + b.Sigma, linear.Children[0].Sigma, 14);
        Assert.Equal(Math.Sqrt(a.Sigma * a.Sigma + b.Sigma * b.Sigma), Math.Sqrt(rss.Sigma * rss.Sigma - Math.Pow(rss.Children[2].Sigma, 2)), 12);
    }

    private static IEnumerable<BudgetNode> Leaves(BudgetNode node) =>
        node.Children.Count == 0 ? new[] { node } : node.Children.SelectMany(Leaves);
}

public class ToleranceFileTests
{
    [Fact]
    public void ASetReadsWithBiasesAndDistributionsAndWritesBackTheSame()
    {
        const string text = "# a set\nTFRN 3 5\nTTHI 4 -0.02 0.08 normal:3   # high side\nTEDX 3-4 0.02 endpoint\n\nTIND 1 0.001 uniform\n";
        var items = ToleranceSpec.ParseSet(text);
        Assert.Equal(4, items.Count);
        Assert.Equal(-5.0, items[0].Min);
        Assert.Null(items[0].Distribution);
        Assert.Equal(0.03, items[1].Center, 14);
        Assert.Equal(Distribution.Normal(3.0), items[1].Distribution);
        Assert.IsType<ElementDecentre>(items[2].Tolerance);
        Assert.Equal(Distribution.EndPoint, items[2].Distribution);

        var again = ToleranceSpec.ParseSet(ToleranceSpec.FormatSet(items));
        Assert.Equal(items, again);
    }

    [Fact]
    public void AGroupTiltsPivotIsReadAndWrittenBack()
    {
        var items = ToleranceSpec.ParseSet("TETX 1-4 0.05 pivot:6.2\nTETY 1-4 -0.02 0.05 normal:3 pivot:-1.5\nTETX 5-6 0.05\n");
        Assert.Equal(6.2, ((ElementTilt)items[0].Tolerance).PivotOffset);
        Assert.Equal(-1.5, ((ElementTilt)items[1].Tolerance).PivotOffset);
        Assert.Equal(Distribution.Normal(3.0), items[1].Distribution);
        Assert.Equal(0.0, ((ElementTilt)items[2].Tolerance).PivotOffset);

        string written = ToleranceSpec.FormatSet(items);
        Assert.Contains("TETX 1-4 0.05 pivot:6.2\n", written);
        Assert.Contains("TETX 5-6 0.05\n", written);
        Assert.Equal(items, ToleranceSpec.ParseSet(written));
    }

    /// <summary>The pivot moves the tilt: about the center of curvature, a lone sphere does not change at all.</summary>
    [Fact]
    public void APivotFromTheFileIsWhereTheTiltTurns()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        double radius = lens.System.Surfaces[3].Radius;
        var item = ToleranceSpec.ParseSet(FormattableString.Invariant($"TETX 3-3 0.4 pivot:{radius:R}\n"))[0];
        var perturbed = lens.Perturb();
        item.Tolerance.Apply(perturbed, item.Max);
        var criterion = new RmsWavefrontCriterion();
        Assert.Equal(criterion.Evaluate(lens.Model), criterion.Evaluate(perturbed.Build()), 9);
    }

    [Theory]
    [InlineData("TFRN 3")]
    [InlineData("TTHI 4 0.1 0.05")]
    [InlineData("TTHI 4 -0.1 0.1 lognormal")]
    [InlineData("TTHI 4 -0.1 0.1 normal extra")]
    [InlineData("TEDX 1-2 0.02 pivot:3")]
    [InlineData("TETX 1-2 0.02 pivot:far")]
    [InlineData("TETX 1-2 0.02 pivot:1 pivot:2")]
    public void ABadLineIsRefusedWithItsNumber(string line)
    {
        var e = Assert.Throws<FormatException>(() => ToleranceSpec.ParseSet("TFRN 1 2\n" + line));
        Assert.StartsWith("line 2", e.Message);
    }
}

public class ReportTests
{
    [Fact]
    public void AReportReadsBackWithItsTrees()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = DefaultTolerances.Create(lens).Take(5).ToList();
        var model = KochTests.Synthetic() with
        {
            Terms = KochTests.Synthetic().Terms.Select((t, i) => t with { Item = items[i] }).ToList(),
            Cross = new Dictionary<(int, int), double>(),
        };
        var p = Koch.Predict(model, StatisticsModel.Default);
        var doc = Report.Build("triplet", lens, "test", FocusCompensator.BackFocus(lens), StatisticsModel.Default,
                               items, p, new TravelPrediction(0.01, 0.1), limit: 0.2);

        var back = Report.FromJson(Report.ToJson(doc));

        Assert.Equal(5, back.Tolerances.Count);
        Assert.Equal(items[0].Tolerance.Code, back.Tolerances[0].Code);
        Assert.Equal(p.ProbableWorst, back.Prediction.Level977, 12);
        Assert.Equal(doc.ByKind.Children.Count, back.ByKind.Children.Count);
        Assert.Equal(0.01 + 2.0 * 0.1, back.Compensator!.Needed954, 2);
        Assert.Equal(p.MeanQ - p.Q0, back.Tolerances.Sum(t => t.MeanW2) + p.BiasShift, 12);
    }
}

/// <summary>Koch's prediction and the allocations on a real lens, against lenses traced in full.</summary>
public class LensStatisticsTests
{
    private static readonly RmsWavefrontCriterion Criterion = new();

    /// <summary>
    /// The test triplet with its commercial-grade default set, every cross term evaluated: Koch's
    /// mean and variance of W² against 800 lenses built at random and refocused. (4000 lenses on
    /// 2026-10-12: mean W² 3.376 ± 0.037 against Koch's 3.381; σ 2.34 against 2.29.)
    /// </summary>
    [Fact]
    public void KochPredictsTheTracedMonteCarlo()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var compensator = FocusCompensator.BackFocus(lens);
        var items = DefaultTolerances.Create(lens);
        var table = Sensitivity.Run(lens, items, Criterion, compensator);
        var model = QuadraticModel.Fit(lens, table, Criterion, compensator, CrossTerms.All);
        var prediction = Koch.Predict(model, StatisticsModel.Default);

        var mc = MonteCarlo.Run(lens, items, StatisticsModel.Default, Criterion, compensator, trials: 800, seed: 11);

        double se = Math.Sqrt(mc.VarianceQ / mc.Trials);
        Assert.InRange(prediction.MeanQ, mc.MeanQ - 4 * se, mc.MeanQ + 4 * se);
        Assert.InRange(prediction.SigmaQ / Math.Sqrt(mc.VarianceQ), 0.85, 1.15);
        Assert.InRange(prediction.ProbableWorst / mc.Percentile(Prediction.Yield97), 0.9, 1.1);
    }

    [Fact]
    public void AnAllocatedTripletMeetsItsGoalWhenBuilt()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var compensator = FocusCompensator.BackFocus(lens);
        var items = DefaultTolerances.Create(lens);
        var goal = new AllocationGoal(2.0);
        var budget = Budget.Allocate(lens, items, ShopLimits.FromGrades(lens, items), Criterion, compensator,
                                     goal, StatisticsModel.Default, AllocationMethod.CostOptimal);
        Assert.True(budget.Allocation.Feasible);
        Assert.True(budget.Allocation.Prediction.ProbableWorst <= goal.Limit);
        // Glass is bought by grade: every index and Abbe tolerance is one the glass makers sell.
        foreach (var item in budget.Allocation.Items)
            if (DefaultTolerances.Grades(item.Tolerance, lens) is { } grades)
                Assert.Contains(grades, g => Math.Abs(g - item.HalfWidth) <= 1e-12 * g);

        var mc = MonteCarlo.Run(lens, budget.Allocation.Items, StatisticsModel.Default, Criterion, compensator, trials: 400, seed: 4);
        Assert.InRange(mc.Yield(goal.Limit), 0.95, 1.0);
    }

    /// <summary>
    /// Monte Carlo lenses computed again in OpticStudio one by one, for agreement: the triplet allocated to 2 waves at
    /// least cost, without its glass tolerances (a .zmx names glass, so a changed index cannot be
    /// written), seed 1. Trials 0 and 3 were exported refocused with <c>--export-trials</c> and
    /// OpticStudio's Zernike RMS fit error at one term, 1024², read on 2026-10-12, and again on
    /// 2026-10-30 for the lenses the new per-lens seeding draws: within its grid's 2e-4, as for the
    /// single tolerances.
    /// </summary>
    [Theory]
    [InlineData(0, 2, 0, 2.32943682)]
    [InlineData(0, 0, 1, 0.46350874)]
    [InlineData(0, 1, 2, 1.35436471)]
    [InlineData(3, 2, 1, 2.16476186)]
    [InlineData(3, 0, 0, 1.00588803)]
    public void MonteCarloLensesAgreeWithOpticStudio(int trial, int field, int wave, double opticStudio)
    {
        const string set =
            "TFRN 1 25\nTSTX 1 0.06\nTSTY 1 0.06\nTFRN 2 12\nTSTX 2 0.06\nTSTY 2 0.06\nTFRN 3 8\nTSTX 3 0.04\nTSTY 3 0.04\n" +
            "TFRN 4 15\nTSTX 4 0.05\nTSTY 4 0.05\nTFRN 6 50\nTSTX 6 0.06\nTSTY 6 0.06\nTFRN 7 15\nTSTX 7 0.04\nTSTY 7 0.04\n" +
            "TTHI 1 0.5\nTTHI 2 0.15\nTTHI 3 0.15\nTTHI 4 0.12\nTTHI 5 0.12\nTTHI 6 0.15\n" +
            "TEDX 1-2 0.05\nTEDY 1-2 0.05\nTETX 1-2 0.15\nTETY 1-2 0.15\nTEDX 3-4 0.025\nTEDY 3-4 0.025\nTETX 3-4 0.1\nTETY 3-4 0.1\n" +
            "TEDX 6-7 0.03\nTEDY 6-7 0.03\nTETX 6-7 0.1\nTETY 6-7 0.1\n";
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = ToleranceSpec.ParseSet(set);
        var criterion = new RmsWavefrontCriterion { Options = WavefrontErrorCalculator.Core.WavefrontOptions.ZemaxZernike };

        var x = MonteCarlo.Errors(items, StatisticsModel.Default, seed: 1, trial);
        var perturbed = lens.Perturb();
        for (int i = 0; i < items.Count; i++) items[i].Tolerance.Apply(perturbed, x[i]);
        FocusCompensator.BackFocus(lens).Solve(perturbed, criterion);
        var model = perturbed.Build();

        double ours = WavefrontErrorCalculator.Core.WavefrontCalculator.Compute(model, field, wave, criterion.EffectiveOptions, criterion.Sampling)
                                                                       .Statistics.RmsStandardDeviation;
        Assert.Equal(opticStudio, ours, opticStudio * 2.5e-4);
    }

    [Fact]
    public void ACompensatorOutOfTravelStopsAtItsEnd()
    {
        // A 0.5 mm longer air space wants the back focus about 0.5 mm shorter; 0.1 mm is all it has.
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var free = FocusCompensator.BackFocus(lens);
        var limited = free with { Travel = 0.1 };
        var spec = new[] { ((Tolerance)new ThicknessChange(4), 0.5) };

        var unlimited = Sensitivity.Evaluate(lens, spec, Criterion, free);
        var stopped = Sensitivity.Evaluate(lens, spec, Criterion, limited);

        Assert.False(unlimited.AtLimit);
        Assert.True(Math.Abs(unlimited.Change) > 0.1);
        Assert.True(stopped.AtLimit);
        Assert.Equal(Math.CopySign(0.1, unlimited.Change), stopped.Change, 14);
        Assert.True(stopped.Criterion > unlimited.Criterion);

        // Within its travel, a limit changes nothing.
        var small = new[] { ((Tolerance)new ThicknessChange(4), 0.01) };
        Assert.Equal(Sensitivity.Evaluate(lens, small, Criterion, free).Criterion,
                     Sensitivity.Evaluate(lens, small, Criterion, limited).Criterion, 12);
    }

    /// <summary>
    /// Koch's prediction of the back focus's move - a quadratic in the errors fitted like the
    /// criterion - against the moves of lenses built at random and refocused.
    /// </summary>
    [Fact]
    public void KochPredictsTheCompensatorsTravel()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var compensator = FocusCompensator.BackFocus(lens);
        var items = DefaultTolerances.Create(lens);
        var model = QuadraticModel.Build(lens, items, Criterion, compensator);
        var travel = TravelPrediction.Of(model, StatisticsModel.Default)!;

        var mc = MonteCarlo.Run(lens, items, StatisticsModel.Default, Criterion, compensator, trials: 400, seed: 5);
        var moves = mc.Compensators.ToArray();           // from the refocused nominal
        double mean = moves.Average();
        double sd = Math.Sqrt(moves.Sum(m => (m - mean) * (m - mean)) / (moves.Length - 1));

        Assert.InRange(sd / travel.Sigma, 0.9, 1.1);
        Assert.InRange(mean - travel.Mean, -4 * sd / Math.Sqrt(moves.Length), 4 * sd / Math.Sqrt(moves.Length));
    }

    /// <summary>
    /// A set with biases and its own distributions - the thicknesses made long, a glass normal, a
    /// decentre at its end points - against 1000 lenses traced. (4000 on 2026-10-12: mean W² 1.8695 ±
    /// 0.008 against Koch's 1.8713, skewness 0.12 against 0.12, the 97.7 % lens 1.641 against the
    /// sampled shape's 1.642 - and the gamma's 1.738, which a spread this flat defeats.)
    /// </summary>
    [Fact]
    public void KochPredictsABiasedSetWithItsOwnDistributions()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var compensator = FocusCompensator.BackFocus(lens);
        var items = ToleranceSpec.ParseSet(
            "TFRN 3 5\nTFRN 4 -3 7 normal:3\nTTHI 2 -0.02 0.1\nTTHI 4 0.0 0.1 normal:2\nTTHI 5 -0.05 0.05\n" +
            "TIND 3 0.001 normal:3\nTEDX 3-4 0.02 endpoint\nTEDY 3-4 0.02 endpoint\nTSTX 3 0.03\nTSTY 3 0.03\n");
        var model = QuadraticModel.Build(lens, items, Criterion, compensator, CrossTerms.All);
        var p = Koch.Predict(model, StatisticsModel.Default);

        var mc = MonteCarlo.Run(lens, items, StatisticsModel.Default, Criterion, compensator, trials: 1000, seed: 21);

        double se = Math.Sqrt(mc.VarianceQ / mc.Trials);
        Assert.InRange(p.MeanQ, mc.MeanQ - 4 * se, mc.MeanQ + 4 * se);
        Assert.InRange(p.SigmaQ / Math.Sqrt(mc.VarianceQ), 0.9, 1.1);
        Assert.InRange(p.ProbableWorst / mc.Percentile(Prediction.Yield97), 0.96, 1.04);
        Assert.NotEqual(0.0, p.BiasShift);
    }
}

public class TrialSeedingTests
{
    private static double Correlation(double[] a, double[] b)
    {
        double ma = a.Average(), mb = b.Average();
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sab += (a[i] - ma) * (b[i] - mb);
            saa += (a[i] - ma) * (a[i] - ma);
            sbb += (b[i] - mb) * (b[i] - mb);
        }
        return sab / Math.Sqrt(saa * sbb);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(-4)]
    public void LensesOfARunAreUnrelated(int seed)
    {
        // Each lens's first draw, across a run: no lens like the next, and spread as random draws
        // are - the counts in tenths of the range scatter as a binomial does, neither more nor (as a
        // grid would) far less. Seeding Random with numbers a fixed step apart failed both.
        const int n = 40000;
        var first = Enumerable.Range(0, n).Select(k => SplitMix.ForTrial(seed, k).NextDouble()).ToArray();
        var second = Enumerable.Range(0, n).Select(k => { var r = SplitMix.ForTrial(seed, k); r.NextDouble(); return r.NextDouble(); }).ToArray();
        Assert.True(Math.Abs(Correlation(first[..^1], first[1..])) < 4 / Math.Sqrt(n));
        Assert.True(Math.Abs(Correlation(first, second)) < 4 / Math.Sqrt(n));
        Assert.True(Math.Abs(Correlation(first[..^1], second[1..])) < 4 / Math.Sqrt(n));
        var counts = new double[10];
        foreach (var v in first) counts[(int)(v * 10)]++;
        double expected = n / 10.0, chi2 = counts.Sum(c => (c - expected) * (c - expected) / expected);
        Assert.InRange(chi2, 1.0, 27.9);     // 9 degrees of freedom: the 0.1 % and 99.9 % points
    }
}
