# Building and testing

How to get the source, build the module and the `tol` tool, run the tests, and use the module from another project.

## Requirements

- The [.NET 8 SDK](https://dotnet.microsoft.com/download) or later, or Visual Studio 2022 with the .NET desktop workload.
- Windows, Linux or macOS. CI builds and tests on Windows and Linux.

## Getting the source

```bash
git clone https://github.com/jaruiz6363/TolerancingModule.git
cd TolerancingModule
```

Everything the build needs is in the repository, so a plain clone, Visual Studio's own clone, or GitHub's "Download ZIP" all build as they are: open `TolerancingModule.sln` in Visual Studio, or run `dotnet build`. The one package from outside, SkiaSharp, which `tol` uses to write its charts as PNG, NuGet restores on the first build, so that build needs a connection to nuget.org. The optics are carried in `external/` as `git subtree`s of known commits:

| Folder | Supplies |
|---|---|
| `external/AberrationCalculator` | The lens model, the file readers, glass catalogs, and paraxial and real ray tracing |
| `external/WavefrontErrorCalculator` | Wavefront error over the pupil, and its conventions; the geometric MTF |

### One AberrationCalculator

WavefrontErrorCalculator carries its own copy of AberrationCalculator, also as a subtree. Building both copies would put two versions of the same assemblies in one program. So `AberrationCalculator.Location.props`, at the root of this repository, sets `AberrationCalculatorDir` to `external/AberrationCalculator`. WavefrontErrorCalculator's `Directory.Build.props` looks for that file above itself and builds against it, and the nested copy is never built.


## Building

```bash
dotnet build -c Release
```

This builds every project in `TolerancingModule.sln`:

| Project | Output |
|---|---|
| `src/Tolerancing.Core` | `Tolerancing.Core.dll`: the tolerance model and engine |
| `src/Tolerancing.Drawings` | `Tolerancing.Drawings.dll`: element drawings and their PDF, DXF and SVG writers |
| `src/Tolerancing.Cli` | `tol`, the command-line tool |
| `tests/Tolerancing.Tests` | The tests |

`tol` is in `src/Tolerancing.Cli/bin/Release/net8.0/`. Run it with `dotnet tol.dll`, or with `tol.exe` on Windows. A `catalogs` folder beside it holds the glass catalogs AberrationCalculator bundles (SCHOTT, OHARA, HOYA, CDGM, SUMITA and others), and must stay with it.

To run without building separately:

```bash
dotnet run --project src/Tolerancing.Cli -c Release -- sensitivity tests/Tolerancing.Tests/TestData/cooke_triplet.zmx
```

### A stand-alone tool

To make a copy of `tol` that runs on a machine without .NET:

```bash
dotnet publish src/Tolerancing.Cli -c Release -r win-x64 --self-contained -o publish/win-x64
```

Use `linux-x64`, `osx-arm64` or another runtime identifier for other platforms. Copy the whole output folder, `catalogs` included.

## Testing

```bash
dotnet test -c Release
```

There are about 240 tests. On a 16-core machine the suite takes about 1½ minutes in Release. Most of that time is in the tests that trace hundreds of lenses in full to check the statistics against Monte Carlo.

To run part of the suite, filter by class:

```bash
dotnet test -c Release --filter "FullyQualifiedName~KochTests"
dotnet test -c Release --filter "FullyQualifiedName~DistributionTests|FullyQualifiedName~SpecialFunctionTests"
```

The test classes:

| Classes | What they check | Speed |
|---|---|---|
| `DistributionTests`, `SpecialFunctionTests` | The moments of each distribution against sampling; the normal, gamma and incomplete gamma functions | Fast |
| `KochTests`, `AllocationTests`, `BudgetTreeTests` | Koch's moments against the model's own Monte Carlo; allocation on synthetic models with known answers; budget trees | Fast |
| `MultiCriterionTests` | Several criteria, joint yield, shared traces | Fast to moderate |
| `ToleranceTests`, `TiltDecentreTests`, `PlacementTests`, `SystemCopyTests` | Each tolerance applied to a lens, against an exact answer: power, tilts, decenters, rigid moves | Fast |
| `OpticStudioComparisonTests` | A cross-check: perturbed lenses, refocus, chief rays and per-field RMS, set against the same lenses computed in a commercial program, OpticStudio | Moderate |
| `SensitivityTests`, `CompensatorTests`, `CriteriaTests`, `WavefrontCrossTermTests` | The sensitivity run, refocusing, departure criteria, cross terms from slopes | Moderate |
| `LensStatisticsTests` | Koch against hundreds of traced lenses on the test triplet; an allocated triplet built | Slow |
| `SmithTests` | Smith 1985's laser-disc lens and his change table | Moderate |
| `ElementDrawingTests`, `SheetTests`, `ShopPracticeTests`, `ThicknessCapTests` | Drawings in both standards, and their writers | Fast |
| `ToleranceFileTests`, `ReportTests` | Tolerance files and the JSON report | Fast |
| `CliTests`, `CliErrorTests` | The command-line tool end to end, and its error messages | Slow |
| `RobustnessTests`, `ReviewFindingTests` | Problems found on unlike lenses and in review, each kept as a test | Moderate |

Every lens in the tests is written for them, or transcribed from a paper (Smith 1985). None is taken from a commercial program's samples. The lenses are in `tests/Tolerancing.Tests/TestData`.

## Verification

The tests check each error against an exact answer, the statistics against a published example and small Monte Carlo runs, and, as a cross-check, the same perturbed lenses against a commercial program, OpticStudio. The larger checks behind [verification.md](verification.md), with thousands of lenses traced and a sweep over many lenses, are rerun with:

```bash
verification/run.sh <out-dir> [lens-dir ...]
```

It writes each command's output to `<out-dir>`, and takes about an hour on 16 cores. Each `lens-dir` given adds its `.zmx` and `.lhlt` files to the sweep. On Windows, run it from Git Bash.

## Continuous integration

`.github/workflows/ci.yml` builds and runs every test on `ubuntu-latest` and `windows-latest`, for each push to `main` and each pull request. A run takes about 25 minutes.

## Updating AberrationCalculator and WavefrontErrorCalculator

Each is a `git subtree` of a known commit. To move to the latest:

```bash
git subtree pull --prefix=external/AberrationCalculator https://github.com/jaruiz6363/AberrationCalculator.git main --squash
git subtree pull --prefix=external/WavefrontErrorCalculator https://github.com/jaruiz6363/WavefrontErrorCalculator.git main --squash
dotnet build -c Release && dotnet test -c Release
```

Update both together when WavefrontErrorCalculator itself has moved to a newer AberrationCalculator, so that the module's copy is at least as new as the one WavefrontErrorCalculator was tested with.


Run the whole test suite after an update. Several tests hold exact answers for how each error moves the lens, and some hold the figures of the cross-check with OpticStudio, so a change in the ray tracing breaks them.

## Using the module from another project

Reference `src/Tolerancing.Core/Tolerancing.Core.csproj`, and `src/Tolerancing.Drawings/Tolerancing.Drawings.csproj` for drawings. Both find AberrationCalculator and WavefrontErrorCalculator through `AberrationCalculatorDir` and `WavefrontErrorCalculatorDir`. A project outside this repository must set the same properties, by importing this repository's `AberrationCalculator.Location.props` or its own equivalent, so that one AberrationCalculator is built for everything. `NominalLens.Read` loads the glass catalogs from a `catalogs` folder beside the program. `tol`'s project copies them there with

```xml
<None Include="$(AberrationCalculatorDir)catalogs/**/*" CopyToOutputDirectory="PreserveNewest" LinkBase="catalogs" />
```

and a program of your own needs the same line.

The [user guide](user-guide.md), section 14, has an example of the library in use.

## Repository layout

| Path | Contents |
|---|---|
| `src/Tolerancing.Core` | The tolerance model and engine: lens copies, tolerances (with rigid moves, `Placement`), criteria, compensator, sensitivity, quadratic model, Koch statistics, Monte Carlo, allocation, .zmx export |
| `src/Tolerancing.Drawings` | Element drawings: the drawing model from a lens and its budget, ISO 10110 and ASME layouts, PDF, DXF and SVG writers |
| `src/Tolerancing.Cli` | `tol`, the command-line tool |
| `tests/Tolerancing.Tests` | The tests and their lenses |
| `verification` | The script that reruns the checks in verification.md, and the lenses written for them |
| `docs` | This guide, the user guide, the theory, the verification and the references |
| `external` | AberrationCalculator and WavefrontErrorCalculator, as subtrees |
