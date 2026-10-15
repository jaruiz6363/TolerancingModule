namespace Tolerancing.Core;

/// <summary>
/// What a tolerance set must achieve: a fraction <see cref="Yield"/> of lenses at or better than
/// <see cref="Limit"/>, as Koch's prediction of the given shape has it - by default 97.7 %, Hilbert
/// 1979's "probable worst case", which for a normal spread is the mean plus 2σ. With a
/// <see cref="Travel"/>, the compensator must also refocus the same fraction of lenses within that
/// far of nominal either way.
/// </summary>
public sealed record AllocationGoal(double Limit, double Yield = Prediction.Yield97, SpreadShape Shape = SpreadShape.Sampled)
{
    public double TargetQ => Limit * Limit;

    /// <summary>The compensator's travel either way, in lens units, or null for no limit.</summary>
    public double? Travel { get; init; }

    /// <summary>
    /// The travel of each of the compensator's other adjustments either way, in the order of the
    /// model's <see cref="QuadraticModel.Adjustments"/>, null for one with no limit: each, like the
    /// focus, must keep the goal's fraction of lenses within it.
    /// </summary>
    public IReadOnlyList<double?> AdjustmentTravels { get; init; } = Array.Empty<double?>();

    /// <summary>
    /// With further criteria) (<see cref="Requirement"/>), the yield is of lenses within every limit at
    /// once rather than of each criterion alone. The travel keeps to the yield as it stands.
    /// </summary>
    public bool Joint { get; init; }
}

/// <summary>
/// A further criterion a tolerance set must also meet, at the goal's yield: its quadratic model,
/// fitted to the same tolerances as the first criterion's, and its limit in its own units - a focal
/// length held within 0.25 mm beside an RMS wavefront within 0.1 waves.
/// </summary>
public sealed record Requirement(QuadraticModel Model, double Limit)
{
    public double TargetQ => Limit * Limit;
}

/// <summary>
/// The range a shop can hold a tolerance in: tighter than <see cref="Tightest"/> it cannot, looser
/// than <see cref="Loosest"/> saves nothing (Smith 1985 §VI). Adams 1988's cost of a tolerance is
/// Tightest / t, its "infeasibility". Both are half-widths.
/// </summary>
public readonly record struct ShopLimits(double Tightest, double Loosest)
{
    /// <summary>
    /// The only half-widths the tolerance can take, loosest first - glass is sold in grades - or null
    /// for any value. An allocated value rounds down to one of them.
    /// </summary>
    public IReadOnlyList<double>? Steps { get; init; }

    /// <summary>
    /// For each tolerance, the extra-precise grade's value as the floor - Smith's Table 3 - and as the
    /// ceiling the low-cost grade's or Burge's base, whichever is tighter, since a tolerance looser
    /// than the base costs no less. A kind no grade covers gets a tenth to ten times its present value.
    /// Index and Abbe number keep to the glass grades.
    /// </summary>
    public static ShopLimits[] FromGrades(NominalLens lens, IReadOnlyList<ToleranceItem> items) => items.Select(i =>
    {
        double? tight = DefaultTolerances.Value(i.Tolerance, lens, DefaultTolerances.For(ShopGrade.ExtraPrecise));
        double? loose = DefaultTolerances.Value(i.Tolerance, lens, DefaultTolerances.For(ShopGrade.LowCost));
        double t = tight ?? i.HalfWidth / 10.0, l = loose ?? i.HalfWidth * 10.0;
        if (DefaultTolerances.Base(i.Tolerance, lens) is double b) l = Math.Max(t, Math.Min(l, b));
        if (i.Tolerance is ThicknessChange th)
        {
            // Never so loose the thickness could close up, nor tighter than that allows.
            l = DefaultTolerances.ThicknessCap(lens, th.Surface, l);
            t = Math.Min(t, l);
        }
        return new ShopLimits(t, l) { Steps = DefaultTolerances.Grades(i.Tolerance, lens) };
    }).ToArray();

    /// <summary>
    /// The value rounded down as a drawing states it: to the largest step not above it, or to a
    /// preferred number when there are no steps; never outside the limits.
    /// </summary>
    public double Round(double value)
    {
        double rounded = Steps == null ? Allocator.RoundDown(value)
                       : Steps.Where(s => s <= value * (1.0 + 1e-12)).DefaultIfEmpty(Tightest).Max();
        return Math.Max(Tightest, Math.Min(Loosest, rounded));
    }
}

