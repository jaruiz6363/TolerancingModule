namespace Tolerancing.Core;

/// <summary>A further criterion a tolerance set must meet, and its limit in the criterion's units.</summary>
public sealed record CriterionLimit(ICriterion Criterion, double Limit);

/// <summary>An allocated budget, and the models of the lens it was last allocated on: the first criterion's, and each further one's.</summary>
public sealed record BudgetResult(AllocationResult Allocation, QuadraticModel Model, int Refits)
{
    public IReadOnlyList<QuadraticModel> AlsoModels { get; init; } = Array.Empty<QuadraticModel>();
}

/// <summary>
/// From a trial tolerance set to one that meets a goal - Smith 1985's procedure, with Koch's
/// statistics in place of his RSS: a sensitivity run, a quadratic model fitted to it, an allocation
/// on the model; then the sensitivity run again at the allocated values and the allocation redone on
/// that model, since the first was fitted where the tolerances were, not where they end up
/// (Heshmaty-Manesh 1986's correction for non-linearity). Each further criterion has a model of its
/// own, refitted with the first, from the same traces: each lens is refocused once and every
/// criterion measured on it.
/// </summary>
public static class Budget
{
    public static BudgetResult Allocate(NominalLens lens, IReadOnlyList<ToleranceItem> trial, IReadOnlyList<ShopLimits> limits,
                                        ICriterion criterion, FocusCompensator? compensator,
                                        AllocationGoal goal, StatisticsModel statistics, AllocationMethod method,
                                        CrossTerms cross = CrossTerms.Wavefront, int refits = 1, bool round = true,
                                        IReadOnlyList<CriterionLimit>? also = null)
    {
        ArgumentNullException.ThrowIfNull(trial);
        also ??= Array.Empty<CriterionLimit>();
        var items = trial;
        AllocationResult? result = null;
        QuadraticModel? model = null;
        var alsoModels = new List<QuadraticModel>();
        for (int pass = 0; pass <= refits; pass++)
        {
            var models = QuadraticModel.Build(lens, items, also.Select(a => a.Criterion).Prepend(criterion).ToList(), compensator, cross);
            model = models[0];
            alsoModels = models.Skip(1).ToList();
            result = Allocator.Allocate(model, limits, goal, statistics, method, round,
                                        alsoModels.Select((m, c) => new Requirement(m, also[c].Limit)).ToList());
            items = result.Items;
        }
        return new BudgetResult(result!, model!, refits) { AlsoModels = alsoModels };
    }
}
