namespace Tolerancing.Core;

/// <summary>How tightly the shop works: the four grades of Smith 1985, Table 3.</summary>
public enum ShopGrade { LowCost, Commercial, Precision, ExtraPrecise }

/// <summary>The default size of each kind of tolerance at one shop grade.</summary>
public sealed record GradeValues(double Fringes, double ThicknessMm, double Index, double AbbePercent,
                                 double SurfaceTiltDeg, double SurfaceDecentreMm,
                                 double ElementDecentreMm, double ElementTiltDeg, double IrregularityFringes);

/// <summary>
/// A starting set of tolerances for a lens, from the shop grade - the "trial budget" of Smith 1985
/// and Hilbert 1979's default set, before any is scaled to the lens's sensitivities:
/// <list type="bullet">
/// <item>power and irregularity in fringes, and wedge (surface tilt in x and y), on every surface where the index changes;</item>
/// <item>surface decentre on a conic or aspheric surface - on a sphere it is the same error as a tilt;</item>
/// <item>thickness of every glass and every air space, except the one the compensator refocuses -
/// the back focus unless another is given - and any of no thickness, two surfaces in contact;</item>
/// <item>index and Abbe number of every glass;</item>
/// <item>decentre and tilt in x and y of every element, cemented group and mirror.</item>
/// </list>
/// </summary>
public static class DefaultTolerances
{
    /// <summary>
    /// Smith 1985, Table 3, for radius (rings), irregularity (rings), thickness, index and surface tilt (there in radians:
    /// 0.005, 0.001, 0.0003, 0.0001). Abbe number is not in his table; the percentages are the glass
    /// makers' standard and closer steps (Schott: ±0.8 %, ±0.5 %, ±0.3 %). Element decentre and tilt
    /// are not in it either; they are spaced between Ginsberg 1981's "fairly loose" and "fairly tight"
    /// (0.25 to 0.025 mm, 2 to 0.3 mrad).
    /// </summary>
    public static GradeValues For(ShopGrade grade) => grade switch
    {
        ShopGrade.LowCost => new(50.0, 0.5, 0.002, 0.8, Deg(0.005), 0.1, 0.2, 0.2, 10.0),
        ShopGrade.Commercial => new(10.0, 0.2, 0.002, 0.8, Deg(0.001), 0.05, 0.1, 0.1, 2.0),
        ShopGrade.Precision => new(5.0, 0.1, 0.001, 0.5, Deg(0.0003), 0.02, 0.05, 0.05, 1.0),
        ShopGrade.ExtraPrecise => new(1.0, 0.05, 0.0002, 0.3, Deg(0.0001), 0.01, 0.025, 0.02, 0.25),
        _ => throw new ArgumentOutOfRangeException(nameof(grade)),
    };

    private static double Deg(double radians) => radians * 180.0 / Math.PI;

    /// <summary>Burge's base wedge, as the light deviation it makes: 6 arcminutes.</summary>
    public const double BaseDeviationDeg = 0.1;

    /// <summary>
    /// Burge's "base" tolerances (OPTI 521, rules of thumb for lenses and glass): what any shop holds
    /// with no special effort, so a looser tolerance saves nothing. Thickness ±0.2 mm; index ±0.001
    /// and Abbe ±0.8 %, the glass makers' standard grade; a surface tilt that deviates light by no
    /// more than 6′, Δn·θ for a refracting surface and 2θ for a mirror. Null for a kind he gives no
    /// base for - a decentre or a tilt in the mount, power in fringes (his 20 µm of sag is looser than any grade).
    /// </summary>
    public static double? Base(Tolerance tolerance, NominalLens lens) => tolerance switch
    {
        ThicknessChange => 0.2,
        IndexChange => 0.001,
        AbbeChange a => Abbe(lens, a.Surface) * 0.8 / 100.0,
        SurfaceTilt t => BaseDeviationDeg / Deviation(lens, t.Surface),
        _ => null,
    };

    /// <summary>The light deviation per unit tilt of a surface: twice for a mirror, the index step for a refraction.</summary>
    private static double Deviation(NominalLens lens, int surface) =>
        lens.System.Surfaces[surface].IsMirror ? 2.0 : Math.Max(Math.Abs(lens.Nd[surface] - lens.Nd[surface - 1]), 1e-9);

    private static double Abbe(NominalLens lens, int surface) => (lens.Nd[surface] - 1.0) / (lens.NF[surface] - lens.NC[surface]);

    /// <summary>
    /// The index tolerances glass is sold in, loosest first: Burge's standard ±0.001, ±0.0005 and
    /// ±0.0002, with ±0.0003 between.
    /// </summary>
    public static readonly double[] IndexGrades = { 0.001, 0.0005, 0.0003, 0.0002 };

    /// <summary>The Abbe number tolerances glass is sold in, as percentages, loosest first.</summary>
    public static readonly double[] AbbeGradesPercent = { 0.8, 0.5, 0.3, 0.2 };

    /// <summary>
    /// The only half-widths a tolerance can take, loosest first - the glass grades for index and
    /// Abbe number - or null for a tolerance that can take any value.
    /// </summary>
    public static double[]? Grades(Tolerance tolerance, NominalLens lens) => tolerance switch
    {
        IndexChange => IndexGrades,
        AbbeChange a => AbbeGradesPercent.Select(p => Abbe(lens, a.Surface) * p / 100.0).ToArray(),
        _ => null,
    };