public enum AllocationMethod
{
    /// <summary>
    /// Every tolerance - every x-y pair counted once - makes the same increase of the criterion, all
    /// scaled together until the prediction meets the goal: Hilbert 1979's equal drop, Smith 1985's
    /// "inversely proportional to its sensitivity", Koch's clamp-and-rescale.
    /// </summary>
    EqualEffect,

    /// <summary>
    /// The least total cost Σ Tightest/t that meets the goal: Adams 1988, solved for Koch's
    /// prediction rather than his linear RSS, by its Lagrange condition.
    /// </summary>
    CostOptimal,
}

/// <summary>One allocated tolerance set: the items at their new values, and what Koch predicts of it.</summary>
public sealed record AllocationResult(AllocationMethod Method, AllocationGoal Goal,
                                      IReadOnlyList<ToleranceItem> Items, IReadOnlyList<ShopLimits> Limits,
                                      Prediction Prediction, bool Feasible)
{
    /// <summary>Adams's cost, Σ Tightest/t over the allocated half-widths, each x-y pair once.</summary>
    public double Cost { get; init; }

    /// <summary>The compensator's predicted move, when there is a compensator.</summary>
    public TravelPrediction? Travel { get; init; }

    /// <summary>Each of the compensator's other adjustments' predicted move, named.</summary>
    public IReadOnlyList<(string Label, TravelPrediction Travel)> AdjustmentTravels { get; init; } = Array.Empty<(string, TravelPrediction)>();

    /// <summary>What Koch predicts of each further criterion (<see cref="Requirement"/>) for the allocated set, in order.</summary>
    public IReadOnlyList<Prediction> Also { get; init; } = Array.Empty<Prediction>();

    /// <summary>With further criteria, the fraction of lenses on the models meeting every limit at once (<see cref="Koch.JointYield"/>).</summary>
    public double? JointYield { get; init; }

    /// <summary>
    /// The yield each criterion was held to: the goal's, or for a <see cref="AllocationGoal.Joint"/>
    /// goal the higher one that leaves the goal's fraction within every limit at once.
    /// </summary>
    public double MarginalYield { get; init; }

    public bool AtTightest(int i) => Items[i].HalfWidth <= Limits[i].Tightest * (1.0 + 1e-9);
    public bool AtLoosest(int i) => Items[i].HalfWidth >= Limits[i].Loosest * (1.0 - 1e-9);
}

