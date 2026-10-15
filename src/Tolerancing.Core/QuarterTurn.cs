using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.Models;
using WavefrontErrorCalculator.Core;

namespace Tolerancing.Core;

/// <summary>
/// A lens turned a quarter turn about its axis, x into y: how the module evaluates a field on the
/// x axis. AberrationCalculator and WavefrontErrorCalculator trace fields on the y axis only; a lens
/// performs at the field point (x, 0) as the same lens turned through +90° performs at (0, x). So
/// every perturbation is turned - each surface's decenter and tilt, its Zernike departure, the image
/// plane's tilt - and the x fields become y fields of the turned lens. The pupil's Gaussian
/// quadrature has a multiple of four arms, so it samples the turned pupil as it did the lens's own.
///
/// <para>A surface both tilted about x and about y is turned to first order in its tilts:
/// AberrationCalculator's frame turns about y and then about x, an order a quarter turn exchanges,
/// which changes the surface's orientation only by the product of the two tilts.</para>
/// </summary>
public static class QuarterTurn
{
    private static readonly Lazy<GlassCatalog> Catalog = new(CatalogLocator.LoadBundled);

    /// <summary>
    /// The lens turned through +90°, with only its x fields, now on the y axis: field k of the result
    /// is <paramref name="xFields"/>[k] of the lens.
    /// </summary>
    public static LensModel Turn(LensModel lens, IReadOnlyList<int> xFields)
    {
        var system = SystemCopy.Of(lens.System);
        foreach (var s in system.Surfaces) Turn(s);
        system.Fields = xFields.Select(f => new Field(lens.System.Fields[f].X, lens.System.Fields[f].Weight)).ToList();
        return new LensModel(system, Catalog.Value, w => lens.Indices(w), lens.StopHeight);
    }

    /// <summary>A point (x, y) turned through +90°: (−y, x).</summary>
    public static void Turn(Surface s)
    {
        (s.DecenterX, s.DecenterY) = (-s.DecenterY, s.DecenterX);
        // AberrationCalculator's TiltX turns the opposite way to its TiltY about their axes
        // (Surface.TiltX), so as a rotation the pair is (−TiltX, TiltY), which turns as a vector.
        (s.TiltX, s.TiltY) = (s.TiltY, -s.TiltX);
        if (s.HasZernike) s.FringeZernike = TurnZernike(s.FringeZernike);
    }

    /// <summary>
    /// A Fringe Zernike departure turned through +90°: each pair cos mθ, sin mθ becomes
    /// cos m(θ − 90°), sin m(θ − 90°) of the turned surface, rotationally symmetric terms unchanged.
    /// </summary>
    public static double[] TurnZernike(double[] z)
    {
        var turned = (double[])z.Clone();
        int j = 1;
        for (int s = 0; s <= 6 && j < z.Length; s++)
            for (int m = s; m >= 0 && j < z.Length; m--)
            {
                if (s == 6 && m > 0) continue;
                if (m == 0) { j++; continue; }
                if (j + 1 >= z.Length) { j += 2; continue; }
                double c = Math.Round(Math.Cos(m * Math.PI / 2.0)), sn = Math.Round(Math.Sin(m * Math.PI / 2.0));
                double a = z[j], b = z[j + 1];
                turned[j] = a * c - b * sn;
                turned[j + 1] = a * sn + b * c;
                j += 2;
            }
        return turned;
    }
}
