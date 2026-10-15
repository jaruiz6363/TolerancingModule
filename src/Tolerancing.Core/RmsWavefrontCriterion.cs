using WavefrontErrorCalculator.Core;

namespace Tolerancing.Core;

/// <summary>
/// The performance a tolerance is judged by: RMS wavefront error in waves, from
/// WavefrontErrorCalculator, combined over fields and wavelengths as
/// √(Σ w_f w_λ W²_rms / Σ w_f w_λ) with the lens's own weights.
///
/// <para>A lens that cannot be traced - a ray that misses a surface, a field that does not
/// arrive - scores +∞, so a compensator moves away from it and a tolerance that breaks the lens
/// shows as the worst there is rather than as a crash.</para>
/// </summary>
public sealed class RmsWavefrontCriterion : ICriterion
{
    public WavefrontOptions Options { get; init; } = new();

    /// <summary>
    /// Gauss quadrature over the pupil by default (Forbes 1988): exact for a wavefront polynomial
    /// up to ρ³⁰ and an azimuthal order of 23, with 192 rays. Rings or a grid weighted ray by ray
    /// count the rim's rays as heavily as the centre's and overstate the RMS of a wavefront that
    /// grows towards the edge - by 15 % on the test triplet's axis.
    /// </summary>
    public Sampling Sampling { get; init; } = new Sampling.GaussQuadrature(8, 24);

    /// <summary>The options the wavefront is computed with: quadrature samples are weighted by their rule.</summary>
    public WavefrontOptions EffectiveOptions =>
        Sampling is Sampling.GaussQuadrature ? Options with { Weighting = Weighting.Quadrature } : Options;

    /// <summary>Every wavelength of the lens, each referred as <see cref="Options"/> says; otherwise the primary alone.</summary>
    public bool Polychromatic { get; init; } = true;

    public string Name => Polychromatic ? "RMS wavefront (waves, polychromatic)" : "RMS wavefront (waves, primary λ)";

    public string Units => "waves";

    /// <summary>
    /// Fields left out of the criterion: those the nominal lens cannot be traced at, which would
    /// otherwise make every evaluation +∞ (<see cref="WithoutUntraceableFields"/>).
    /// </summary>
    public IReadOnlySet<int> ExcludedFields { get; init; } = new HashSet<int>();

    /// <summary>
    /// This criterion with every field the nominal lens cannot be traced at left out - a wide-angle
    /// field no ray of which reaches the stop, a pencil the apertures cut off - and those fields.
    /// </summary>
    public RmsWavefrontCriterion WithoutUntraceableFields(LensModel lens, out List<int> excluded)
    {
        excluded = new List<int>();
        var sites = new FieldSites(lens);
        for (int f = 0; f < lens.System.Fields.Count; f++)
        {
            try
            {
                var (model, field) = sites[f];
                var s = WavefrontCalculator.Compute(model, field, model.PrimaryWavelength, EffectiveOptions, Sampling).Statistics;
                if (s.Count == 0 || !double.IsFinite(s.RmsStandardDeviation)) excluded.Add(f);
            }
            catch (Exception e) when (e is InvalidOperationException or ArithmeticException)
            {
                excluded.Add(f);
            }
        }
        if (excluded.Count == lens.System.Fields.Count)
            throw new InvalidOperationException("the lens cannot be traced at any of its fields");
        return excluded.Count == 0 ? this : new RmsWavefrontCriterion
        {
            Options = Options, Sampling = Sampling, Polychromatic = Polychromatic, ExcludedFields = excluded.ToHashSet(),
        };
    }