/// <summary>
/// Sizes each tolerance of a set so that the set just meets a goal, from a quadratic model of the
/// criterion. The x and y of a decentre or tilt get one value, since a drawing gives them one, and
/// a biased range keeps its bias in proportion. Every half-width stays within its shop limits; when
/// even the tightest cannot meet the goal, the result says so and holds everything at its tightest.
///
/// <para>The goal is a set of constraints, each written as a ratio that must not exceed one: each
/// criterion's level against its limit - the first criterion's and those of any further
/// <see cref="Requirement"/>s - and, with a travel, the compensator's needed travel against it. Each
/// criterion is met by the goal's fraction of lenses on its own; <see cref="AllocationResult.JointYield"/>
/// says how many meet them all.</para>
///
/// <para>A <see cref="AllocationGoal.Joint"/> goal holds every criterion to one higher yield y, found
/// so that the goal's fraction of lenses meet every limit at once. The same errors drive every
/// criterion, so the joint yield lies between the union bound 1 − Σ(1 − y) and y itself: y lies
/// between the goal's yield and 1 − (1 − goal)/N for N criteria. The joint failures are near
/// proportional to each criterion's, so y is found in two or three allocations by scaling the
/// failures it allows, the joint yield counted on the models' draws (<see cref="Koch.JointYield"/>).</para>
/// </summary>
public static class Allocator
{
    public static AllocationResult Allocate(QuadraticModel model, IReadOnlyList<ShopLimits> limits, AllocationGoal goal,
                                            StatisticsModel statistics, AllocationMethod method, bool round = true,
                                            IReadOnlyList<Requirement>? also = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        also ??= Array.Empty<Requirement>();
        if (!goal.Joint || also.Count == 0)
            return Allocate(model, limits, goal with { Joint = false }, goal.Yield, statistics, method, round, also) with { Goal = goal };

        // In the fraction failing: each criterion is held to failures f, and the lenses failing any
        // one of them number F(f), between f and N f. F is near proportional to f, so f is scaled by
        // the target over F until F is just within the target - two or three allocations.
        var exact = new Dictionary<double, AllocationResult>();
        AllocationResult At(double f, bool rounded)
        {
            if (!rounded && exact.TryGetValue(f, out var known)) return known;
            var result = Allocate(model, limits, goal with { Joint = false, Yield = 1.0 - f }, goal.Yield, statistics, method, rounded, also);
            if (!rounded) exact[f] = result;
            return result;
        }
        double target = 1.0 - goal.Yield, floor = target / (also.Count + 1);
        double Failing(AllocationResult r) => 1.0 - r.JointYield!.Value;

        double f = target;
        var r = At(f, rounded: false);
        if (!r.Feasible || Failing(r) <= target) return At(f, round) with { Goal = goal };

        // A bracket in ln f: too many fail at the top; within the target, or no set at all, below.
        // A secant through its ends, in ln F against ln f, aimed at 99 % of the target and kept off
        // the ends; the first step takes F as proportional to f. Done within 2 % of the target.
        double failX = Math.Log(f), failV = Math.Log(Failing(r) / target);
        double? meetX = null, impossibleX = null;
        double meetV = 0.0, aim = Math.Log(0.99);
        double x = Math.Log(Math.Clamp(f * 0.99 * target / Failing(r), floor, target));
        for (int pass = 0; pass < 8; pass++)
        {
            r = At(Math.Exp(x), rounded: false);
            if (!r.Feasible) impossibleX = x;                    // no set meets this f, nor any smaller
            else
            {
                double v = Math.Log(Math.Max(Failing(r), 1e-300) / target);
                if (v <= 0.0)
                {
                    meetX = x;
                    meetV = v;
                    if (v >= Math.Log(0.98)) break;
                }
                else
                {
                    failX = x;
                    failV = v;
                    if (x <= Math.Log(floor) + 1e-12) break;    // the union bound's f, and still too many fail
                }
            }
            double lower = meetX ?? impossibleX ?? Math.Log(floor);
            double width = failX - lower;
            if (width < 1e-4) break;
            double next = meetX is double m && impossibleX is not double
                ? m + (aim - meetV) * (failX - m) / (failV - meetV)
                : meetX == null && impossibleX == null ? x + aim - failV : 0.5 * (lower + failX);
            x = Math.Clamp(next, lower + 0.05 * width, failX - 0.05 * width);
            if (meetX == null && impossibleX == null) x = Math.Max(next, Math.Log(floor));
        }
        // No f met the target: the tightest set if none is possible, else the union bound - within
        // each criterion's target/N, at most the target fail any. Rounding down only tightens, so the
        // rounded set keeps the joint yield.
        double chosen = meetX ?? impossibleX ?? Math.Log(floor);
        return At(Math.Exp(chosen), round) with { Goal = goal };
    }

