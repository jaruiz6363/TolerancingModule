namespace Tolerancing.Core;

/// <summary>What one tolerance - or one x-y pair drawn as one error - adds to the mean and the variance of Q.</summary>
public sealed record Contribution(IReadOnlyList<int> Terms, string Label, double Mean, double Variance);

/// <summary>
/// Koch's prediction of the spread of Q = W² over many lenses built to a tolerance set, and what it
/// says of W: Q's mean, variance and third moment, and a shape for the rest.
///
/// <para>Koch takes Q as normal, from the central limit theorem; but Q is a sum of squares and a few
/// dominant errors shape it. Four shapes, checked on the test triplet against thousands of lenses
/// traced (2026-10-12), the 97.7 % lens in waves:</para>
/// <list type="bullet">
/// <item>default set, uniform errors: traced 3.09; normal 2.82, gamma 3.05, Pearson III 3.04, sampled 3.06;</item>
/// <item>biases, end-point decentres: traced 1.641; normal 1.70, gamma 1.74, Pearson III 1.71, sampled 1.642.</item>
/// </list>
/// <para>The moments are right in both (skewness 0.12 against 0.12 in the second), but no shape
/// fitted to them follows a spread as flat as end-point errors give. The sampled shape draws Q on
/// the quadratic model itself - no assumption beyond the model - and is the default.</para>
/// </summary>
public sealed record Prediction(double Q0, double MeanQ, double VarianceQ, double CrossVariance,
                                IReadOnlyList<Contribution> Contributions)
{
    public SpreadShape Shape { get; init; } = SpreadShape.Sampled;

    /// <summary>How far the biases alone move Q, Q(centres) − Q₀: part of the mean, and not random.</summary>
    public double BiasShift { get; init; }

    public double SigmaQ => Math.Sqrt(Math.Max(0.0, VarianceQ));

    /// <summary>The criterion at Q's mean plus <paramref name="sigmas"/> of its standard deviations.</summary>
    public double Level(double sigmas) => Math.Sqrt(Math.Max(0.0, MeanQ + sigmas * SigmaQ));

    /// <summary>√(mean Q): the RMS over the lenses built of the criterion, which is itself an RMS.</summary>
    public double RootMeanSquare => Level(0.0);

    /// <summary>The value of Q that a fraction <paramref name="p"/> of lenses are at or below, in <see cref="Shape"/>.</summary>
    public double QuantileQ(double p)
    {
        if (SigmaQ == 0.0) return MeanQ;
        switch (Shape)
        {
            case SpreadShape.Gamma when MeanQ > 0.0:
                double k = MeanQ * MeanQ / VarianceQ, theta = VarianceQ / MeanQ;
                return theta * Special.GammaQuantile(k, p);
            case SpreadShape.Sampled when Sample != null:
                return Percentile(Sample.Value, p);
            case SpreadShape.Pearson when Math.Abs(Skewness) > 1e-3:
                // A gamma of shape 4/γ², scale σγ/2, shifted to Q's mean; mirrored for negative skew.
                double g = Math.Abs(Skewness), shape = 4.0 / (g * g), scale = SigmaQ * g / 2.0;
                return Skewness > 0.0
                    ? MeanQ - shape * scale + scale * Special.GammaQuantile(shape, p)
                    : MeanQ + shape * scale - scale * Special.GammaQuantile(shape, 1.0 - p);
            default:
                return MeanQ + Special.NormalQuantile(p) * SigmaQ;
        }
    }

    /// <summary>The criterion that a fraction <paramref name="p"/> of lenses are at or better than.</summary>
    public double Quantile(double p) => Math.Sqrt(Math.Max(0.0, QuantileQ(p)));

    /// <summary>The criterion 97.7 % of lenses are at or better than: Hilbert 1979's probable worst case.</summary>
    public double ProbableWorst => Quantile(Yield97);

    /// <summary>Φ(2), the fraction of a normal below its mean plus 2σ.</summary>
    public const double Yield97 = 0.97724986805182079;

    /// <summary>
    /// Q for each of many lenses drawn on the quadratic model - its own Monte Carlo, sorted, made
    /// when first asked for. The draws are the same for every prediction of a model, so a quantile
    /// moves smoothly as the tolerances are scaled.
    /// </summary>
    internal Lazy<double[]>? Sample { get; init; }

    /// <summary>How many lenses <see cref="SpreadShape.Sampled"/> draws.</summary>
    public const int Draws = 20000;

    /// <summary>
    /// The mean, variance and third central moment of Q over the sampled lenses: for a model that
    /// draws on a spot synthesis - an MTF - the moments to quote, since Koch's are the quadratic's.
    /// </summary>
    public (double Mean, double Variance, double Third) SampledMoments
    {
        get
        {
            var q = Sample?.Value ?? throw new InvalidOperationException("this prediction has no sample");
            double mean = q.Average();
            double variance = q.Average(v => (v - mean) * (v - mean));
            double third = q.Average(v => (v - mean) * (v - mean) * (v - mean));
            return (mean, variance, third);
        }
    }

    private static double Percentile(double[] sorted, double p)
    {
        double at = p * (sorted.Length - 1);
        int lo = (int)Math.Floor(at), hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (at - lo) * (sorted[hi] - sorted[lo]);
    }

    private static double Fraction(double[] sorted, double q)
    {
        int below = Array.BinarySearch(sorted, q);
        if (below < 0) below = ~below; else while (below < sorted.Length && sorted[below] <= q) below++;
        return below / (double)sorted.Length;
    }

    /// <summary>The third central moment of Q.</summary>
    public double ThirdQ { get; init; }

    /// <summary>Q's skewness, κ₃/σ³: 2/√k for a gamma of shape k, nothing for a normal.</summary>
    public double Skewness => SigmaQ > 0.0 ? ThirdQ / Math.Pow(SigmaQ, 3) : 0.0;

    /// <summary>The fraction of lenses whose criterion is at most <paramref name="limit"/>.</summary>
    public double Yield(double limit)
    {
        double q = limit * limit;
        if (SigmaQ == 0.0) return q >= MeanQ ? 1.0 : 0.0;
        switch (Shape)
        {
            case SpreadShape.Gamma when MeanQ > 0.0:
                return Special.GammaP(MeanQ * MeanQ / VarianceQ, q * MeanQ / VarianceQ);
            case SpreadShape.Sampled when Sample != null:
                return Fraction(Sample.Value, q);
            case SpreadShape.Pearson when Math.Abs(Skewness) > 1e-3:
                double g = Math.Abs(Skewness), shape = 4.0 / (g * g), scale = SigmaQ * g / 2.0;
                return Skewness > 0.0
                    ? Special.GammaP(shape, Math.Max(0.0, (q - MeanQ) / scale + shape))
                    : 1.0 - Special.GammaP(shape, Math.Max(0.0, (MeanQ - q) / scale + shape));
            default:
                return Special.NormalCdf((q - MeanQ) / SigmaQ);
        }
    }
}

