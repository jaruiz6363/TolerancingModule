using WavefrontErrorCalculator.Core;

namespace Tolerancing.Core;

/// <summary>
/// What a lens is judged by: a value W ≥ 0, smaller better, +∞ for a lens that cannot be traced.
/// The statistics are of W² (<see cref="QuadraticModel"/>).
/// </summary>
public interface ICriterion
{
    string Name { get; }

    /// <summary>The units of W and of a limit on it: "waves", "mm".</summary>
    string Units { get; }

    double Evaluate(LensModel lens);

    /// <summary>
    /// The value as it is quoted, and a limit is given, for a W: W itself, or for an
    /// <see cref="MtfCriterion"/> the MTF, 1 − W².
    /// </summary>
    double Quoted(double w) => w;

    /// <summary>W for a quoted value: the inverse of <see cref="Quoted"/>.</summary>
    double FromQuoted(double value) => value;

    /// <summary>Whether a limit on the quoted value is a floor, larger being better - an MTF - rather than a ceiling.</summary>
    bool IsFloor => false;
}

/// <summary>A criterion measured on the lens as a compensator leaves it after refocusing by <see cref="Focus"/>, not by itself.</summary>
public interface IFocusedCriterion : ICriterion
{
    /// <summary>The image quality a focus compensator refocuses by.</summary>
    RmsWavefrontCriterion Focus { get; }
}

/// <summary>
/// A criterion that is a departure from the nominal lens - a focal length, a back focus, where the
/// axial image falls - measured as W = |d|, the length of the departure d. Milby 2009 and Smith 1985
/// list these beside image quality: a lens can be sharp and still have the wrong focal length.
///
/// <para><b>Focus.</b> A focus compensator still refocuses for the sharpest image, by
/// <see cref="Focus"/>, as the lens would be focused in use; the departure is measured where that
/// leaves it. Refocusing by the departure itself would only hide it.</para>
///
/// <para><b>Reference.</b> The departure is from the nominal lens refocused the same way
/// (<see cref="Referenced"/>), so a file not saved at best focus does not count as an error.</para>
///
/// <para><b>Cross terms.</b> d is linear in small errors, so W² = |d|² has the cross terms
/// 2 g_i·g_j, with g_i the slope of d in error i - known from the sensitivity run's two ends,
/// for every pair, without tracing pairs (<see cref="QuadraticModel.Fit"/>).</para>
/// </summary>
public abstract record DeviationCriterion : IFocusedCriterion
{
    /// <summary>The image quality a focus compensator refocuses by.</summary>
    public RmsWavefrontCriterion Focus { get; init; } = new();

    /// <summary>The refocused nominal lens's values, which departures are measured from; empty until <see cref="Referenced"/>.</summary>
    public IReadOnlyList<double> Reference { get; init; } = Array.Empty<double>();

    public abstract string Name { get; }

    public virtual string Units => "mm";

    /// <summary>The quantity itself for this lens, one value per direction; NaN or ∞ where it cannot be found.</summary>
    public abstract double[] Measure(LensModel lens);

    /// <summary>The departure from <see cref="Reference"/>, one value per direction.</summary>
    public double[] Deviation(LensModel lens)
    {
        if (Reference.Count == 0) throw new InvalidOperationException($"{Name}: no reference; call Referenced with the nominal lens first");
        double[] value;
        try { value = Measure(lens); }
        catch (Exception e) when (e is InvalidOperationException or ArithmeticException)
        {
            value = Reference.Select(_ => double.NaN).ToArray();
        }
        return value.Select((v, k) => v - Reference[k]).ToArray();
    }

    public double Evaluate(LensModel lens) => Length(Deviation(lens));

    /// <summary>|d|, or +∞ when any part of it could not be found.</summary>
    public static double Length(IReadOnlyList<double> d) =>
        d.All(double.IsFinite) ? Math.Sqrt(d.Sum(v => v * v)) : double.PositiveInfinity;

    /// <summary>
    /// This criterion measured from <paramref name="lens"/> as <paramref name="compensator"/>, free of
    /// any travel limit, refocuses it by <see cref="Focus"/>.
    /// </summary>
    public DeviationCriterion Referenced(NominalLens lens, FocusCompensator? compensator)
    {
        var perturbed = lens.Perturb();
        if (compensator != null) compensator.Free().Solve(perturbed, Focus);
        var value = Measure(perturbed.Build());
        if (!value.All(double.IsFinite))
            throw new InvalidOperationException($"{Name}: the nominal lens has none ({string.Join(", ", value)})");
        return this with { Reference = value };
    }
}

/// <summary>
/// The paraxial effective focal length's change, in lens units. Paraxial, so a decentre or tilt -
/// which changes the focal length only to second order - shows no effect.
/// </summary>
public sealed record EflCriterion : DeviationCriterion
{
    public override string Name => "EFL change (mm)";

    public override double[] Measure(LensModel lens) => new[] { (double)lens.Paraxial.Efl };
}