    private static AllocationResult Allocate(QuadraticModel model, IReadOnlyList<ShopLimits> limits, AllocationGoal goal, double travelYield,
                                             StatisticsModel statistics, AllocationMethod method, bool round,
                                             IReadOnlyList<Requirement> also)
    {
        if (limits.Count != model.Terms.Count) throw new ArgumentException("one shop limit per term");
        if (goal.Travel != null && model.Compensator == null)
            throw new ArgumentException("a travel goal needs a model with a compensator");
        foreach (var r in also)
        {
            if (r.Model.Terms.Count != model.Terms.Count ||
                r.Model.Terms.Where((t, i) => t.Item.Tolerance != model.Terms[i].Item.Tolerance).Any())
                throw new ArgumentException($"{r.Model.Criterion}: its model is not of the same tolerances");
            if (!(r.Limit > 0.0)) throw new ArgumentException($"{r.Model.Criterion}: the limit must be positive");
        }
        // The criteria, the first one's first, and the Q each must keep to.
        var models = new[] { model }.Concat(also.Select(r => r.Model)).ToArray();
        var goalQ = new[] { goal.TargetQ }.Concat(also.Select(r => r.TargetQ)).ToArray();
        var groups = AllocationGroups(model.Terms.Select(t => t.Item).ToList());
        var lo = groups.Select(g => g.Max(i => limits[i].Tightest)).ToArray();
        var hi = groups.Select(g => g.Min(i => limits[i].Loosest)).ToArray();
        for (int k = 0; k < groups.Count; k++)
            if (!(lo[k] > 0.0 && hi[k] >= lo[k]))
                throw new ArgumentException($"{model.Terms[groups[k][0]].Item}: its shop limits are not a range");

        // One ratio per constraint, for working targets of Q - the goal's, or the sampled correction's.
        List<Func<double[], double>> Constraints(double[] targets, SpreadShape shape)
        {
            var list = models.Select((m, c) => (Func<double[], double>)(t =>
                LevelQ(m, Expand(groups, t, model.Terms.Count), statistics, goal.Yield, shape) / targets[c])).ToList();
            if (goal.Travel is double travel)
                list.Add(t => TravelPrediction.Of(Koch.WithHalfWidths(model, Expand(groups, t, model.Terms.Count)), statistics)!
                                  .Needed(travelYield) / travel);
            for (int a = 0; a < goal.AdjustmentTravels.Count && a < model.Adjustments.Count; a++)
                if (goal.AdjustmentTravels[a] is double reach)
                {
                    int k = a;
                    list.Add(t => TravelPrediction.OfMove(Koch.WithHalfWidths(model, Expand(groups, t, model.Terms.Count)).Adjustments[k], statistics)
                                      .Needed(travelYield) / reach);
                }
            return list;
        }
        double[] Solve(double[] targets, SpreadShape shape)
        {
            var constraints = Constraints(targets, shape);
            bool Meets(double[] t) => constraints.All(c => c(t) <= 1.0);
            return method == AllocationMethod.EqualEffect
                ? EqualEffect(models, targets, groups, lo, hi, Meets)
                : CostOptimal(models, targets, groups, lo, hi, constraints, Meets);
        }

        var exact = Constraints(goalQ, goal.Shape);
        bool MeetsGoal(double[] t) => exact.All(c => c(t) <= 1.0);
        double[] values;
        bool feasible = true;
        if (MeetsGoal(hi)) values = hi;                               // everything at its loosest is good enough
        else if (!MeetsGoal(lo)) { values = lo; feasible = false; }
        else if (goal.Shape != SpreadShape.Sampled) values = Solve(goalQ, goal.Shape);
        else
        {
            // The sampled quantile costs a model Monte Carlo each time; the search needs thousands of
            // them. So it is solved on Pearson's three-moment shape, with each limit moved by how far
            // that shape is from the sampled one at the answer, until the sampled levels are the goal's:
            // each criterion that binds, or that the sampled level puts over its limit.
            var working = (double[])goalQ.Clone();
            values = Solve(working, SpreadShape.Pearson);
            for (int pass = 0; pass < 6; pass++)
            {
                var t = Expand(groups, values, model.Terms.Count);
                var sampled = models.Select(m => LevelQ(m, t, statistics, goal.Yield, goal.Shape)).ToArray();
                bool within = sampled.Select((s, c) => s <= goalQ[c]).All(b => b);
                if (within && sampled.Select((s, c) => s >= 0.995 * goalQ[c]).Any(b => b)) break;
                var ratios = Constraints(working, SpreadShape.Pearson).Take(models.Length).Select(r => r(values)).ToArray();
                bool moved = false;
                for (int c = 0; c < models.Length; c++)
                {
                    if (sampled[c] <= goalQ[c] && ratios[c] < 0.99 && models.Length > 1) continue;
                    // Aim a little inside the goal, so the last pass lands within it.
                    working[c] *= 0.9975 * goalQ[c] / sampled[c];
                    moved = true;
                }
                if (!moved) break;
                if (!Constraints(working, SpreadShape.Pearson).All(c => c(lo) <= 1.0)) { values = lo; break; }
                values = MeetsWorking(hi) ? hi : Solve(working, SpreadShape.Pearson);
                bool MeetsWorking(double[] v) => Constraints(working, SpreadShape.Pearson).All(c => c(v) <= 1.0);
            }
        }

        // Whatever the search found, the answer must meet the goal itself: an insensitive group the
        // search left at its loosest, or a sampled correction that ran out of passes, can leave it just
        // over. Then every value is drawn back towards its tightest, which meets the goal, by one factor.
        if (feasible && !MeetsGoal(values))
        {
            var found = values;
            double[] Toward(double s) => found.Select((v, k) => lo[k] + s * (v - lo[k])).ToArray();
            values = Toward(Bisect(s => !MeetsGoal(Toward(s)), 0.0, 1.0));
        }

        if (round && feasible)
            values = values.Select((v, k) => groups[k].Length == 1
                                                 ? Math.Max(lo[k], Math.Min(hi[k], limits[groups[k][0]].Round(v)))
                                                 : Math.Max(lo[k], Math.Min(hi[k], RoundDown(v)))).ToArray();

        var widths = Expand(groups, values, model.Terms.Count);
        var updated = Koch.WithHalfWidths(model, widths);
        var updatedAlso = also.Select(r => Koch.WithHalfWidths(r.Model, widths)).ToList();
        return new AllocationResult(method, goal, updated.Terms.Select(t => t.Item).ToList(), limits.ToArray(),
                                    Koch.Predict(updated, statistics) with { Shape = goal.Shape }, feasible)
        {
            Cost = groups.Select((g, k) => lo[k] / values[k]).Sum(),
            MarginalYield = goal.Yield,
            Travel = TravelPrediction.Of(updated, statistics),
            AdjustmentTravels = TravelPrediction.OfAdjustments(updated, statistics),
            Also = updatedAlso.Select(m => Koch.Predict(m, statistics) with { Shape = goal.Shape }).ToList(),
            JointYield = also.Count == 0 ? null
                : Koch.JointYield(new[] { updated }.Concat(updatedAlso).ToList(),
                                  new[] { goal.Limit }.Concat(also.Select(r => r.Limit)).ToList(), statistics),
        };
    }