/// <summary>The distribution a <see cref="Prediction"/> takes Q to have, from its moments.</summary>
public enum SpreadShape
{
    /// <summary>Koch 1978's: from many errors of like size, by mean and variance.</summary>
    Normal,

    /// <summary>A sum of squares, by mean and variance: anchored at zero, its upper tail long.</summary>
    Gamma,

    /// <summary>
    /// Pearson's type III, by mean, variance and skewness: a gamma free to start anywhere, so a
    /// tail no longer or shorter than the third moment says.
    /// </summary>
    Pearson,

    /// <summary>
    /// No shape: Q drawn for 20 000 lenses on the quadratic model itself. Only the model is
    /// assumed - not normality, not moments - and the draws cost microseconds each.
    /// </summary>
    Sampled,
}

/// <summary>
/// Koch's second-moment method (1978): the mean and variance of a quadratic performance change from
/// the moments of each error's distribution, without tracing one lens more than the sensitivity
/// run did. Errors are independent - an x-y pair drawn as one error in a direction all round is one
/// error here - and each symmetric about the centre of its range.
///
/// <para>A range off nominal (a bias, centre c) is taken out first: with x = c + u, the model in u
/// has Q₀′ = Q(c), B_i′ = B_i + 2A_i c_i + Σ_j c_ij c_j, and the same A and cross terms (Koch's
/// general eq. 18 does this with odd moments).</para>
///
/// <para>Each error alone (eqs. 20, 21): mean A σ², variance A²(ν₄ − σ⁴) + B²σ². Each cross term
/// adds c² σ_i² σ_j² to the variance and nothing to the mean.</para>
///
/// <para>An x-y pair as ρ in a direction θ uniform on the circle (eqs. 23-27, re-derived here: the
/// printed eq. 26 has a stray square). With S = (A_x + A_y)/2, Δ = (A_x − A_y)/2 and D the pair's
/// cross term, Q's change is ρ²(S + Δ cos 2θ + ½D sin 2θ) + ρ(B_x cos θ + B_y sin θ); averaging over
/// θ, the mean is S ν₂ and the variance ν₄(S² + Δ²/2 + D²/8) + ν₂(B_x² + B_y²)/2 − (S ν₂)², with
/// ν₂, ν₄ the moments of ρ.</para>
/// </summary>
public static class Koch
{
    /// <summary>The prediction for the model's tolerances as they stand.</summary>
    public static Prediction Predict(QuadraticModel model, StatisticsModel statistics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(statistics);
        var items = model.Terms.Select(t => t.Item).ToList();
        var centered = Centered(model);
        var groups = Groups(items, statistics);
        var secondMoment = new double[items.Count];      // E[u²] of each error about its centre
        var fourthMoment = new double[items.Count];      // E[u⁴], the marginal for a member of a polar pair
        var dSquare = new double[items.Count];           // E[D x²]: an error's own centred change times its square
        var pairXY = new Dictionary<int, (int Partner, double Value)>();   // E[D xy] of a polar pair, by member
        double third = 0.0;                              // the third cumulant of Q
        var inPolarPair = new HashSet<(int, int)>();
        var contributions = new List<Contribution>();

        foreach (var g in groups)
        {
            if (g.Length == 1)
            {
                int i = g[0];
                var term = centered.Terms[i];
                double t = items[i].HalfWidth;
                var d = items[i].Distribution ?? statistics.Axis;
                double s2 = d.Variance * t * t, n4 = d.Fourth * Math.Pow(t, 4);
                secondMoment[i] = s2;
                fourthMoment[i] = n4;
                dSquare[i] = term.A * (n4 - s2 * s2);
                double n6 = d.Sixth * Math.Pow(t, 6);
                third += Math.Pow(term.A, 3) * (n6 - 3.0 * s2 * n4 + 2.0 * s2 * s2 * s2) + 3.0 * term.A * term.B * term.B * (n4 - s2 * s2);
                contributions.Add(new Contribution(g, Label(term.Item), term.A * s2,
                                                   term.A * term.A * (n4 - s2 * s2) + term.B * term.B * s2));
            }
            else
            {
                int ix = g[0], iy = g[1];
                var x = centered.Terms[ix];
                var y = centered.Terms[iy];
                double t = items[ix].HalfWidth;
                var radial = statistics.Polar!;
                double v2 = radial.Second * t * t, v4 = radial.Fourth * Math.Pow(t, 4);
                double dxy = model.Cross.TryGetValue((Math.Min(ix, iy), Math.Max(ix, iy)), out var c) ? c : 0.0;
                double s = (x.A + y.A) / 2.0, delta = (x.A - y.A) / 2.0;
                double mean = s * v2;
                double variance = v4 * (s * s + delta * delta / 2.0 + dxy * dxy / 8.0)
                                  + v2 * (x.B * x.B + y.B * y.B) / 2.0 - mean * mean;
                secondMoment[ix] = secondMoment[iy] = v2 / 2.0;
                fourthMoment[ix] = fourthMoment[iy] = 3.0 * v4 / 8.0;
                // Over θ: E[x⁴] = 3ν₄/8, E[x²y²] = ν₄/8, E[x²] = ν₂/2.
                dSquare[ix] = v4 * (3.0 * x.A + y.A) / 8.0 - mean * v2 / 2.0;
                dSquare[iy] = v4 * (3.0 * y.A + x.A) / 8.0 - mean * v2 / 2.0;
                pairXY[ix] = (iy, dxy * v4 / 8.0);
                pairXY[iy] = (ix, dxy * v4 / 8.0);
                third += PairThird(x, y, dxy, radial, t, mean);
                inPolarPair.Add((Math.Min(ix, iy), Math.Max(ix, iy)));
                contributions.Add(new Contribution(g, $"{x.Item.Tolerance.Code[..^1]}X/Y {ToleranceSpec.Surfaces(x.Item.Tolerance)}", mean, variance));
            }
        }

        double crossVariance = 0.0;
        foreach (var ((i, j), c) in model.Cross)
            if (!inPolarPair.Contains((i, j)))
                crossVariance += c * c * secondMoment[i] * secondMoment[j];

        third += CrossThird(centered, inPolarPair, secondMoment, dSquare, pairXY);

        double meanQ = centered.Q0 + contributions.Sum(k => k.Mean);
        double varianceQ = contributions.Sum(k => k.Variance) + crossVariance;
        return new Prediction(model.Q0, meanQ, varianceQ, crossVariance, contributions)
        {
            BiasShift = centered.Q0 - model.Q0,
            ThirdQ = third,
            Sample = new Lazy<double[]>(() => SampleQ(model, statistics)),
        };
    }