    /// <summary>
    /// The criterion as a vector v with W = |v|: every pupil sample of every field and wavelength,
    /// less its field and wavelength's weighted mean, times √(w_f w_λ a_k / Σ w_f w_λ Σ a), a_k the
    /// sample's weight. Each W²_rms is a weighted variance, so Σ v² over the samples that arrive is
    /// W² exactly. A vignetted sample is NaN: two lenses' vectors compare only where the same samples
    /// arrive. Null when a field cannot be traced, or when the samples are weighted by their exit
    /// area, which changes with the lens.
    /// </summary>
    public double[]? Vector(LensModel lens)
    {
        if (EffectiveOptions.Weighting == Weighting.ExitArea || EffectiveOptions.IncludeVignettedAsZero) return null;
        var system = lens.System;
        var waves = Polychromatic ? Enumerable.Range(0, system.Wavelengths.Count) : new[] { lens.PrimaryWavelength };
        var parts = new List<(double Weight, IReadOnlyList<WavefrontSample> Samples)>();
        var sites = new FieldSites(lens);
        try
        {
            for (int f = 0; f < system.Fields.Count; f++)
            {
                var (model, field) = sites[f];
                var options = ExcludedFields.Contains(f) ? EffectiveOptions : ImagePlane.For(model, field, EffectiveOptions);
                foreach (int w in ExcludedFields.Contains(f) ? Array.Empty<int>() : waves)
                {
                    double weight = system.Fields[f].Weight * system.Wavelengths[w].Weight;
                    if (weight <= 0.0) continue;
                    var samples = WavefrontCalculator.Compute(model, field, w, options, Sampling).Samples;
                    if (samples.All(s => s.Vignetted) || samples.Any(s => !s.Vignetted && !double.IsFinite(s.W))) return null;
                    parts.Add((weight, samples));
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArithmeticException)
        {
            return null;
        }
        double weights = parts.Sum(p => p.Weight);
        if (!(weights > 0.0)) return null;
        var v = new List<double>();
        foreach (var (weight, samples) in parts)
        {
            var arrive = samples.Where(s => !s.Vignetted).ToList();
            double total = arrive.Sum(s => s.Weight);
            // Less the mean whatever the preset's RMS definition, as Evaluate takes the standard deviation.
            double mean = arrive.Sum(s => s.Weight * s.W) / total;
            foreach (var s in samples)
                v.Add(s.Vignetted ? double.NaN : Math.Sqrt(weight / weights * s.Weight / total) * (s.W - mean));
        }
        return v.ToArray();
    }

    public double Evaluate(LensModel lens)
    {
        var system = lens.System;
        var waves = Polychromatic
            ? Enumerable.Range(0, system.Wavelengths.Count)
            : new[] { lens.PrimaryWavelength };
        double sum = 0.0, weights = 0.0;
        var sites = new FieldSites(lens);
        try
        {
            for (int f = 0; f < system.Fields.Count; f++)
            {
                var (model, field) = sites[f];
                var options = ExcludedFields.Contains(f) ? EffectiveOptions : ImagePlane.For(model, field, EffectiveOptions);
                foreach (int w in ExcludedFields.Contains(f) ? Array.Empty<int>() : waves)
                {
                    double weight = system.Fields[f].Weight * system.Wavelengths[w].Weight;
                    if (weight <= 0.0) continue;
                    var s = WavefrontCalculator.Compute(model, field, w, options, Sampling).Statistics;
                    if (s.Count == 0 || !double.IsFinite(s.RmsStandardDeviation)) return double.PositiveInfinity;
                    sum += weight * s.RmsStandardDeviation * s.RmsStandardDeviation;
                    weights += weight;
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArithmeticException)
        {
            return double.PositiveInfinity;
        }
        return weights > 0.0 ? Math.Sqrt(sum / weights) : double.PositiveInfinity;
    }
}

/// <summary>
/// Where each field of a lens is traced: on the lens itself, or for a field on the x axis on the
/// lens turned a quarter turn (<see cref="QuarterTurn"/>), built once, when first needed.
/// </summary>
public sealed class FieldSites
{
    private readonly LensModel _lens;
    private readonly List<int> _x;
    private LensModel? _turned;

    public FieldSites(LensModel lens)
    {
        _lens = lens;
        _x = Enumerable.Range(0, lens.System.Fields.Count).Where(f => Math.Abs(lens.System.Fields[f].X) > 1e-12).ToList();
    }

    /// <summary>The model field <paramref name="f"/> of the lens is traced on, and its index there.</summary>
    public (LensModel Model, int Field) this[int f]
    {
        get
        {
            int k = _x.IndexOf(f);
            if (k < 0) return (_lens, f);
            _turned ??= QuarterTurn.Turn(_lens, _x);
            return (_turned, k);
        }
    }
}
