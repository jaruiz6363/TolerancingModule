namespace Tolerancing.Core;

/// <summary>
/// One kind of manufacturing error, applied to a perturbed lens by an amount. The codes are
/// OpticStudio's tolerance operands, so a table reads the way a designer already knows.
///
/// <para>Tilts and decentres are traced by AberrationCalculator's real ray trace, each moved
/// surface in its own frame (LocalFrame). Its paraxial trace stays the nominal lens's.</para>
/// </summary>
public abstract record Tolerance(int Surface)
{
    public abstract string Code { get; }
    public abstract string Units { get; }
    public abstract void Apply(PerturbedLens lens, double amount);

    protected static void CheckSurface(PerturbedLens lens, int surface)
    {
        if (surface < 1 || surface >= lens.System.Surfaces.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(surface), surface,
                $"surface {surface} is not between the object and the image");
    }
}

/// <summary>
/// Power error in fringes (TFRN): the surface's sag at its semi-diameter changes by
/// fringes × λ/2 at the test wavelength. The curvature that does that is found exactly, through
/// the surface's whole sag, conic and polynomial terms included, so a strong or aspheric surface
/// is not given a paraxial approximation of its fringes.
/// </summary>
public sealed record PowerFringes(int Surface, double TestWavelengthUm = PowerFringes.HeNe) : Tolerance(Surface)
{
    /// <summary>The helium-neon line, OpticStudio's default test wavelength.</summary>
    public const double HeNe = 0.6328;

    public override string Code => "TFRN";
    public override string Units => "fringes";

    public override void Apply(PerturbedLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        var s = lens.System.Surfaces[Surface];
        double h = SemiDiameter(lens, Surface);
        double target = s.Sag(h) + amount * TestWavelengthUm * 1e-3 / 2.0;
        s.Curvature = CurvatureForSag(s, h, target);
    }

    /// <summary>
    /// The surface's semi-diameter as the file states it; failing that, the paraxial marginal plus
    /// chief ray heights there, which is what an automatic semi-diameter approximates.
    /// </summary>
    public static double SemiDiameter(PerturbedLens lens, int surface)
    {
        double h = lens.System.Surfaces[surface].SemiDiameter;
        if (h > 0.0) return h;
        var p = lens.Nominal.Model.Paraxial;
        return Math.Abs(p.Y[surface]) + Math.Abs(p.Ybar[surface]);
    }

    /// <summary>The curvature giving <paramref name="sag"/> at height <paramref name="h"/>, by secant on the full sag.</summary>
    private static double CurvatureForSag(AberrationCalculator.Core.Models.Surface s, double h, double sag)
    {
        double c0 = s.Curvature;
        // The sphere through the point is the start: exact for a sphere, close for anything else.
        double c1 = 2.0 * sag / (sag * sag + h * h);
        double f0 = SagWith(s, c0, h) - sag, f1 = SagWith(s, c1, h) - sag;
        for (int i = 0; i < 50 && Math.Abs(f1) > 1e-15; i++)
        {
            if (f1 == f0) break;
            double c2 = c1 - f1 * (c1 - c0) / (f1 - f0);
            (c0, f0) = (c1, f1);
            c1 = c2;
            f1 = SagWith(s, c1, h) - sag;
        }
        if (!double.IsFinite(f1) || Math.Abs(f1) > 1e-12)
            throw new InvalidOperationException($"no curvature gives a sag of {sag} at height {h}");
        return c1;
    }

    private static double SagWith(AberrationCalculator.Core.Models.Surface s, double c, double h)
    {
        double keep = s.Curvature;
        s.Curvature = c;
        double sag = s.Sag(h);
        s.Curvature = keep;
        return sag;
    }
}

/// <summary>
/// Irregularity (TIRR) in fringes at the test wavelength over the same aperture as
/// <see cref="PowerFringes"/>: the surface's departure from the best-fitting sphere, as a test plate
/// or interferometer reads it once power is taken out. Half of it is astigmatism, half spherical
/// aberration, the split Williamson 1989 and Ginsberg 1981 make for the two kinds a shop leaves:
///
/// <list type="bullet">
/// <item>astigmatism, Fringe Z5 = ρ² cos 2θ, its cylinder along x and y;</item>
/// <item>spherical, Fringe Z9 − Z1 = 6ρ⁴ − 6ρ², which has no power and leaves the vertex where it is.</item>
/// </list>
///
/// <para>Each has a peak to valley of half the irregularity, fringes × λ/4 of sag. Neither changes
/// the paraxial lens. AberrationCalculator's real ray trace traces the departure as a sag added to
/// the surface, over a unit circle of the aperture's radius. A negative amount turns the
/// astigmatism through 90° and the spherical part over. The orientation is fixed, x and y, on
/// every surface, so astigmatism on two surfaces adds as if aligned, which leans to the cautious
/// side.</para>
/// </summary>
public sealed record SurfaceIrregularity(int Surface, double TestWavelengthUm = PowerFringes.HeNe) : Tolerance(Surface)
{
    public override string Code => "TIRR";
    public override string Units => "fringes";

