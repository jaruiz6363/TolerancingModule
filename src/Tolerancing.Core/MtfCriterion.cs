using WavefrontErrorCalculator.Core;

namespace Tolerancing.Core;

/// <summary>Which direction an MTF is measured in.</summary>
public enum MtfOrientation
{
    /// <summary>Lines running radially: modulation across them is in x.</summary>
    Sagittal,

    /// <summary>Lines running tangentially: modulation across them is in y, the meridian.</summary>
    Tangential,
}

/// <summary>
/// The geometric MTF at one field, frequency and orientation (<see cref="GeometricMtf"/>, Kidger's
/// summation) - the geometric MTF alone, with no diffraction factor. Larger is better: a limit is a
/// floor on it.
///
/// <para><b>As a criterion.</b> W = √(1 − MTF), so W² = 1 − MTF: smaller is better and never
/// negative, and the sensitivity run, the budget and the allocation's steering read its quadratic
/// terms as MTF drops. Only what is quoted is converted: <see cref="Quoted"/> gives the MTF,
/// <see cref="FromQuoted"/> turns a floor into a limit on W.</para>
///
/// <para><b>Statistics.</b> An MTF saturates, which no quadratic follows, so its spread is drawn
/// from a <see cref="SpotSynthesis"/> instead: the spot moved linearly by every error, and Kidger's
/// sum of it (<see cref="QuadraticModel.Synthesis"/>).</para>
///
/// <para><b>Focus.</b> A focus compensator refocuses for the sharpest image by <see cref="Focus"/>,
/// as the lens would be focused in use, and the MTF is measured there. The rays are traced as that
/// RMS wavefront traces them - aimed at the real stop or not, stopped by the same apertures - so a
/// vignetted ray is missing from both.</para>
///
/// <para><b>Where it holds.</b> A geometric MTF needs aberrations well above the diffraction limit;
/// close to it the geometric part tends to 1 and the criterion stops seeing the errors. There the
/// RMS wavefront is the criterion to use.</para>
/// </summary>
public sealed record MtfCriterion : IFocusedCriterion
{
    public RmsWavefrontCriterion Focus { get; init; } = new();

    /// <summary>The field, in the lens's field units: one of the fields the lens lists.</summary>
    public double Field { get; init; }

    /// <summary>The spatial frequency, in cycles per lens unit at the image (cycles/mm).</summary>
    public double Frequency { get; init; }

    public MtfOrientation Orientation { get; init; } = MtfOrientation.Tangential;

    /// <summary>Rings of Kidger's pattern (<see cref="MtfOptions.Rings"/>, whose default this is).</summary>
    public int Rings { get; init; } = new MtfOptions().Rings;

    /// <summary>The index of <see cref="Field"/> among the lens's fields.</summary>
    public int FieldIndex(LensModel lens)
    {
        for (int f = 0; f < lens.System.Fields.Count; f++)
            if (Math.Abs(lens.System.Fields[f].Y - Field) < 1e-9 * Math.Max(1.0, Math.Abs(Field)) && Math.Abs(lens.System.Fields[f].X) < 1e-12) return f;
        throw new ArgumentException($"{Name}: the lens has no field {Field:G6}; its fields are " +
                                    string.Join(", ", lens.System.Fields.Select(x => x.Y.ToString("G6", System.Globalization.CultureInfo.InvariantCulture))));
    }

    public string Name =>
        $"MTF {(Orientation == MtfOrientation.Tangential ? "T" : "S")} at {Frequency:G4} cy/mm, field {Field:G4}" +
        (Focus.Polychromatic ? "" : " (primary λ)");

    public string Units => "";

    public bool IsFloor => true;

    public double Quoted(double w) => 1.0 - w * w;

    public double FromQuoted(double value) => Math.Sqrt(Math.Max(0.0, 1.0 - value));

    public double Evaluate(LensModel lens)
    {
        double m = Mtf(lens);
        return double.IsFinite(m) ? Math.Sqrt(Math.Max(0.0, 1.0 - m)) : double.PositiveInfinity;
    }

