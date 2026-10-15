using Tolerancing.Core;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>
/// The nominal lens is read through AberrationCalculator and evaluated through
/// WavefrontErrorCalculator. The singlet is written for these tests: R = ±50 mm, 5 mm of N-BK7,
/// a 10 mm entrance pupil, fields 0° and 5°.
/// </summary>
public class NominalPerformanceTests
{
    private static readonly string Singlet = Path.Combine(AppContext.BaseDirectory, "TestData", "biconvex_singlet.zmx");

    [Fact]
    public void FocalLengthIsTheThickLensFormula()
    {
        var lens = LensModel.Read(Singlet);
        double n = lens.Indices(lens.PrimaryWavelength)[1];
        // 1/f = (n − 1)(c1 − c2) + (n − 1)² t c1 c2 / n, with c1 = 0.02, c2 = −0.02, t = 5.
        double power = (n - 1.0) * 0.04 - (n - 1.0) * (n - 1.0) * 5.0 * 0.0004 / n;

        var nominal = NominalPerformance.Evaluate(lens);

        Assert.Equal(1.0 / power, nominal.Efl, 9);
        Assert.Equal(0.5875618, nominal.WavelengthUm, 7);
    }

    [Fact]
    public void EveryFieldHasAWavefront()
    {
        var nominal = NominalPerformance.Evaluate(LensModel.Read(Singlet));

        Assert.Equal(2, nominal.Fields.Count);
        Assert.Equal(0.0, nominal.Fields[0].FieldValue);
        Assert.Equal(5.0, nominal.Fields[1].FieldValue);
        Assert.All(nominal.Fields, f => Assert.True(f.Rms > 0.0 && double.IsFinite(f.Rms)));
        // A singlet's coma and astigmatism make the edge of the field worse than its centre.
        Assert.True(nominal.Fields[1].Rms > nominal.Fields[0].Rms);
    }
}