    /// <summary>The largest part of a thickness a tolerance may take: beyond it a thin centre or a narrow air space could close up.</summary>
    public const double ThicknessFraction = 0.2;

    /// <summary>
    /// A thickness tolerance no larger than <see cref="ThicknessFraction"/> of the thickness itself - a grade's
    /// ±0.5 mm on a 0.26 mm centre would let the glass go negative.
    /// </summary>
    public static double ThicknessCap(NominalLens lens, int surface, double tolerance) =>
        Math.Min(tolerance, ThicknessFraction * Math.Abs(lens.System.Surfaces[surface].Thickness));

    /// <summary>
    /// The half-width the grade gives a tolerance of this kind, or null for a kind the grades do not
    /// cover (a radius in lens units, a conic).
    /// </summary>
    public static double? Value(Tolerance tolerance, NominalLens lens, GradeValues g) => tolerance switch
    {
        PowerFringes => g.Fringes,
        SurfaceIrregularity or SymmetricIrregularity => g.IrregularityFringes,
        ThicknessChange => g.ThicknessMm,
        IndexChange => g.Index,
        AbbeChange a => Abbe(lens, a.Surface) * g.AbbePercent / 100.0,
        SurfaceTilt => g.SurfaceTiltDeg,
        SurfaceDecentre => g.SurfaceDecentreMm,
        ElementDecentre => g.ElementDecentreMm,
        ElementTilt => g.ElementTiltDeg,
        _ => null,
    };

    public static List<ToleranceItem> Create(NominalLens lens, ShopGrade grade = ShopGrade.Commercial,
                                             double testWavelengthUm = PowerFringes.HeNe,
                                             FocusCompensator? compensator = null)
    {
        var g = For(grade);
        var items = new List<ToleranceItem>();
        int last = lens.BackFocusSurface;

        for (int s = 1; s <= last; s++)
        {
            var surface = lens.System.Surfaces[s];
            bool refracts = surface.IsMirror || Math.Abs(lens.Nd[s] - lens.Nd[s - 1]) > 1e-9;
            if (!refracts) continue;
            items.Add(ToleranceItem.Symmetric(new PowerFringes(s, testWavelengthUm), g.Fringes));
            items.Add(ToleranceItem.Symmetric(new SurfaceIrregularity(s, testWavelengthUm), g.IrregularityFringes));
            items.Add(ToleranceItem.Symmetric(new SurfaceTilt(s, Axis.X), g.SurfaceTiltDeg));
            items.Add(ToleranceItem.Symmetric(new SurfaceTilt(s, Axis.Y), g.SurfaceTiltDeg));
            if (surface.Conic != 0.0 || surface.AsphericCoefficients.Any(a => a != 0.0))
            {
                items.Add(ToleranceItem.Symmetric(new SurfaceDecentre(s, Axis.X), g.SurfaceDecentreMm));
                items.Add(ToleranceItem.Symmetric(new SurfaceDecentre(s, Axis.Y), g.SurfaceDecentreMm));
            }
        }
        int focus = compensator?.Surface ?? last;
        // A space of no thickness - two surfaces in contact - has none to tolerance.
        for (int s = 1; s <= last; s++)
            if (s != focus && ThicknessCap(lens, s, g.ThicknessMm) is double t && t > 0.0)
                items.Add(ToleranceItem.Symmetric(new ThicknessChange(s), t));
        for (int s = 1; s < last; s++)
        {
            if (!lens.IsGlass(s)) continue;
            items.Add(ToleranceItem.Symmetric(new IndexChange(s), g.Index));
            double vd = (lens.Nd[s] - 1.0) / (lens.NF[s] - lens.NC[s]);
            items.Add(ToleranceItem.Symmetric(new AbbeChange(s), vd * g.AbbePercent / 100.0));
        }
        foreach (var (first, end) in Elements(lens))
        {
            items.Add(ToleranceItem.Symmetric(new ElementDecentre(first, end, Axis.X), g.ElementDecentreMm));
            items.Add(ToleranceItem.Symmetric(new ElementDecentre(first, end, Axis.Y), g.ElementDecentreMm));
            items.Add(ToleranceItem.Symmetric(new ElementTilt(first, end, Axis.X), g.ElementTiltDeg));
            items.Add(ToleranceItem.Symmetric(new ElementTilt(first, end, Axis.Y), g.ElementTiltDeg));
        }
        return items;
    }

    /// <summary>
    /// The elements of the lens, first and last surface: a run of glass, cemented surfaces included,
    /// from the surface it enters at to the one it leaves by; and every mirror on its own.
    /// </summary>
    public static List<(int First, int Last)> Elements(NominalLens lens)
    {
        var elements = new List<(int, int)>();
        int last = lens.BackFocusSurface;
        for (int s = 1; s <= last; s++)
        {
            if (lens.System.Surfaces[s].IsMirror) { elements.Add((s, s)); continue; }
            if (!lens.IsGlass(s)) continue;
            int end = s;
            while (end < last && lens.IsGlass(end)) end++;
            elements.Add((s, end));
            s = end;
        }
        return elements;
    }
}
