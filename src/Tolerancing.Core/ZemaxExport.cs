using System.Globalization;
using System.Text;
using AberrationCalculator.Core.Enums;

namespace Tolerancing.Core;

/// <summary>
/// Writes a perturbed lens as an OpticStudio .zmx, so a toleranced lens can be opened there and its
/// numbers compared with these. A moved surface is wrapped in a pair of coordinate breaks, so nothing
/// beyond it moves - as AberrationCalculator traces it.
///
/// <para><b>The conventions, found by tracing both programs on 2026-10-12.</b> AberrationCalculator's
/// LocalFrame puts a point at <c>R (p − d)</c> with <c>R = Rx(−TiltX)·Ry(TiltY)</c>: the frame turns
/// about y first, then about x, and its x tilt is the NEGATIVE of OpticStudio's. A coordinate break
/// of order 0 decentres and then turns about x first, so it cannot say this directly; one of order 1
/// turns about y then x and then decentres in the turned frame. So the first break is order 1, with
/// tilts (−TiltX, TiltY) and the decentre <c>R d</c> - its z part as the break's thickness - and the
/// second undoes it with every value negated and order 0, as OpticStudio documents for an inverse.</para>
///
/// <para>Standard and even-asphere surfaces; an entrance pupil diameter, image-space F/# or object-space
/// NA; angle or object-height fields. A changed index or Abbe number is refused: a .zmx states glass
/// by name. So is an irregularity (<see cref="SurfaceIrregularity"/>, <see cref="SymmetricIrregularity"/>), a Zernike departure no
/// standard or even-asphere surface can state.</para>
/// </summary>
public static class ZemaxExport
{
    public static void Write(PerturbedLens lens, string path) => File.WriteAllText(path, ToZmx(lens));

    public static string ToZmx(PerturbedLens lens)
    {
        if (lens.GlassChanges.Count > 0)
            throw new NotSupportedException("a changed index or Abbe number cannot be written to a .zmx");
        var model = lens.Build();            // writes the composed moves onto the surfaces
        var sys = model.System;
        if (sys.Surfaces.Any(s => s.HasZernike))
            throw new NotSupportedException("an irregularity (TIRR, TRSI) cannot be written to a .zmx; leave it out of the set to export");

        var c = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("R", c);
        var z = new StringBuilder();
        void L(string line) => z.Append(line).Append('\n');

        L("VERS 221026 6 20120530 20120530");
        L("MODE SEQ");
        L($"NAME {(string.IsNullOrWhiteSpace(sys.Title) ? "PERTURBED" : sys.Title)} (perturbed)");
        L("UNIT MM X W X CM MR CPMM");
        L(sys.Aperture.Type switch
        {
            ApertureType.EPD => $"ENPD {F(sys.Aperture.Value)}",
            ApertureType.FNumber => $"FNUM {F(sys.Aperture.Value)} 0",
            ApertureType.ObjectSpaceNA => $"OBNA {F(sys.Aperture.Value)} 0",
            var a => throw new NotSupportedException($"an aperture of type {a} is not written"),
        });
        if (sys.GlassCatalogs.Count > 0) L($"GCAT {string.Join(" ", sys.GlassCatalogs)}");
        int fieldType = sys.FieldType switch
        {
            FieldType.ObjectAngle => 0,
            FieldType.ObjectHeight => 1,
            var t => throw new NotSupportedException($"fields of type {t} are not written"),
        };
        L($"FTYP {fieldType} 0 {sys.Fields.Count} {sys.Wavelengths.Count} 0 0 0 1");
        L("XFLN " + string.Join(" ", sys.Fields.Select(f => F(f.X))));
        L("YFLN " + string.Join(" ", sys.Fields.Select(f => F(f.Y))));
        L("FWGN " + string.Join(" ", sys.Fields.Select(f => F(f.Weight))));
        for (int w = 0; w < sys.Wavelengths.Count; w++)
            L($"WAVM {w + 1} {F(sys.Wavelengths[w].Value)} {F(sys.Wavelengths[w].Weight)}");
        L($"PWAV {sys.PrimaryWavelengthIndex + 1}");

        int n = 0;
        for (int s = 0; s < sys.Surfaces.Count; s++)
        {
            var surf = sys.Surfaces[s];
            bool moved = s > 0 && surf.IsPerturbed;
            double ax = 0, ay = 0, ex = 0, ey = 0, ez = 0;
            if (moved)
            {
                ax = -Deg(surf.TiltX);
                ay = Deg(surf.TiltY);
                var r = Placement.Tilt(surf.TiltX, surf.TiltY).R;      // LocalFrame's own matrix
                ex = r[0] * surf.DecenterX + r[1] * surf.DecenterY + r[2] * surf.DecenterZ;
                ey = r[3] * surf.DecenterX + r[4] * surf.DecenterY + r[5] * surf.DecenterZ;
                ez = r[6] * surf.DecenterX + r[7] * surf.DecenterY + r[8] * surf.DecenterZ;
                Break(z, ref n, ex, ey, ax, ay, 1, ez, F);
            }

            L($"SURF {n++}");
            if (surf.IsStop) L("  STOP");
            bool asphere = surf.AsphericCoefficients.Any(a => a != 0.0);
            L(asphere ? "  TYPE EVENASPH" : "  TYPE STANDARD");
            L($"  CURV {F(surf.Curvature)} 0 0 0 0 \"\"");
            if (asphere)
                for (int k = 0; k < surf.AsphericCoefficients.Length; k++)
                    L($"  PARM {k + 2} {F(surf.AsphericCoefficients[k])}");
            if (surf.Conic != 0.0) L($"  CONI {F(surf.Conic)}");
            if (!string.IsNullOrEmpty(surf.Material))
                L(surf.IsMirror ? "  GLAS MIRROR 0 0 1.5 40 0 0 0 0 0 0" : $"  GLAS {surf.Material} 0 0 1.5 40 0 0 0 0 0 0");
            if (surf.SemiDiameter > 0.0) L($"  DIAM {F(surf.SemiDiameter)} 0 0 0 1 \"\"");

            if (moved)
            {
                L($"  DISZ {F(-ez)}");                                   // back along the turned axis
                Break(z, ref n, -ex, -ey, -ax, -ay, 0, surf.Thickness, F);
            }
            else
            {
                L($"  DISZ {(double.IsInfinity(surf.Thickness) ? "INFINITY" : F(surf.Thickness))}");
            }
        }
        return z.ToString();
    }

    private static double Deg(double radians) => radians * 180.0 / Math.PI;

    private static void Break(StringBuilder z, ref int n, double dx, double dy, double tx, double ty, int order,
                              double thickness, Func<double, string> f)
    {
        z.Append($"SURF {n++}\n  TYPE COORDBRK\n  CURV 0.0 0 0 0 0 \"\"\n");
        z.Append($"  PARM 1 {f(dx)}\n  PARM 2 {f(dy)}\n  PARM 3 {f(tx)}\n  PARM 4 {f(ty)}\n  PARM 5 0\n  PARM 6 {order}\n");
        z.Append($"  DISZ {f(thickness)}\n");
    }
}