    /// <summary>
    /// Each tolerance at the half-width where it alone raises Q by δ, clamped to its limits, with δ
    /// the largest that meets the goal. Q's rise is taken along its increasing envelope, so a
    /// tolerance whose criterion falls again past a peak is not given more for it. With several
    /// criteria each rise is a fraction of that criterion's own target Q, and a tolerance is sized by
    /// the criterion it uses most of: none takes more than δ of any one budget.
    /// </summary>
    private static double[] EqualEffect(QuadraticModel[] models, double[] targets, List<int[]> groups, double[] lo, double[] hi,
                                        Func<double[], bool> meets)
    {
        double Effect(int k, double t)
        {
            double most = 0.0;
            for (int c = 0; c < models.Length; c++)
                most = Math.Max(most, groups[k].Sum(i => Envelope(models[c].Terms[i], t)) / targets[c]);
            return most;
        }

        double[] At(double delta) => groups.Select((g, k) =>
        {
            if (Effect(k, hi[k]) <= delta) return hi[k];
            if (Effect(k, lo[k]) >= delta) return lo[k];
            return Bisect(t => Effect(k, t) >= delta, lo[k], hi[k]);
        }).ToArray();

        double small = groups.Select((g, k) => Effect(k, lo[k])).Where(e => e > 0.0).DefaultIfEmpty(1e-30).Min();
        double large = groups.Select((g, k) => Effect(k, hi[k])).Max();
        double logDelta = Bisect(ld => !meets(At(Math.Exp(ld))), Math.Log(small) - 1.0, Math.Log(Math.Max(large, small)) + 1.0);
        return At(Math.Exp(logDelta));
    }

