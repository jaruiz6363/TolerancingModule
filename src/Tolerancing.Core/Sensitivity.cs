namespace Tolerancing.Core;

/// <summary>
/// One tolerance at each end of its range: the criterion there, after the compensator, and how
/// far the compensator had to move. Changes are from the nominal lens with its compensator
/// adjusted the same way, so that a change is the tolerance's alone and not the design's own
/// distance from best focus.
/// </summary>
public sealed record SensitivityRow(ToleranceItem Item,
                                    double MinCriterion, double MinCompensator,
                                    double MaxCriterion, double MaxCompensator,
                                    double Reference)
{
    /// <summary>For a <see cref="DeviationCriterion"/>, the departure at each end; for an MTF, its intercepts; null otherwise.</summary>
    public IReadOnlyList<double>? MinDeviation { get; init; }
    public IReadOnlyList<double>? MaxDeviation { get; init; }

    /// <summary>Each of the compensator's adjustments at each end, from where it sets the nominal lens; null when it has none.</summary>
    public IReadOnlyList<double>? MinAdjustments { get; init; }
    public IReadOnlyList<double>? MaxAdjustments { get; init; }

    public double MinChange => MinCriterion - Reference;
    public double MaxChange => MaxCriterion - Reference;

    /// <summary>The larger increase of the two ends, or zero if neither end is worse.</summary>
    public double WorstIncrease => Math.Max(0.0, Math.Max(MinChange, MaxChange));
}

/// <summary>A sensitivity analysis (Ginsberg 1981 Fig. 5; Hilbert 1979 Table 6).</summary>
public sealed record SensitivityTable(string Criterion, double Nominal, double NominalCompensated,
                                      double NominalCompensator, FocusCompensator? Compensator,
                                      IReadOnlyList<SensitivityRow> Rows)
{
    /// <summary>The refocused nominal lens's departure, or for an RMS wavefront run with vectors its <see cref="RmsWavefrontCriterion.Vector"/>.</summary>
    public IReadOnlyList<double>? NominalDeviation { get; init; }

    /// <summary>The compensator's adjustments on the nominal lens: what each row's are measured from; null when it has none.</summary>
    public IReadOnlyList<double>? NominalAdjustments { get; init; }

    /// <summary>
    /// The root sum of squares of each tolerance's worse increase. It treats the tolerances as
    /// independent and linear, which a quadratic criterion such as RMS wavefront is not (Koch 1978):
    /// an estimate to rank a budget by, not a prediction of yield.
    /// </summary>
    public double RssIncrease => Math.Sqrt(Rows.Sum(r => r.WorstIncrease * r.WorstIncrease));

    public double Estimated => NominalCompensated + RssIncrease;

    /// <summary>The rows in order of their worse increase, largest first.</summary>
    public IEnumerable<SensitivityRow> WorstOffenders => Rows.OrderByDescending(r => r.WorstIncrease);
}

