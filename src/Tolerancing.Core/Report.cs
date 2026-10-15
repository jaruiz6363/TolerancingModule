using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tolerancing.Core;

/// <summary>
/// A tolerance analysis as JSON: the tolerance set with each item's share, Koch's prediction, the
/// compensator's travel, both budget trees, and the Monte Carlo check when there is one. It is
/// what the drawings and other programs read, so its names are kept stable.
/// </summary>
public static class Report
{
    public sealed record Item(string Code, string Surfaces, double Min, double Max, string? Distribution,
                              double? Tightest, double? Loosest, double MeanW2, double VarianceW2,
                              double? Pivot = null);

    public sealed record Spread(string Shape, double Nominal, double RootMeanSquare, double SigmaW2,
                                double Median, double Level841, double Level977, double? Limit, double? Yield,
                                double CrossVariance, double BiasShift)
    {
        /// <summary>For a criterion quoted otherwise than as W - an MTF - the same levels as quoted.</summary>
        public QuotedLevels? Quoted { get; init; }
    }

    /// <summary>
    /// Levels as a criterion quotes them, for one whose W is not what a specification states: for
    /// an MTF, W = √(1 − MTF), and these are MTFs. The level a fraction of lenses meets is then a
    /// floor that fraction stay above, and <c>Floor</c> says so.
    /// </summary>
    public sealed record QuotedLevels(bool Floor, double Nominal, double Median, double Level841, double Level977, double? Limit);

    public sealed record Travel(double Mean, double Sigma, double Needed954, double Needed997, double? Allowed);

    /// <summary>One of the compensator's other adjustments: its predicted move, its travel if limited, and the range the Monte Carlo moved it over, in its units.</summary>
    public sealed record AdjustmentTravel(string Label, string Units, double Mean, double Sigma, double Needed954, double Needed997,
                                          double? Allowed, double? MonteCarloMin, double? MonteCarloMax);

    public sealed record Node(string Name, double MeanW2, double SigmaW2, double Equivalent, double? Allowance,
                              IReadOnlyList<Node> Children);

    public sealed record Simulation(int Trials, int Seed, double RootMeanSquare, double SigmaW2, double Median,
                                    double Level841, double Level977, double? Yield, int Untraceable,
                                    double? CompensatorMin, double? CompensatorMax, int CompensatorAtLimit)
    {
        /// <summary>For a criterion quoted otherwise than as W, the traced levels as quoted.</summary>
        public QuotedLevels? Quoted { get; init; }
    }

    public sealed record Check(int Lenses, double RmsError, double MaxError, int Untraceable, bool Passes);

    /// <summary>A further criterion the set was judged by: its prediction at its own limit, and the Monte Carlo's yield there.</summary>
    public sealed record Requirement(string Criterion, string Units, double Limit, IReadOnlyList<double>? Reference,
                                     Spread Prediction, double? MonteCarloYield)
    {
        /// <summary>This criterion's model against the same lenses traced as the first's.</summary>
        public Check? ModelCheck { get; init; }
    }