    /// <summary>
    /// Adams's Lagrange condition Tightest_g / t_g² = Σ_m λ_m ∂r_m/∂t_g over the constraints r_m ≤ 1
    /// (his eq. 20 for several). Written as t_g = (Tightest_g / Σ_m λ_m h_mg)^⅓ with
    /// h_mg = (∂r_m/∂t_g)/t_g - constant when a constraint is quadratic in the tolerances, as Adams's
    /// RSS is - it is solved by holding h, finding the multipliers, and repeating with h at the new
    /// values. The multipliers meet complementary slackness - a multiplier is zero unless its
    /// constraint binds, r_m = 1 - found by Newton's method on the binding set, started from the last
    /// iteration's. Should that fail, by bisection: the smallest λ₁ that meets the first constraint,
    /// with λ₂ = 0 if that meets the second, else the smallest λ₂ that does, λ₁ re-found for each;
    /// and so on for each further one.
    /// </summary>
    private static double[] CostOptimal(QuadraticModel[] models, double[] targets, List<int[]> groups, double[] lo, double[] hi,
                                        List<Func<double[], double>> constraints, Func<double[], bool> meets)
    {
        var t = EqualEffect(models, targets, groups, lo, hi, meets);
        double[]? start = null;
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var h = constraints.Select(r =>
            {
                var hm = new double[groups.Count];
                for (int k = 0; k < groups.Count; k++)
                {
                    double step = 1e-4 * t[k];
                    var up = (double[])t.Clone();
                    var down = (double[])t.Clone();
                    up[k] += step;
                    down[k] -= step;
                    hm[k] = Math.Max(0.0, (r(up) - r(down)) / (2.0 * step) / t[k]);
                }
                return hm;
            }).ToArray();

            double[] At(double[] lambda) => groups.Select((g, k) =>
            {
                double d = 0.0;
                for (int m = 0; m < lambda.Length; m++) d += lambda[m] * h[m][k];
                return d <= 0.0 ? hi[k] : Math.Max(lo[k], Math.Min(hi[k], Math.Cbrt(lo[k] / d)));
            }).ToArray();

            var lambda = Newton(start) ?? Nested();
            start = lambda;

            // The multipliers by Newton's method in ln λ on the binding constraints, ln r_m = 0, from
            // the last iteration's. Every constraint evaluation is a Koch prediction; this takes a few
            // dozen, where bisecting each multiplier inside the others' took tens of thousands. A
            // broken constraint with no multiplier enters, at the one that alone would give the present
            // tolerances; a multiplier driven to nothing, or that no longer moves anything, leaves.
            // Null if it does not converge, for the bisection to settle.
            double[]? Newton(double[]? from)
            {
                int count = constraints.Count;
                var l = from == null ? new double[count] : (double[])from.Clone();
                double[] Ratios(double[] x)
                {
                    var u = At(x);
                    return constraints.Select(c => c(u)).ToArray();
                }
                const double aim = -5e-10;                   // just inside each limit, so the set meets them
                var r = Ratios(l);
                for (int step = 0; step < 60; step++)
                {
                    bool entered = false;
                    for (int m = 0; m < count; m++)
                    {
                        if (l[m] != 0.0 || r[m] <= 1.0) continue;
                        var u = t;                                 // the tolerances of the last iteration
                        var alone = groups.Select((g, k) => h[m][k] > 0.0 ? lo[k] / (u[k] * u[k] * u[k] * h[m][k]) : double.NaN)
                                          .Where(double.IsFinite).OrderBy(v => v).ToList();
                        if (alone.Count == 0) return null;
                        l[m] = alone[alone.Count / 2];
                        entered = true;
                    }
                    if (entered) r = Ratios(l);
                    var active = Enumerable.Range(0, count).Where(m => l[m] > 0.0).ToList();
                    if (active.Count == 0) return r.All(v => v <= 1.0) ? l : null;
                    var residual = active.Select(m => Math.Log(r[m]) - aim).ToArray();
                    if (residual.All(v => Math.Abs(v) < 5e-10) && r.All(v => v <= 1.0)) return l;

                    // ∂ ln r_m / ∂ ln λ_k over the binding set, by forward steps.
                    var jacobian = new double[active.Count, active.Count];
                    for (int j = 0; j < active.Count; j++)
                    {
                        var x = (double[])l.Clone();
                        x[active[j]] *= Math.Exp(1e-6);
                        var rj = Ratios(x);
                        for (int i = 0; i < active.Count; i++)
                            jacobian[i, j] = (Math.Log(rj[active[i]]) - Math.Log(r[active[i]])) / 1e-6;
                    }
                    // A slack constraint whose multiplier hardly moves anything beside the others has none:
                    // driving it to zero would only make the step ill-conditioned.
                    var columns = new double[active.Count];
                    for (int j = 0; j < active.Count; j++)
                        for (int i = 0; i < active.Count; i++) columns[j] = Math.Max(columns[j], Math.Abs(jacobian[i, j]));
                    bool left = false;
                    for (int j = 0; j < active.Count; j++)
                        if (r[active[j]] < 1.0 && columns[j] < 1e-3 * columns.Max()) { l[active[j]] = 0.0; left = true; }
                    if (left) { r = Ratios(l); continue; }

                    var delta = SolveLinear(jacobian, residual.Select(v => -v).ToArray());
                    if (delta == null) return null;
                    double longest = delta.Max(Math.Abs), scale = longest > 2.0 ? 2.0 / longest : 1.0;
                    // Backtrack until the step reduces the worst miss.
                    double before = Miss(l, r), after = double.PositiveInfinity;
                    double[] trial = l, rTrial = r;
                    for (int halving = 0; halving < 12 && !(after < before); halving++, scale *= 0.5)
                    {
                        trial = (double[])l.Clone();
                        for (int i = 0; i < active.Count; i++) trial[active[i]] *= Math.Exp(scale * delta[i]);
                        rTrial = Ratios(trial);
                        after = Miss(trial, rTrial);
                    }
                    if (!(after < before)) return null;
                    l = trial;
                    r = rTrial;
                }
                return null;

                // How far from the solution: a binding constraint off its limit, or any constraint over it.
                static double Miss(double[] x, double[] ratios) =>
                    ratios.Select((v, m) => x[m] > 0.0 ? Math.Abs(Math.Log(v) - aim) : Math.Max(0.0, Math.Log(v))).Max();
            }

            double[] Nested()
            {
                var lambda = WithFirst(new double[constraints.Count]);
                // Each further multiplier in turn, the smallest that meets its constraint with the others
                // held; with three or more constraints, sweeps until none changes, since raising one can
                // leave another larger than it need be.
                for (int sweep = 0; sweep < (constraints.Count > 2 ? 50 : 1); sweep++)
                {
                    bool changed = false;
                    for (int m = 1; m < constraints.Count; m++)
                    {
                        int mm = m;
                        double v = 0.0;
                        if (constraints[m](At(With(lambda, m, 0.0))) > 1.0)
                            v = Math.Exp(Bisect(ll => constraints[mm](At(With(lambda, mm, Math.Exp(ll)))) <= 1.0, -200.0, 200.0, returnAbove: true));
                        changed |= Math.Abs(v - lambda[m]) > 1e-9 * Math.Max(v, lambda[m]);
                        lambda = With(lambda, m, v);
                    }
                    if (!changed) break;
                }
                return lambda;
            }

            // The smallest multiplier that meets constraint m, the others held: a large one makes everything tight.
            double Smallest(int m, double[] lambda, Func<double[], bool> ok)
            {
                var trial = (double[])lambda.Clone();
                trial[m] = 0.0;
                if (ok(At(trial))) return 0.0;
                double log = Bisect(ll => { trial[m] = Math.Exp(ll); return ok(At(trial)); }, -200.0, 200.0, returnAbove: true);
                return Math.Exp(log);
            }

            double[] WithFirst(double[] l)
            {
                l[0] = Smallest(0, l, u => constraints[0](u) <= 1.0);
                return l;
            }
            // Multiplier m set to v, the first re-found for it.
            double[] With(double[] l, int m, double v)
            {
                var c = (double[])l.Clone();
                c[m] = v;
                return WithFirst(c);
            }

            var next = At(lambda);
            double change = next.Select((v, k) => Math.Abs(v - t[k]) / t[k]).Max();
            t = next;
            if (change < 1e-9) break;
        }
        return t;
    }

    /// <summary>The Q that the goal's fraction of lenses are at or below, for the half-widths given.</summary>
    public static double LevelQ(QuadraticModel model, IReadOnlyList<double> halfWidths, StatisticsModel statistics, AllocationGoal goal) =>
        LevelQ(model, halfWidths, statistics, goal.Yield, goal.Shape);

    private static double LevelQ(QuadraticModel model, IReadOnlyList<double> halfWidths, StatisticsModel statistics,
                                 double yield, SpreadShape shape) =>
        (Koch.Predict(model, halfWidths, statistics) with { Shape = shape }).QuantileQ(yield);

    /// <summary>The tolerances sized together: each x-y pair, and every other tolerance alone.</summary>
    public static List<int[]> AllocationGroups(IReadOnlyList<ToleranceItem> items)
    {
        var pairs = QuadraticModel.AxisPairs(items);
        var paired = new HashSet<int>(pairs.SelectMany(p => new[] { p.X, p.Y }));
        var groups = new List<int[]>();
        for (int i = 0; i < items.Count; i++)
        {
            if (!paired.Contains(i)) groups.Add(new[] { i });
            groups.AddRange(pairs.Where(p => p.X == i).Select(p => new[] { p.X, p.Y }));
        }
        return groups;
    }

    /// <summary>
    /// Rounds down to the preferred numbers 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8 of each decade, as a
    /// drawing states a tolerance; down, so the set stays within its goal.
    /// </summary>
    public static double RoundDown(double value)
    {
        if (!(value > 0.0) || !double.IsFinite(value)) return value;
        double decade = Math.Pow(10.0, Math.Floor(Math.Log10(value)));
        double mantissa = value / decade;
        double[] steps = { 1.0, 1.2, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0 };
        double best = 1.0;
        foreach (double s in steps)
            if (s <= mantissa * (1.0 + 1e-12)) best = s;
        int exponent = (int)Math.Floor(Math.Log10(value));
        return Math.Round(best * decade, Math.Clamp(2 - exponent, 0, 15));   // 0.15, not 0.15000000000000002
    }

    /// <summary>
    /// The larger rise of Q at the two ends of the range when its half-width is <paramref name="h"/> -
    /// each end at (r ± 1)h, r the range's bias in half-widths - along each end's increasing envelope.
    /// </summary>
    private static double Envelope(QuadraticTerm term, double h)
    {
        double r = term.Item.HalfWidth > 0.0 ? term.Item.Center / term.Item.HalfWidth : 0.0;
        double Rise(double m)
        {
            double a = term.A * m * m, b = term.B * m, t = h;
            if (a < 0.0)
            {
                if (b <= 0.0) return 0.0;
                t = Math.Min(t, b / (-2.0 * a));     // past the peak, Q only falls
            }
            return Math.Max(0.0, (a * t + b) * t);
        }
        return Math.Max(Rise(r + 1.0), Rise(r - 1.0));
    }

    private static double[] Expand(List<int[]> groups, double[] values, int count)
    {
        var t = new double[count];
        for (int k = 0; k < groups.Count; k++)
            foreach (int i in groups[k]) t[i] = values[k];
        return t;
    }

    /// <summary>x with A x = b, by elimination with partial pivoting; null when A is singular.</summary>
    private static double[]? SolveLinear(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        double size = 0.0;
        foreach (double v in a) size = Math.Max(size, Math.Abs(v));
        for (int c = 0; c < n; c++)
        {
            int pivot = c;
            for (int r = c + 1; r < n; r++)
                if (Math.Abs(m[r, c]) > Math.Abs(m[pivot, c])) pivot = r;
            if (!(Math.Abs(m[pivot, c]) > 1e-12 * size)) return null;
            if (pivot != c)
            {
                for (int k = 0; k < n; k++) (m[c, k], m[pivot, k]) = (m[pivot, k], m[c, k]);
                (x[c], x[pivot]) = (x[pivot], x[c]);
            }
            for (int r = c + 1; r < n; r++)
            {
                double f = m[r, c] / m[c, c];
                for (int k = c; k < n; k++) m[r, k] -= f * m[c, k];
                x[r] -= f * x[c];
            }
        }
        for (int c = n - 1; c >= 0; c--)
        {
            for (int k = c + 1; k < n; k++) x[c] -= m[c, k] * x[k];
            x[c] /= m[c, c];
        }
        return x;
    }

    /// <summary>
    /// The boundary of a predicate false below and true above, by bisection to 1e-12 of the interval:
    /// the last point found false, or with <paramref name="returnAbove"/> the first found true.
    /// </summary>
    private static double Bisect(Func<double, bool> isAbove, double lo, double hi, bool returnAbove = false)
    {
        for (int i = 0; i < 200 && hi - lo > 1e-12 * Math.Max(1.0, Math.Abs(hi)); i++)
        {
            double mid = 0.5 * (lo + hi);
            if (isAbove(mid)) hi = mid; else lo = mid;
        }
        return returnAbove ? hi : lo;
    }
}

