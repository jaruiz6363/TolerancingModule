using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.Models;
using AberrationCalculator.Core.IO;
using WavefrontErrorCalculator.Core;

namespace Tolerancing.Core;

/// <summary>
/// The lens as designed, with what perturbing it needs: the refractive index of every medium at
/// every wavelength of the lens, and at d, F and C for a change of Abbe number.
///
/// <para>Indices are indexed like the surfaces: entry <c>s</c> is the medium after surface
/// <c>s</c>, as AberrationCalculator's <see cref="IndexResolver"/> builds them.</para>
/// </summary>
/// <summary>Which field points a lens is judged at: the file's, each with its mirror image at −y, or with its images on the x axis as well.</summary>
public enum FieldSymmetry { File, Y, XY }

public sealed class NominalLens
{
    public NominalLens(OpticalSystem system, GlassCatalog catalog)
    {
        System = system ?? throw new ArgumentNullException(nameof(system));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Model = new LensModel(system, catalog);
        Indices = Enumerable.Range(0, system.Wavelengths.Count).Select(w => Model.Indices(w)).ToArray();
        Nd = IndexResolver.Build(system, catalog, IndexResolver.LambdaD);
        NF = IndexResolver.Build(system, catalog, IndexResolver.LambdaF);
        NC = IndexResolver.Build(system, catalog, IndexResolver.LambdaC);
    }

    /// <summary>
    /// Reads a lens in any format AberrationCalculator reads, with its bundled glass, its fields
    /// judged as <paramref name="fields"/> says (<see cref="MirrorFields"/>).
    /// </summary>
    public static NominalLens Read(string path, FieldSymmetry fields = FieldSymmetry.Y)
    {
        var catalog = CatalogLocator.LoadBundled();
        var system = LensFile.Read(path, catalog);
        int fileFields = system.Fields.Count;
        if (fields != FieldSymmetry.File) MirrorFields(system, fields == FieldSymmetry.XY);
        return new NominalLens(system, catalog) { FileFieldCount = fileFields };
    }

    /// <summary>
    /// The fields the lens file gives, before <see cref="MirrorFields"/> appended their mirror
    /// images: the first this many of <see cref="OpticalSystem.Fields"/>.
    /// </summary>
    public int FileFieldCount { get => _fileFieldCount ?? System.Fields.Count; init => _fileFieldCount = value; }

    private readonly int? _fileFieldCount;

    /// <summary>
    /// Adds each off-axis field's mirror image across the axis, −y, after the file's fields, and
    /// splits the weight between the two. A rotationally symmetric lens performs the same at y and
    /// −y, so its criterion is unchanged; a decenter or tilt breaks that symmetry, and a lens judged
    /// on one side of the field only can look better than it is on the other. OpticStudio's
    /// "Y-Symmetric" tolerancing fields have the same purpose.
    ///
    /// <para>With <paramref name="xy"/>, each also gets its two images on the x axis, (±y, 0), the
    /// weight shared four ways, as OpticStudio's "XY-Symmetric" fields: a decenter or tilt in x is then
    /// judged where it acts as one in y is. A field on the x axis is evaluated on the lens turned a
    /// quarter turn (<see cref="QuarterTurn"/>). A file's fields lie on the y axis; one off it is
    /// refused.</para>
    /// </summary>
    public static void MirrorFields(OpticalSystem system, bool xy = false)
    {
        var mirrored = new List<Field>();
        foreach (var f in system.Fields)
        {
            if (Math.Abs(f.X) > 1e-12) throw new NotSupportedException($"a field off the y axis, x = {f.X}, is not supported");
            if (Math.Abs(f.Y) < 1e-12) continue;
            f.Weight /= xy ? 4.0 : 2.0;
            mirrored.Add(new Field(-f.Y, f.Weight));
            if (xy)
            {
                mirrored.Add(new Field(0.0, f.Weight) { X = f.Y });
                mirrored.Add(new Field(0.0, f.Weight) { X = -f.Y });
            }
        }
        system.Fields.AddRange(mirrored);
    }

    public OpticalSystem System { get; }
    public GlassCatalog Catalog { get; }
    public LensModel Model { get; }