    /// <summary>
    /// The third central moment of one polar pair's change: (ρ²g + ρh − μ)³ averaged, g and h the
    /// quadratic and linear parts in θ, ρ's moments taken from its distribution and θ's average by
    /// the trapezoid rule on 72 points - exact for these trigonometric polynomials, of degree 6 at most.
    /// </summary>
    private static double PairThird(QuadraticTerm x, QuadraticTerm y, double dxy, RadialDistribution radial, double t, double mean)
    {
        var nu = Enumerable.Range(0, 7).Select(n => radial.Moment(n) * Math.Pow(t, n)).ToArray();
        double g3 = 0, g2h = 0, gh2 = 0, h3 = 0, g2 = 0, gh = 0, h2 = 0, g1 = 0, h1 = 0;
        const int points = 72;
        for (int k = 0; k < points; k++)
        {
            double th = 2.0 * Math.PI * k / points, c = Math.Cos(th), s = Math.Sin(th);
            double g = x.A * c * c + y.A * s * s + dxy * c * s, h = x.B * c + y.B * s;
            g3 += g * g * g; g2h += g * g * h; gh2 += g * h * h; h3 += h * h * h;
            g2 += g * g; gh += g * h; h2 += h * h; g1 += g; h1 += h;
        }
        double m = mean;
        return (nu[6] * g3 + 3 * nu[5] * g2h + 3 * nu[4] * gh2 + nu[3] * h3
                - 3 * m * (nu[4] * g2 + 2 * nu[3] * gh + nu[2] * h2) + 3 * m * m * (nu[2] * g1 + nu[1] * h1)) / points
               - m * m * m;
    }