    public override void Apply(PerturbedLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        var s = lens.System.Surfaces[Surface];
        double half = amount * TestWavelengthUm * 1e-3 / 4.0;      // each half's peak to valley, sag
        var z = new double[Math.Max(10, s.FringeZernike.Length)];
        Array.Copy(s.FringeZernike, z, s.FringeZernike.Length);
        z[5] += half / 2.0;            // ρ² cos 2θ runs from −1 to 1
        z[9] += half / 1.5;            // 6ρ⁴ − 6ρ² + 1 runs from −1/2 to 1
        z[1] -= half / 1.5;            // ... less its constant, so the vertex stays
        s.FringeZernike = z;
        s.ZernikeRadius = PowerFringes.SemiDiameter(lens, Surface);
    }
}

/// <summary>
/// Rotationally invariant irregularity (TRSI) in fringes at the test wavelength over the same
/// aperture as <see cref="PowerFringes"/>: ISO 10110-5's quantity C, the part of the irregularity
/// that is the same at every azimuth - the zonal error an asphere is left with. OpticStudio has no
/// operand for it; the code is this module's.
///
/// <para>It is traced as Fringe Z9 − Z1 = 6ρ⁴ − 6ρ², with a peak to valley of fringes × λ/2 of
/// sag. Z9 is orthogonal to power, so a best-fit sphere leaves it whole, and of the rotationally
/// invariant Zernike terms it costs the most RMS wavefront for its peak to valley (1/√5 against
/// its 1.5; Z16's is 1/√7 against 2), so a tolerance sized on it is not too loose for a higher-order
/// zone of the same peak to valley. A negative amount turns it over.</para>
/// </summary>
public sealed record SymmetricIrregularity(int Surface, double TestWavelengthUm = PowerFringes.HeNe) : Tolerance(Surface)
{
    public override string Code => "TRSI";
    public override string Units => "fringes";

    public override void Apply(PerturbedLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        var s = lens.System.Surfaces[Surface];
        double pv = amount * TestWavelengthUm * 1e-3 / 2.0;         // peak to valley, sag
        var z = new double[Math.Max(10, s.FringeZernike.Length)];
        Array.Copy(s.FringeZernike, z, s.FringeZernike.Length);
        z[9] += pv / 1.5;              // 6ρ⁴ − 6ρ² + 1 runs from −1/2 to 1
        z[1] -= pv / 1.5;              // ... less its constant, so the vertex stays
        s.FringeZernike = z;
        s.ZernikeRadius = PowerFringes.SemiDiameter(lens, Surface);
    }

    /// <summary>The sag it adds at height <paramref name="h"/> of an aperture of radius <paramref name="radius"/>, per unit of peak to valley.</summary>
    public static double Shape(double h, double radius)
    {
        double r2 = h * h / (radius * radius);
        return (6.0 * r2 * r2 - 6.0 * r2) / 1.5;
    }

    /// <summary><see cref="Shape"/>'s slope along the radius at height <paramref name="h"/>, per unit of peak to valley.</summary>
    public static double Slope(double h, double radius)
    {
        double rho = h / radius;
        return (24.0 * rho * rho * rho - 12.0 * rho) / (1.5 * radius);
    }
}

/// <summary>Radius error (TRAD), in lens units. A plane has no radius to change: use fringes.</summary>
public sealed record RadiusChange(int Surface) : Tolerance(Surface)
{
    public override string Code => "TRAD";
    public override string Units => "mm";

    public override void Apply(PerturbedLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        var s = lens.System.Surfaces[Surface];
        if (s.Curvature == 0.0)
            throw new InvalidOperationException($"surface {Surface} is a plane; tolerance its power in fringes");
        s.Radius += amount;
    }
}

/// <summary>
/// Thickness error (TTHI), in lens units. With <paramref name="Adjust"/>, that surface's thickness
/// takes up the change, so everything beyond it stays where it was - an element made thick inside
/// a fixed barrel.
/// </summary>
public sealed record ThicknessChange(int Surface, int? Adjust = null) : Tolerance(Surface)
{
    public override string Code => "TTHI";
    public override string Units => "mm";