    /// <summary>Index of the medium after each surface, at each of the lens's wavelengths.</summary>
    public IReadOnlyList<double[]> Indices { get; }

    public double[] Nd { get; }
    public double[] NF { get; }
    public double[] NC { get; }

    /// <summary>The surface before the image: its thickness is the back focus.</summary>
    public int BackFocusSurface => System.Surfaces.Count - 2;

    /// <summary>Whether the medium after <paramref name="surface"/> is glass rather than air.</summary>
    public bool IsGlass(int surface) =>
        surface >= 0 && surface < System.Surfaces.Count && !System.Surfaces[surface].IsMirror
        && Math.Abs(Nd[surface] - 1.0) > 1e-9;

    /// <summary>A copy of the lens to perturb, sharing nothing with this one.</summary>
    public PerturbedLens Perturb() => new(this);
}

/// <summary>
/// One perturbed copy of a <see cref="NominalLens"/>. Tolerances change its surfaces and its
/// indices; <see cref="Build"/> makes the lens model to trace, with the perturbed indices in place
/// of the catalog's.
/// </summary>
public sealed class PerturbedLens
{
    internal PerturbedLens(NominalLens nominal)
    {
        Nominal = nominal;
        System = SystemCopy.Of(nominal.System);
        Indices = nominal.Indices.Select(a => (double[])a.Clone()).ToArray();
    }

    private PerturbedLens(PerturbedLens other)
    {
        Nominal = other.Nominal;
        System = SystemCopy.Of(other.System);
        Indices = other.Indices.Select(a => (double[])a.Clone()).ToArray();
        foreach (var (k, v) in other.GlassChanges) GlassChanges[k] = v;
        foreach (var (k, v) in other._elementMoves) _elementMoves[k] = v;
        foreach (var (k, v) in other._surfaceMoves) _surfaceMoves[k] = v;
    }

    /// <summary>A copy of the lens as it now stands, errors and moves included, sharing nothing with it: a compensator tries its settings on these.</summary>
    public PerturbedLens Clone() => new(this);

    public NominalLens Nominal { get; }
    public OpticalSystem System { get; }

    /// <summary>Index of the medium after each surface, at each wavelength; tolerances change these.</summary>
    public double[][] Indices { get; }

    /// <summary>The changes of n_d and V_d made so far to the glass after each surface.</summary>
    public Dictionary<int, (double Nd, double Vd)> GlassChanges { get; } = new();

    private readonly Dictionary<int, Placement> _elementMoves = new();
    private readonly Dictionary<int, Placement> _surfaceMoves = new();

    /// <summary>
    /// Moves the surface as part of its element or group - decentred or tilted in the barrel.
    /// Moves of the element compose in the order they are made.
    /// </summary>
    public void MoveWithElement(int surface, Placement move) => Add(_elementMoves, surface, move);

    /// <summary>
    /// Moves the surface on its own element - a wedge. It is made inside the element's move,
    /// whichever was applied first, so a wedged element tilted in its cell is both at once.
    /// </summary>
    public void MoveSurface(int surface, Placement move) => Add(_surfaceMoves, surface, move);

    private void Add(Dictionary<int, Placement> moves, int surface, Placement move)
    {
        if (Nominal.System.Surfaces[surface].IsPerturbed)
            throw new NotSupportedException($"surface {surface} is decentered or tilted in the design itself");
        moves[surface] = moves.TryGetValue(surface, out var sofar) ? move.Within(sofar) : move;
    }

    /// <summary>
    /// The lens model of the lens as it now stands. It reads <see cref="System"/> lazily, so it is
    /// to be used before the lens is changed again.
    /// </summary>
    public LensModel Build()
    {
        foreach (int s in _elementMoves.Keys.Union(_surfaceMoves.Keys))
        {
            var element = _elementMoves.TryGetValue(s, out var e) ? e : Placement.Identity;
            var own = _surfaceMoves.TryGetValue(s, out var o) ? o : Placement.Identity;
            own.Within(element).ApplyTo(System.Surfaces[s]);
        }
        // The design's stop: a decenter ahead of it moves the axial marginal ray, which would resize
        // the stop measured on this lens - to first order in y, not in x.
        return new(System, Nominal.Catalog, w => Indices[w], Nominal.Model.StopHeight);
    }
}
