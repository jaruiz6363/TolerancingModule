using AberrationCalculator.Core.RayTrace;
using Tolerancing.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>
/// Smith 1985's worked example, the 14 mm NA 0.42 laser recording lens of his Table 1, at 0.82 µm.
/// He gives no stop; it is put at the first surface, which the clear apertures suit, and the field
/// barely depends on it. His 0.7 mm field at the short conjugate, at 0.18×, is an object 1.944 mm high.
///
/// <para>His change table (Table 2) is not this module's criterion: each entry is a P-V OPD from
/// third-order spherical, tangential coma and astigmatism, by his rules of thumb (eqs. 2-6). It is
/// compared here with 3.5 × the RMS of the wavefront each change adds - √(A t²), Smith's own
/// RMS = P-V / 3.5 - at the field's edge, refocused by the air space as his lens is.</para>
/// </summary>
public class SmithTests
{
    private static readonly string Lens = Path.Combine(AppContext.BaseDirectory, "TestData", "smith1985_laser.zmx");

    [Fact]
    public void TheLensIsSmithsFourteenMillimetreNa042()
    {
        var lens = ToleranceTests.Read(Lens);
        var p = lens.Model.Paraxial;
        Assert.InRange(p.Efl, 13.8, 14.0);                      // "14mm"
        Assert.InRange(1.0 / (2.0 * p.FNumber), 0.41, 0.43);    // "NA 0.42"

        int w = lens.Model.PrimaryWavelength;
        var edge = RealRayTrace.Trace(lens.System, lens.Model.Indices(w), p, lens.System.Fields[2].Y, 0.0, 0.0, atParaxialFocus: false);
        Assert.InRange(Math.Abs(edge.Y), 0.345, 0.355);         // "a field of 0.7mm"
        Assert.Equal(1.764, lens.Indices[w][1], 3);              // his SF11 index at 0.82 µm
    }

    [Fact]
    public void OnAxisItIsAsWellCorrectedAsSmithSays()
    {
        // "an OPD of .04 waves on axis", P-V: about 0.011 RMS by his 3.5.
        var lens = ToleranceTests.Read(Lens, s => s.Fields.RemoveRange(1, 2));
        var r = Sensitivity.Evaluate(lens, null, 0.0, new RmsWavefrontCriterion(), new FocusCompensator(8));
        Assert.InRange(r.Criterion, 0.005, 0.015);
    }

    // Smith's Table 2, last column: P-V waves for 10 rings, 0.2 mm, an index of 0.001, a tilt of 0.001 rad.
    private static readonly double[] Radius = { .016, .021, .058, .027, .029, .029, .014, .060 };
    private static readonly double[] Thickness = { .004, .035, .037, .027, .036, .059 };   // T7 below
    private static readonly double[] Index = { .008, .004, .005, .004 };
    private static readonly double[] Tilt = { .044, .070, .180, .093, .102, .106, .028, .081 };

    private static Dictionary<string, double> ChangeTable()
    {
        var lens = ToleranceTests.Read(Lens, s => s.Fields.RemoveRange(0, 2));
        var items = new List<ToleranceItem>();
        for (int s = 1; s <= 8; s++)
        {
            items.Add(ToleranceItem.Symmetric(new PowerFringes(s, 0.59), 10.0));   // sodium D, his eq. 7's test λ
            items.Add(ToleranceItem.Symmetric(new SurfaceTilt(s, Axis.X), 0.001 * 180.0 / Math.PI));
        }
        for (int s = 1; s <= 6; s++) items.Add(ToleranceItem.Symmetric(new ThicknessChange(s), 0.2));
        foreach (int s in new[] { 1, 3, 5, 7 }) items.Add(ToleranceItem.Symmetric(new IndexChange(s), 0.001));

        var criterion = new RmsWavefrontCriterion();
        var compensator = new FocusCompensator(8);
        var table = Sensitivity.Run(lens, items, criterion, compensator);
        var model = QuadraticModel.Fit(lens, table, criterion, compensator, CrossTerms.None);
        return model.Terms.ToDictionary(t => $"{t.Item.Tolerance.Code}{t.Item.Tolerance.Surface}",
                                        t => 3.5 * Math.Sqrt(Math.Max(0.0, t.A)) * t.Item.Max);
    }

    private static readonly Lazy<Dictionary<string, double>> Table = new(ChangeTable);

    /// <summary>
    /// Radius, thickness and index: each within 2.5× of Smith's, the median ratio about 1.5 - ours the
    /// larger, as it keeps every aberration where he kept three.
    /// </summary>
    [Fact]
    public void RadiusThicknessAndIndexChangesAreSmithsToWithinTwoAndAHalf()
    {
        var ratios = new List<double>();
        for (int s = 1; s <= 8; s++) ratios.Add(Table.Value[$"TFRN{s}"] / Radius[s - 1]);
        for (int s = 1; s <= 6; s++) ratios.Add(Table.Value[$"TTHI{s}"] / Thickness[s - 1]);
        int[] glass = { 1, 3, 5, 7 };
        for (int k = 0; k < 4; k++) ratios.Add(Table.Value[$"TIND{glass[k]}"] / Index[k]);

        Assert.All(ratios, r => Assert.InRange(r, 0.4, 2.5));
        double median = ratios.OrderBy(r => r).ElementAt(ratios.Count / 2);
        Assert.InRange(median, 1.0, 2.0);
    }

    /// <summary>
    /// Tilts: in Smith's order but for one neighbouring pair (Spearman 0.98) - surface 3 the worst,
    /// 7 the least - and 2 to 3 times his values. This module and OpticStudio trace a tilted surface
    /// alike (TiltDecentreTests), so the factor is his third-order coma estimate's, not a
    /// tilt this module gets wrong.
    /// </summary>
    [Fact]
    public void TiltsRankAsSmithsDo()
    {
        var ours = Enumerable.Range(1, 8).Select(s => Table.Value[$"TSTX{s}"]).ToArray();
        Assert.True(Spearman(ours, Tilt) >= 0.95, $"Spearman {Spearman(ours, Tilt):F3}");
        Assert.All(ours.Select((v, k) => v / Tilt[k]), r => Assert.InRange(r, 1.5, 3.5));
    }

    private static double Spearman(double[] a, double[] b)
    {
        double[] Rank(double[] v) => v.Select(x => (double)v.Count(y => y > x) + (v.Count(y => y == x) + 1) / 2.0).ToArray();
        var ra = Rank(a);
        var rb = Rank(b);
        double d2 = ra.Select((r, k) => (r - rb[k]) * (r - rb[k])).Sum();
        int n = a.Length;
        return 1.0 - 6.0 * d2 / (n * (n * (double)n - 1.0));
    }
}
