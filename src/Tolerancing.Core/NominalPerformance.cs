using WavefrontErrorCalculator.Core;

namespace Tolerancing.Core;

/// <summary>
/// The wavefront error of one field at the primary wavelength, in waves. <see cref="Strehl"/> is
/// the Maréchal estimate, null beyond λ/14 RMS where it no longer holds.
/// </summary>
public sealed record FieldWavefront(int Field, double FieldValue, double Rms, double PeakToValley,
                                    double? Strehl, int Vignetted)
{
    /// <summary>Why the field could not be evaluated, or null.</summary>
    public string? Failure { get; init; }
}

/// <summary>
/// The as-designed performance every tolerance is measured against: first-order data from
/// AberrationCalculator's paraxial trace, and the RMS wavefront error of each field from
/// WavefrontErrorCalculator.
/// </summary>
public sealed record NominalPerformance(double Efl, double Bfl, double FNumber, int PrimaryWavelength,
                                        double WavelengthUm, IReadOnlyList<FieldWavefront> Fields)
{
    /// <summary>The sampling used when none is given: the criterion's, Gauss quadrature, so the two agree.</summary>
    public static Sampling DefaultSampling { get; } = new RmsWavefrontCriterion().Sampling;

    /// <summary>
    /// Each field at the primary wavelength. A field no ray of which can be traced - aiming that
    /// cannot find the stop at a wide angle, a pencil the apertures cut off - is reported with its
    /// reason rather than ending the evaluation.
    /// </summary>
    public static NominalPerformance Evaluate(LensModel lens, WavefrontOptions? options = null,
                                              Sampling? sampling = null)
    {
        ArgumentNullException.ThrowIfNull(lens);
        sampling ??= DefaultSampling;
        options ??= new RmsWavefrontCriterion { Sampling = sampling }.EffectiveOptions;
        int w = lens.PrimaryWavelength;
        var fields = new List<FieldWavefront>();
        for (int f = 0; f < lens.System.Fields.Count; f++)
        {
            try
            {
                var s = WavefrontCalculator.Compute(lens, f, w, options, sampling).Statistics;
                fields.Add(new FieldWavefront(f, lens.System.Fields[f].Y, s.RmsStandardDeviation,
                                              s.PeakToValley, s.StrehlIsValid ? s.StrehlMarechal : null,
                                              s.Vignetted));
            }
            catch (Exception e) when (e is InvalidOperationException or ArithmeticException)
            {
                fields.Add(new FieldWavefront(f, lens.System.Fields[f].Y, double.NaN, double.NaN, null, 0) { Failure = e.Message });
            }
        }
        var p = lens.Paraxial;
        return new NominalPerformance(p.Efl, p.Bfl, p.FNumber, w, lens.WavelengthUm(w), fields);
    }
}