    /// <summary>
    /// What the cross terms add to Q's third cumulant, with D_i each error's own centred change (a
    /// polar pair's for its members) and C_ij = c_ij x_i x_j:
    /// 3E[(ΣD)²ΣC] = 6Σ c B_i B_j m₂ᵢ m₂ⱼ, since E[D_i x_i] = B_i m₂ᵢ;
    /// 3E[ΣD (ΣC)²] = 3Σ c²(E[D_i x_i²] m₂ⱼ + E[D_j x_j²] m₂ᵢ), and for two edges from one error k to
    /// the two members of a polar pair, 6 c_kx c_ky m₂ₖ E[D xy];
    /// E[(ΣC)³] = 6Σ over triangles c_ij c_jk c_ki m₂ᵢ m₂ⱼ m₂ₖ.
    /// A polar pair's own cross term is in its D, not here.
    /// </summary>
    private static double CrossThird(QuadraticModel centered, HashSet<(int, int)> inPolarPair, double[] m2, double[] dSquare,
                                     Dictionary<int, (int Partner, double Value)> pairXY)
    {
        var edges = centered.Cross.Where(e => !inPolarPair.Contains(e.Key)).ToList();
        var weight = edges.ToDictionary(e => e.Key, e => e.Value);
        double Weight(int a, int b) => weight.TryGetValue((Math.Min(a, b), Math.Max(a, b)), out var w) ? w : 0.0;
        double sum = 0.0;
        foreach (var ((i, j), c) in edges)
        {
            sum += 6.0 * c * centered.Terms[i].B * centered.Terms[j].B * m2[i] * m2[j];
            sum += 3.0 * c * c * (dSquare[i] * m2[j] + dSquare[j] * m2[i]);
        }
        foreach (var (x, (y, value)) in pairXY)
        {
            if (y < x) continue;                       // each pair once
            for (int k = 0; k < m2.Length; k++)
                if (k != x && k != y) sum += 6.0 * Weight(k, x) * Weight(k, y) * m2[k] * value;
        }
        // Triangles i < j < l, walking i-j then j-l: each edge's onward edges by their first vertex,
        // so every pair is not walked against every pair - a departure criterion has them all.
        var onward = edges.GroupBy(e => e.Key.Item1).ToDictionary(g => g.Key, g => g.Select(e => (L: e.Key.Item2, C: e.Value)).ToList());
        foreach (var ((i, j), cij) in edges)
            if (onward.TryGetValue(j, out var next))
                foreach (var (l, cjl) in next)
                    if (weight.TryGetValue((i, l), out var cil))
                        sum += 6.0 * cij * cjl * cil * m2[i] * m2[j] * m2[l];
        return sum;
    }


