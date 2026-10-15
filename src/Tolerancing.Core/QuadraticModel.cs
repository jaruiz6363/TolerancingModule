namespace Tolerancing.Core;

/// <summary>
/// One tolerance's effect on the square of the criterion, Q = W²: Q ≈ Q₀ + A x² + B x for an
/// error x in the tolerance's own units, fitted through the sensitivity run's two ends and the
/// compensated nominal (Koch 1978 eq. 17).
/// </summary>
public sealed record QuadraticTerm(ToleranceItem Item, double A, double B)
{
    /// <summary>Q's change for an error of <paramref name="x"/>.</summary>
    public double Change(double x) => (A * x + B) * x;

    /// <summary>The larger increase of Q at ±t, or zero if neither is an increase.</summary>
    public double WorseIncrease(double t) => Math.Max(0.0, Math.Max(Change(t), Change(-t)));
}

/// <summary>
/// The criterion's square as a quadratic in every error at once,
/// Q = Q₀ + Σ (A_i x_i² + B_i x_i) + Σ_{i&lt;j} c_ij x_i x_j - the model Koch's moments are taken of.
///
/// <para><b>Why the square.</b> An RMS wavefront error is the root of a mean square, and a mean
/// square is quadratic in small changes of the lens - the wavefront is linear in them - with the
/// compensator's best setting included, since the minimum over focus of a quadratic is again
/// quadratic. W itself is not: near a well-corrected design it goes as √(Q₀ + A x²), which no
/// parabola in x follows. So the fit is to W² and statistics are of W², then read back as W.</para>
///
/// <para><b>Cross terms.</b> Two errors that make the same aberration - two wedges that both make
/// coma - add their wavefronts before squaring, which c_ij carries. It costs one more evaluation per
/// pair. They add only to the variance (Koch eq. 21); <see cref="CrossTerms.Pairs"/> takes them
/// for the x and y of one decentre or tilt, <see cref="CrossTerms.All"/> for every pair, and
/// <see cref="CrossTerms.Wavefront"/>, the default, has an RMS wavefront's from its pupil slopes untraced.</para>
/// </summary>
public sealed record QuadraticModel(string Criterion, double Q0, IReadOnlyList<QuadraticTerm> Terms,
                                    IReadOnlyDictionary<(int, int), double> Cross)
{
    /// <summary>
    /// The compensator's move from its nominal setting, as a quadratic in the same errors (Q₀ = 0),
    /// when there is a compensator: Koch's statistics of it give the travel the compensator needs.
    /// </summary>
    public QuadraticModel? Compensator { get; init; }

    /// <summary>
    /// Each of the compensator's other adjustments' move, as a quadratic in the same errors (Q₀ = 0),
    /// named by the adjustment: for the travel each needs, as <see cref="Compensator"/> is for the focus.
    /// </summary>
    public IReadOnlyList<QuadraticModel> Adjustments { get; init; } = Array.Empty<QuadraticModel>();

    /// <summary>The largest set <see cref="CrossTerms.Auto"/> evaluates every pair of: 80 tolerances, 3160 pairs.</summary>
    public const int AutoAllLimit = 80;

    /// <summary>The compensated nominal criterion, √Q₀.</summary>
    public double Nominal => Math.Sqrt(Q0);

    /// <summary>
    /// For an MTF, the spot synthesis that <see cref="Evaluate"/> draws on instead of the quadratic,
    /// which an MTF's saturation defeats; the quadratic terms then only steer an allocation and
    /// share the budget out.
    /// </summary>
    public SpotSynthesis? Synthesis { get; init; }

    /// <summary>
    /// How near nominal a pair is traced for its cross term: at both maxima, and if the lens cannot
    /// be traced there, at half and then a quarter of them. The cross term is Q of the pair less Q₀
    /// and each term's own change at the same errors, which at the maxima is the plain difference.
    /// </summary>
    public static readonly double[] PairFractions = { 1.0, 0.5, 0.25 };

    /// <summary>Pairs (indices into <see cref="Terms"/>) traced nearer nominal than their maxima, which left the lens untraceable.</summary>
    public IReadOnlyList<(int, int)> PartlyTraced { get; init; } = Array.Empty<(int, int)>();

    /// <summary>Pairs the lens could not be traced with at any of <see cref="PairFractions"/>: their cross terms are left out, as zero.</summary>
    public IReadOnlyList<(int, int)> Untraced { get; init; } = Array.Empty<(int, int)>();

    /// <summary>The model's Q for one error of each term - by the synthesis if there is one - with a missing cross term counted as zero.</summary>
    public double Evaluate(IReadOnlyList<double> x)
    {
        if (Synthesis != null) return Synthesis.Q(x);
        double q = Q0;
        for (int i = 0; i < Terms.Count; i++) q += Terms[i].Change(x[i]);
        foreach (var ((i, j), c) in Cross) q += c * x[i] * x[j];
        return q;
    }

    /// <summary>
    /// The pairs of terms that are the x and y of one decentre or tilt, x first: the same kind of
    /// tolerance on the same surfaces, one about each axis.
    /// </summary>
    public static List<(int X, int Y)> AxisPairs(IReadOnlyList<ToleranceItem> items)
    {
        var pairs = new List<(int, int)>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Tolerance is not IAxisTolerance { Axis: Axis.X } x) continue;
            var partner = x.WithAxis(Axis.Y);
            for (int j = 0; j < items.Count; j++)
                if (items[j].Tolerance.Equals(partner)) { pairs.Add((i, j)); break; }
        }
        return pairs;
    }

    /// <summary>
    /// The model of a tolerance set: a sensitivity run at ± the larger end of each range - so a range
    /// all on one side of nominal, a bias, is still fitted on both sides - then the fit. The
    /// compensator is used without a travel limit, which would bend the quadratic; its travel is
    /// predicted from the compensator model instead.
    /// </summary>
    public static QuadraticModel Build(NominalLens lens, IReadOnlyList<ToleranceItem> items, ICriterion criterion,
                                       FocusCompensator? compensator, CrossTerms cross = CrossTerms.Wavefront) =>
        Build(lens, items, new[] { criterion }, compensator, cross)[0];

    /// <summary>
    /// One model per criterion of the same tolerance set, from one sensitivity run and one trace of
    /// each pair for them all: each lens is perturbed and refocused once, and every criterion
    /// measured on it. Each model is the one its criterion would have alone.
    /// </summary>
    public static IReadOnlyList<QuadraticModel> Build(NominalLens lens, IReadOnlyList<ToleranceItem> items, IReadOnlyList<ICriterion> criteria,
                                                      FocusCompensator? compensator, CrossTerms cross = CrossTerms.Wavefront)
    {
        var free = compensator?.Free();
        var probes = items.Select(i => ToleranceItem.Symmetric(i.Tolerance, Math.Max(Math.Abs(i.Min), Math.Abs(i.Max)))).ToList();
        var tables = Sensitivity.Run(lens, probes, criteria, free, wavefronts: cross == CrossTerms.Wavefront);
        QuadraticTerm[] Actual(IReadOnlyList<QuadraticTerm> terms) => terms.Select((t, i) => t with { Item = items[i] }).ToArray();
        return Fit(lens, tables, criteria, free, cross).Select(model => model with
        {
            Terms = Actual(model.Terms),
            Compensator = model.Compensator == null ? null : model.Compensator with { Terms = Actual(model.Compensator.Terms) },
            Adjustments = model.Adjustments.Select(a => a with { Terms = Actual(a.Terms) }).ToList(),
        }).ToList();
    }

    /// <summary>
    /// Fits the model to a sensitivity run - a parabola through each tolerance's two ends and the
    /// compensated nominal - and evaluates the lens with two errors at once, both at their maximum,
    /// for each cross term asked for. With a compensator, its move is fitted the same way, as
    /// <see cref="Compensator"/>.
    /// </summary>
    public static QuadraticModel Fit(NominalLens lens, SensitivityTable table, ICriterion criterion,
                                     FocusCompensator? compensator, CrossTerms cross = CrossTerms.Wavefront) =>
        Fit(lens, new[] { table }, new[] { criterion }, compensator, cross)[0];

    /// <summary>
    /// The models of several criteria, each fitted to its own sensitivity table, all of one tolerance
    /// set. A departure's cross terms come from its slopes (<see cref="DeviationCross"/>); the other
    /// criteria share one trace of each pair. Under <see cref="CrossTerms.Wavefront"/>, a table run
    /// without vectors has every pair traced.
    /// </summary>
    public static IReadOnlyList<QuadraticModel> Fit(NominalLens lens, IReadOnlyList<SensitivityTable> tables, IReadOnlyList<ICriterion> criteria,
                                                    FocusCompensator? compensator, CrossTerms cross = CrossTerms.Wavefront)
    {
        if (tables.Count != criteria.Count || tables.Count == 0) throw new ArgumentException("one sensitivity table per criterion");
        var q0 = tables.Select(t => t.NominalCompensated * t.NominalCompensated).ToArray();
        if (!q0.All(double.IsFinite)) throw new InvalidOperationException("the nominal lens cannot be traced, so there is nothing to fit");
        var rows = tables[0].Rows;
        int count = rows.Count;

        // The pairs whose cross terms are wanted, for the criteria that are not departures; of them,
        // those an RMS wavefront has from its slopes (CrossTerms.Wavefront) need no trace.
        var traced = Enumerable.Range(0, criteria.Count).Where(m => criteria[m] is not DeviationCriterion || cross == CrossTerms.None).ToList();
        var effective = cross switch
        {
            CrossTerms.Auto => count <= AutoAllLimit ? CrossTerms.All : CrossTerms.Pairs,
            CrossTerms.Wavefront => CrossTerms.All,
            _ => cross,
        };
        var pairs = new List<(int, int)>();
        if (traced.Count > 0 && effective == CrossTerms.Pairs)
            pairs.AddRange(AxisPairs(rows.Select(r => r.Item).ToList()).Select(p => (Math.Min(p.X, p.Y), Math.Max(p.X, p.Y))));
        else if (traced.Count > 0 && effective == CrossTerms.All)
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    pairs.Add((i, j));
        var slopes = criteria.Select((c, m) => cross == CrossTerms.Wavefront && c is RmsWavefrontCriterion ? WavefrontSlopes(tables[m]) : null).ToArray();
        // An MTF is drawn from its spot synthesis whatever cross terms are asked for; under CrossTerms.Wavefront its
        // quadratic cross terms come from the synthesis too, untraced.
        var synth = criteria.Select((c, m) => c is MtfCriterion f
            ? SpotSynthesis.From(tables[m], f.InterceptWeights(lens.Model), f.Frequency)
            : null).ToArray();
        bool Derived(int m, (int I, int J) pair) => slopes[m] is { } s && s[pair.I] != null && s[pair.J] != null
                                                     || cross == CrossTerms.Wavefront && synth[m] != null;
        var wanted = pairs.Where(pair => traced.Any(m => !Derived(m, pair))).ToList();
        var at = wanted.Select((pair, k) => (pair, k)).ToDictionary(x => x.pair, x => x.k);

        // Each pair at both maxima; a pair that leaves the lens untraceable there is tried nearer
        // nominal (PairFractions), and left out, its cross term zero, if it cannot be traced at all.
        var pairCriteria = traced.Select(m => criteria[m]).ToList();
        var both = new Compensated[wanted.Count][];
        var fraction = new double[wanted.Count];
        Parallel.For(0, wanted.Count, k =>
        {
            var (i, j) = wanted[k];
            foreach (double s in PairFractions)
            {
                Compensated[]? r = null;
                try
                {
                    r = Sensitivity.Evaluate(lens, new[] { (rows[i].Item.Tolerance, s * rows[i].Item.Max), (rows[j].Item.Tolerance, s * rows[j].Item.Max) },
                                             pairCriteria, compensator);
                }
                catch (Exception e) when (e is InvalidOperationException or ArithmeticException) { }
                if (r != null && r.All(c => double.IsFinite(c.Criterion)))
                {
                    (both[k], fraction[k]) = (r, s);
                    return;
                }
            }
        });
        var partly = wanted.Where((_, k) => fraction[k] is > 0.0 and < 1.0).ToList();
        var untraced = wanted.Where((_, k) => fraction[k] == 0.0).ToList();

        return criteria.Select((criterion, m) =>
        {
            var table = tables[m];
            var terms = table.Rows.Select(r => FitTerm(r, q0[m])).ToList();
            var moves = table.Rows.Select(r => FitParabola(r.Item, r.MinCompensator, 0.0, r.MaxCompensator)).ToList();
            int adjusted = compensator?.Adjustments.Count ?? 0;
            var adjustmentMoves = Enumerable.Range(0, adjusted).Select(k => table.Rows.Select(r =>
                FitParabola(r.Item, r.MinAdjustments?[k] ?? 0.0, 0.0, r.MaxAdjustments?[k] ?? 0.0)).ToList()).ToList();
            var adjustmentCross = Enumerable.Range(0, adjusted).Select(_ => new Dictionary<(int, int), double>()).ToList();
            var crossTerms = new Dictionary<(int, int), double>();
            var crossMoves = new Dictionary<(int, int), double>();
            int p = traced.IndexOf(m);
            if (p < 0)
                crossTerms = DeviationCross(table);
            else
                foreach (var (i, j) in pairs)
                {
                    if (Derived(m, (i, j)))
                    {
                        if (synth[m] is { } spot)
                        {
                            double ti = table.Rows[i].Item.Max, tj = table.Rows[j].Item.Max;
                            var x = new double[count];
                            x[i] = ti;
                            double qi = spot.Q(x);
                            x[j] = tj;
                            double qij = spot.Q(x);
                            x[i] = 0.0;
                            double qj = spot.Q(x);
                            double cs = (qij - qi - qj + spot.Q(new double[count])) / (ti * tj);
                            if (cs != 0.0 && double.IsFinite(cs)) crossTerms[(i, j)] = cs;
                            continue;
                        }
                        double c = 0.0;
                        var (gi, gj) = (slopes[m]![i]!, slopes[m]![j]!);
                        for (int k = 0; k < gi.Length; k++) c += gi[k] * gj[k];
                        if (c != 0.0) crossTerms[(i, j)] = 2.0 * c;
                        continue;
                    }
                    // Q(both) − Q₀ less each term's own change at the same errors: at the maxima the
                    // terms' changes are the table's, so this is the plain difference there.
                    int t = at[(i, j)];
                    if (both[t] == null) continue;
                    var r = both[t][p];
                    double xi = fraction[t] * table.Rows[i].Item.Max, xj = fraction[t] * table.Rows[j].Item.Max;
                    crossTerms[(i, j)] = (r.Criterion * r.Criterion - q0[m] - terms[i].Change(xi) - terms[j].Change(xj)) / (xi * xj);
                    crossMoves[(i, j)] = (r.Change - table.NominalCompensator - moves[i].Change(xi) - moves[j].Change(xj)) / (xi * xj);
                    for (int k = 0; k < adjusted; k++)
                        if (r.Adjustments is { } ra)
                            adjustmentCross[k][(i, j)] = (ra[k] - (table.NominalAdjustments?[k] ?? 0.0)
                                                          - adjustmentMoves[k][i].Change(xi) - adjustmentMoves[k][j].Change(xj)) / (xi * xj);
                }
            return new QuadraticModel(table.Criterion, q0[m], terms, crossTerms)
            {
                Synthesis = synth[m],
                Compensator = compensator == null ? null : new QuadraticModel("compensator move", 0.0, moves, crossMoves),
                Adjustments = Enumerable.Range(0, adjusted)
                    .Select(k => new QuadraticModel(compensator!.Adjustments[k].Label, 0.0, adjustmentMoves[k], adjustmentCross[k])).ToList(),
                PartlyTraced = p < 0 ? Array.Empty<(int, int)>() : partly.Where(pair => !Derived(m, pair)).ToList(),
                Untraced = p < 0 ? Array.Empty<(int, int)>() : untraced.Where(pair => !Derived(m, pair)).ToList(),
            };
        }).ToList();
    }

    /// <summary>
    /// The largest share of the nominal lens's arriving pupil samples whose vignetting a tolerance
    /// may change at its ends and still have its cross terms from its slopes.
    /// </summary>
    public const double MaxVignettingChange = 0.10;

    /// <summary>
    /// Each tolerance's slope of an RMS wavefront's <see cref="RmsWavefrontCriterion.Vector"/>
    /// between its two ends, or null where it cannot be used: a vector missing, or more than
    /// <paramref name="maxChanged"/> of the nominal lens's arriving samples vignetted differently at
    /// an end. A sample vignetted at either end or nominally has no slope. Samples that come and go
    /// make the rest of the vector a little off too, since each is weighted by the arriving total;
    /// that is the price of keeping the slopes of a lens whose rim rays clip.
    /// </summary>
    public static double[]?[] WavefrontSlopes(SensitivityTable table, double maxChanged = MaxVignettingChange)
    {
        var nominal = table.NominalDeviation;
        int arriving = nominal?.Count(v => !double.IsNaN(v)) ?? 0;
        return table.Rows.Select(r =>
        {
            if (nominal == null || arriving == 0 || r.MinDeviation is not { } lo || r.MaxDeviation is not { } hi
                || lo.Count != nominal.Count || hi.Count != nominal.Count) return null;
            var g = new double[nominal.Count];
            int changed = 0;
            for (int k = 0; k < g.Length; k++)
            {
                bool gone = double.IsNaN(nominal[k]);
                if (double.IsNaN(lo[k]) != gone || double.IsNaN(hi[k]) != gone) changed++;
                g[k] = gone || double.IsNaN(lo[k]) || double.IsNaN(hi[k]) ? 0.0 : (hi[k] - lo[k]) / (r.Item.Max - r.Item.Min);
            }
            return changed <= maxChanged * arriving ? g : null;
        }).ToArray();
    }

    /// <summary>
    /// Every cross term of a departure's square, |d|², from the sensitivity run alone: d is linear
    /// in small errors, d ≈ Σ g_i x_i, so c_ij = 2 g_i·g_j, with g_i the slope of d between the two
    /// ends of tolerance i. Exact to second order when the reference departure is zero, as it is for
    /// the refocused nominal; no pair need be traced, so it is every pair however many there are.
    /// A pair whose slopes are orthogonal - an x tilt and a y tilt of a boresight - has none.
    /// </summary>
    public static Dictionary<(int, int), double> DeviationCross(SensitivityTable table)
    {
        var slopes = table.Rows.Select(r =>
        {
            if (r.MinDeviation == null || r.MaxDeviation == null)
                throw new ArgumentException($"{r.Item}: the sensitivity run has no departures to take slopes of");
            return r.MinDeviation.Select((lo, k) => (r.MaxDeviation[k] - lo) / (r.Item.Max - r.Item.Min)).ToArray();
        }).ToArray();
        var cross = new Dictionary<(int, int), double>();
        for (int i = 0; i < slopes.Length; i++)
            for (int j = i + 1; j < slopes.Length; j++)
            {
                double c = 2.0 * slopes[i].Select((g, k) => g * slopes[j][k]).Sum();
                if (c != 0.0) cross[(i, j)] = c;
            }
        return cross;
    }

    /// <summary>
    /// The parabola Q₀ + A x² + B x through (min, Q_min), (0, Q₀) and (max, Q_max). The ends need not
    /// be symmetric, but they must lie on either side of nominal.
    /// </summary>
    public static QuadraticTerm FitTerm(SensitivityRow row, double q0)
    {
        double qlo = row.MinCriterion * row.MinCriterion, qhi = row.MaxCriterion * row.MaxCriterion;
        if (!double.IsFinite(qlo) || !double.IsFinite(qhi))
            throw new InvalidOperationException($"{row.Item}: the lens cannot be traced at an end of its range");
        return FitParabola(row.Item, qlo, q0, qhi);
    }

    /// <summary>The parabola v₀ + A x² + B x through (min, v_min), (0, v₀) and (max, v_max).</summary>
    public static QuadraticTerm FitParabola(ToleranceItem item, double atMin, double atNominal, double atMax)
    {
        double lo = item.Min, hi = item.Max;
        if (!(lo < 0.0 && hi > 0.0))
            throw new ArgumentException($"{item}: a quadratic needs a range on both sides of nominal");
        double slopeHi = (atMax - atNominal) / hi, slopeLo = (atMin - atNominal) / lo;
        double a = (slopeHi - slopeLo) / (hi - lo);
        return new QuadraticTerm(item, a, slopeHi - a * hi);
    }
}