    public override void Apply(PerturbedLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        lens.System.Surfaces[Surface].Thickness += amount;
        if (Adjust is int a)
        {
            CheckSurface(lens, a);
            lens.System.Surfaces[a].Thickness -= amount;
        }
    }
}

/// <summary>
/// Index error (TIND) of the glass after the surface: n_d changes by the amount and the Abbe number
/// stays, so every wavelength's index moves with the dispersion scaled as n_d − 1.
/// </summary>
public sealed record IndexChange(int Surface) : Tolerance(Surface)
{
    public override string Code => "TIND";
    public override string Units => "";

    public override void Apply(PerturbedLens lens, double amount) =>
        Glass.Change(lens, Surface, deltaNd: amount, deltaVd: 0.0);
}

/// <summary>
/// Abbe number error (TABB) of the glass after the surface, absolute: n_d stays and the dispersion
/// n_F − n_C changes to (n_d − 1)/(V_d + amount).
/// </summary>
public sealed record AbbeChange(int Surface) : Tolerance(Surface)
{
    public override string Code => "TABB";
    public override string Units => "";

    public override void Apply(PerturbedLens lens, double amount) =>
        Glass.Change(lens, Surface, deltaNd: 0.0, deltaVd: amount);
}

/// <summary>Conic constant error (TCON).</summary>
public sealed record ConicChange(int Surface) : Tolerance(Surface)
{
    public override string Code => "TCON";
    public override string Units => "";

    public override void Apply(PerturbedLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        lens.System.Surfaces[Surface].Conic += amount;
    }
}

/// <summary>
/// Decentre of one surface on its element (TSDX, TSDY), in lens units. On a sphere it is a wedge:
/// the same as tilting the surface about its centre of curvature.
/// </summary>
public sealed record SurfaceDecentre(int Surface, Axis Axis) : Tolerance(Surface), IAxisTolerance
{
    public Tolerance WithAxis(Axis axis) => this with { Axis = axis };

    public override string Code => Axis == Axis.X ? "TSDX" : "TSDY";
    public override string Units => "mm";

    public override void Apply(PerturbedLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        lens.MoveSurface(Surface, Axis == Axis.X ? Placement.Decentre(amount, 0.0) : Placement.Decentre(0.0, amount));
    }
}

/// <summary>
/// Tilt of one surface about its vertex (TSTX, TSTY), in degrees: the wedge between the two faces
/// of an element, ISO 10110-6's centring error. A tilt about x turns the surface in the y-z plane.
///
/// <para>The sign is OpticStudio's, so TSTX +0.1 here is TSTX +0.1 there. AberrationCalculator's own
/// <c>Surface.TiltX</c> is the opposite sign (traced in both programs, 2026-10-12), which is why an
/// x tilt is handed to it negated; its y tilt agrees.</para>
/// </summary>
public sealed record SurfaceTilt(int Surface, Axis Axis) : Tolerance(Surface), IAxisTolerance
{
    public Tolerance WithAxis(Axis axis) => this with { Axis = axis };

    public override string Code => Axis == Axis.X ? "TSTX" : "TSTY";
    public override string Units => "deg";

    public override void Apply(PerturbedLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        double a = amount * Math.PI / 180.0;
        lens.MoveSurface(Surface, Axis == Axis.X ? Placement.Tilt(-a, 0.0) : Placement.Tilt(0.0, a));
    }
}

/// <summary>
/// Decentre of an element or group, surfaces <see cref="Tolerance.Surface"/> to <paramref name="Last"/>,
/// moved together (TEDX, TEDY), in lens units: the element sits off the barrel's axis, unwedged.
/// </summary>
public sealed record ElementDecentre(int Surface, int Last, Axis Axis) : Tolerance(Surface), IAxisTolerance
{
    public Tolerance WithAxis(Axis axis) => this with { Axis = axis };

    public override string Code => Axis == Axis.X ? "TEDX" : "TEDY";
    public override string Units => "mm";

    public override void Apply(PerturbedLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        CheckSurface(lens, Last);
        var move = Axis == Axis.X ? Placement.Decentre(amount, 0.0) : Placement.Decentre(0.0, amount);
        for (int s = Surface; s <= Last; s++) lens.MoveWithElement(s, move);
    }
}