/// <summary>
/// Adams 1988's closed forms for the least Σ τ_k / t_k (τ_k the tightest each tolerance can be)
/// under one constraint on a linear performance change of sensitivities s_k.
/// </summary>
public static class Adams
{
    /// <summary>Worst case, Σ |s_k| t_k = c (eq. 15): t_k = √(τ_k/|s_k|) · c / Σ √(τ_j |s_j|) - tolerance ∝ |s|^−½.</summary>
    public static double[] WorstCase(IReadOnlyList<double> s, IReadOnlyList<double> tightest, double c)
    {
        double sum = s.Select((v, k) => Math.Sqrt(tightest[k] * Math.Abs(v))).Sum();
        return s.Select((v, k) => Math.Sqrt(tightest[k] / Math.Abs(v)) * c / sum).ToArray();
    }

    /// <summary>RSS, Σ s_k² t_k² = c² (eq. 17): t_k = ∛(τ_k/s_k²) · c / √(Σ s_j^⅔ τ_j^⅔) - tolerance ∝ s^−⅔.</summary>
    public static double[] Rss(IReadOnlyList<double> s, IReadOnlyList<double> tightest, double c)
    {
        double sum = s.Select((v, k) => Math.Pow(v * v, 1.0 / 3.0) * Math.Pow(tightest[k], 2.0 / 3.0)).Sum();
        return s.Select((v, k) => Math.Cbrt(tightest[k] / (v * v)) * c / Math.Sqrt(sum)).ToArray();
    }
}
