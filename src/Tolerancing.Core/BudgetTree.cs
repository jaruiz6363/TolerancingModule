namespace Tolerancing.Core;

/// <summary>How a node of a budget tree combines its children's spreads.</summary>
public enum Combination
{
    /// <summary>Independent: standard deviations add in quadrature (Sinclair's RSS node).</summary>
    Rss,

    /// <summary>Fully correlated, as one cause behind them all: standard deviations add (Sinclair's linear node).</summary>
    Linear,
}

/// <summary>
/// One node of an error budget (Sinclair 1990, Fig. 2): a share of the criterion's degradation held
/// by a group of tolerances - a kind of error, an element, a subassembly. A leaf names the
/// tolerances it holds; a branch holds its children's. <see cref="Allowance"/>, in the criterion's
/// units, is what the node is allowed.
/// </summary>
public sealed record BudgetNode(string Name, IReadOnlyList<BudgetNode> Children, IReadOnlyList<int> Terms,
                                Combination Rule = Combination.Rss)
{
    public double? Allowance { get; init; }

    public static BudgetNode Leaf(string name, IEnumerable<int> terms) => new(name, Array.Empty<BudgetNode>(), terms.ToArray());

    public static BudgetNode Branch(string name, IEnumerable<BudgetNode> children, Combination rule = Combination.Rss) =>
        new(name, children.Where(c => c.Children.Count > 0 || c.Terms.Count > 0).ToArray(), Array.Empty<int>(), rule);
}

/// <summary>
/// A node's share of the predicted degradation: the mean and standard deviation it adds to W², and
/// the criterion that would be on its own - √(mean + 2σ), a node's "equivalent" in waves, which
/// combine by root sum of squares to the parent's as Smith 1985's "RSS of class" does.
/// </summary>
public sealed record NodeResult(string Name, double Mean, double Sigma, double? Allowance, IReadOnlyList<NodeResult> Children)
{
    public double Equivalent => Math.Sqrt(Math.Max(0.0, Mean + 2.0 * Sigma));

    /// <summary>Whether the node is within its allowance; true when it has none.</summary>
    public bool WithinAllowance => Allowance is not double a || Equivalent <= a;
}

/// <summary>
/// Error budget trees over a tolerance set, and their evaluation from Koch's prediction. The root
/// always holds two more leaves: the cross terms between tolerances ("interactions") and
/// the biases' fixed shift of the mean, combined by the root's own rule (RSS in both trees here).
/// The design's own W² is not in the tree.
/// </summary>
public static class BudgetTree
{
    /// <summary>By kind of error, as Smith's change table groups them.</summary>
    public static BudgetNode ByKind(IReadOnlyList<ToleranceItem> items)
    {
        var kinds = new (string Name, Func<Tolerance, bool> Holds)[]
        {
            ("Surface form", t => t is PowerFringes or SurfaceIrregularity or SymmetricIrregularity or RadiusChange or ConicChange),
            ("Surface centering (wedge)", t => t is SurfaceTilt or SurfaceDecentre),
            ("Thickness and air spaces", t => t is ThicknessChange),
            ("Glass", t => t is IndexChange or AbbeChange),
            ("Element alignment", t => t is ElementDecentre or ElementTilt),
        };
        var leaves = kinds.Select(k => BudgetNode.Leaf(k.Name, Indices(items, k.Holds))).ToList();
        var placed = leaves.SelectMany(l => l.Terms).ToHashSet();
        leaves.Add(BudgetNode.Leaf("Other", Enumerable.Range(0, items.Count).Where(i => !placed.Contains(i))));
        return BudgetNode.Branch("All tolerances", leaves);
    }

    /// <summary>
    /// By element, each split into what the lens maker holds - its surfaces, thickness, wedge and
    /// glass - and what the mount holds - its decentre and tilt - with the air spaces on their own:
    /// the budget the optician and the mechanical designer are each handed (Sinclair; Hilbert).
    /// </summary>
    public static BudgetNode ByElement(NominalLens lens, IReadOnlyList<ToleranceItem> items)
    {
        var nodes = new List<BudgetNode>();
        var placed = new HashSet<int>();
        foreach (var (first, last) in DefaultTolerances.Elements(lens))
        {
            bool OnElement(int s) => s >= first && s <= last;
            var make = Indices(items, t => t switch
            {
                ElementDecentre or ElementTilt => false,
                ThicknessChange or IndexChange or AbbeChange => t.Surface >= first && t.Surface < last,   // its glass
                _ => OnElement(t.Surface),
            }).ToList();
            var mount = Indices(items, t => t is ElementDecentre e && e.Surface == first && e.Last == last
                                             || t is ElementTilt r && r.Surface == first && r.Last == last).ToList();
            placed.UnionWith(make);
            placed.UnionWith(mount);
            string name = first == last ? $"Mirror {first}" : $"Element {first}-{last}";
            nodes.Add(BudgetNode.Branch(name, new[] { BudgetNode.Leaf("Lens maker", make), BudgetNode.Leaf("Mount", mount) }));
        }
        var air = Indices(items, t => t is ThicknessChange).Where(i => !placed.Contains(i)).ToList();
        placed.UnionWith(air);
        nodes.Add(BudgetNode.Leaf("Air spaces", air));
        nodes.Add(BudgetNode.Leaf("Other", Enumerable.Range(0, items.Count).Where(i => !placed.Contains(i))));
        return BudgetNode.Branch("All tolerances", nodes);
    }

    public static NodeResult Evaluate(BudgetNode root, Prediction prediction)
    {
        var byTerm = new Dictionary<int, Contribution>();
        foreach (var c in prediction.Contributions)
            foreach (int t in c.Terms) byTerm[t] = c;
        var result = Evaluate(root, byTerm);
        var extra = new List<NodeResult>(result.Children)
        {
            new("Interactions (cross terms)", 0.0, Math.Sqrt(prediction.CrossVariance), null, Array.Empty<NodeResult>()),
            new("Biases", prediction.BiasShift, 0.0, null, Array.Empty<NodeResult>()),
        };
        return Combine(root.Name, extra, root.Rule, root.Allowance);
    }

    private static NodeResult Evaluate(BudgetNode node, IReadOnlyDictionary<int, Contribution> byTerm)
    {
        if (node.Children.Count == 0)
        {
            // An x-y pair drawn as one error is one contribution: count it once.
            var contributions = node.Terms.Where(byTerm.ContainsKey).Select(t => byTerm[t]).Distinct().ToList();
            return new NodeResult(node.Name, contributions.Sum(c => c.Mean),
                                  Math.Sqrt(Math.Max(0.0, contributions.Sum(c => c.Variance))), node.Allowance,
                                  Array.Empty<NodeResult>());
        }
        return Combine(node.Name, node.Children.Select(c => Evaluate(c, byTerm)).ToList(), node.Rule, node.Allowance);
    }

    private static NodeResult Combine(string name, IReadOnlyList<NodeResult> children, Combination rule, double? allowance)
    {
        double sigma = rule == Combination.Rss
            ? Math.Sqrt(children.Sum(c => c.Sigma * c.Sigma))
            : children.Sum(c => c.Sigma);
        return new NodeResult(name, children.Sum(c => c.Mean), sigma, allowance, children);
    }

    private static IEnumerable<int> Indices(IReadOnlyList<ToleranceItem> items, Func<Tolerance, bool> holds) =>
        Enumerable.Range(0, items.Count).Where(i => holds(items[i].Tolerance));
}