    /// <summary>The MTF itself; NaN when no ray reaches the image.</summary>
    public double Mtf(LensModel lens)
    {
        var result = Compute(lens);
        return Orientation == MtfOrientation.Tangential ? result.Mtf[0].Tangential : result.Mtf[0].Sagittal;
    }

    /// <summary>The whole calculation, sagittal and tangential, with the perfect lens's MTF beside it.</summary>
    public MtfResult Compute(LensModel lens)
    {
        var options = new MtfOptions { Rings = Rings, Wavelength = Focus.Polychromatic ? null : lens.PrimaryWavelength, DiffractionLimited = false };
        try
        {
            return GeometricMtf.Compute(lens, FieldIndex(lens), new[] { Frequency }, ImagePlane.For(lens, FieldIndex(lens), Focus.EffectiveOptions), options);
        }
        catch (Exception e) when (e is InvalidOperationException or ArithmeticException)
        {
            var none = new[] { new GeometricMtf.Modulation(double.NaN, double.NaN) };
            return new MtfResult(FieldIndex(lens), new[] { Frequency }, none, none, none, double.NaN, double.NaN, 0.0, 0, 0);
        }
    }

    /// <summary>The wavelengths summed: every one, or the primary alone.</summary>
    private int[] Wavelengths(LensModel lens) => Focus.Polychromatic
        ? Enumerable.Range(0, lens.System.Wavelengths.Count).ToArray()
        : new[] { lens.PrimaryWavelength };

    /// <summary>
    /// Where each ray of Kidger's pattern lands, across the lines - x for sagittal, y for
    /// tangential - wavelength by wavelength; NaN for a ray that does not arrive. The spot that
    /// <see cref="SpotSynthesis"/> moves.
    /// </summary>
    public double[] Intercepts(LensModel lens)
    {
        var pupil = GeometricMtf.Pupil(Rings);
        int field = FieldIndex(lens);
        var options = ImagePlane.For(lens, field, Focus.EffectiveOptions);
        return Wavelengths(lens)
            .SelectMany(w => GeometricMtf.Trace(lens, field, w, options, pupil))
            .Select(ray => !ray.Ok ? double.NaN : Orientation == MtfOrientation.Tangential ? ray.ImagePoint.Y : ray.ImagePoint.X)
            .ToArray();
    }

    /// <summary>Each intercept's weight in the sum: its wavelength's share, every ray of a wavelength alike.</summary>
    public double[] InterceptWeights(LensModel lens)
    {
        int rays = GeometricMtf.Pupil(Rings).Length;
        var waves = Wavelengths(lens);
        double total = waves.Sum(w => lens.System.Wavelengths[w].Weight);
        return waves.SelectMany(w => Enumerable.Repeat(lens.System.Wavelengths[w].Weight / total, rays)).ToArray();
    }

    /// <summary>
    /// "mtf:field:frequency:s|t" as the command line writes it, focused by <paramref name="rms"/>:
    /// mtf:14:30:t is the tangential MTF at 30 cycles/mm, 14 field units off axis.
    /// </summary>
    public static MtfCriterion Parse(string text, RmsWavefrontCriterion rms)
    {
        var parts = text.Split(':');
        if (parts.Length != 4 || !parts[0].Equals("mtf", StringComparison.OrdinalIgnoreCase)
            || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double field)
            || !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double frequency)
            || !(frequency > 0.0))
            throw new ArgumentException($"{text}: write an MTF as mtf:field:frequency:s|t, as mtf:14:30:t");
        var orientation = parts[3].ToLowerInvariant() switch
        {
            "t" or "tangential" => MtfOrientation.Tangential,
            "s" or "sagittal" => MtfOrientation.Sagittal,
            _ => throw new ArgumentException($"{text}: the orientation is s (sagittal) or t (tangential)"),
        };
        return new MtfCriterion { Focus = rms, Field = field, Frequency = frequency, Orientation = orientation };
    }
}