/// <summary>
/// The paraxial back focal length's change, from the last surface to the paraxial focus, in lens
/// units. With the back focus as the compensator this is how far it must refocus, so it is
/// meant for a lens with no focus adjustment, or one focused by another air space.
/// </summary>
public sealed record BflCriterion : DeviationCriterion
{
    public override string Name => "BFL change (mm)";

    public override double[] Measure(LensModel lens) => new[] { (double)lens.Paraxial.Bfl };
}

/// <summary>
/// Boresight: where the image of the axial object point falls - the chief ray of the on-axis
/// field, aimed at the stop's centre, at the image plane, primary wavelength. A decentred or
/// tilted element moves it; it is the line of sight's error.
///
/// <para>By default it is an angle in degrees, x and y: the line of sight's turn in object space,
/// the image's shift divided by the image height per unit of field angle, the paraxial chief
/// ray's height at the image over its slope in object space - the focal length, for an object at
/// infinity. <see cref="InImagePlane"/> gives the shift itself, in lens units.</para>
/// </summary>
public sealed record BoresightCriterion : DeviationCriterion
{
    /// <summary>The shift at the image in lens units, in place of the line of sight's angle.</summary>
    public bool InImagePlane { get; init; }

    public override string Name => InImagePlane ? "boresight (mm at the image)" : "boresight (degrees, line of sight)";

    public override string Units => InImagePlane ? "mm" : "deg";

    /// <summary>
    /// The image height per unit of field angle (its tangent): the paraxial chief ray's height at
    /// the image over its slope in object space. For an object at infinity, the focal length.
    /// </summary>
    public static double ImageHeightPerAngle(LensModel lens)
    {
        // The chief ray's height where the marginal ray crosses the axis - the paraxial image, not
        // the image plane, which a defocus would move it off.
        var p = lens.Paraxial;
        double y = (double)p.Y[^1], u = (double)p.U[^1], yb = (double)p.Ybar[^1], ub = (double)p.Ubar[^1];
        double slope = (double)p.Ubar[0], height = u != 0.0 ? yb - ub * y / u : yb;
        if (slope == 0.0 || !double.IsFinite(height / slope))
            throw new InvalidOperationException("boresight: the chief ray has no slope in object space, so no line-of-sight angle; use boresight-mm");
        return height / slope;
    }

    /// <summary>The on-axis field of <paramref name="lens"/>.</summary>
    public static int AxialField(LensModel lens)
    {
        for (int f = 0; f < lens.System.Fields.Count; f++)
            if (Math.Abs(lens.System.Fields[f].Y) < 1e-12 && Math.Abs(lens.System.Fields[f].X) < 1e-12) return f;
        throw new ArgumentException("boresight needs an on-axis field (a field of height 0)");
    }

    public override double[] Measure(LensModel lens)
    {
        // The chief ray does not depend on the pupil sampling: the fewest rays the rule allows.
        int axial = AxialField(lens);
        var g = WavefrontCalculator.Compute(lens, axial, lens.PrimaryWavelength, ImagePlane.For(lens, axial, Focus.EffectiveOptions),
                                            new Sampling.GaussQuadrature(1, 1)).Geometry;
        if (InImagePlane) return new[] { g.ChiefImagePoint.X, g.ChiefImagePoint.Y };
        double toDegrees = 180.0 / Math.PI / Math.Abs(ImageHeightPerAngle(lens));
        return new[] { g.ChiefImagePoint.X * toDegrees, g.ChiefImagePoint.Y * toDegrees };
    }
}

/// <summary>What a criterion measures, as the command line names it.</summary>
public static class Criteria
{
    /// <summary>
    /// "rms", "efl", "bfl", "boresight" (degrees), "boresight-mm" or "mtf:field:frequency:s|t": the RMS wavefront itself, or a
    /// departure or an MTF focused by it.
    /// </summary>
    public static ICriterion Create(string name, RmsWavefrontCriterion rms, NominalLens lens, FocusCompensator? compensator)
    {
        if (name.StartsWith("mtf", StringComparison.OrdinalIgnoreCase))
        {
            var mtf = MtfCriterion.Parse(name, rms);
            var nominal = lens.Perturb();
            if (compensator != null) compensator.Free().Solve(nominal, rms);
            if (!double.IsFinite(mtf.Evaluate(nominal.Build())))
                throw new InvalidOperationException($"{mtf.Name}: no ray of the nominal lens reaches the image at this field");
            return mtf;
        }
        DeviationCriterion? deviation = name.ToLowerInvariant() switch
        {
            "rms" or "wavefront" => null,
            "efl" => new EflCriterion { Focus = rms },
            "bfl" => new BflCriterion { Focus = rms },
            "boresight" => new BoresightCriterion { Focus = rms },
            "boresight-mm" => new BoresightCriterion { Focus = rms, InImagePlane = true },
            _ => throw new ArgumentException($"no criterion {name}: rms, efl, bfl, boresight, boresight-mm or mtf:field:frequency:s|t"),
        };
        return deviation == null ? rms : deviation.Referenced(lens, compensator);
    }
}