    /// <summary>Q on <see cref="Prediction.Draws"/> lenses drawn on the model, with a fixed seed, sorted.</summary>
    private static double[] SampleQ(QuadraticModel model, StatisticsModel statistics)
    {
        var items = model.Terms.Select(t => t.Item).ToList();
        var groups = Groups(items, statistics);
        var q = new double[Prediction.Draws];
        Parallel.For(0, q.Length, k => q[k] = model.Evaluate(Draw(groups, items, statistics, new SplitMix((ulong)k * 0x2545F4914F6CDD1DUL + 101))));
        Array.Sort(q);
        return q;
    }

    /// <summary>
    /// The fraction of lenses meeting every limit at once, each model - one per criterion, all of one
    /// tolerance set - against its own limit, on the lenses <see cref="SampleQ"/> draws. The same
    /// errors drive every criterion, so this is not the product of their yields; it is at least one
    /// minus the sum of their failures.
    /// </summary>
    public static double JointYield(IReadOnlyList<QuadraticModel> models, IReadOnlyList<double> limits, StatisticsModel statistics)
    {
        if (models.Count == 0 || limits.Count != models.Count) throw new ArgumentException("one limit per model");
        var items = models[0].Terms.Select(t => t.Item).ToList();
        if (models.Any(m => m.Terms.Count != items.Count || m.Terms.Select(t => t.Item).Where((item, i) => item != items[i]).Any()))
            throw new ArgumentException("the models are not of one tolerance set");
        var groups = Groups(items, statistics);
        int met = 0;
        Parallel.For(0, Prediction.Draws, () => 0, (k, _, count) =>
        {
            var x = Draw(groups, items, statistics, new SplitMix((ulong)k * 0x2545F4914F6CDD1DUL + 101));
            for (int c = 0; c < models.Count; c++)
                if (!(models[c].Evaluate(x) <= limits[c] * limits[c])) return count;
            return count + 1;
        }, count => Interlocked.Add(ref met, count));
        return met / (double)Prediction.Draws;
    }

    /// <summary>The prediction with every tolerance at the half-width given, its bias scaled with it.</summary>
    public static Prediction Predict(QuadraticModel model, IReadOnlyList<double> halfWidths, StatisticsModel statistics) =>
        Predict(WithHalfWidths(model, halfWidths), statistics);

    /// <summary>The model with each tolerance's range changed to the half-width given.</summary>
    public static QuadraticModel WithHalfWidths(QuadraticModel model, IReadOnlyList<double> halfWidths)
    {
        if (halfWidths.Count != model.Terms.Count) throw new ArgumentException("one half-width per term");
        List<QuadraticTerm> Scaled(IReadOnlyList<QuadraticTerm> terms) =>
            terms.Select((t, i) => t with { Item = t.Item.WithHalfWidth(halfWidths[i]) }).ToList();
        return model with
        {
            Terms = Scaled(model.Terms),
            Compensator = model.Compensator == null ? null : model.Compensator with { Terms = Scaled(model.Compensator.Terms) },
            Adjustments = model.Adjustments.Select(a => a with { Terms = Scaled(a.Terms) }).ToList(),
        };
    }

    /// <summary>The model in each error's distance from the centre of its range.</summary>
    public static QuadraticModel Centered(QuadraticModel model)
    {
        var c = model.Terms.Select(t => t.Item.Center).ToArray();
        if (c.All(v => v == 0.0)) return model;
        var b = model.Terms.Select((t, i) => t.B + 2.0 * t.A * c[i]).ToArray();
        foreach (var ((i, j), k) in model.Cross)
        {
            b[i] += k * c[j];
            b[j] += k * c[i];
        }
        return model with
        {
            Q0 = model.Evaluate(c),
            Terms = model.Terms.Select((t, i) => t with { B = b[i] }).ToList(),
            Synthesis = null,      // the centred model is the quadratic alone, for the moments; a synthesis is in the original errors
        };
    }

