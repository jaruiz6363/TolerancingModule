using AberrationCalculator.Core.Enums;
using Tolerancing.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>An RMS wavefront's cross terms from the slopes of its pupil samples, without tracing pairs.</summary>
public class WavefrontCrossTermTests
{
    /// <summary>The triplet with its rear lens held to a 4.5 mm semi-diameter: 20 of its 1728 samples clip.</summary>
    private static NominalLens Vignetted() =>
        ToleranceTests.Read(ToleranceTests.Triplet, s =>
        {
            foreach (int k in new[] { 6, 7 })
            {
                s.Surfaces[k].SemiDiameter = 4.5;
                s.Surfaces[k].SemiDiameterMode = SemiDiameterMode.Fixed;
            }
        });

    private static (NominalLens Lens, RmsWavefrontCriterion Rms, FocusCompensator Compensator, List<ToleranceItem> Items) Setup()
    {
        var lens = Vignetted();
        var rms = new RmsWavefrontCriterion().WithoutUntraceableFields(lens.Model, out _);
        var compensator = FocusCompensator.BackFocus(lens);
        var items = DefaultTolerances.Create(lens, ShopGrade.Commercial, PowerFringes.HeNe, compensator).ToList();
        return (lens, rms, compensator, items);
    }

    [Fact]
    public void TheVectorsLengthIsTheCriterionAndAClippedSampleIsLeftOut()
    {
        var (lens, rms, _, _) = Setup();
        var v = rms.Vector(lens.Model)!;
        Assert.Equal(20, v.Count(double.IsNaN));
        Assert.Equal(rms.Evaluate(lens.Model), Math.Sqrt(v.Where(x => !double.IsNaN(x)).Sum(x => x * x)), 9);
    }

    /// <summary>A preset whose own RMS is about zero still has the criterion's standard deviation as the vector's length.</summary>
    [Theory]
    [InlineData("optiland")]
    [InlineData("zemax")]
    public void TheVectorsLengthIsTheCriterionUnderEveryPreset(string preset)
    {
        var lens = ToleranceTests.Read(ToleranceTests.Triplet);
        var rms = new RmsWavefrontCriterion { Options = WavefrontErrorCalculator.Core.WavefrontOptions.Preset(preset) };
        var v = rms.Vector(lens.Model)!;
        Assert.Equal(rms.Evaluate(lens.Model), Math.Sqrt(v.Sum(x => x * x)), 9);
    }

    [Fact]
    public void ATolerancesSlopesAreKeptWhileItClipsFewSamples()
    {
        var (lens, rms, compensator, items) = Setup();
        var probes = items.Select(i => ToleranceItem.Symmetric(i.Tolerance, Math.Max(Math.Abs(i.Min), Math.Abs(i.Max)))).ToList();
        var table = Sensitivity.Run(lens, probes, new ICriterion[] { rms }, compensator, wavefronts: true)[0];

        // Of the tolerances that move rays - not an irregularity, a figure error of a fraction of a
        // micrometer - some change which rim rays clip. (Before the lens kept the design's stop,
        // an error ahead of it resized the stop too, and most did.)
        var moving = Enumerable.Range(0, items.Count).Where(k => items[k].Tolerance is not SurfaceIrregularity).ToList();
        var strictSlopes = QuadraticModel.WavefrontSlopes(table, 0.0);
        int strict = moving.Count(k => strictSlopes[k] != null);
        int relaxed = QuadraticModel.WavefrontSlopes(table).Count(s => s != null);
        Assert.InRange(strict, 1, moving.Count - 1);
        Assert.Equal(items.Count, relaxed);                 // but none by more than 10 % of the samples
    }

    /// <summary>
    /// Every third tolerance, so that the pairs are few enough to trace: the 97.7 % level and the
    /// spread from the slopes are those of the traced pairs, though half the tolerances clip.
    /// </summary>
    [Fact]
    public void CrossTermsFromTheSlopesPredictWhatTracedPairsDo()
    {
        var (lens, rms, compensator, all) = Setup();
        var items = all.Where((_, i) => i % 3 == 0).ToList();

        var traced = QuadraticModel.Build(lens, items, rms, compensator, CrossTerms.All);
        var slopes = QuadraticModel.Build(lens, items, rms, compensator, CrossTerms.Wavefront);

        Assert.Equal(traced.Q0, slopes.Q0, 12);
        double num = 0.0, den = 0.0;
        foreach (var (pair, c) in traced.Cross)
        {
            double scale = items[pair.Item1].HalfWidth * items[pair.Item2].HalfWidth;
            num += Math.Abs(slopes.Cross.GetValueOrDefault(pair) - c) * scale;
            den += Math.Abs(c) * scale;
        }
        Assert.True(num / den < 0.15, $"cross terms differ by {num / den:P1}");

        var (pt, ps) = (Koch.Predict(traced, StatisticsModel.Default), Koch.Predict(slopes, StatisticsModel.Default));
        Assert.Equal(1.0, ps.ProbableWorst / pt.ProbableWorst, 2);
        Assert.Equal(1.0, ps.SigmaQ / pt.SigmaQ, 1);
    }

    /// <summary>The slopes are the default, for a model and for an allocation.</summary>
    [Fact]
    public void TheSlopesAreTheDefault()
    {
        var (lens, rms, compensator, all) = Setup();
        var items = all.Where((_, i) => i % 3 == 0).ToList();

        var byDefault = QuadraticModel.Build(lens, items, rms, compensator);
        var slopes = QuadraticModel.Build(lens, items, rms, compensator, CrossTerms.Wavefront);
        Assert.Equal(slopes.Cross.OrderBy(p => p.Key), byDefault.Cross.OrderBy(p => p.Key));

        var parameter = typeof(Budget).GetMethod(nameof(Budget.Allocate))!.GetParameters().Single(p => p.ParameterType == typeof(CrossTerms));
        Assert.Equal(CrossTerms.Wavefront, parameter.DefaultValue);
    }
}
