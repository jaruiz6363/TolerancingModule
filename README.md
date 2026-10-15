# TolerancingModule

A general-purpose lens tolerancing module, as a .NET library and a command-line tool, `tol`. Load a rotationally symmetric design made of spheres and even aspheres, tolerance it, and draw its elements for fabrication:

- **Sensitivity:** each tolerance at both ends of its range, the lens refocused after each.
- **Statistics:** the spread of the criterion over every lens built to a tolerance set, predicted by Koch's second-moment method (1978), and checked against traced lenses by Monte Carlo.
- **Allocation:** tolerances sized so that a given fraction of lenses meets one limit or several, by equal effect or least cost, rounded to preferred numbers and the grades glass is sold in.
- **Drawings:** one sheet per element for vendors, to ISO 10110 or US practice, as PDF, DXF and SVG.

The criterion is the polychromatic RMS wavefront error, a departure from the nominal lens (effective focal length, back focal length or boresight), or the geometric MTF at one field, frequency and orientation, held above a floor.

It is meant for lenses designed rotationally symmetric about one optical axis: centered spherical and rotationally symmetric aspheric surfaces, with the object, the stop and the image on the axis. The lenses it traces are not symmetric, since each error decenters or tilts something; a design that is not symmetric to begin with, with tilted or decentered surfaces, fold mirrors, off-axis mirrors or freeforms, is outside what it was made and checked for ([user guide](docs/user-guide.md), section 3).

The optics come from two libraries, carried in `external/` as subtrees:

- [AberrationCalculator](https://github.com/jaruiz6363/AberrationCalculator): the lens model; readers for .zmx, .lhlt, CODE V, OSLO and other formats; glass; paraxial and real ray tracing.
- [WavefrontErrorCalculator](https://github.com/jaruiz6363/WavefrontErrorCalculator): wavefront error, Zernike fits and the geometric MTF.

## Quick start

The build needs the .NET 8 SDK or later.

```bash
git clone https://github.com/jaruiz6363/TolerancingModule.git
cd TolerancingModule
dotnet build -c Release
```

Or open `TolerancingModule.sln` in Visual Studio 2022 and build: everything it needs is in the repository, except SkiaSharp for `tol`'s PNG charts, which NuGet restores on the first build.

Then, on the test triplet:

```bash
TOL="dotnet src/Tolerancing.Cli/bin/Release/net8.0/tol.dll"
LENS=tests/Tolerancing.Tests/TestData/cooke_triplet.zmx

$TOL sensitivity $LENS --top 5                  # the worst five tolerances of the commercial grade
$TOL statistics  $LENS --montecarlo 200         # the spread over built lenses, and a check
$TOL allocate    $LENS --limit 2 --method cost --write-tolerances allocated.tol --report allocated.json
$TOL drawings    $LENS --tolerances allocated.tol --out drawings
```

`$TOL` with no arguments lists every command and option.

## Documentation

- [User guide](docs/user-guide.md): every command and option, the files read and written, how to read the output, and the library.
- [Theory](docs/theory.md): the methods and the papers they come from, and where the module departs from them.
- [Verification](docs/verification.md): how the results have been checked: the optics in their own libraries against published tables, closed forms and independent methods; each error against an exact answer; the statistics against thousands of traced lenses and a published example; and, as a cross-check, a commercial program. What was found, and the known limitations.
- [Building and testing](docs/building.md): the build, the tests, CI, and using the module from another project.
- [References](docs/references.md): the papers, standards and software.
- [Examples](examples/README.md): every report and file `tol` writes, drawings included, on a Cooke triplet, and an aspheric singlet drawn with its sag table.
- [Changelog](CHANGELOG.md): what changed, step by step.

## Layout

| Path | Contents |
|---|---|
| `src/Tolerancing.Core` | Tolerances, criteria, compensator, sensitivity, the quadratic model, Koch statistics, Monte Carlo, allocation, .zmx export |
| `src/Tolerancing.Drawings` | Element drawings: the drawing model, ISO 10110 and ASME layouts, PDF, DXF and SVG writers |
| `src/Tolerancing.Cli` | `tol`, the command-line tool |
| `tests/Tolerancing.Tests` | Tests. Every lens in them is written for them or transcribed from a paper, never taken from a program's samples |
| `verification/` | `run.sh`, which reruns the checks in the verification document; the lenses it uses; and `WModel`, which builds Koch's model in W beside the module's in W² and sets both against traced lenses |
| `examples/` | What `tol` prints and writes on the test triplet and an aspheric singlet: reports, tolerance files, the JSON report, and element drawings in ISO 10110 and ASME style; `run.sh` regenerates them |
| `docs/` | The documents above |
| `external/` | AberrationCalculator and WavefrontErrorCalculator, as subtrees |

## License

MIT

`tol` uses [SkiaSharp](https://github.com/mono/SkiaSharp) (MIT), restored from NuGet, to write its charts as PNG. The libraries, `Tolerancing.Core` and `Tolerancing.Drawings`, depend on nothing outside this repository.
