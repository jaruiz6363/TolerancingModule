using AberrationCalculator.Core.IO;
using AberrationCalculator.Core.Models;
using Tolerancing.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>
/// What a sweep of unlike lenses found (2026-10-12): wide fields no ray of which passes, files not at
/// best focus, apertures and fields other than an entrance pupil and angles, and mirrors whose
/// errors cancel across the system.
/// </summary>
public class RobustnessTests
{
    private static readonly string Smith = Path.Combine(AppContext.BaseDirectory, "TestData", "smith1985_laser.zmx");

    /// <summary>The triplet with a fourth field at 80°, where no ray gets through its stop.</summary>
    private static NominalLens WithAHopelessField() =>
        ToleranceTests.Read(ToleranceTests.Triplet, s => s.Fields.Add(new Field { Y = 80.0, Weight = 1.0 }));

    [Fact]
    public void AFieldNoRayOfWhichPassesIsReportedNotThrown()
    {
        var lens = WithAHopelessField();
        var n = NominalPerformance.Evaluate(lens.Model);
        Assert.Equal(4, n.Fields.Count);
        Assert.Null(n.Fields[2].Failure);
        Assert.NotNull(n.Fields[3].Failure);
        Assert.True(double.IsNaN(n.Fields[3].Rms));
    }

    [Fact]
    public void AnUntraceableFieldIsLeftOutOfTheCriterionAndTheRestIsTheSame()
    {
        var lens = WithAHopelessField();
        var criterion = new RmsWavefrontCriterion();
        Assert.True(double.IsPositiveInfinity(criterion.Evaluate(lens.Model)));

        var traceable = criterion.WithoutUntraceableFields(lens.Model, out var excluded);

        Assert.Equal(new[] { 3 }, excluded);
        var plain = ToleranceTests.Read(ToleranceTests.Triplet);
        Assert.Equal(criterion.Evaluate(plain.Model), traceable.Evaluate(lens.Model), 12);
        Assert.Same(criterion, criterion.WithoutUntraceableFields(plain.Model, out var none));
        Assert.Empty(none);
    }

    [Fact]
    public void TheTravelIsCentredWhereTheNominalLensRefocuses()
    {
        // The triplet saved 0.3 mm out of focus: the compensator first moves back 0.3 mm, which must
        // not count against a 0.1 mm travel.
        var lens = ToleranceTests.Read(ToleranceTests.Triplet, s => s.Surfaces[7].Thickness += 0.3);
        var criterion = new RmsWavefrontCriterion();
        var free = FocusCompensator.BackFocus(lens);
        var centred = (free with { Travel = 0.1 }).Centred(lens, criterion);
        Assert.InRange(centred.Center, -0.35, -0.25);

        var nominal = Sensitivity.Evaluate(lens, null, 0.0, criterion, centred);
        Assert.False(nominal.AtLimit);
        Assert.Equal(Sensitivity.Evaluate(lens, null, 0.0, criterion, free).Criterion, nominal.Criterion, 12);

        var uncentred = Sensitivity.Evaluate(lens, null, 0.0, criterion, free with { Travel = 0.1 });
        Assert.True(uncentred.AtLimit);
    }

    [Fact]
    public void MonteCarloFocusMovesAreFromTheRefocusedNominal()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet, s => s.Surfaces[7].Thickness += 0.3);
        var items = new[] { ToleranceItem.Symmetric(new PowerFringes(3), 2.0) };
        var mc = MonteCarlo.Run(lens, items, StatisticsModel.Default, new RmsWavefrontCriterion(),
                                FocusCompensator.BackFocus(lens), trials: 20, seed: 2);
        Assert.All(mc.Compensators, c => Assert.InRange(c, -0.12, 0.12));      // not about -0.3
    }

    [Fact]
    public void ALensWithAnObjectNaAndObjectHeightFieldsExportsAndReadsBackTheSame()
    {
        var catalog = AberrationCalculator.Core.Glass.CatalogLocator.LoadBundled();
        var lens = new NominalLens(LensFile.Read(Smith, catalog), catalog);
        string path = Path.Combine(Path.GetTempPath(), $"tol-export-{Guid.NewGuid():N}.zmx");
        try
        {
            ZemaxExport.Write(lens.Perturb(), path);
            string text = File.ReadAllText(path);
            Assert.Contains("OBNA 0.0756 0", text);
            Assert.Contains("FTYP 1 ", text);
            var back = new NominalLens(LensFile.Read(path, catalog), catalog);
            var criterion = new RmsWavefrontCriterion();
            Assert.Equal(criterion.Evaluate(lens.Model), criterion.Evaluate(back.Model), 12);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnFNumberApertureIsExportedAsOne()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet, s =>
        {
            s.Aperture.Type = AberrationCalculator.Core.Enums.ApertureType.FNumber;
            s.Aperture.Value = 5.0;
        });
        Assert.Contains("FNUM 5 0", ZemaxExport.ToZmx(lens.Perturb()));
    }

    [Fact]
    public void ASmallSetEvaluatesEveryCrossTermByDefault()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = DefaultTolerances.Create(lens).Take(6).ToList();
        var criterion = new RmsWavefrontCriterion();
        var compensator = FocusCompensator.BackFocus(lens);
        var model = QuadraticModel.Fit(lens, Sensitivity.Run(lens, items, criterion, compensator), criterion, compensator);
        Assert.Equal(6 * 5 / 2, model.Cross.Count);
        Assert.Equal(6 * 5 / 2, model.Compensator!.Cross.Count);
    }
}