/// <summary>
/// Tilt of an element or group as a rigid body (TETX, TETY), in degrees, about a pivot on the axis
/// <paramref name="PivotOffset"/> from its first vertex - the first vertex itself by default, as
/// OpticStudio's TETX. Every vertex swings off the axis and along it; nothing beyond the element moves.
/// </summary>
public sealed record ElementTilt(int Surface, int Last, Axis Axis, double PivotOffset = 0.0) : Tolerance(Surface), IAxisTolerance
{
    public Tolerance WithAxis(Axis axis) => this with { Axis = axis };

    public override string Code => Axis == Axis.X ? "TETX" : "TETY";
    public override string Units => "deg";

    public override void Apply(PerturbedLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        CheckSurface(lens, Last);
        double a = amount * Math.PI / 180.0;
        double fromFirst = 0.0;       // this vertex's distance from the first, along the axis
        for (int s = Surface; s <= Last; s++)
        {
            double pivot = PivotOffset - fromFirst;
            lens.MoveWithElement(s, Axis == Axis.X ? Placement.TiltAbout(-a, 0.0, pivot) : Placement.TiltAbout(0.0, a, pivot));
            fromFirst += lens.System.Surfaces[s].Thickness;
        }
    }
}

public enum Axis { X, Y }

/// <summary>
/// A tolerance with an x and a y kind - a decentre or a tilt. The two of one surface or element are
/// one error in some direction, so statistics may draw them together and an allocation gives them
/// one value.
/// </summary>
public interface IAxisTolerance
{
    Axis Axis { get; }

    /// <summary>The same tolerance about the other axis, or this one.</summary>
    Tolerance WithAxis(Axis axis);
}

/// <summary>
/// A tolerance and the range it may take: <see cref="Min"/> is usually negative. A range not centred
/// on nominal is a bias - a shop that makes thicknesses on the high side, to leave stock to rework
/// (Heshmaty-Manesh 1986). <see cref="Distribution"/> says how errors spread over the range, if not
/// as the statistics model's default.
/// </summary>
public sealed record ToleranceItem(Tolerance Tolerance, double Min, double Max)
{
    public Distribution? Distribution { get; init; }

    public double Center => 0.5 * (Min + Max);
    public double HalfWidth => 0.5 * (Max - Min);

    /// <summary>The same tolerance with half-width <paramref name="halfWidth"/>, its bias scaled with it.</summary>
    public ToleranceItem WithHalfWidth(double halfWidth)
    {
        double center = HalfWidth > 0.0 ? Center * halfWidth / HalfWidth : 0.0;
        return this with { Min = center - halfWidth, Max = center + halfWidth };
    }

    public static ToleranceItem Symmetric(Tolerance tolerance, double plusMinus) =>
        new(tolerance, -Math.Abs(plusMinus), Math.Abs(plusMinus));

    public override string ToString() =>
        $"{Tolerance.Code} {ToleranceSpec.Surfaces(Tolerance)} [{Min:G4}, {Max:G4}]{(Distribution == null ? "" : $" {Distribution}")}";
}

internal static class Glass
{
    /// <summary>
    /// Changes the glass after <paramref name="surface"/> to n_d + Δn_d and V_d + ΔV_d, keeping its
    /// partial dispersions: n′(λ) = n′_d + (n(λ) − n_d)·D′/D, with D = n_F − n_C. The same model
    /// OpticStudio's model glass uses for an index or Abbe tolerance, to first order.
    /// </summary>
    public static void Change(PerturbedLens lens, int surface, double deltaNd, double deltaVd)
    {
        var nominal = lens.Nominal;
        if (!nominal.IsGlass(surface))
            throw new InvalidOperationException($"the medium after surface {surface} is not glass");
        // Both changes of one glass accumulate, so an index and an Abbe error applied together
        // (as in a Monte Carlo trial) are both kept.
        var (totalNd, totalVd) = lens.GlassChanges.TryGetValue(surface, out var sofar) ? sofar : (0.0, 0.0);
        totalNd += deltaNd;
        totalVd += deltaVd;
        lens.GlassChanges[surface] = (totalNd, totalVd);

        double nd = nominal.Nd[surface];
        double d = nominal.NF[surface] - nominal.NC[surface];
        double vd = (nd - 1.0) / d;
        double ndNew = nd + totalNd;
        double dNew = (ndNew - 1.0) / (vd + totalVd);
        for (int w = 0; w < lens.Indices.Length; w++)
        {
            double n = nominal.Indices[w][surface];
            lens.Indices[w][surface] = ndNew + (n - nd) * dNew / d;
        }
    }
}
