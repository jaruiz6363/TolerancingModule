namespace Tolerancing.Core;

/// <summary>
/// The criterion of each lens of a Monte Carlo run, and the compensator's move for it from where it
/// refocuses the nominal lens. A lens that could not be traced scores +∞; it fails every limit and is
/// left out of the moments.
/// </summary>
public sealed record MonteCarloResult(IReadOnlyList<double> Criteria, IReadOnlyList<double> Compensators)
{
    private double[] Finite => Criteria.Where(double.IsFinite).ToArray();

    public int Trials => Criteria.Count;
    public int Untraceable => Criteria.Count(c => !double.IsFinite(c));

    /// <summary>The mean of Q = W² over the lenses traced, to set against Koch's.</summary>
    public double MeanQ => Finite.Select(c => c * Math.Abs(c)).DefaultIfEmpty(double.NaN).Average();

    public double VarianceQ
    {
        get
        {
            var q = Finite.Select(c => c * Math.Abs(c)).ToArray();
            if (q.Length < 2) return double.NaN;
            double m = q.Average();
            return q.Sum(v => (v - m) * (v - m)) / (q.Length - 1);
        }
    }

    /// <summary>The third central moment of Q over the lenses traced.</summary>
    public double ThirdQ
    {
        get
        {
            var q = Finite.Select(c => c * Math.Abs(c)).ToArray();
            if (q.Length < 3) return double.NaN;
            double m = q.Average();
            return q.Sum(v => (v - m) * (v - m) * (v - m)) / q.Length;
        }
    }

    public double Mean => Finite.DefaultIfEmpty(double.NaN).Average();

    /// <summary>The fraction of all lenses, untraceable ones included, at most <paramref name="limit"/>.</summary>
    public double Yield(double limit) => Criteria.Count(c => c <= limit) / (double)Criteria.Count;

    /// <summary>The criterion that a fraction <paramref name="p"/> of the lenses are at or better than.</summary>
    public double Percentile(double p)
    {
        var sorted = Criteria.OrderBy(c => c).ToArray();
        double at = p * (sorted.Length - 1);
        int lo = (int)Math.Floor(at), hi = Math.Min(lo + 1, sorted.Length - 1);
        return double.IsInfinity(sorted[hi]) ? sorted[hi] : sorted[lo] + (at - lo) * (sorted[hi] - sorted[lo]);
    }

    /// <summary>Each further criterion of each lens, on the lens as the compensator left it for the first; empty without any.</summary>
    public IReadOnlyList<IReadOnlyList<double>> Also { get; init; } = Array.Empty<IReadOnlyList<double>>();

    /// <summary>
    /// The fraction of all lenses within every limit at once: the first criterion within
    /// <paramref name="limit"/>, each further one within its own.
    /// </summary>
    public double JointYield(double limit, IReadOnlyList<double> alsoLimits)
    {
        if (alsoLimits.Count != Also.Count) throw new ArgumentException("one limit per further criterion");
        int met = 0;
        for (int k = 0; k < Criteria.Count; k++)
            if (Criteria[k] <= limit && Also.Select((a, c) => a[k] <= alsoLimits[c]).All(b => b)) met++;
        return met / (double)Criteria.Count;
    }

    /// <summary>The lenses whose compensator ran to the end of its travel.</summary>
    public int CompensatorAtLimit { get; init; }

    /// <summary>The range of the compensator's moves over the lenses traced: the travel it needs.</summary>
    public (double Min, double Max) CompensatorRange
    {
        get
        {
            var moves = Compensators.Where(double.IsFinite).ToArray();
            return moves.Length == 0 ? (0.0, 0.0) : (moves.Min(), moves.Max());
        }
    }

    /// <summary>Each lens's setting of each of the compensator's other adjustments, NaN for a lens that cannot be traced; empty when it has none.</summary>
    public IReadOnlyList<double[]> Adjustments { get; init; } = Array.Empty<double[]>();

    /// <summary>The range of adjustment <paramref name="k"/> over the lenses traced, from where it sets the nominal lens.</summary>
    public (double Min, double Max) AdjustmentRange(int k)
    {
        var moves = Adjustments.Select(a => a[k]).Where(double.IsFinite).ToArray();
        return moves.Length == 0 ? (0.0, 0.0) : (moves.Min(), moves.Max());
    }
}

