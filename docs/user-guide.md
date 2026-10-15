# TolerancingModule user guide

This guide covers `tol`, the command-line tool, and the library behind it. It explains what each command does, what every option means, how to read the output, and which files go in and come out. The methods behind the numbers are in [theory.md](theory.md).

Contents:

1. [The workflow](#1-the-workflow)
2. [Running tol](#2-running-tol)
3. [Lens files](#3-lens-files)
4. [Tolerances](#4-tolerances)
5. [Commands](#5-commands)
6. [Criteria](#6-criteria)
7. [The compensator](#7-the-compensator)
8. [How errors are drawn](#8-how-errors-are-drawn)
9. [Cross terms](#9-cross-terms)
10. [Reading the statistics](#10-reading-the-statistics)
11. [Allocation](#11-allocation)
12. [Element drawings](#12-element-drawings)
13. [Files tol writes](#13-files-tol-writes)
14. [Using the library](#14-using-the-library)
15. [Troubleshooting](#15-troubleshooting)

---

## 1. The workflow

Tolerancing answers one question: how loose can each part of a lens be made, so that enough of the lenses built still meet the specification? The commands follow the steps a designer takes (Ginsberg 1981; Smith 1985):

| Step | Command | What you get |
|---|---|---|
| Check the design | `tol nominal` | First-order data and RMS wavefront error for each field |
| See what matters | `tol sensitivity` | Each tolerance at both ends of its range, refocused, ranked by effect |
| Look at one lens | `tol evaluate` | A lens with chosen errors applied, field by field |
| Predict the yield | `tol statistics` | The spread of the criterion over all the lenses built, from Koch's method, optionally checked by Monte Carlo |
| Size the tolerances | `tol allocate` | Every tolerance sized so that a fraction of lenses (97.7 % by default) meets a limit |
| Check the allocated set | `tol statistics --tolerances` | The prediction for a set you have edited, and a Monte Carlo of it |
| Draw the parts | `tol drawings` | One ISO 10110 or ASME drawing per element, as PDF, DXF and SVG |

A typical session on a new lens:

```bash
tol nominal lens.zmx
tol sensitivity lens.zmx --top 15
tol allocate lens.zmx --limit 0.25 --method cost --write-tolerances lens.tol --report lens.json
# edit lens.tol by hand if you need to: a value your shop prefers, a bias
tol statistics lens.zmx --tolerances lens.tol --limit 0.25 --montecarlo 1000
tol drawings lens.zmx --tolerances lens.tol --out drawings
```

## 2. Running tol

Building is described in [building.md](building.md). From a clone, run the tool with:

```bash
dotnet run --project src/Tolerancing.Cli -c Release -- <command> <lens> [options]
```

or run the built `tol` executable directly. The examples here write `tol`.

- Options can be given in any order after the lens.
- Numbers are always read with a decimal point, whatever the machine's locale.
- An option the command does not know is an error, so a typo does not go unnoticed.
- Run `tol` with no arguments to print the usage summary.
- The exit code is 0 on success, 1 for an error in the input (a missing file, an infeasible option combination), and 2 for a command line `tol` cannot parse.

Lengths are in the lens's units, normally millimeters. Wavelengths are in micrometers. RMS wavefront error is in waves at each wavelength.

> **Windows console.** The output uses λ, µ, σ, ² and ±. If they come out as `?`, switch the console to UTF-8 with `chcp 65001`, or redirect the output to a file.

## 3. Lens files

**The lens must be rotationally symmetric as designed.** `tol` is meant for centered systems: spherical and rotationally symmetric aspheric surfaces about one optical axis, with the object, the stop and the image on it, and fields given along y. The tolerances then make each lens it traces non-symmetric, and those it handles in full. A design that is not symmetric to begin with (tilted or decentered surfaces, coordinate breaks, fold mirrors, off-axis mirrors, toroids or freeforms) is outside what the module was made and checked for, and its results there are not to be relied on.

`tol` reads a lens through AberrationCalculator, which opens these formats:

| Extension | Format |
|---|---|
| `.zmx` | Zemax OpticStudio sequential |
| `.seq` | CODE V |
| `.len`, `.osl` | OSLO |
| `.otx`, `.opt` | Optalix |
| `.lhlt` | LensHH-LT |
| `.json` | Optiland |

What `tol` takes from the file:

- **Surfaces** are numbered as in the file, with the object as surface 0. Every tolerance names surfaces by these numbers.
- **The image** is the last surface. The surface before it, whose thickness is the back focus, is the default compensator.
- **Fields and wavelengths**, with their weights, define the RMS criterion: the weighted RMS over every field and wavelength. Each off-axis field is judged on both sides of the axis, at y and at −y, its weight shared between them, because a decenter or a tilt makes a lens differ there (theory, section 2.3). `--fields xy` judges each also at (±y, 0) on the x axis, the weight shared four ways, so that an error in x is judged where it acts as the same error in y is. `--fields file` judges the file's fields alone.
- **The aperture** (entrance pupil diameter, F-number or object NA) is held fixed under every tolerance.
- **Semi-diameters** that the file holds fixed act as apertures, and clip rays (vignetting). Semi-diameters left on automatic follow the beam and clip nothing.
- **Glass** comes from the catalogs the file names.

A field that no ray of the nominal lens can get through, such as an 80° field behind a small stop, is left out of the RMS criterion with a warning:

```
warning: no ray of field 80 passes the nominal lens; left out of the criterion
```

The rest of the analysis goes on without it. If no field can be traced, `tol` stops.

## 4. Tolerances

### 4.1 Kinds of tolerance

The codes are OpticStudio's tolerance operands, so a table reads the way a designer already knows it. `TRSI` is the one exception: OpticStudio has no operand for it.

| Code | Error | Units | Applies to |
|---|---|---|---|
| `TFRN` | Power: the sag at the semi-diameter changes by fringes × λ/2 at the test wavelength | fringes | A surface |
| `TIRR` | Irregularity: the departure from the best-fitting sphere, half astigmatism and half spherical, each fringes × λ/4 of sag peak to valley | fringes | A surface |
| `TRSI` | Rotationally invariant irregularity, ISO 10110-5's C: a zone, fringes × λ/2 of sag peak to valley | fringes | A surface |
| `TRAD` | Radius | lens units | A curved surface |
| `TTHI` | Thickness or air space | lens units | The space after a surface |
| `TIND` | Refractive index n_d; the Abbe number stays | index | The glass after a surface |
| `TABB` | Abbe number V_d; n_d stays | Abbe units | The glass after a surface |
| `TCON` | Conic constant | — | A surface |
| `TSDX`, `TSDY` | Decenter of one surface on its element | lens units | A surface |
| `TSTX`, `TSTY` | Tilt of one surface about its vertex: the wedge between an element's faces | degrees | A surface |
| `TEDX`, `TEDY` | Decenter of an element or cemented group, moved rigidly | lens units | Surfaces first–last |
| `TETX`, `TETY` | Tilt of an element or group as a rigid body, about its first vertex or a pivot on the axis (section 4.3) | degrees | Surfaces first–last |

Notes on each:

- **Power** is found exactly: the curvature that changes the sag at the semi-diameter by the stated fringes is solved on the full sag, conic and aspheric terms included. The semi-diameter is the file's, or failing that the paraxial marginal plus chief ray height. One fringe is half a wavelength of sag at the test wavelength, 0.6328 µm unless `--test-wavelength` says otherwise.
- **Irregularity** is what a test plate or interferometer reads once power is taken out. Half of it is astigmatism (Fringe Z5, its cylinder along x and y) and half spherical (Fringe Z9 less its constant, which has no power and leaves the vertex in place), the split Williamson 1989 and Ginsberg 1981 make. Each half has a peak to valley of fringes × λ/4 of sag, over the same aperture as power. AberrationCalculator's real ray trace traces it as a sag added to the surface; the paraxial lens does not change. Its orientation is fixed, so astigmatism on two surfaces adds as if aligned, which leans to the cautious side. A .zmx cannot carry it, so `--export` and `--export-trials` refuse a set that has it.
- **Rotationally invariant irregularity** is the zonal error left by making an asphere, the same at every azimuth. It is traced as Fringe Z9 less its constant (6ρ⁴ − 6ρ²), with a peak to valley of fringes × λ/2 of sag. Z9 is orthogonal to power, so taking out the best-fitting sphere leaves it whole. Of the rotationally invariant Zernike terms it costs the most RMS wavefront for its peak to valley, so a tolerance sized on it is not too loose for a finer zone of the same peak to valley. It adds to TIRR's spherical half; it is not taken out of it. The default set does not carry it: add it to the tolerance file for the surfaces that need it, usually the aspheres. Allocation keeps it within the irregularity grades, 0.25 to 10 fringes. Like TIRR, a .zmx cannot carry it.
- **Index and Abbe** errors keep the glass's partial dispersions, as OpticStudio's model glass does.
- **Tilt signs** are OpticStudio's: `TSTX +0.1` here is `TSTX +0.1` there. A tilt about x turns the surface in the y–z plane.
- **Surface decenter** on a sphere is the same error as a tilt. The default set puts it only on conic and aspheric surfaces.
- **Element tolerances** move every surface of the element together, and nothing beyond it moves.
- An **x and y pair** (TSTX and TSTY of one surface, TEDX and TEDY of one element) is one physical error in some direction. Allocation gives the pair one value, and `--polar` can draw it as one error (section 8).

### 4.2 The default set

Without `--tolerances`, a command builds a starting set from a shop grade (`--grade`, commercial by default). This is the "trial budget" of Smith 1985 and the default set of Hilbert 1979. It holds:

- power, irregularity and wedge (TSTX, TSTY) on every surface where the index changes, and on mirrors;
- surface decenter (TSDX, TSDY) on every conic or aspheric surface;
- the thickness of every glass and every air space, except the one the compensator moves;
- the index and Abbe number of every glass;
- the decenter and tilt, x and y, of every element, cemented group and mirror.

The values by grade:

| Kind | Low cost | Commercial | Precision | Extra precise |
|---|---|---|---|---|
| Power (fringes) | 50 | 10 | 5 | 1 |
| Irregularity (fringes) | 10 | 2 | 1 | 0.25 |
| Thickness (mm) | 0.5 | 0.2 | 0.1 | 0.05 |
| Index | 0.002 | 0.002 | 0.001 | 0.0002 |
| Abbe number (% of V_d) | 0.8 | 0.8 | 0.5 | 0.3 |
| Surface tilt (degrees) | 0.286 | 0.0573 | 0.0172 | 0.00573 |
| Surface decenter (mm) | 0.1 | 0.05 | 0.02 | 0.01 |
| Element decenter (mm) | 0.2 | 0.1 | 0.05 | 0.025 |
| Element tilt (degrees) | 0.2 | 0.1 | 0.05 | 0.02 |

- Power, irregularity, thickness, index and surface tilt are Smith 1985's Table 3. His tilts are 0.005, 0.001, 0.0003 and 0.0001 rad.
- The Abbe percentages are the glass makers' standard and closer steps.
- The element decenters and tilts are spaced between Ginsberg 1981's "fairly loose" and "fairly tight" values.
- No thickness tolerance is more than a fifth of the thickness itself, so a thin center or a narrow air space cannot close up.

### 4.3 Tolerance files

`--tolerances <file>` replaces the grade's set with your own. `--write-tolerances <file>` writes a set in the same form, so an allocated set can be edited and run again.

One tolerance per line:

```
CODE surfaces ±t [distribution]
CODE surfaces min max [distribution]
```

- `surfaces` is one surface number, or `first-last` for an element tolerance (`TEDX 1-2`).
- An element tolerance may span several elements, a group in the barrel (`TEDX 1-4`). Each element or group moves from the barrel's axis, independently of the others, so two groups move relative to each other by the difference. To hold one group as the datum, give it no group tolerance and put the whole relative error on the other. An element tolerance and a group tolerance on the same glass both apply, the element's move made on top of the group's.
- `±t` gives a symmetric range: write the positive half-width, `0.05`.
- `min max` gives any range. A range not centered on zero is a **bias**: for example, thicknesses made long to leave stock for rework.
- `distribution` overrides `--distribution` for that line: `uniform`, `endpoint`, or `normal` / `normal:k` (section 8).
- `pivot:d`, on a `TETX` or `TETY` line only, tilts the element or group about the point on the axis d lens units from its first vertex: positive toward the image, negative toward the object. Without it the pivot is the first vertex, as OpticStudio's. A barrel group that rocks on a seat 6.2 mm behind its front vertex is `TETX 1-4 0.05 pivot:6.2`.
- `#` starts a comment; blank lines are ignored.

An example:

```
# cooke_triplet.zmx, allocated, then edited
TFRN 1 10
TSTX 1 0.05
TSTY 1 0.05
TTHI 2 0.1
TTHI 4 -0.02 0.08 normal:3     # air space made long, normally distributed
TIND 1 0.0005
TABB 1 0.3
TEDX 3-4 0.025
TEDY 3-4 0.025
TETX 1-4 0.05 pivot:6.2       # the front group, rocking on its seat
```

A malformed line is reported with its line number:

```
tol: line 6: 'TTHI 4 0.1 0.05': the range is empty
```

## 5. Commands

### 5.1 `tol nominal <lens>`

First-order data, and the RMS and peak-to-valley wavefront error and Strehl ratio of each field at the primary wavelength, with how many pupil samples are vignetted.

```
$ tol nominal cooke_triplet.zmx
cooke_triplet.zmx: 9 surfaces
EFL 50.2693   BFL 43.6538   F/# 5.027   λ 0.5876 µm
field        RMS (waves)   P-V (waves)   Strehl   vignetted
       0       0.34712       1.19847        —           0
      10       1.40171       7.50315        —           0
      14       1.71712       8.91208        —           0
```

Strehl is shown only where WavefrontErrorCalculator judges its Maréchal estimate valid, which needs a small RMS.

### 5.2 `tol sensitivity <lens> [options]`

Each tolerance of the default set at its minimum and its maximum, one at a time, with the lens refocused after each. This is the sensitivity table of Ginsberg 1981 and Hilbert 1979.

| Option | Meaning | Default |
|---|---|---|
| `--grade lowcost\|commercial\|precision\|extraprecise` | The shop grade of the set | commercial |
| `--criterion rms\|efl\|bfl\|boresight\|mtf:field:frequency:s\|t` | What the lens is judged by (section 6) | rms |
| `--test-wavelength <µm>` | The wavelength fringes are counted at | 0.6328 |
| `--preset <name>` | WavefrontErrorCalculator convention: `reference`, `zemax`, `optiland`, `lenshhlt` | reference |
| `--primary` | The primary wavelength only, not every wavelength | |
| `--no-compensator` | No refocusing | |
| `--compensator <surface>` | Refocus with this surface's thickness instead of the back focus | |
| `--adjust <kind>[:travel]` | A compensator beside the focus, repeatable: `space:S`, `tilt:F-L`, `decenter:F-L` or `image-tilt` (section 7) | none |
| `--fields symmetric\|xy\|file` | Judge each field and its mirror image at −y, those and their images on the x axis as well, or the file's fields alone (section 3) | symmetric |
| `--top <n>` | List only the n worst tolerances | all |

```
$ tol sensitivity cooke_triplet.zmx --top 4
cooke_triplet.zmx: 48 tolerances, Commercial grade, fringes at 0.6328 µm
criterion   RMS wavefront (waves, polychromatic)
compensators focus (surface 7, back focus), nominal moved +0.043565 mm
nominal     1.362985   refocused 1.358671

type  surf        min        max   criterion@min     change   criterion@max     change    comp@min    comp@max
TEDY   3-4       -0.1        0.1        1.781232  +0.422561        1.781232  +0.422561     +0.0047     +0.0047
TEDX   3-4       -0.1        0.1        1.740936  +0.382265        1.740936  +0.382265     +0.0044     +0.0044
TEDY   6-7       -0.1        0.1        1.630605  +0.271934        1.630605  +0.271934     -0.0051     -0.0051
TTHI     4       -0.2        0.2        1.625287  +0.266616        1.113107  -0.245564     +0.1389     -0.1350

RSS of the worse increases 0.864307; estimated criterion 2.222978 (linear, independent: a ranking, not a yield)
```

How to read it:

- **nominal** is the file as saved; **refocused** is the nominal lens after the compensator. Every change is measured from the refocused nominal, so a file not saved at best focus does not count against any tolerance.
- **criterion@min, criterion@max** are the criterion at each end; **change** is the difference from the refocused nominal.
- **comp@min, comp@max** are how far the compensator moved, from where it refocuses the nominal lens.
- The rows are in order of their larger increase.
- A tolerance can make the lens *better* at one end. Here the air space after surface 4 made 0.2 mm longer partly cancels the triplet's own aberrations. That is why both ends are shown, and why the statistics are of the square of the criterion (theory, section 3). A decenter costs the same at both ends, since each field is judged on both sides of the axis (section 3).
- The x and y decenters of the middle element differ (0.38 and 0.42 waves), because the fields lie on the y axis. `--fields xy` judges them alike: 0.40 waves each.
- The **RSS** line combines the worse increases as if they were independent and linear. It ranks a set; it does not predict a yield. Use `statistics` for that.

### 5.3 `tol evaluate <lens> --apply CODE:surface:amount ... [options]`

One lens with any errors you choose applied together, shown surface by surface, field by field and wavelength by wavelength. Use it to understand one row of a sensitivity table, or to compare one lens with another program.

| Option | Meaning |
|---|---|
| `--apply CODE:surface:amount` | A tolerance and its amount, repeatable. Element tolerances take `first-last`: `TETX:1-2:0.05` |
| `--compensate` | Refocus with the back focus |
| `--export <file.zmx>` | Write the perturbed lens for OpticStudio, with coordinate breaks around each moved surface |
| `--test-wavelength`, `--preset`, `--primary` | As for `sensitivity` |

```
$ tol evaluate cooke_triplet.zmx --apply TFRN:3:10 --apply TTHI:2:0.2 --compensate
cooke_triplet.zmx TFRN:3:10 TTHI:2:0.2
EFL 49.930967   BFL 42.876617   criterion 1.586081   back focus moved -0.833450 mm
surf          radius     thickness   index@primary
   1    21.249468763   2.500000000     1.620409965
   2  -158.730158730   5.200000000     1.000000000
   3   -20.350659730   1.000000000     1.620040137
...
field   chief ray at the image, primary λ, launched at the paraxial pupil center (x, y)
    0      0.000000000    0.000000000
   10      0.000000000    8.794509510
...
field    wave(µm)      RMS (waves)
    0   0.4861327         0.615172
    0   0.5875618         0.358000
...
```

Unlike `sensitivity`, `evaluate` does not refocus unless you ask with `--compensate`.

### 5.4 `tol statistics <lens> [options]`

Koch's prediction of the spread of the criterion over all the lenses built to a tolerance set (theory, section 5). By default the set is the grade's; `--tolerances` gives your own.

| Option | Meaning | Default |
|---|---|---|
| `--grade`, `--test-wavelength`, `--preset`, `--primary`, `--fields` | As for `sensitivity` | |
| `--no-compensator`, `--compensator <surface>` | As for `sensitivity` | back focus |
| `--tolerances <file>` | Your own set instead of the grade's (section 4.3) | |
| `--criterion rms\|efl\|bfl\|boresight\|mtf:field:frequency:s\|t` | What the lens is judged by | rms |
| `--limit <value>` | Report the yield at this value of the criterion, in its units; for an MTF, the least MTF (section 6.3) | |
| `--also <criterion>:<limit>` | A further criterion and its limit, repeatable (section 6.2) | |
| `--travel <mm>` | The compensator's travel either way (section 7) | unlimited |
| `--adjust <kind>[:travel]` | A compensator beside the focus, with its travel (section 7) | none |
| `--distribution uniform\|endpoint\|normal[:k]` | How each error is spread over its range (section 8) | uniform |
| `--polar disc\|ring\|rayleigh[:k]` | Draw each x–y pair as one error in a random direction (section 8) | x and y independent |
| `--cross wavefront\|all\|auto\|pairs\|none` | Which cross terms to evaluate (section 9) | wavefront |
| `--shape sampled\|gamma\|pearson\|normal` | How the spread's levels are read (section 10.3) | sampled |
| `--montecarlo <n>` | Trace n random lenses in full to check the prediction | 0 |
| `--seed <s>` | The Monte Carlo seed | 1 |
| `--no-check` | Skip the 12-lens model check (section 10.4) | |
| `--tree kind\|element\|none` | The error budget printed (section 10.2) | kind |
| `--report <file.json>` | Write everything as JSON (section 13) | |
| `--write-tolerances <file>` | Write the set as a tolerance file | |
| `--export-trials <dir>` | Write the first Monte Carlo lenses (up to 10) as .zmx, refocused | |
| `--plots <dir>` | Graphs of the Monte Carlo, as SVG and PNG and one HTML page (section 10.5) | |

The output is explained in section 10.

### 5.5 `tol allocate <lens> --limit <value> [options]`

Sizes every tolerance so that a fraction of lenses, 97.7 % by default, meets the limit (section 11). It takes all the `statistics` options, and these:

| Option | Meaning | Default |
|---|---|---|
| `--limit <value>` | The limit, in the criterion's units; for an MTF, the least MTF (required) | |
| `--yield <fraction>` | The fraction of lenses that must be within the limit | 0.977 |
| `--method equal\|cost` | Equal effect, or least cost (section 11.1) | equal |
| `--joint` | With `--also`: the yield is of lenses within every limit at once | |
| `--refits <n>` | Sensitivity reruns at the allocated values | 1 |
| `--no-round` | Keep exact values rather than rounding down to preferred numbers | |

The starting set (`--grade` or `--tolerances`) chooses *which* tolerances are allocated. Their values are replaced.

### 5.6 `tol drawings <lens> --tolerances <file> --out <dir> [options]`

One drawing per piece of glass, from a tolerance set (section 12).

| Option | Meaning | Default |
|---|---|---|
| `--tolerances <file>` | The set to draw from, normally what `allocate --write-tolerances` wrote (required) | |
| `--out <dir>` | Where to write the drawings (required) | |
| `--standard iso\|asme` | ISO 10110, or ASME Y14.18 with MIL-PRF-13830B | iso |
| `--settings <file.json>` | Cosmetics, irregularity, chamfers, coatings, title block | defaults |
| `--adjust <kind>[:travel]` | As for `statistics` (section 7), repeatable: an element or group decentered or tilted to align the lens is drawn as set at assembly, with its travel if given, not held to the mount tolerance the set gives it | none |
| `--test-wavelength <µm>` | As for the tolerances | 0.6328 |

### 5.7 `tol settings <file.json>`

Writes a drawing settings file holding every default, for you to edit (section 12.2).

## 6. Criteria

### 6.1 What a lens is judged by

`--criterion` chooses the criterion W. Smaller is better, and `--limit` is in its units. An MTF is the exception: larger is better, and `--limit` is the least MTF allowed (section 6.3).

| `--criterion` | W | Units |
|---|---|---|
| `rms` (default) | RMS wavefront error, combined over fields and wavelengths with the lens's weights | waves |
| `efl` | Change of the paraxial effective focal length | lens units |
| `bfl` | Change of the paraxial back focal length | lens units |
| `boresight` | The line of sight's error: where the image of the axial object point falls, the on-axis chief ray at the image, as an angle in object space | degrees |
| `boresight-mm` | The same, as the distance at the image from nominal | lens units |
| `mtf:field:frequency:s\|t` | The geometric MTF at one field, frequency and orientation (section 6.3) | none |

**RMS wavefront** is √(Σ w_f w_λ W²_rms / Σ w_f w_λ) over the fields and wavelengths, with the file's weights. Each W_rms is the standard deviation of the wavefront over the pupil, sampled by Gauss quadrature (Forbes 1988): 8 rings by 24 arms, exact for smooth wavefronts. `--primary` uses the primary wavelength alone. `--preset` chooses the reference sphere and other conventions to match another program.

**EFL, BFL and boresight** are departures from the nominal lens. A lens can be sharp and still have the wrong focal length (Smith 1985; Milby 2009).

- The compensator still refocuses for the sharpest image, as the lens is focused in use, and the departure is measured there.
- The departure is from the nominal lens refocused the same way. The output gives that nominal value.
- EFL and BFL are paraxial, so decenters and tilts, which change them only to second order, show no effect on them.
- With the back focus as the compensator, the BFL's change *is* the compensator's move, and `tol` says so. For a fixed-focus lens, use `--no-compensator`.
- Boresight needs an on-axis field (a field of height 0). In degrees it is the image's shift over the image height per unit of field angle: the paraxial chief ray's height at the paraxial image over its slope in object space, which is the focal length for an object at infinity. A 0.05 mm shift on a 50 mm lens is 0.057°. `boresight-mm` gives the shift itself.

### 6.2 Several criteria at once

`--also <criterion>:<limit>` adds a further criterion with its own limit. It can be repeated:

```bash
tol allocate lens.zmx --limit 2.2 --also efl:0.25 --also boresight:0.2 --method cost     # boresight within 0.2°
```

- `statistics` predicts each criterion, and the fraction of lenses within every limit at once. A Monte Carlo counts that fraction from the lenses it traces.
- `allocate` meets every limit at the goal's yield. If even the tightest set misses, `tol` names the criterion that fails:
  ```
  NOT FEASIBLE: every tolerance at its tightest still misses the goal: EFL change (mm) 0.1097 mm against 0.1
  ```
- Each lens is refocused once, for the sharpest image, and every criterion is measured on that lens. The models are exactly those each criterion would have alone.
- Name each criterion once: `--criterion efl --also efl:0.2` is an error.

**Each criterion alone, or all at once.** By default each criterion is met by 97.7 % of lenses on its own. Fewer lenses meet them all, since the same errors drive every criterion, so `tol` prints the joint fraction:

```
            within every limit at once: 98.0 % of lenses (each yield above is of one criterion alone)
```

With `--joint`, the yield is of lenses within every limit at once. Every criterion is then held to one higher yield, chosen so that the goal's fraction meets them all. The output says what that yield was:

```
            97.7 % within every limit at once: each criterion held to 98.96 %
```

### 6.3 An MTF floor

`--criterion mtf:field:frequency:s|t` judges the lens by its MTF at one field, one spatial frequency and one orientation, and `--limit` is the least MTF a lens may have:

```bash
tol statistics lens.zmx --criterion mtf:0:20:t --limit 0.3
tol allocate lens.zmx --criterion mtf:14:10:s --limit 0.3 --method cost
tol allocate lens.zmx --limit 2.2 --also mtf:0:20:t:0.3
```

| Part | Meaning |
|---|---|
| `field` | One of the fields the lens file lists, in its units. Any other value is an error that lists the lens's fields |
| `frequency` | Cycles per lens unit at the image: cycles/mm for a lens in millimeters |
| `s` or `t` | Sagittal (lines running radially, modulation across them in x) or tangential (modulation in y, the meridian) |

**What is computed.** The geometric MTF, by Kidger's direct summation over the spot (Kidger 2002, section 4.5; theory, section 3.4), from WavefrontErrorCalculator. It is the geometric MTF alone, **with no diffraction factor**, so it is not the MTF a diffraction-based program prints.

- The rays are those of the RMS wavefront: aimed as it aims them, stopped by the same apertures. A vignetted ray is left out of both.
- Every wavelength is summed with the file's weights; `--primary` uses the primary wavelength alone.
- The compensator refocuses for the sharpest image by the RMS wavefront, as the lens is focused in use, and the MTF is measured there. It does not refocus for the best MTF at this one frequency.

**Where it holds.** A geometric MTF needs aberrations well above the diffraction limit. Close to it, the geometric MTF tends to 1 at every frequency the lens can pass and stops seeing the errors. For a lens near the diffraction limit, use the RMS wavefront.

**Choosing the frequency.** Pick one where the nominal MTF is well above the floor, as a specification would. Near a dip of the nominal MTF, errors often *raise* the MTF, and the statistics, though still correct, say little about the lens's quality.

**The output.** The model's terms are in W² = 1 − MTF, so the contributions and the error budget are MTF drops. The levels are MTF floors, read "97.7 % above":

```
criterion   MTF T at 20 cy/mm, field 0
compensators focus (surface 7, back focus) for the sharpest image
note        an MTF is modeled as W² = 1 − MTF: its W² terms below are MTF drops
...
synthesis   nominal MTF 0.57499   mean MTF 0.51334   σ(MTF) 0.12504  skew -0.07   (20000 lenses drawn on the linear spot)
            84.1 % above 0.38574   97.7 % above 0.25330 (sampled)   yield at 0.3: 94.6 %
model check 12 lenses traced against the spot synthesis: within 5.5 % RMS, 14.6 % at worst of the MTF's scatter
Monte Carlo 1000 lenses, seed 9: mean MTF 0.50824 ± 0.00419 against the synthesis 0.51334   σ(MTF) 0.13241  skew -0.16
            median 0.51362   84.1 % above 0.37598   97.7 % above 0.23123   yield at 0.3: 91.5 %
```

**How the spread is found.** An MTF saturates: once other errors have spread the spot, a further error changes it less. No quadratic model can follow that, so for an MTF `tol` does not use Koch's moments. It moves every ray of the spot linearly with every error, using the slopes from the sensitivity run, and takes Kidger's sum of the moved spot for each of 20 000 lenses drawn (theory, section 3.4). The sensitivity run is all it traces. The cross terms come from the same synthesis, whatever `--cross` says, and the allocation steers by them.

The model check compares the synthesis with 12 traced lenses as a fraction of the MTF's scatter among them, and warns above 25 % (section 10.4). On the test triplet, in five cases, the synthesis put the 97.7 % floor within 0.007 of 2000 traced lenses and the yield within 0.4 points, with no lean either way ([verification.md](verification.md), section 7).

## 7. Compensators

A compensator is an adjustment made after the lens is built, to bring back the best performance it can. `tol` always refocuses: a thickness, the back focus by default, set to minimize the criterion (or, for a departure or an MTF, the RMS wavefront). Beside the focus it can make other adjustments, solved together with it.

- `--compensator <surface>` refocuses with another thickness, for example an air space a focusing group moves along.
- `--no-compensator` analyzes the lens as built, with no adjustment.
- `--travel <mm>` limits the focus to that far either way from where it refocuses the nominal lens.
- `--adjust <kind>[:travel]`, repeatable, adds an adjustment beside the focus:

| `--adjust` | Adjusts | Units |
|---|---|---|
| `space:S` | The air space (or any thickness) after surface S, as a second focusing motion | mm |
| `tilt:F-L` | The element or cemented group from surface F to L, tilted in its cell about its first vertex, in x and in y | degrees |
| `decenter:F-L` | The same element or group, shifted across the axis in its cell, in x and in y | mm |
| `image-tilt` | The image plane, a detector, tilted about the image center in x and in y | degrees |

  A number after another colon limits the adjustment to that far either way from zero, its nominal setting: `tilt:6-7:0.5` lets the last element of a triplet tilt half a degree either way. For example, a lens whose last element is aligned in its cell, focused, and imaged on a shimmed detector:

  ```
  tol statistics lens.zmx --adjust tilt:6-7:0.5 --adjust image-tilt:1 --montecarlo 1000
  ```

- **How they are set.** The focus is found first, alone, as it always was. Then the focus and every adjustment are refined together by damped least squares on the wavefront, until the criterion stops falling (theory, section 2.2). An adjustment that reaches its travel is held there, and a lens that ran out of any travel is counted in the Monte Carlo.
- **Image tilt** is evaluated, field by field, as the shift of focus the tilted plane makes where that field's chief ray meets it. For an RMS wavefront that is exact to first order. The plane turns about the image center, where the axial field's chief ray lands. With every field on the y axis, as a rotationally symmetric lens's file has them, a tilt about y has nothing to act on, and stays at zero; `--fields xy` gives it fields to act on.
- **What each move needs.** Each adjustment's move, like the focus's, is predicted from its own quadratic model, and reported as a mean and spread, with the travel that covers 95.4 % and 99.7 % of lenses:
  ```
  focus (surface 7) moves +0.0007 ± 0.5854 mm: ±1.1716 mm for 95.4 %, ±1.7570 mm for 99.7 %
  tilt x 6-7 moves -0.0009 ± 0.5845 deg: ±1.1698 deg for 95.4 %, ±1.7543 deg for 99.7 %
  ```
  The Monte Carlo reports the range each moved over.
- **Allocation keeps to every travel.** `allocate` keeps the goal's fraction of lenses within the focus's travel and within each adjustment's, beside the criterion's limit.

The thickness the focus moves is never given a tolerance itself. An adjustment of an element's tilt or decenter does not remove that element's tolerance: the error is made, and the adjustment then takes it out as far as it can.


## 8. How errors are drawn

Each error is assumed to lie anywhere in its range, independently of the others. `--distribution` sets how it is spread across the range, for every tolerance without a distribution of its own in the tolerance file:

| Distribution | Meaning | σ for a range ±t |
|---|---|---|
| `uniform` (default) | Equally likely anywhere in the range. OpticStudio's default, Hilbert 1979's, and Smith 1985's conservative case | t/√3 |
| `endpoint` | At one end or the other. The worst case a sensitivity run assumes | t |
| `normal` | A normal distribution cut off at ±2σ | about t/2.3 |
| `normal:k` | A normal cut off at ±kσ | about t/k |

`--polar` draws each x–y pair (a wedge's TSTX and TSTY, a decenter's TEDX and TEDY) as one error of size ρ ≤ t in a direction uniform around the circle, instead of two independent errors. This is Koch 1978's polar case, and how a real wedge or decenter behaves: it has one size and some direction.

| `--polar` | ρ | Meaning |
|---|---|---|
| `disc` | Uniform over the disc ρ ≤ t | Any error up to t, all round |
| `ring` | Always t | Every part at the tolerance, in some direction |
| `rayleigh[:k]` | Circular normal in x and y, cut at t = kσ (k = 2 by default) | The usual model of a centering error |

Under `--polar`, a pair needs one tolerance for both members. A pair with a distribution of its own in the tolerance file is drawn as two independent errors.

## 9. Cross terms

Two errors that make the same aberration add their wavefronts before the RMS is taken, so their combined effect is not the sum of their separate effects. The quadratic model carries this as a cross term for each pair of tolerances (theory, section 4). Cross terms add to the spread, not the mean. How much depends on the lens: they are 22 % of the variance of the triplet's commercial set, and 78 % in a Cassegrain, whose two mirrors' tilts and decenters largely cancel each other. Leaving them out can understate the spread badly.

| `--cross` | Cross terms evaluated | Cost |
|---|---|---|
| `wavefront` (default) | Every pair, for the RMS criterion, from the slopes of the pupil samples, with no pair traced | None for most pairs |
| `all` | Every pair, each traced as a lens with both errors | n(n − 1)/2 extra traces |
| `auto` | `all` for up to 80 tolerances, `pairs` beyond | |
| `pairs` | Only the x and y of each decenter and tilt | One trace per pair |
| `none` | None | None |

- **Departures** (EFL, BFL, boresight) always get every pair's cross term from their slopes, at no cost, whatever `--cross` says.
- **An MTF** has its spread from the spot synthesis, which holds every interaction itself (section 6.3). Its cross terms, which only steer the allocation, come from the synthesis under the default, at no cost.
- **`wavefront`**, the default, needs only the sensitivity run. On the triplet it traces none of the 861 pairs, and moves the 97.7 % level by 0.1 % from tracing them all. On the 126-tolerance fisheye it took about two minutes; tracing every pair had taken 80 minutes, on 110 tolerances before irregularity was in the set. A tolerance that changes which pupil samples are vignetted, by more than 10 % of those that arrive, keeps its traced pairs. Its model check was better than traced pairs' on 14 of 17 lenses, the same on one, and 0.1 point worse on two ([verification.md](verification.md), section 8).
- **`all`** traces every pair at both maxima. Use it to compare with `wavefront`, or for a lens far from quadratic, where the two can differ. A pair that leaves the lens untraceable at both maxima is traced nearer nominal, and left out with a warning if it cannot be traced at all (section 15).
- **`pairs`**, which `auto` uses above 80 tolerances, can miss most of the spread. On the 126-tolerance fisheye it put the 97.7 % level at 5.33 waves, against 7.39 from `wavefront` (and 7.42 with every pair traced, before irregularity was in the set). In a Cassegrain, the cross terms between the two mirrors' tilts and decenters are 78 % of the variance. Pairs alone gave a 97.7 % level of 1.10 waves where 1000 traced lenses gave 1.46, before irregularity was in the set.
- The compensator's own model, from which its travel is predicted, has cross terms only for the pairs that are traced. On the triplet, leaving the others out moved the predicted travel by 0.02 %.

## 10. Reading the statistics

The output of `tol statistics` on the triplet's commercial set:

```
$ tol statistics cooke_triplet.zmx
cooke_triplet.zmx: 48 tolerances, Commercial grade to start, fringes at 0.6328 µm
criterion   RMS wavefront (waves, polychromatic)
compensators focus (surface 7, back focus)
errors     uniform, x and y independent; cross terms wavefront

largest contributions to the mean of W² and its variance
tolerance           mean of W²     σ² of W²
TEDY 3-4            4.423E-001   1.565E-001
TEDX 3-4            3.950E-001   1.248E-001
TEDY 6-7            2.710E-001   5.874E-002
...

error budget by kind: mean and σ added to W², and each node alone as √(mean + 2σ) waves
All tolerances                      1.620E+000  1.456E+000   2.12870
  Surface form                      4.633E-002  5.244E-001   1.04644
  Surface centering (wedge)         4.410E-002  1.397E-002   0.26839
  Thickness and air spaces          9.537E-002  6.510E-001   1.18214
  Glass                             1.990E-003  7.509E-002   0.39010
  Element alignment                 1.432E+000  6.128E-001   1.63035
  Interactions (cross terms)        0.000E+000  1.019E+000   1.42763
  Biases                            0.000E+000  0.000E+000   0.00000

Koch        nominal 1.35867   √mean W² 1.86178   σ(W²) 1.456E+000  skew 0.80  (cross terms 49 % of the variance)
            mean+2σ 2.52534 (normal)   84.1 % 2.21442   97.7 % 2.62167 (sampled)
            focus (surface 7) moves +0.0029 ± 0.5833 mm: ±1.1695 mm for 95.4 %, ±1.7528 mm for 99.7 %
model check 12 lenses traced against the quadratic model: within 0.9 % RMS, 2.4 % at worst
```

### 10.1 Contributions

Each tolerance adds to the mean of W², the criterion squared, and to its variance. The table lists the twelve largest. The mean says how much a tolerance degrades the average lens. The variance says how much it spreads the lenses out. A tolerance can have a large mean and little variance: the triplet's x decenter of its middle element degrades every lens about equally.

### 10.2 The error budget

The tree groups the contributions, after Sinclair 1990.

- `--tree kind` groups them as Smith's change table does: surface form, surface centering (wedge), thickness and air spaces, glass, element alignment.
- `--tree element` splits each element into what the lens maker holds and what the mount holds, plus the air spaces.
- Every tree ends with two more leaves. **Interactions** is the cross terms' variance. **Biases** is the shift of the mean that biased ranges make.
- Each node gives the mean and σ it adds to W², and its **equivalent**: √(mean + 2σ), the criterion that node alone would cause in 97.7 % of lenses if it were normal. Equivalents combine in quadrature, as Smith's "RSS of class" does.

### 10.3 The prediction

| Value | Meaning |
|---|---|
| `nominal` | The refocused nominal lens's criterion |
| `√mean W²` | The RMS of the criterion over the lenses built |
| `σ(W²)`, `skew` | The spread of W² and its skewness |
| `cross terms … of the variance` | How much of the spread comes from interactions between tolerances |
| `mean+2σ (normal)` | Koch 1978's level: the criterion at the mean plus two standard deviations of W², as if W² were normal |
| `84.1 %`, `97.7 %` | The criterion that fraction of lenses are at or better than, by `--shape`. The 97.7 % level is Hilbert 1979's "probable worst case" |
| `yield at <limit>` | The fraction of lenses within `--limit` |

W² is a sum of squares, so its spread is skewed toward poor lenses, and the normal level understates the tail. `--shape` chooses how the levels are read:

| `--shape` | How |
|---|---|
| `sampled` (default) | W² drawn for 20 000 lenses on the quadratic model itself. Only the model is assumed |
| `pearson` | Pearson type III from the mean, variance and skewness |
| `gamma` | A gamma distribution from the mean and variance |
| `normal` | Koch's own: a normal from the mean and variance |

On the triplet against 4000 traced lenses, the 97.7 % level was 2.68 waves traced, 2.63 sampled, 2.63 Pearson, 2.64 gamma and 2.53 normal.

### 10.4 The model check

Koch's method is only as good as the quadratic model. A lens pushed far from its design by its tolerances may not be quadratic in them, for example when vignetting comes and goes. So `statistics` and `allocate` draw 12 random lenses, trace each in full, refocus it, and set its criterion beside the model's for the same errors:

```
model check 12 lenses traced against the quadratic model: within 0.9 % RMS, 2.4 % at worst
```

- **Within 10 % RMS:** trust the prediction.
- **10 to 15 %:** `tol` prints a caution. Check the prediction with `--montecarlo`.
- **Over 15 %, or any lens untraceable:** `tol` prints a warning to trust the Monte Carlo instead of Koch.

For a departure criterion, each error is taken as a fraction of the spread rather than of the lens's own value, since one lens can have almost no departure. An MTF is checked the same way, against the scatter of the MTF itself among the 12 lenses, and against the spot synthesis rather than a quadratic. Its bar is higher, 25 %, because the levels come from 20 000 lenses, whose errors partly average out: on the triplet, a set that missed by 26 % lens by lens still put the 97.7 % floor within 0.02 of 1000 traced lenses, on the safe side. `--no-check` skips the check. With `--also`, every criterion is checked on the same 12 lenses.

### 10.5 Monte Carlo

`--montecarlo <n>` builds n lenses at random, every error drawn at once, refocuses each, and traces it in full. It checks everything the prediction assumes: the quadratic model, the cross terms evaluated, and the shape of the spread.

```
Monte Carlo 200 lenses, seed 1: √mean W² 1.49666   σ(W²) 9.145E-001  skew 0.83   mean W² 2.2400E+000 ± 6.5E-002 against Koch 2.2362E+000
            median 1.43821   84.1 % 1.74517   97.7 % 2.16994   yield at 2.2: 99.0 %
            focus (surface 7) moved -3.1010 to +3.4909 mm
```

- The mean of W² is given with its standard error, so you can see whether it agrees with Koch's.
- A lens that cannot be traced scores +∞. It fails every limit, is left out of the moments, and is counted.
- Each trial has its own random generator, seeded from `--seed` and its number, so a run gives the same lenses however it is spread over threads.
- The 97.7 % level from n lenses is uncertain: with 200 lenses only about 5 lie beyond it. Use 1000 or more to judge a tail.
- `--plots <dir>` draws the Monte Carlo: for each criterion, a histogram of the traced lenses with the count Koch's prediction expects in each bin, and their cumulative fraction against the prediction's, both marked with the nominal value, the two 97.7 % levels and the limit; for each compensator, a histogram of its moves against a normal of the predicted mean and spread, marked with the predicted 95.4 % range and the travel. Each is written as SVG and as PNG, at twice its 760 by 440 pixels for slides and documents; `monte-carlo.html` holds them all, with the two 97.7 % levels and yields. It needs `--montecarlo`. The PNGs are painted by SkiaSharp; where its native library cannot load, `tol` writes the SVGs alone and says why.
- `--export-trials <dir>` writes the first lenses (up to 10) as .zmx files, refocused, and prints each one's criterion field by field, to open in OpticStudio and compare lens by lens. Glass errors and irregularity cannot be carried in a .zmx, which names glass by catalog and has no surface for the irregularity's departure; leave them out of the set to export.

## 11. Allocation

### 11.1 Methods

| `--method` | Rule | Source |
|---|---|---|
| `equal` (default) | Every tolerance, each x–y pair counted once, makes the same increase of the criterion, all scaled together until the goal is just met | Hilbert 1979's equal drop; Smith 1985 |
| `cost` | The least total cost Σ tightest/t that meets the goal | Adams 1988 |

Least cost loosens the tolerances the criterion is insensitive to, and tightens the sensitive ones, until a tightening costs as much as it saves. On the triplet allocated to 2 waves, it cost 7.53 against 7.94 for equal effect, for the same yield. Section 7 of the theory explains both.

### 11.2 Shop limits

No tolerance is allocated tighter than a shop can hold, or looser than saves anything:

| Limit | Value |
|---|---|
| Tightest | The extra-precise grade's value |
| Loosest | The low-cost grade's value, or Burge's base (OPTI 521) if that is tighter. The base is what any shop holds without effort, so loosening past it saves nothing: thickness ±0.2 mm, index ±0.001, Abbe ±0.8 %, and a surface tilt that deviates light by 6′ |
| Thickness | Never more than a fifth of the thickness |

A kind no grade covers (a radius in lens units, a conic) gets a tenth to ten times its starting value.

### 11.3 Rounding

A drawing states a tolerance in round numbers, so allocated values are rounded **down**, which keeps the set within its goal:

- index and Abbe number to the grades glass is sold in: ±0.001, ±0.0005, ±0.0003, ±0.0002 for index, and ±0.8, 0.5, 0.3, 0.2 % for Abbe number;
- everything else to the preferred numbers 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8 of each decade.

Rounding makes the predicted level a little better than the limit. `--no-round` keeps exact values.

### 11.4 Refits

The quadratic model is fitted at the starting tolerances, not where the allocation ends up. So after allocating, `tol` reruns the sensitivities at the allocated values and allocates again (Heshmaty-Manesh 1986). `--refits <n>` sets how many times; the default of 1 is usually enough.

### 11.5 The allocation table

```
goal        97.7 % of lenses within 2.2 waves (sampled spread)   method EqualEffect   refits 1

type  surf          min          max      tightest       loosest   mean of W²     σ² of W²
TFRN     1          -50           50             1            50   4.180E-003   1.247E-002 loosest
TST*     1        -0.15         0.15       0.00573        0.1612   1.667E-002   3.029E-002
...
TED*   3-4       -0.025        0.025         0.025           0.2   5.269E-002   1.278E-001 tightest
* an x-y pair, such as TST* for TSTX and TSTY: one error in some direction, given one value; its mean and σ² are the two together
cost (Adams, Σ tightest/t)  7.20
```

- `TST*`, `TSD*`, `TED*`, `TET*` are x–y pairs, TSTX and TSTY and so on: one error in some direction, which gets one value. The row's mean and σ² of W² are the two together, and the tolerance file writes both lines. A line under the table says so.
- `tightest` and `loosest` are the shop limits; a flag says when a value sits at one of them. `adjusted` marks an error a compensator takes out, such as the decenter of an element that `--adjust decenter` aligns: its value does not matter.
- A tolerance at its tightest is one the design is too sensitive to. If many are, consider a compensator, or a design less sensitive to them.
- The prediction and model check that follow are for the allocated set.

### 11.6 When the goal cannot be met

If even the tightest set misses the goal, `tol` says which criterion fails, and by how much, and holds every tolerance at its tightest:

```
$ tol allocate cooke_triplet.zmx --limit 1.4
...
            NOT FEASIBLE: every tolerance at its tightest still misses the goal: RMS wavefront (waves, polychromatic) 1.724 waves against 1.4
```

Here the nominal triplet is already at 1.36 waves, so even the extra-precise grade leaves too little margin.

The ways out are a looser limit, a lower yield, a compensator, or a less sensitive design. If everything at its loosest already meets the goal, every tolerance is set to its loosest.

## 12. Element drawings

A drawing's tolerances come from two files. The optical ones (power, irregularity, rotationally invariant irregularity, tilt, conic, thickness, index, Abbe number, mount) are read from the tolerance file (section 4.3): to change one, edit its line and draw again. Everything else comes from the settings file (section 12.2). Two things cannot be set yet:

- **A radius tolerance.** It is always worked out from the power in fringes (section 12.1). To get a given radius tolerance, write the fringes that give it on the TFRN line.
- **Values per element.** The edge diameter's tolerance and margin, the chamfers, the surface imperfections and the material classes are the same on every drawing. Per element, the settings change only the title, part number, description and notes.

### 12.1 What is drawn

`tol drawings` draws one sheet per piece of glass, written as PDF, DXF (R12, on layers) and SVG, with `drawings.pdf` holding them all.

```
$ tol drawings cooke_triplet.zmx --tolerances allocated.tol --out drawings
OPT-01  LENS 1-2               N-SK16     CT 2.500  Ø15.00  edge 0.96
      warning: edge thickness 0.96 mm: thin to make
OPT-02  LENS 3-4               F2         CT 1.000  Ø11.00  edge 2.55
OPT-03  LENS 6-7               N-SK16     CT 2.500  Ø15.00  edge 0.52
      warning: edge thickness 0.52 mm: thin to make
      warning: bevels up to 0.6 mm wide would take the whole 0.52 mm edge
3 element drawings (Iso10110) in drawings: one .pdf, .dxf and .svg each, and drawings.pdf with all
```

Two notations:

- **ISO 10110** (`--standard iso`, the default): an A4 sheet with the three-field table (left surface | material | right surface).
  - Surfaces: radius, clear aperture, chamfer, 3/A(B/C) form (ISO 10110-5), 4/σ tilt, or 4/σ(L) with the decenter L on an asphere (ISO 10110-6), 5/ surface imperfections, coating.
  - Material: n_d and ν_d with their tolerances, 0/ stress birefringence, 1/ bubbles, 2/ inhomogeneity and striae.
- **US practice** (`--standard asme`): an ANSI A sheet with a surface table and numbered notes in the manner of ASME Y14.18.
  - Power and irregularity in fringes, or a radius tolerance in place of power (below).
  - Scratch-dig per MIL-PRF-13830B.
  - Wedge as an edge thickness difference, with its beam deviation.

What comes from the tolerance set:

| Drawing value | From |
|---|---|
| Power | TFRN, restated at one test wavelength. More than `maximumPowerFringes` (10) on a curved surface is stated as a radius tolerance instead (below) |
| Irregularity | TIRR, restated at the same test wavelength |
| Rotationally invariant irregularity | TRSI, restated at the same wavelength: C in 3/A(B/C), and in the US form a note. A C larger than B is warned of, since C is a part of B |
| Surface tilt | TSTX/TSTY, in arcminutes, referred to the cylinder's datum axis |
| Asphere decenter | TSDX/TSDY: L in ISO 10110-6's 4/σ(L), as `4/3'(0.010)`; in the US form a note |
| Center thickness | TTHI, a biased range kept as its two limits |
| Index, Abbe number | TIND, TABB (as a percentage) |
| Conic constant | TCON, beside k in the aspheric surface's note. On a sphere it is analyzed but not drawn, with a warning |
| Mount decenter and tilt | TEDX/TEDY, TETX/TETY, as a note for the assembly drawing, with a tilt's pivot when it is not the first vertex. A group's are on each of its elements, marked with the group's surfaces. An element aligned at assembly (`--adjust decenter:5-6:0.3`) is noted as "decenter x and y set at assembly to align the lens, within ±0.3 mm" in place of its decenter tolerance |

- The section view is drawn through each surface's full sag, aspheres included, to a standard scale. Center thickness and edge diameter are dimensioned.
- Cemented pieces get one drawing each. Their shared face is uncoated and noted to match its mate, and they carry the group's mount tolerances.
- A surface that cannot reach the edge, such as a fisheye's deep front meniscus, gets a flat land from its clear aperture.
- Mirrors are not drawn, because a lens file gives no substrate.
- **Power past what a test plate is read to.** Power in fringes is counted against a test plate, which shops do up to about ten fringes; Burge's typical specification is 4 fringes of power and 1 of irregularity. A looser power, as least-cost allocation gives a surface the lens is insensitive to, is stated as a tolerance on the radius, measured with a spherometer. It is the change of radius that the allocated fringes allow over the clear aperture, the smaller of the two directions, rounded down to a preferred number: 30 fringes on the triplet's first surface, R 21.249 over 14 mm, is ±0.15 mm. ISO 10110 writes it on the radius, `R  21.249 ±0.15 CX`, with a dash for power in the form indication, `3/-(2)`; the US form puts it in the radius column and a dash under POWER.
- **Sag tables.** Each aspheric surface gets a sag table beside the view, as in ISO 10110-12's examples: the height h from the vertex to the clear aperture's edge in a round step (1, 2, 2.5 or 5 times a power of ten, at most 11 rows), the sag z there from the vertex along the axis (positive to the right, the standard's sign), ±Δz in µm and ±Δs in mrad. Δz is the most that the surface's power, irregularity and rotationally invariant irregularity, each at its full tolerance and with the worst signs together, move the sag at that height, and Δs the most they tilt the surface there: ISO 10110-5 allows both a form deviation and a max slope deviation to be given in a table with the sag. It uses the shapes the analysis traced: power through the full aspheric sag (or, where power is drawn as a radius tolerance, that radius as drawn, rounded down from the fringes), TIRR's astigmatic half at its largest azimuth and its spherical half, and TRSI's zone. So the table asks for nothing the analysis did not allow, and it is tighter than 3/A(B/C) alone, which lets an irregularity take any shape. On the triplet's first surface made aspheric, with 3 fringes of power, 1 of irregularity and 0.5 of TRSI, Δz is 1.03 µm at the edge: 0.95 from power, 0.08 from the astigmatism, and nothing from the zones, which have no sag there. Δs there is 0.66 mrad, most of it, 0.36, from the zones, which are steepest at the edge.

### 12.2 Settings

Everything a tolerance set does not hold comes from a settings file. `tol settings <file.json>` writes one with every default:

```json
{
  "standard": "Iso10110",
  "maximumPowerFringes": 10,
  "edgeMargin": 1,
  "diameterTolerance": 0.05,
  "chamferMin": null,
  "chamferMax": null,
  "imperfections": "5/3x0.16",
  "scratchDig": "60-40",
  "stressBirefringence": "0/20",
  "bubbles": "1/3x0.16",
  "inhomogeneity": "2/1;1",
  "coating": null,
  "coatings": {},
  "coatingSpecs": {},
  "asphereSlopeMrad": null,
  "asphereSlopeSamplingMm": null,
  "notes": [],
  "elements": {},
  "defaultFringes": 5,
  "defaultIrregularity": 1,
  "defaultThickness": 0.1,
  "defaultTiltArcmin": 3,
  "defaultIndex": 0.001,
  "defaultAbbePercent": 0.8,
  "project": "",
  "company": "",
  "drawnBy": "",
  "numberPrefix": "OPT-",
  "date": null
}
```

| Setting | Meaning |
|---|---|
| `standard` | `Iso10110` or `Asme`; `--standard` overrides it |
| `maximumPowerFringes` | The most power stated in fringes; more is stated as a radius tolerance (section 12.1) |
| `edgeMargin` | How much larger the edge diameter is than the largest clear aperture, on the diameter |
| `diameterTolerance` | The edge diameter's tolerance, all on the minus side, so the lens always fits its cell |
| `chamferMin`, `chamferMax` | Protective chamfer face widths. Left null, sized from the diameter as Burge gives it (0.3 mm to Ø25, 0.5 from Ø50, 1 from Ø150, 2 from Ø400), up to twice that |
| `imperfections`, `scratchDig` | Surface imperfections: ISO 10110-7, or MIL-PRF-13830B scratch-dig |
| `stressBirefringence`, `bubbles`, `inhomogeneity` | ISO 10110-2, -3 and -4 material classes |
| `coating`, `coatings` | One coating for every surface, or by surface number (`{"3": "AR1"}`) |
| `coatingSpecs` | What a coating name stands for (`{"AR1": "R < 0.5 % 450–650 nm, 0–15°, MIL-C-675"}`), written out as a note on each drawing that uses it |
| `asphereSlopeMrad`, `asphereSlopeSamplingMm` | A max slope deviation in mrad for every aspheric surface, over a sampling length in mm, written as a note (ISO 10110-5). It is for the mid-spatial ripple a shop leaves, which the analysis does not see; the sag table's ±Δs is the low-order part. A value tighter than the table allows is warned of |
| `notes` | Notes added to every drawing |
| `elements` | Per-element changes, keyed by the element's first surface (`"1"`) or its default number (`"OPT-01"`): `title`, `number` (your part number), `description` (an extra title-block line) and `notes` for that element only |
| `defaultFringes`, `defaultIrregularity`, `defaultThickness`, `defaultTiltArcmin`, `defaultIndex`, `defaultAbbePercent` | Values to use for tolerances the set left out. The drawing warns when it uses one |
| `project`, `company`, `drawnBy`, `numberPrefix`, `date` | The title block. The project defaults to the lens file's name. A value too long for its cell is set smaller, and if it still does not fit, cut short with "..." and a warning |

A key under `elements` that names no element, or two elements given one number, is an error. Notes that do not fit on the sheet are reported as warnings.

## 13. Files tol writes

| File | Written by | Contents |
|---|---|---|
| Tolerance file | `--write-tolerances` | The set, in the form `--tolerances` reads (section 4.3) |
| JSON report | `--report` | Everything about the analysis, below |
| `.zmx` | `evaluate --export`, `--export-trials` | A perturbed lens for OpticStudio, with coordinate breaks around each moved surface |
| Drawings | `drawings` | One `.pdf`, `.dxf` and `.svg` per element, and `drawings.pdf` |
| Settings | `settings` | The drawing settings with every default |

The JSON report is what the drawings and other programs read, so its names are kept stable. Its top level:

| Field | Contents |
|---|---|
| `lens`, `criterion`, `units`, `reference` | The lens file, the criterion and its units, and for a departure the refocused nominal value it is measured from |
| `compensatorSurface`, `statistics`, `method` | The compensator's surface, how errors were drawn, and the allocation method if any |
| `tolerances` | Each tolerance: `code`, `surfaces`, `min`, `max`, `distribution`, shop limits `tightest` and `loosest`, its share `meanW2` and `varianceW2`, and an element tilt's `pivot` (null at the first vertex) |
| `prediction` | `shape`, `nominal`, `rootMeanSquare`, `sigmaW2`, `median`, `level841`, `level977`, `limit`, `yield`, `crossVariance`, `biasShift` |
| `compensator` | The predicted move: `mean`, `sigma`, `needed954`, `needed997`, `allowed` |
| `adjustments` | Each adjustment beside the focus: `label`, `units`, its predicted move as for `compensator`, and the Monte Carlo's `monteCarloMin` and `monteCarloMax` |
| `byKind`, `byElement` | The two budget trees, each node with `meanW2`, `sigmaW2`, `equivalent`, `allowance` and `children` |
| `monteCarlo` | When run: `trials`, `seed`, its levels and yield, `untraceable`, the compensator's range and how many ran out of travel |
| `modelCheck` | `lenses`, `rmsError`, `maxError`, `untraceable`, `passes` |
| `feasible`, `cost` | For an allocation |
| `also` | Each further criterion: its name, units, limit, reference, prediction, Monte Carlo yield and model check |
| `jointYield`, `monteCarloJointYield` | With further criteria, the fraction within every limit at once |
| `joint`, `marginalYield` | For a `--joint` allocation, and the yield each criterion was held to |

## 14. Using the library

The command-line tool is a thin layer over `Tolerancing.Core`. A program can call it directly. The prediction for a lens's commercial set, and an allocation:

```csharp
using Tolerancing.Core;

var lens = NominalLens.Read("cooke_triplet.zmx");
var rms = new RmsWavefrontCriterion().WithoutUntraceableFields(lens.Model, out _);
var compensator = FocusCompensator.BackFocus(lens);
var items = DefaultTolerances.Create(lens, ShopGrade.Commercial, PowerFringes.HeNe, compensator);

// The quadratic model, and Koch's prediction from it.
var model = QuadraticModel.Build(lens, items, rms, compensator);   // cross terms from the wavefront slopes
var prediction = Koch.Predict(model, StatisticsModel.Default);
Console.WriteLine($"97.7 % of lenses within {prediction.ProbableWorst:F3} waves");

// Allocate to 2.2 waves at 97.7 %, least cost.
var goal = new AllocationGoal(2.2);
var budget = Budget.Allocate(lens, items, ShopLimits.FromGrades(lens, items), rms, compensator,
                             goal, StatisticsModel.Default, AllocationMethod.CostOptimal);
foreach (var item in budget.Allocation.Items) Console.WriteLine(item);

// Check it on 500 lenses traced in full.
var mc = MonteCarlo.Run(lens, budget.Allocation.Items, StatisticsModel.Default, rms, compensator, 500, seed: 1);
Console.WriteLine($"Monte Carlo yield at 2.2 waves: {mc.Yield(2.2):P1}");
```

An MTF criterion works in W = √(1 − MTF), as the tool does. `Quoted` turns W back into the MTF, and `FromQuoted` turns a floor into a limit on W:

```csharp
var mtf = (MtfCriterion)Criteria.Create("mtf:0:20:t", rms, lens, compensator);
var mtfModel = QuadraticModel.Build(lens, items, mtf, compensator);         // carries the spot synthesis
var spread = Koch.Predict(mtfModel, StatisticsModel.Default);                 // drawn on the synthesis
Console.WriteLine($"97.7 % of lenses above MTF {mtf.Quoted(spread.ProbableWorst):F3}");
Console.WriteLine($"yield at MTF 0.3: {spread.Yield(mtf.FromQuoted(0.3)):P1}");
```

The main types:

| Type | Role |
|---|---|
| `NominalLens` | A lens read from a file, and perturbed copies of it |
| `Tolerance`, `ToleranceItem` | A kind of error, and an error with its range and distribution |
| `DefaultTolerances`, `ToleranceSpec` | The grade's starting set, and tolerance files |
| `ICriterion` and its kinds | What a lens is judged by |
| `FocusCompensator` | The refocus |
| `Sensitivity` | The sensitivity run |
| `QuadraticModel` | The model of W² in the errors |
| `Koch`, `Prediction` | The prediction and its levels |
| `Allocator`, `Budget` | Allocation on a model, and the refit loop around it |
| `MonteCarlo`, `ModelCheck` | Traced lenses, to check the model |
| `BudgetTree`, `Report` | Error budgets and the JSON report |
| `Tolerancing.Drawings` | Element drawings and their writers |

## 15. Troubleshooting

**A run is slow.** The cost is mostly traces: two per tolerance for the sensitivities, plus one per pair for any cross terms that are traced. The default, `--cross wavefront`, traces almost none for the RMS criterion; `--cross all` or `auto` on a set of 80 tolerances traces 3160 pairs. A Monte Carlo traces every lens in full, so 1000 lenses of a wide-angle lens take minutes.

**"the lens is not quadratic in these tolerances".** The model check found the model more than 15 % off. Koch's prediction cannot be trusted, so run `--montecarlo` and judge by that. The usual causes are tolerances so loose that rays vignette or the lens changes character, as with a fisheye at the commercial grade. Tighter tolerances, or an allocation (which tightens what matters), usually bring the model back.

**"pair(s) could not be traced at both maxima".** A pair of errors at the ends of their ranges leaves the lens untraceable, though each traces alone. It happens only to pairs that are traced: under `--cross all` or `auto`, or under the default for a tolerance that changes the vignetting. `tol` traces the pair again with both errors at half their maxima, then a quarter, and takes the cross term there, against the model's own terms at the same errors. The run goes on, and the note names the pairs.

**"pair(s) could not be traced together even at a quarter of their maxima".** Then the cross term is left out, as zero, and the warning names the pairs. The prediction may understate the spread. The tolerances are too loose for this lens: tighten the two named, and confirm with `--montecarlo`, whose untraceable lenses are counted as failures.

**"NOT FEASIBLE".** Even the tightest set misses the goal. See section 11.6.

**The Monte Carlo and Koch disagree in the tail but not the mean.** Check the model check first. If the model is good, the difference is usually the shape of the spread: compare `--shape sampled` with the Monte Carlo, and use more lenses, since a 97.7 % level from a few hundred lenses is itself uncertain.

**"the spot is not linear in these tolerances".** For an MTF, the synthesis missed 12 traced lenses by more than 25 % of their scatter. Run `--montecarlo` and judge by that. It can follow an allocation that loosens a tolerance the MTF barely notices to first order: on the triplet, the equal-effect set for an MTF of 0.3 opened the middle element's decenter to ±0.1 mm and drew the warning, while the least-cost set passed at 2.2 %.

**"MTF … the lens has no field".** An MTF criterion names one of the fields in the lens file, by its value. The message lists them.

**The mean MTF of the built lenses is above the nominal.** The frequency is near a dip of the nominal MTF, where errors move the spot out of the dip as often as into it. Choose a frequency where the nominal MTF is well above the floor (section 6.3).

**The BFL criterion shows nothing but the compensator's move.** With the back focus as the compensator, it is. Use `--no-compensator`, or `--compensator` with another air space.

**Decenters show no effect on EFL or BFL.** They are paraxial quantities, and a decenter or tilt changes them only to second order.