/// <summary>Which cross terms a <see cref="QuadraticModel"/> evaluates.</summary>
public enum CrossTerms
{
    None,

    /// <summary>
    /// <see cref="All"/> for a set of up to <see cref="QuadraticModel.AutoAllLimit"/> tolerances, <see cref="Pairs"/>
    /// beyond. Pairs alone miss errors that cancel or add across elements - in a Cassegrain the
    /// cross terms between the mirrors' tilts and decentres are 78 % of the variance, and the 97.7 %
    /// level with pairs alone was 1.10 waves where 1000 traced lenses gave 1.46 (all: 1.45).
    /// </summary>
    Auto,

    /// <summary>The x and y of each decentre and tilt: one evaluation per pair.</summary>
    Pairs,

    /// <summary>Every pair of tolerances: n(n − 1)/2 evaluations.</summary>
    All,

    /// <summary>
    /// The default. Every pair, an RMS wavefront's from the sensitivity run without tracing pairs.
    /// W² is the length squared of the vector of weighted pupil samples (<see cref="RmsWavefrontCriterion.Vector"/>),
    /// so if each error changes the refocused wavefront linearly, the cross term is 2 g_i·g_j with
    /// g_i the slope of the vector between tolerance i's ends. It leaves out the wavefront's own
    /// curvature against the nominal lens's residual: the 97.7 % level moved by 0.3 % at most on
    /// the vignetted triplet, the fisheye and a Cassegrain. A tolerance whose ends change which
    /// samples clip by more than <see cref="QuadraticModel.MaxVignettingChange"/> of them has its
    /// pairs traced. On 17 lenses its model check was better than <see cref="Auto"/>'s on 14 - the
    /// fisheye's 45 % became 20 % - the same on two and 0.1 point worse on one, and the triplet's
    /// 861 traced pairs became none.
    /// </summary>
    Wavefront,
}