/// <summary>
/// An MTF for any set of errors without tracing: Starke and Wise's linear synthesis (Appl. Opt. 19,
/// 1768, 1980) applied to the spot. Each ray's landing point across the lines moves linearly with
/// each error, y_k = y₀_k + Σ g_ik x_i, with g_i its slope between tolerance i's two ends in the
/// sensitivity run - refocused, so the refocus is in the slopes - and Kidger's sum of that spot is
/// the MTF.
///
/// <para><b>Why.</b> An MTF saturates: once other errors have spread the spot, a further error
/// changes it less. No quadratic in the MTF can show that - with independent errors a quadratic's
/// variance is at least the sum of each error's alone - and on a Cooke triplet at the
/// precision grade the sum was 1.7 times what traced lenses gave together. The rays are what is
/// nearly linear in the errors, and the sum's exponentials make the saturation themselves; on four
/// lenses the synthesis put the 97.7 % floor within 0.035 of 400 traced lenses where quadratics in
/// the MTF or its logarithm missed by up to 0.4.</para>
///
/// <para>A ray that does not arrive at nominal is left out; one stopped at either end of a
/// tolerance keeps its nominal place for that tolerance, as a wavefront sample does
/// (<see cref="QuadraticModel.WavefrontSlopes"/>).</para>
/// </summary>
/// <param name="Nominal">The refocused nominal lens's intercepts (<see cref="MtfCriterion.Intercepts"/>).</param>
/// <param name="Slopes">Each tolerance's slope of every intercept, per unit of the tolerance.</param>
/// <param name="Weights">Each intercept's weight (<see cref="MtfCriterion.InterceptWeights"/>).</param>
public sealed record SpotSynthesis(IReadOnlyList<double> Nominal, IReadOnlyList<double[]> Slopes, IReadOnlyList<double> Weights,
                                   double Frequency)
{
    /// <summary>The MTF of the lens with errors <paramref name="x"/>, in each tolerance's units.</summary>
    public double Mtf(IReadOnlyList<double> x)
    {
        var y = Nominal.ToArray();
        for (int i = 0; i < Slopes.Count; i++)
        {
            double xi = x[i];
            if (xi == 0.0) continue;
            var g = Slopes[i];
            for (int k = 0; k < y.Length; k++) y[k] += g[k] * xi;
        }
        double re = 0.0, im = 0.0, total = 0.0, phase = -2.0 * Math.PI * Frequency;
        for (int k = 0; k < y.Length; k++)
        {
            if (double.IsNaN(y[k])) continue;
            re += Weights[k] * Math.Cos(phase * y[k]);
            im += Weights[k] * Math.Sin(phase * y[k]);
            total += Weights[k];
        }
        return total > 0.0 ? Math.Sqrt(re * re + im * im) / total : double.NaN;
    }

    /// <summary>The criterion's W² = 1 − MTF for the errors <paramref name="x"/>.</summary>
    public double Q(IReadOnlyList<double> x) => 1.0 - Mtf(x);

    /// <summary>
    /// The synthesis from a sensitivity run whose rows carry intercepts: null if they do not.
    /// </summary>
    public static SpotSynthesis? From(SensitivityTable table, IReadOnlyList<double> weights, double frequency)
    {
        var nominal = table.NominalDeviation;
        if (nominal == null || weights.Count != nominal.Count) return null;
        var slopes = new double[table.Rows.Count][];
        for (int i = 0; i < slopes.Length; i++)
        {
            var r = table.Rows[i];
            if (r.MinDeviation is not { } lo || r.MaxDeviation is not { } hi || lo.Count != nominal.Count || hi.Count != nominal.Count) return null;
            var g = new double[nominal.Count];
            for (int k = 0; k < g.Length; k++)
                g[k] = double.IsNaN(nominal[k]) || double.IsNaN(lo[k]) || double.IsNaN(hi[k]) ? 0.0 : (hi[k] - lo[k]) / (r.Item.Max - r.Item.Min);
            slopes[i] = g;
        }
        return new SpotSynthesis(nominal, slopes, weights, frequency);
    }
}