    /// <summary>
    /// The errors drawn as one: every tolerance alone, except that when the statistics draw x-y pairs
    /// polar, each pair with no distribution of its own is one group, x first. A pair's two
    /// half-widths must then be equal.
    /// </summary>
    public static List<int[]> Groups(IReadOnlyList<ToleranceItem> items, StatisticsModel statistics)
    {
        var pairs = statistics.Polar != null
            ? QuadraticModel.AxisPairs(items).Where(p => items[p.X].Distribution == null && items[p.Y].Distribution == null).ToList()
            : new List<(int X, int Y)>();
        var paired = new HashSet<int>(pairs.SelectMany(p => new[] { p.X, p.Y }));
        var groups = new List<int[]>();
        for (int i = 0; i < items.Count; i++)
        {
            if (!paired.Contains(i)) { groups.Add(new[] { i }); continue; }
            foreach (var p in pairs.Where(p => p.X == i))
            {
                if (Math.Abs(items[p.X].HalfWidth - items[p.Y].HalfWidth) > 1e-12 * items[p.X].HalfWidth)
                    throw new ArgumentException($"{items[p.X]} and {items[p.Y]} are drawn as one error and need one tolerance");
                groups.Add(new[] { p.X, p.Y });
            }
        }
        return groups;
    }

    /// <summary>One lens's errors, in the units of each tolerance, for a Monte Carlo trial.</summary>
    public static double[] Draw(IReadOnlyList<int[]> groups, IReadOnlyList<ToleranceItem> items,
                                StatisticsModel statistics, Random random)
    {
        var x = new double[items.Count];
        foreach (var g in groups)
        {
            if (g.Length == 1)
            {
                var item = items[g[0]];
                x[g[0]] = item.Center + item.HalfWidth * (item.Distribution ?? statistics.Axis).Sample(random);
                continue;
            }
            double rho = items[g[0]].HalfWidth * statistics.Polar!.SampleRadius(random);
            double theta = 2.0 * Math.PI * random.NextDouble();
            x[g[0]] = items[g[0]].Center + rho * Math.Cos(theta);
            x[g[1]] = items[g[1]].Center + rho * Math.Sin(theta);
        }
        return x;
    }

    /// <summary>Each tolerance's half-width.</summary>
    public static double[] HalfWidths(IReadOnlyList<ToleranceItem> items) => items.Select(i => i.HalfWidth).ToArray();

    private static string Label(ToleranceItem item) => $"{item.Tolerance.Code} {ToleranceSpec.Surfaces(item.Tolerance)}";
}

/// <summary>
/// Koch's prediction of the compensator's move over the lenses built: nearly linear in the errors,
/// so taken as normal. <see cref="Needed"/> is the travel either way that covers a fraction of them -
/// Hilbert 1979's compensator range, at mean + 2σ for 95.4 %.
/// </summary>
public sealed record TravelPrediction(double Mean, double Sigma)
{
    public double Needed(double fraction) => Math.Abs(Mean) + Special.NormalQuantile(0.5 + 0.5 * fraction) * Sigma;

    /// <summary>The fraction of lenses whose move is at most <paramref name="move"/>, taking the move as normal.</summary>
    public double Fraction(double move) => Sigma > 0.0 ? Special.NormalCdf((move - Mean) / Sigma) : move >= Mean ? 1.0 : 0.0;

    /// <summary>The compensator's move for the model's tolerances, or null if it has no compensator.</summary>
    public static TravelPrediction? Of(QuadraticModel model, StatisticsModel statistics) =>
        model.Compensator == null ? null : OfMove(model.Compensator, statistics);

    /// <summary>The move a model of one compensator's move predicts: its mean and spread over the lenses built.</summary>
    public static TravelPrediction OfMove(QuadraticModel move, StatisticsModel statistics)
    {
        var p = Koch.Predict(move, statistics);
        return new TravelPrediction(p.MeanQ, p.SigmaQ);
    }

    /// <summary>Each of the compensator's other adjustments' predicted move, named, in the model's order.</summary>
    public static IReadOnlyList<(string Label, TravelPrediction Travel)> OfAdjustments(QuadraticModel model, StatisticsModel statistics) =>
        model.Adjustments.Select(a => (a.Criterion, OfMove(a, statistics))).ToList();
}