/// <summary>The defects an independent review of M2-M4 found (2026-10-12), each held fixed.</summary>
public class ReviewFindingTests
{
    [Fact]
    public void ANormalCutOffAtNoSigmasIsRefusedRatherThanSampledForever()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Distribution.Normal(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialDistribution.Rayleigh(-1.0));
        var e = Assert.Throws<FormatException>(() => ToleranceSpec.ParseSet("TFRN 1 2\nTTHI 2 0.1 normal:0\n"));
        Assert.StartsWith("line 2", e.Message);
    }

    [Fact]
    public void ALensWhoseErrorsCannotBeMadeScoresInfinityAndHasNoMove()
    {
        // A million fringes: no curvature gives that sag, so every trial's lens cannot be built.
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var items = new[] { ToleranceItem.Symmetric(new PowerFringes(1), 1e6) };
        var mc = MonteCarlo.Run(lens, items, new StatisticsModel(Distribution.EndPoint), new RmsWavefrontCriterion(),
                                FocusCompensator.BackFocus(lens), trials: 4);
        Assert.Equal(4, mc.Untraceable);
        Assert.All(mc.Compensators, c => Assert.True(double.IsNaN(c)));
        Assert.Equal((0.0, 0.0), mc.CompensatorRange);
        Assert.Equal(0.0, mc.Yield(1e9));
    }

    [Fact]
    public void ANominalThatCannotBeTracedIsSaidSoNotFittedAsNaN()
    {
        var row = new SensitivityRow(ToleranceItem.Symmetric(new ThicknessChange(1), 0.1), 1.0, 0.0, 1.0, 0.0, double.PositiveInfinity);
        var table = new SensitivityTable("test", double.PositiveInfinity, double.PositiveInfinity, 0.0, null, new[] { row });
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        Assert.Throws<InvalidOperationException>(() => QuadraticModel.Fit(lens, table, new RmsWavefrontCriterion(), null, CrossTerms.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAllocationWithAnInsensitiveGroupStillMeetsItsGoal(bool crossTerm)
    {
        // The second term has no effect of its own, so the equal-effect search leaves it at its
        // loosest whatever δ is. With its cross term to the tilt it still adds variance: the search's
        // end then misses the goal, and the answer must be drawn back until it meets it.
        var model = KochTests.Synthetic();
        model = model with
        {
            Terms = model.Terms.Select((t, i) => i == 1 ? t with { A = 0.0, B = 0.0 } : t).ToList(),
            Cross = model.Cross.Where(c => crossTerm || c.Key != (1, 2)).ToDictionary(c => c.Key, c => c.Value),
        };
        var limits = model.Terms.Select(t => new ShopLimits(t.Item.Max / 50.0, t.Item.Max * 50.0)).ToArray();
        foreach (var method in new[] { AllocationMethod.EqualEffect, AllocationMethod.CostOptimal })
        {
            var goal = new AllocationGoal(0.2, Shape: SpreadShape.Pearson);
            var result = Allocator.Allocate(model, limits, goal, StatisticsModel.Default, method, round: false);
            Assert.True(result.Feasible);
            Assert.True(result.Prediction.ProbableWorst <= goal.Limit * (1.0 + 1e-9), $"{method}: {result.Prediction.ProbableWorst}");
            if (!crossTerm) Assert.True(result.AtLoosest(1), method.ToString());
        }
    }

    [Fact]
    public void ARangeWhollyBelowNominalIsDrawnWithBothSigns()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var settings = new Tolerancing.Drawings.DrawingSettings { Date = "2026-10-12" };
        var d = Tolerancing.Drawings.ElementDrawings.Build(lens, new[] { new ToleranceItem(new ThicknessChange(3), -0.08, -0.02) }, settings)[1];
        var text = string.Join("\n", Tolerancing.Drawings.Layout.Draw(d, settings).Marks.OfType<Tolerancing.Drawings.Label>().Select(l => l.Text));
        Assert.Contains("1.000 -0.020/-0.080", text);
    }

    [Fact]
    public void AnEdgeTheMarginMakesTooThinIsDrawnBackTowardsTheAperture()
    {
        // The last element made 2 mm thick: at the clear aperture plus a millimetre its faces cross.
        var lens = ToleranceTests.Read(ToleranceTests.Triplet, s => s.Surfaces[6].Thickness = 2.0);
        var d = Tolerancing.Drawings.ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(), new Tolerancing.Drawings.DrawingSettings())[2];
        Assert.InRange(d.Diameter, 14.0, 14.9);
        Assert.True(d.EdgeThickness >= 0.5 || d.Diameter == 14.0);
        Assert.Contains(d.Warnings, w => w.Contains("cut to"));
    }

    [Fact]
    public void ASurfaceThatCannotReachTheEdgeEndsInAFlatLand()
    {
        // Surface 4 of radius 5.26 under a clear aperture of 10: past Ø10.5 it has no sag, and the
        // edge is Ø11. It is drawn with a land from its clear aperture out, as a fisheye's front
        // meniscus is made.
        var lens = ToleranceTests.Read(ToleranceTests.Triplet, s => s.Surfaces[4].Curvature = 0.19);
        var settings = new Tolerancing.Drawings.DrawingSettings { Date = "2026-10-12" };
        var d = Tolerancing.Drawings.ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(), settings)[1];
        Assert.Equal(11.0, d.Diameter);
        Assert.Equal(10.0, d.Back.LandFrom);
        Assert.Null(d.Front.LandFrom);
        Assert.True(double.IsFinite(d.EdgeThickness));
        Assert.Equal(Tolerancing.Drawings.ElementDrawing.Sag(d.Back, 5.0), Tolerancing.Drawings.ElementDrawing.Sag(d.Back, 5.5));
        var text = string.Join("\n", Tolerancing.Drawings.Layout.Draw(d, settings).Marks.OfType<Tolerancing.Drawings.Label>().Select(l => l.Text));
        Assert.Contains("flat land from Ø10.00", text);
    }
}