    public sealed record Document(string Lens, string Criterion, int? CompensatorSurface, string Statistics,
                                  string? Method, IReadOnlyList<Item> Tolerances, Spread Prediction, Travel? Compensator,
                                  Node ByKind, Node ByElement, Simulation? MonteCarlo, bool? Feasible, double? Cost)
    {
        /// <summary>The quadratic model against lenses traced one by one (<see cref="Core.ModelCheck"/>).</summary>
        public Check? ModelCheck { get; init; }

        /// <summary>The units of the criterion and its limit: "waves", "mm".</summary>
        public string? Units { get; init; }

        /// <summary>For a departure criterion, the refocused nominal lens's value it is measured from (EFL, BFL, boresight x and y).</summary>
        public IReadOnlyList<double>? Reference { get; init; }

        /// <summary>The further criteria, each with its own limit.</summary>
        public IReadOnlyList<Requirement>? Also { get; init; }

        /// <summary>With further criteria, the fraction of lenses within every limit at once: on the models, and in the Monte Carlo.</summary>
        public double? JointYield { get; init; }

        public double? MonteCarloJointYield { get; init; }

        /// <summary>True when the allocation's yield was of lenses within every limit at once.</summary>
        public bool? Joint { get; init; }

        /// <summary>For a joint allocation, the yield each criterion was held to so that the joint yield is the goal's.</summary>
        public double? MarginalYield { get; init; }

        /// <summary>The compensator's adjustments beside the focus, each with its predicted move (<see cref="TravelPrediction.OfAdjustments"/>).</summary>
        public IReadOnlyList<AdjustmentTravel>? Adjustments { get; init; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string ToJson(Document document) => JsonSerializer.Serialize(document, Options);

    public static Document FromJson(string json) => JsonSerializer.Deserialize<Document>(json, Options)
                                                    ?? throw new InvalidDataException("not a tolerance report");

    /// <summary>Builds the report of a prediction for a tolerance set, allocated or not.</summary>
    public static Document Build(string lensName, NominalLens lens, string criterion, FocusCompensator? compensator,
                                 StatisticsModel statistics, IReadOnlyList<ToleranceItem> items, Prediction prediction,
                                 TravelPrediction? travel, double? limit = null, AllocationResult? allocation = null,
                                 MonteCarloResult? monteCarlo = null, int seed = 0, ModelCheck? check = null,
                                 ICriterion? quoted = null)
    {
        var byTerm = new Dictionary<int, Contribution>();
        foreach (var c in prediction.Contributions)
            foreach (int t in c.Terms) byTerm[t] = c;

        var rows = items.Select((item, i) =>
        {
            // A pair drawn as one error shares one contribution: each half reports half of it.
            var c = byTerm.TryGetValue(i, out var found) ? found : null;
            double share = c == null ? 0.0 : 1.0 / c.Terms.Count;
            return new Item(item.Tolerance.Code, ToleranceSpec.Surfaces(item.Tolerance), item.Min, item.Max,
                            item.Distribution == null ? null : ToleranceSpec.FormatDistribution(item.Distribution),
                            allocation?.Limits[i].Tightest, allocation?.Limits[i].Loosest,
                            (c?.Mean ?? 0.0) * share, (c?.Variance ?? 0.0) * share, ToleranceSpec.Pivot(item.Tolerance));
        }).ToList();

        var spread = SpreadOf(prediction, limit, quoted);

        Simulation? sim = null;
        if (monteCarlo != null)
        {
            bool comp = monteCarlo.Compensators.Count > 0;
            sim = new Simulation(monteCarlo.Trials, seed, Math.Sqrt(monteCarlo.MeanQ), Math.Sqrt(monteCarlo.VarianceQ),
                                 monteCarlo.Percentile(0.5), monteCarlo.Percentile(0.841), monteCarlo.Percentile(Prediction.Yield97),
                                 limit is double l2 ? monteCarlo.Yield(l2) : null, monteCarlo.Untraceable,
                                 comp ? monteCarlo.CompensatorRange.Min : null, comp ? monteCarlo.CompensatorRange.Max : null,
                                 monteCarlo.CompensatorAtLimit)
            {
                Quoted = QuotedOf(quoted, double.NaN, monteCarlo.Percentile(0.5), monteCarlo.Percentile(0.841),
                                  monteCarlo.Percentile(Prediction.Yield97), limit),
            };
        }

        return new Document(
            lensName, criterion, compensator?.Surface, Describe(statistics),
            allocation?.Method.ToString(), rows, spread,
            travel == null ? null : new Travel(travel.Mean, travel.Sigma, travel.Needed(0.9545), travel.Needed(0.9973), compensator?.Travel),
            Convert(BudgetTree.Evaluate(BudgetTree.ByKind(items), prediction)),
            Convert(BudgetTree.Evaluate(BudgetTree.ByElement(lens, items), prediction)),
            sim, allocation?.Feasible, allocation?.Cost)
        {
            ModelCheck = check == null ? null : CheckOf(check),
        };
    }

    /// <summary>
    /// A prediction's spread, and its yield at the limit (on W) when there is one; with a criterion
    /// quoted otherwise than as W, its levels as quoted too.
    /// </summary>
    public static Spread SpreadOf(Prediction prediction, double? limit, ICriterion? criterion = null) =>
        new(prediction.Shape.ToString().ToLowerInvariant(), Math.Sqrt(prediction.Q0),
            // An MTF is drawn on its spot synthesis, and its moments are the sampled lenses'.
            criterion is { IsFloor: true } ? Math.Sqrt(Math.Max(0.0, prediction.SampledMoments.Mean)) : prediction.RootMeanSquare,
            criterion is { IsFloor: true } ? Math.Sqrt(prediction.SampledMoments.Variance) : prediction.SigmaQ, prediction.Quantile(0.5),
            prediction.Quantile(0.841), prediction.ProbableWorst, limit,
            limit is double l ? prediction.Yield(l) : null, prediction.CrossVariance, prediction.BiasShift)
        {
            Quoted = QuotedOf(criterion, Math.Sqrt(prediction.Q0), prediction.Quantile(0.5), prediction.Quantile(0.841), prediction.ProbableWorst, limit),
        };

    private static QuotedLevels? QuotedOf(ICriterion? criterion, double nominal, double median, double level841, double level977, double? limit) =>
        criterion is not { IsFloor: true } c ? null
            : new QuotedLevels(true, c.Quoted(nominal), c.Quoted(median), c.Quoted(level841), c.Quoted(level977),
                               limit is double l ? c.Quoted(l) : null);

    /// <summary>A model check as the report gives it.</summary>
    public static Check CheckOf(Core.ModelCheck check) =>
        new(check.Lenses.Count, check.RmsError, check.MaxError, check.Untraceable, check.Passes);

    public static string Describe(StatisticsModel statistics) =>
        $"{statistics.Axis}{(statistics.Polar == null ? ", x and y independent" : $"; x-y pairs as one error, {statistics.Polar}")}";

    private static Node Convert(NodeResult r) =>
        new(r.Name, r.Mean, r.Sigma, r.Equivalent, r.Allowance, r.Children.Select(Convert).ToList());
}
