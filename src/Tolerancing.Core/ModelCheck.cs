namespace Tolerancing.Core;

/// <summary>
/// The quadratic model against the lens itself, lens by lens: a few lenses drawn at random, each
/// traced in full and refocused, and its criterion set beside the model's for the same errors.
///
/// <para>Koch's statistics are only as good as the model, and a lens pushed far from its design by
/// its tolerances - a fisheye at commercial grade, vignetting that comes and goes - is not
/// quadratic in them. Comparing distributions needs hundreds of lenses to see that; comparing the
/// same lenses sees it in a dozen.</para>
/// </summary>
public sealed record ModelCheck(IReadOnlyList<(double Model, double Traced)> Lenses)
{
    /// <summary>
    /// The RMS relative error of the model a check passes with. The RMS, not the worst lens: errors
    /// of a few lenses partly cancel in the statistics - on the LensHH-LT Cooke triplet one lens in
    /// twelve was 15.6 % off, the RMS 8.3 %, and Koch agreed with 600 traced lenses to 1 % in mean W².
    /// </summary>
    public const double Tolerance = 0.15;

    /// <summary>
    /// The RMS error a check of a quoted criterion - an MTF, measured against the traced lenses' scatter
    /// rather than their value - passes with: a quarter of the scatter. On the Petzval at commercial
    /// grade the synthesis was 16.5 % off lens by lens and its 97.7 % floor 0.025 from 400 traced lenses.
    /// </summary>
    public const double ScatterTolerance = 0.25;

    /// <summary>The tolerance this check is held to: <see cref="Tolerance"/>, or <see cref="ScatterTolerance"/> for a quoted criterion.</summary>
    public double Limit => Quoted != null ? ScatterTolerance : Tolerance;

    /// <summary>
    /// Each lens's error as a part of the traced lenses' RMS rather than of its own value: for a
    /// departure such as the focal length's, which one lens can have almost none of, so that its
    /// own value would make any small error look large.
    /// </summary>
    public bool RelativeToSpread { get; init; }

    /// <summary>
    /// For a criterion quoted otherwise than as W - an MTF - how it is quoted: each lens's error is then
    /// of the quoted value, as a part of the traced lenses' scatter about their mean, so that a model
    /// that misses the spread is seen however far the MTF sits from 1.
    /// </summary>
    public Func<double, double>? Quoted { get; init; }

    private IEnumerable<double> Errors
    {
        get
        {
            if (Quoted != null)
            {
                var quoted = Lenses.Where(l => double.IsFinite(l.Traced)).Select(l => (Model: Quoted(l.Model), Traced: Quoted(l.Traced))).ToList();
                double mean = quoted.Select(l => l.Traced).DefaultIfEmpty(double.NaN).Average();
                double scatter = Math.Sqrt(quoted.Select(l => (l.Traced - mean) * (l.Traced - mean)).DefaultIfEmpty(double.NaN).Average());
                return quoted.Select(l => (l.Model - l.Traced) / scatter);
            }
            var traced = Lenses.Where(l => double.IsFinite(l.Traced) && l.Traced > 0.0).ToList();
            double spread = Math.Sqrt(traced.Select(l => l.Traced * l.Traced).DefaultIfEmpty(double.NaN).Average());
            return traced.Select(l => (l.Model - l.Traced) / (RelativeToSpread ? spread : l.Traced));
        }
    }

    /// <summary>The RMS of the model's relative errors.</summary>
    public double RmsError => Math.Sqrt(Errors.Select(e => e * e).DefaultIfEmpty(double.NaN).Average());

    /// <summary>The largest of the model's relative errors.</summary>
    public double MaxError => Errors.Select(Math.Abs).DefaultIfEmpty(double.NaN).Max();

    /// <summary>Lenses that could not be traced at all.</summary>
    public int Untraceable => Lenses.Count(l => !double.IsFinite(l.Traced));

    /// <summary>Whether the model's RMS error is within <see cref="Limit"/> and every lens could be traced.</summary>
    public bool Passes => Untraceable == 0 && RmsError <= Limit;

    public static ModelCheck Run(NominalLens lens, QuadraticModel model, StatisticsModel statistics,
                                 ICriterion criterion, FocusCompensator? compensator,
                                 int lenses = 12, int seed = 7919) =>
        Run(lens, new[] { model }, statistics, new[] { criterion }, compensator, lenses, seed)[0];

    /// <summary>
    /// The check of several criteria's models, all of one tolerance set, on the same lenses: each
    /// traced and refocused once, every criterion measured on it.
    /// </summary>
    public static IReadOnlyList<ModelCheck> Run(NominalLens lens, IReadOnlyList<QuadraticModel> models, StatisticsModel statistics,
                                                IReadOnlyList<ICriterion> criteria, FocusCompensator? compensator,
                                                int lenses = 12, int seed = 7919)
    {
        if (models.Count != criteria.Count || models.Count == 0) throw new ArgumentException("one model per criterion");
        var items = models[0].Terms.Select(t => t.Item).ToList();
        var free = compensator?.Free();
        var results = criteria.Select(_ => new (double, double)[lenses]).ToArray();
        Parallel.For(0, lenses, k =>
        {
            var x = MonteCarlo.Errors(items, statistics, seed, k);
            var traced = Sensitivity.Evaluate(lens, items.Select((item, i) => (item.Tolerance, x[i])), criteria, free);
            for (int m = 0; m < criteria.Count; m++)
                results[m][k] = (Math.Sqrt(Math.Max(0.0, models[m].Evaluate(x))), traced[m].Criterion);
        });
        return criteria.Select((c, m) => new ModelCheck(results[m]) { RelativeToSpread = c is DeviationCriterion, Quoted = c.IsFloor ? c.Quoted : null }).ToList();
    }
}