/// <summary>Found drawing the Eggert patent lens (2026-10-12): a 0.263 mm centre allocated ±0.5 mm.</summary>
public class ThicknessCapTests
{
    private static NominalLens ThinMiddle() =>
        ToleranceTests.Read(ToleranceTests.Triplet, s => s.Surfaces[3].Thickness = 0.3);

    /// <summary>
    /// Found verifying on a patent lens whose stop touches its first surface (2026-10-13): a space of
    /// no thickness was given a tolerance of ±0, which no quadratic can be fitted to.
    /// </summary>
    [Fact]
    public void ASpaceOfNoThicknessHasNoTolerance()
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet, s => { s.Surfaces[5].Thickness += s.Surfaces[4].Thickness; s.Surfaces[4].Thickness = 0.0; });
        var items = DefaultTolerances.Create(lens);
        Assert.DoesNotContain(items, i => i.Tolerance is ThicknessChange { Surface: 4 });
        Assert.Contains(items, i => i.Tolerance is ThicknessChange { Surface: 5 });
        Assert.All(items, i => Assert.True(i.Max > i.Min));
    }

    [Fact]
    public void ADefaultThicknessToleranceIsNoMoreThanAFifthOfTheThickness()
    {
        var lens = ThinMiddle();
        var items = DefaultTolerances.Create(lens, ShopGrade.LowCost);
        var middle = items.Single(i => i.Tolerance is ThicknessChange { Surface: 3 });
        Assert.Equal(0.3 * DefaultTolerances.ThicknessFraction, middle.Max, 12);
        var air = items.Single(i => i.Tolerance is ThicknessChange { Surface: 2 });
        Assert.Equal(DefaultTolerances.For(ShopGrade.LowCost).ThicknessMm, air.Max, 12);     // 5 mm: the grade\'s 0.5 stands
    }

    [Fact]
    public void AllocationNeverLoosensAThicknessPastItsCap()
    {
        var lens = ThinMiddle();
        var items = DefaultTolerances.Create(lens);
        var limits = ShopLimits.FromGrades(lens, items);
        int i = items.FindIndex(t => t.Tolerance is ThicknessChange { Surface: 3 });
        Assert.Equal(0.06, limits[i].Loosest, 12);
        Assert.Equal(0.05, limits[i].Tightest, 12);
    }

    [Fact]
    public void ADrawingSaysWhenACentreIsThinOrItsToleranceSwallowsIt()
    {
        var lens = ThinMiddle();
        var settings = new Tolerancing.Drawings.DrawingSettings();
        var thin = Tolerancing.Drawings.ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(), settings with { DefaultThickness = 0.05 })[1];
        Assert.Contains(thin.Warnings, w => w.Contains("center thickness 0.3 mm: thin"));
        var swallowed = Tolerancing.Drawings.ElementDrawings.Build(lens, new[] { ToleranceItem.Symmetric(new ThicknessChange(3), 0.5) }, settings)[1];
        Assert.Contains(swallowed.Warnings, w => w.Contains("goes to nothing"));
    }
}