/// <summary>
/// Lenses built at random to a tolerance set, every error drawn at once and the compensator set on
/// each: the check on Koch's prediction, and on everything it assumes - a quadratic criterion, no
/// cross terms beyond those evaluated, the spread's shape. A lens that cannot be traced, or whose
/// errors leave a surface with no shape (a sag no curvature gives), scores +∞ and has no move.
///
/// <para>Each trial has a generator seeded from the run's seed and its own number, so a run is the
/// same however its trials are spread over threads.</para>
/// </summary>
public static class MonteCarlo
{
    /// <summary>Lenses traced in full - WavefrontErrorCalculator on each.</summary>
    public static MonteCarloResult Run(NominalLens lens, IReadOnlyList<ToleranceItem> items, StatisticsModel statistics,
                                       ICriterion criterion, FocusCompensator? compensator,
                                       int trials, int seed = 1, IReadOnlyList<ICriterion>? also = null)
    {
        ArgumentNullException.ThrowIfNull(lens);
        var groups = Koch.Groups(items, statistics);
        var nominal = compensator == null ? (Compensated?)null
            : Sensitivity.Evaluate(lens, null, 0.0, criterion, compensator.Free());
        double reference = nominal?.Change ?? 0.0;
        int adjusted = compensator?.Adjustments.Count ?? 0;
        var adjustments = Enumerable.Range(0, trials).Select(_ => new double[adjusted]).ToArray();
        also ??= Array.Empty<ICriterion>();
        var others = also.Select(_ => new double[trials]).ToArray();
        var criteria = new double[trials];
        var moves = new double[trials];
        var limited = new bool[trials];
        Parallel.For(0, trials, k =>
        {
            var x = Errors(groups, items, statistics, seed, k);
            Compensated r;
            double[] more;
            try { (r, more) = Sensitivity.Evaluate(lens, items.Select((item, i) => (item.Tolerance, x[i])), criterion, also, compensator); }
            catch (Exception e) when (e is InvalidOperationException or ArithmeticException)
            {
                r = new Compensated(double.NaN, double.PositiveInfinity);
                more = also.Select(_ => double.PositiveInfinity).ToArray();
            }
            for (int c = 0; c < more.Length; c++) others[c][k] = double.IsNaN(more[c]) ? double.PositiveInfinity : more[c];
            criteria[k] = r.Criterion;
            moves[k] = double.IsFinite(r.Criterion) ? r.Change - reference : double.NaN;
            limited[k] = r.AtLimit;
            for (int a = 0; a < adjusted; a++)
                adjustments[k][a] = double.IsFinite(r.Criterion) && r.Adjustments is { } ra ? ra[a] - (nominal?.Adjustments?[a] ?? 0.0) : double.NaN;
        });
        return new MonteCarloResult(criteria, compensator == null ? Array.Empty<double>() : moves)
        {
            CompensatorAtLimit = limited.Count(b => b),
            Also = others,
            Adjustments = adjusted == 0 ? Array.Empty<double[]>() : adjustments,
        };
    }

    /// <summary>
    /// Lenses evaluated on the quadratic model alone. It tests the moment algebra, not the model:
    /// on enough trials it must agree with <see cref="Koch.Predict(QuadraticModel, StatisticsModel)"/> exactly.
    /// </summary>
    public static MonteCarloResult RunModel(QuadraticModel model, StatisticsModel statistics, int trials, int seed = 1)
    {
        var items = model.Terms.Select(t => t.Item).ToList();
        var groups = Koch.Groups(items, statistics);
        var criteria = new double[trials];
        Parallel.For(0, trials, k =>
        {
            var q = model.Evaluate(Koch.Draw(groups, items, statistics, TrialRandom(seed, k)));
            criteria[k] = Math.Sign(q) * Math.Sqrt(Math.Abs(q));     // keeps Q's sign, so W² gives back Q
        });
        return new MonteCarloResult(criteria, Array.Empty<double>());
    }

    /// <summary>The errors of trial <paramref name="trial"/> of a run with <paramref name="seed"/>: the same lens <see cref="Run"/> builds.</summary>
    public static double[] Errors(IReadOnlyList<ToleranceItem> items, StatisticsModel statistics, int seed, int trial) =>
        Errors(Koch.Groups(items, statistics), items, statistics, seed, trial);

    private static double[] Errors(IReadOnlyList<int[]> groups, IReadOnlyList<ToleranceItem> items, StatisticsModel statistics,
                                   int seed, int trial) =>
        Koch.Draw(groups, items, statistics, TrialRandom(seed, trial));

    private static Random TrialRandom(int seed, int trial) => SplitMix.ForTrial(seed, trial);
}