public static class Sensitivity
{
    /// <summary>
    /// Evaluates every tolerance at its minimum and its maximum, one at a time, in parallel: each
    /// evaluation perturbs a copy of the lens of its own.
    /// </summary>
    public static SensitivityTable Run(NominalLens lens, IReadOnlyList<ToleranceItem> items,
                                       ICriterion criterion, FocusCompensator? compensator)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        return Run(lens, items, new[] { criterion }, compensator)[0];
    }

    /// <summary>
    /// One sensitivity table per criterion, from one trace of each end: the lens is perturbed and
    /// refocused once, and every criterion measured on it (<see cref="Evaluate(NominalLens, IEnumerable{ValueTuple{Tolerance, double}}, IReadOnlyList{ICriterion}, FocusCompensator?)"/>).
    /// Each table is the one its criterion would have alone.
    /// </summary>
    public static IReadOnlyList<SensitivityTable> Run(NominalLens lens, IReadOnlyList<ToleranceItem> items,
                                                      IReadOnlyList<ICriterion> criteria, FocusCompensator? compensator,
                                                      bool wavefronts = false)
    {
        ArgumentNullException.ThrowIfNull(lens);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count == 0) throw new ArgumentException("no criterion to run");

        var nominal = criteria.Select(c => c.Evaluate(lens.Model)).ToArray();
        var reference = Evaluate(lens, Array.Empty<(Tolerance, double)>(), criteria, compensator, wavefronts);
        var ends = new Compensated[items.Count * 2][];
        Parallel.For(0, items.Count * 2, k =>
        {
            var item = items[k / 2];
            ends[k] = Evaluate(lens, new[] { (item.Tolerance, k % 2 == 0 ? item.Min : item.Max) }, criteria, compensator, wavefronts);
        });

        return criteria.Select((criterion, m) =>
        {
            var rows = items.Select((item, i) =>
            {
                var (lo, hi) = (ends[2 * i][m], ends[2 * i + 1][m]);
                return new SensitivityRow(item, lo.Criterion, lo.Change - reference[m].Change, hi.Criterion, hi.Change - reference[m].Change,
                                          reference[m].Criterion) { MinDeviation = lo.Deviation, MaxDeviation = hi.Deviation,
                    MinAdjustments = Less(lo.Adjustments, reference[m].Adjustments), MaxAdjustments = Less(hi.Adjustments, reference[m].Adjustments) };
            }).ToList();
            return new SensitivityTable(criterion.Name, nominal[m], reference[m].Criterion, reference[m].Change, compensator, rows)
            {
                NominalDeviation = reference[m].Deviation,
                NominalAdjustments = reference[m].Adjustments,
            };
        }).ToList();
    }

    /// <summary>The criterion of the lens with one tolerance applied, after the compensator if there is one.</summary>
    public static Compensated Evaluate(NominalLens lens, Tolerance? tolerance, double amount,
                                       ICriterion criterion, FocusCompensator? compensator) =>
        Evaluate(lens, tolerance == null ? Array.Empty<(Tolerance, double)>() : new[] { (tolerance, amount) },
                 criterion, compensator);

    /// <summary>The criterion of the lens with several tolerances applied together, after the compensator.</summary>
    public static Compensated Evaluate(NominalLens lens, IEnumerable<(Tolerance Tolerance, double Amount)> applied,
                                       ICriterion criterion, FocusCompensator? compensator) =>
        Evaluate(lens, applied, new[] { criterion }, compensator)[0];

    /// <summary>
    /// The criterion of the lens with several tolerances applied, after the compensator, and each
    /// further criterion measured on the same lens.
    /// </summary>
    public static (Compensated Result, double[] Also) Evaluate(NominalLens lens, IEnumerable<(Tolerance Tolerance, double Amount)> applied,
                                                               ICriterion criterion, IReadOnlyList<ICriterion> also,
                                                               FocusCompensator? compensator)
    {
        var all = Evaluate(lens, applied, also.Prepend(criterion).ToList(), compensator);
        return (all[0], all.Skip(1).Select(r => r.Criterion).ToArray());
    }

    /// <summary>
    /// Every criterion of the lens with several tolerances applied, after the compensator. The
    /// compensator refocuses once for all the criteria that refocus alike - by the same image
    /// quality, which for a departure is its <see cref="DeviationCriterion.Focus"/> - and each is
    /// measured on that lens; criteria that refocus differently get a lens each. Each result is the
    /// one its criterion would have alone, departure included. With <paramref name="wavefronts"/>,
    /// an RMS wavefront's result carries its <see cref="RmsWavefrontCriterion.Vector"/> as its
    /// departure, for cross terms from the slopes; an MTF's always carries its <see cref="MtfCriterion.Intercepts"/>, for its synthesis.
    /// </summary>
    public static Compensated[] Evaluate(NominalLens lens, IEnumerable<(Tolerance Tolerance, double Amount)> applied,
                                         IReadOnlyList<ICriterion> criteria, FocusCompensator? compensator, bool wavefronts = false)
    {
        // An MTF always carries its spot, for the synthesis its statistics are drawn from.
        Func<ICriterion, WavefrontErrorCalculator.Core.LensModel, double[]?> vector = (c, m) => c switch
        {
            RmsWavefrontCriterion r when wavefronts => r.Vector(m),
            MtfCriterion f => f.Intercepts(m),
            _ => null,
        };
        return Evaluate(lens, applied, criteria, compensator, vector);
    }

    private static Compensated[] Evaluate(NominalLens lens, IEnumerable<(Tolerance Tolerance, double Amount)> applied,
                                          IReadOnlyList<ICriterion> criteria, FocusCompensator? compensator,
                                          Func<ICriterion, WavefrontErrorCalculator.Core.LensModel, double[]?>? vector)
    {
        var errors = applied.ToList();
        var results = new Compensated[criteria.Count];
        foreach (var group in Enumerable.Range(0, criteria.Count).GroupBy(i => compensator == null ? null : FocusOf(criteria[i])))
        {
            var perturbed = lens.Perturb();
            foreach (var (tolerance, amount) in errors) tolerance.Apply(perturbed, amount);
            int first = group.First();
            Compensated? focused = compensator?.Solve(perturbed, criteria[first]);
            var model = perturbed.Build();
            foreach (int i in group)
            {
                results[i] = focused is { } f
                    ? (i == first ? f : Measure(criteria[i], model) with { Change = f.Change, AtLimit = f.AtLimit, Adjustments = f.Adjustments })
                    : Measure(criteria[i], model);
                if (vector?.Invoke(criteria[i], model) is { } v) results[i] = results[i] with { Deviation = v };
            }
        }
        return results;
    }

    private static IReadOnlyList<double>? Less(IReadOnlyList<double>? a, IReadOnlyList<double>? b) =>
        a == null ? null : a.Select((v, k) => v - (b == null ? 0.0 : b[k])).ToArray();

    /// <summary>What a compensator refocuses by for this criterion: itself, or the image quality of a departure or an MTF.</summary>
    public static ICriterion FocusOf(ICriterion criterion) => criterion is IFocusedCriterion f ? f.Focus : criterion;

    /// <summary>The criterion of a built lens, with the departure it is the length of for a <see cref="DeviationCriterion"/>.</summary>
    private static Compensated Measure(ICriterion criterion, WavefrontErrorCalculator.Core.LensModel model)
    {
        if (criterion is not DeviationCriterion deviation) return new Compensated(0.0, criterion.Evaluate(model));
        var d = deviation.Deviation(model);
        return new Compensated(0.0, DeviationCriterion.Length(d)) { Deviation = d };
    }
}
