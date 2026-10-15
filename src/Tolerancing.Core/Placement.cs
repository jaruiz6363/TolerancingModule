using AberrationCalculator.Core.Models;

namespace Tolerancing.Core;

/// <summary>
/// A rigid move of one surface's frame, in AberrationCalculator's LocalFrame convention: a point
/// <c>p</c> of the mechanical frame is at <c>R (p − d)</c> in the surface's own. <c>d</c> is where
/// the surface's vertex went, <c>R</c> how its frame turned.
///
/// <para>A wedge (the surface tilted on its own element) and the element tilted in the barrel are
/// two such moves, and they compose as rotations, not by adding angles. The composite is written
/// back into the surface's decentres and two tilts: a rotationally symmetric surface is unchanged
/// by turning it about its own axis, so its axis direction and vertex say everything.</para>
/// </summary>
public readonly record struct Placement(double[] R, double Dx, double Dy, double Dz)
{
    public static Placement Identity => new(new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, 0.0, 0.0, 0.0);

    public static Placement Decentre(double dx, double dy, double dz = 0.0) => Identity with { Dx = dx, Dy = dy, Dz = dz };

    /// <summary>
    /// Tilts in radians about x and y, through the vertex. The matrix is LocalFrame's own -
    /// <c>Rx(−tiltX)·Ry(tiltY)</c>, the x tilt negated - so that a tilt here is a tilt there.
    /// </summary>
    public static Placement Tilt(double tiltX, double tiltY)
    {
        double cb = Math.Cos(-tiltX), sb = Math.Sin(-tiltX);
        double ca = Math.Cos(tiltY), sa = Math.Sin(tiltY);
        return new(new[] { ca, 0.0, -sa, sb * sa, cb, sb * ca, cb * sa, -sb, cb * ca }, 0.0, 0.0, 0.0);
    }

    /// <summary>
    /// Turned by the tilts about a pivot on the axis, <paramref name="pivotZ"/> from the vertex:
    /// the vertex swings to <c>P − Rᵀ P</c>, off the axis and along it.
    /// </summary>
    public static Placement TiltAbout(double tiltX, double tiltY, double pivotZ)
    {
        var t = Tilt(tiltX, tiltY);
        var r = t.R;
        // Rᵀ P with P = (0, 0, pivotZ): the third row of R, times pivotZ.
        return t with { Dx = -r[6] * pivotZ, Dy = -r[7] * pivotZ, Dz = pivotZ - r[8] * pivotZ };
    }

    /// <summary>
    /// This move made inside <paramref name="outer"/>'s frame: first the outer, then this one.
    /// <c>p_s = R_i (R_o (p − d_o) − d_i)</c>, so <c>R = R_i R_o</c> and <c>d = d_o + R_oᵀ d_i</c>.
    /// </summary>
    public Placement Within(Placement outer)
    {
        var a = R; var o = outer.R;
        var m = new double[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                m[3 * i + j] = a[3 * i] * o[j] + a[3 * i + 1] * o[3 + j] + a[3 * i + 2] * o[6 + j];
        return new(m,
                   outer.Dx + o[0] * Dx + o[3] * Dy + o[6] * Dz,
                   outer.Dy + o[1] * Dx + o[4] * Dy + o[7] * Dz,
                   outer.Dz + o[2] * Dx + o[5] * Dy + o[8] * Dz);
    }

    /// <summary>
    /// Writes the move onto the surface. The local axis is the third row of R, which for LocalFrame's
    /// matrix is <c>(cos(tx) sin(ty), sin(tx), cos(tx) cos(ty))</c>; the turn about that axis is dropped.
    /// </summary>
    public void ApplyTo(Surface surface)
    {
        surface.DecenterX = Dx;
        surface.DecenterY = Dy;
        surface.DecenterZ = Dz;
        surface.TiltX = Math.Asin(Math.Clamp(R[7], -1.0, 1.0));
        surface.TiltY = Math.Atan2(R[6], R[8]);
    }
}
