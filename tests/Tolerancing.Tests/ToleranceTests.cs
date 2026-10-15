using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.IO;
using AberrationCalculator.Core.Models;
using Tolerancing.Core;
using Xunit;

namespace Tolerancing.Tests;

/// <summary>Each tolerance changes the lens the way its definition says, checked against first principles.</summary>
public class ToleranceTests
{
    internal static readonly string Singlet = Path.Combine(AppContext.BaseDirectory, "TestData", "biconvex_singlet.zmx");
    internal static readonly string Triplet = Path.Combine(AppContext.BaseDirectory, "TestData", "cooke_triplet.zmx");

    private static readonly GlassCatalog Catalog = CatalogLocator.LoadBundled();

    internal static NominalLens Read(string path, Action<OpticalSystem>? edit = null, bool symmetric = false)
    {
        var system = LensFile.Read(path, Catalog);
        edit?.Invoke(system);
        int fileFields = system.Fields.Count;
        if (symmetric) NominalLens.MirrorFields(system);
        return new NominalLens(system, Catalog) { FileFieldCount = fileFields };
    }

    [Theory]
    [InlineData(1, 10.0, 0.0)]      // a sphere
    [InlineData(2, -3.0, 0.0)]
    [InlineData(1, 5.0, -1.0)]      // a paraboloid: the sag is the conic's, not the sphere's
    [InlineData(2, 2.5, 0.7)]
    public void FringesChangeTheSagAtTheSemiDiameterByHalfAWavelengthEach(int surface, double fringes, double conic)
    {
        var lens = Read(Singlet, s => s.Surfaces[surface].Conic = conic);
        var perturbed = lens.Perturb();
        double h = PowerFringes.SemiDiameter(perturbed, surface);
        double before = perturbed.System.Surfaces[surface].Sag(h);

        new PowerFringes(surface, 0.6328).Apply(perturbed, fringes);

        double after = perturbed.System.Surfaces[surface].Sag(h);
        Assert.Equal(8.0, h);
        Assert.Equal(fringes * 0.6328e-3 / 2.0, after - before, 13);
        Assert.Equal(conic, perturbed.System.Surfaces[surface].Conic);
    }

    [Fact]
    public void APlaneTakesFringesAsTheSphereThroughTheNewSag()
    {
        var lens = Read(Singlet, s => s.Surfaces[2].Curvature = 0.0);
        var perturbed = lens.Perturb();

        new PowerFringes(2, 0.5).Apply(perturbed, 4.0);

        double sag = 4.0 * 0.5e-3 / 2.0;
        Assert.Equal(2.0 * sag / (sag * sag + 64.0), perturbed.System.Surfaces[2].Curvature, 15);
        Assert.Throws<InvalidOperationException>(() => new RadiusChange(2).Apply(lens.Perturb(), 1.0));
    }

    [Fact]
    public void AnIndexChangeMovesTheFocalLengthAsTheThickLensFormulaSays()
    {
        var lens = Read(Singlet);
        var perturbed = lens.Perturb();

        new IndexChange(1).Apply(perturbed, 0.002);

        double n = lens.Indices[0][1] + 0.002;
        double power = (n - 1.0) * 0.04 - (n - 1.0) * (n - 1.0) * 5.0 * 0.0004 / n;
        Assert.Equal(1.0 / power, perturbed.Build().Paraxial.Efl, 9);
        Assert.Equal(lens.Nd[1], lens.Indices[0][1], 12);   // the singlet's one wavelength is d
    }

    [Fact]
    public void AnIndexChangeKeepsTheAbbeNumberAndAnAbbeChangeKeepsTheIndex()
    {
        var lens = Read(Triplet);                // F, d, C: wavelengths 0, 1, 2
        int glass = 3;                           // F2
        double vd = (lens.Nd[glass] - 1.0) / (lens.NF[glass] - lens.NC[glass]);

        var index = lens.Perturb();
        new IndexChange(glass).Apply(index, 0.001);
        var n = index.Indices;
        Assert.Equal(lens.Nd[glass] + 0.001, n[1][glass], 12);
        Assert.Equal(vd, (n[1][glass] - 1.0) / (n[0][glass] - n[2][glass]), 9);

        var abbe = lens.Perturb();
        new AbbeChange(glass).Apply(abbe, -0.5);
        var m = abbe.Indices;
        Assert.Equal(lens.Nd[glass], m[1][glass], 12);
        Assert.Equal(vd - 0.5, (m[1][glass] - 1.0) / (m[0][glass] - m[2][glass]), 9);
    }

    [Fact]
    public void IndexAndAbbeChangesOfOneGlassAccumulate()
    {
        var lens = Read(Triplet);
        double vd = (lens.Nd[1] - 1.0) / (lens.NF[1] - lens.NC[1]);
        var perturbed = lens.Perturb();

        new IndexChange(1).Apply(perturbed, 0.0005);
        new AbbeChange(1).Apply(perturbed, 0.3);
        new IndexChange(1).Apply(perturbed, 0.0005);

        var n = perturbed.Indices;
        Assert.Equal(lens.Nd[1] + 0.001, n[1][1], 12);
        Assert.Equal(vd + 0.3, (n[1][1] - 1.0) / (n[0][1] - n[2][1]), 9);
    }

    [Fact]
    public void AnAdjustedThicknessKeepsEverythingBeyondItInPlace()
    {
        var lens = Read(Triplet);
        var perturbed = lens.Perturb();

        new ThicknessChange(1, Adjust: 2).Apply(perturbed, 0.15);

        var s = perturbed.System.Surfaces;
        Assert.Equal(2.65, s[1].Thickness, 12);
        Assert.Equal(4.85, s[2].Thickness, 12);
        Assert.Equal(lens.System.Surfaces.Sum(x => double.IsFinite(x.Thickness) ? x.Thickness : 0.0),
                     s.Sum(x => double.IsFinite(x.Thickness) ? x.Thickness : 0.0), 12);
    }

    [Fact]
    public void GlassTolerancesRefuseAir()
    {
        var lens = Read(Triplet);
        Assert.Throws<InvalidOperationException>(() => new IndexChange(2).Apply(lens.Perturb(), 0.001));
        Assert.Throws<InvalidOperationException>(() => new AbbeChange(4).Apply(lens.Perturb(), 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThicknessChange(0).Apply(lens.Perturb(), 1.0));
    }

    [Fact]
    public void PerturbingACopyLeavesTheNominalAlone()
    {
        var lens = Read(Triplet);
        var perturbed = lens.Perturb();

        new PowerFringes(1).Apply(perturbed, 10.0);
        new ThicknessChange(4).Apply(perturbed, 0.2);
        new IndexChange(6).Apply(perturbed, 0.002);
        new ConicChange(7).Apply(perturbed, -0.5);

        var again = Read(Triplet);
        for (int s = 0; s < lens.System.Surfaces.Count; s++)
        {
            Assert.Equal(again.System.Surfaces[s].Curvature, lens.System.Surfaces[s].Curvature);
            Assert.Equal(again.System.Surfaces[s].Thickness, lens.System.Surfaces[s].Thickness);
            Assert.Equal(again.System.Surfaces[s].Conic, lens.System.Surfaces[s].Conic);
        }
        Assert.Equal(again.Indices[1], lens.Indices[1]);
        Assert.NotEqual(lens.System.Surfaces[1].Curvature, perturbed.System.Surfaces[1].Curvature);
    }
}
