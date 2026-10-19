# Changelog

Everything here is unreleased. The figures quoted when each step was made have moved as later work changed the defaults, so this file records what changed; [docs/verification.md](docs/verification.md) has the current figures.

## AberrationCalculator 04ae89a, WavefrontErrorCalculator 557ea496 (2026-10-19)

- AberrationCalculator moves from 11876ee to 04ae89a, its PR #8, and WavefrontErrorCalculator from 14b8cdce to 557ea496, its PR #8, which carries the same AberrationCalculator.
- **What changes:** `catalogs/Glass/MISC.AGF` only. Fifteen materials (CaF₂, sapphire, MgF₂, ZnSe, silicon, germanium, Infrasil, water and the plastics) had held dn/dT where SCHOTT's thermal model needs D0 = (2n/(n²−1))·dn/dT. Their thermal constants are now fitted to published measurements, and silicon and germanium carry their expansion coefficients.
- **The module's results are unchanged.** It traces at the catalogs' reference temperature, where the indices are the same as before; only these materials' change with temperature differs.

## WavefrontErrorCalculator 14b8cdce (2026-10-18)

- WavefrontErrorCalculator moves from d6c4c566 to 14b8cdce, its PR #7. AberrationCalculator stays at 11876ee, the copy WavefrontErrorCalculator is tested with.
- **What it adds:** the diffraction MTF over a lens's wavelengths (`DiffractionMtf`), from the autocorrelation of the pupil function, and the Debye integral it rests on, in its method.md §11.
- **The module's results are unchanged.** It doesn't use the diffraction MTF yet: the MTF criterion is still Kidger's geometric MTF. WavefrontErrorCalculator's changes are new code and a refactor inside `Diffraction`, which the module doesn't call. The RMS wavefront, the Zernike fits and the geometric MTF it uses are untouched.

## Visual Studio builds a plain clone (2026-10-15)

- A fresh clone failed to build in Visual Studio, though `dotnet build` and CI passed. `AberrationCalculator.Core.Series` was missing from the solution, and Visual Studio does not build a referenced project that is not in it. And the root `Directory.Build.props` set `TargetFramework` for every project, AberrationCalculator's multi-targeted ones included, which made Visual Studio refuse their netstandard2.0 build. The project is in the solution now, and our five projects name their framework themselves.
- Checked by building a fresh clone with Visual Studio 2022 itself (`devenv /Build`, after a restore): 8 of 8 projects, Debug and Release.

## Verification: the independent evidence first (2026-10-15)

- Section 2 of the verification put the comparison with OpticStudio first, under its name, as if it were what the optics were checked against. It now gives the evidence that needs no other program first: the libraries' own verification, against published tables, closed forms and independent methods, and the module's checks of each error against an exact answer, in a table. The comparison with OpticStudio follows as a cross-check made after the fact, and the README and building guide say so.
- The known limitations no longer list that OpticStudio's tolerancing was not compared, nor that glass errors were left out of the comparison: both took OpticStudio's tolerancing for a standard, which nothing establishes. The model is tested against the traced Monte Carlo.

## Verification: the MTF synthesis against more lenses, and the fisheye (2026-10-15)

- **The MTF synthesis was right; one sample was unlucky.** Five MTF cases had traced 1000 lenses on one shared seed, and the synthesis looked up to 3.1 points optimistic in yield. With 2000 lenses on another seed it is within 0.4 points in every case, with no lean either way, and its 97.7 % floor within 0.007. A first reading, from a check on the same unlucky seed, put the gap down to the irregularity; traced one error at a time and in pairs, the synthesis follows the irregularity exactly. `verification/run.sh` traces 2000 lenses for these cases now.
- **The fisheye against traced lenses.** Koch's 97.7 % level is 7.385 waves against 7.114 from 1000 traced lenses, 3.8 % high.
- `verification/run.sh` runs both.

## Boresight in degrees; graphs of the Monte Carlo (2026-10-15)

- **`boresight` is an angle in degrees**, the line of sight's error in object space: the image's shift over the image height per unit of field angle, the paraxial chief ray's height at the paraxial image over its slope in object space (the focal length, for an object at infinity). Limits are given in degrees: `--also boresight:0.05`. **`boresight-mm`** is the shift at the image, in lens units, as `boresight` was before. The verification, which measured it in millimeters, now runs `boresight-mm`, so its figures stand. The examples' `--also boresight:0.2` is now 0.2°, which on the 50 mm triplet is 0.175 mm, where it had been 0.2 mm.
- **`--plots <dir>`** with `--montecarlo` draws the run as SVG, with one `monte-carlo.html` holding every chart:
  - each criterion as a histogram of the traced lenses, with the count Koch's prediction expects in each bin, and as their cumulative fraction against the prediction's, marked with the nominal value, both 97.7 % levels and the limit;
  - each compensator's moves against a normal of the predicted mean and spread, marked with the predicted 95.4 % range and its travel.
- The examples' statistics run draws its 1000 lenses in `examples/plots`.
- **PNG copies.** `--plots` writes each chart as PNG too, at twice its 760 by 440 pixels, painted by SkiaSharp (MIT) from the same list of marks the SVG is written from. `tol` is the only part that references SkiaSharp, which NuGet restores on the first build; the libraries still depend on nothing outside the repository. Where SkiaSharp's native library cannot load, the SVGs are written alone and `tol` says why.

## Drawings of an element aligned at assembly; long title-block values (2026-10-15)

- **Every compensator on one line.** Reports name them together, the focus first: `compensators focus (surface 8, back focus); decenter x 5-6; decenter y 5-6; image tilt x; image tilt y`, or `compensators none`. The focus had been named only on the criterion line ("refocused by surface 8") and the others on a line of their own. Its travel lines say `focus (surface 8) moves` and `moved` where they had said "compensator".
- **The allocation table explains its marks.** A line under it says what `TST*` and the other x–y pairs are, and `adjusted` marks an error a compensator takes out, such as the decenter of an element `--adjust decenter` aligns, where it had been marked `loosest`.

- **`tol drawings --adjust`**, as `statistics` takes it. An element or group whose decenter or tilt is an alignment adjustment is drawn as "decenter x and y set at assembly to align the lens, within ±0.3 mm", with the travel when one is given, in place of the mount tolerance the set gives it. An allocation writes that tolerance at its loosest, since the adjustment takes it out, and the drawing had stated it as a mount tolerance. A group's tolerance around the element and the element's other mount tolerances are kept.
- **Title block.** A value too long for its cell, such as a project named after a long lens file, is set smaller, down to 1.8 mm text, and if it still does not fit, cut short with "..." and a warning. It had run past the frame.

## Rotationally invariant irregularity; asphere sag tables (2026-10-15)

- **`TRSI`, rotationally invariant irregularity**, in fringes: ISO 10110-5's C, the zonal error an asphere is left with. It is traced as Fringe Z9 less its constant, a peak to valley of fringes × λ/2 of sag, and analyzed and allocated like every other tolerance, within the irregularity grades (0.25 to 10 fringes). The default set does not carry it. OpticStudio has no operand for it, so the code is the module's own; `--export` refuses it as it refuses TIRR.
- Drawings state it as C in the form indication, `3/3(1/0.5)`, and in the US form as a note. A C larger than the irregularity is warned of.
- **Sag tables (ISO 10110-12).** Each aspheric surface's drawing has a table of h, z and ±Δz beside the view, and a note saying what it holds. Δz is the most the surface's power, irregularity and TRSI move the sag at that height, using the shapes the analysis traced; a test checks it against the perturbed surfaces themselves. The view moves left to make room.
- Where power is drawn as a radius tolerance, the sag table follows the radius as drawn, rounded down from the fringes, not the fringes; it had been a little looser than the radius callout (on the aspheric singlet allocated for 0.17 waves, 15.94 µm at the edge where ±0.5 mm on the radius allows 14.35).
- **Slope.** Sag tables have a ±Δs column in mrad: the most the surface's power, irregularity and TRSI tilt it at that height, as ISO 10110-5 allows a max slope deviation to be tabled with the sag. A test checks it against the perturbed surfaces' gradients. The settings `asphereSlopeMrad` and `asphereSlopeSamplingMm` add a slope deviation over a sampling length to every aspheric surface, as a note, for the mid-spatial ripple the analysis does not see; one tighter than the table allows is warned of.
- **4/σ(L).** An asphere's surface decenter is written into the centering indication as ISO 10110-6 has it, `4/3'(0.010)`, where it had been `4/3'  L 0.010` with a note repeating it. The US form keeps its note.
- PDF drawings spell out √, − and Σ in the asphere equation (`sqrt`, `-`, `sum`); they had come out as `?`.

## A tilt's pivot in the tolerance file; the conic tolerance on drawings (2026-10-15)

- **`pivot:d`** on a `TETX` or `TETY` line tilts the element or group about the point on the axis d lens units from its first vertex, toward the image when positive. Without it the pivot is the first vertex, as before. The pivot was only reachable from the library. `--write-tolerances` writes it back, the JSON report carries it as `pivot`, and a drawing's mount note states it.
- **TCON on drawings.** A conic tolerance was analyzed but left off the drawing without a word. It is now stated beside k in the aspheric surface's note, `k = -0.800000 ±0.01`, or as two limits when biased. On a spherical surface it has nothing to qualify and is left off with a warning.

## Fields judged on both sides; compensators beside the focus (2026-10-14)

- **Symmetric fields.** Each off-axis field is judged with its mirror image at −y, the weight shared, as OpticStudio's Y-Symmetric tolerancing fields are: a decenter or a tilt makes a lens differ on the two sides of its field, and a lens judged on one side saw a y decenter as better at one sign and worse at the other. The nominal lens and every rotationally symmetric error are judged as before. On the triplet's commercial set the 97.7 % level moved from 3.08 waves to 2.62, each agreeing with its own traced lenses. `--fields file` judges the file's fields alone.
- **XY fields.** `--fields xy` judges each field also at (±y, 0) on the x axis, the weight shared four ways, as OpticStudio's XY-Symmetric fields: an error in x is then judged as the same error in y is. A field on the x axis is traced on the lens turned a quarter turn. An image tilt about y then has fields to act on.
- **The design's stop.** Every perturbed lens keeps the nominal lens's stop radius (WavefrontErrorCalculator's `LensModel` stop height). Measured on each perturbed lens, a y decenter ahead of the stop resized it to first order and an x decenter did not.
- **The on-axis exit pupil** of a perturbed lens is on the right side of the image (WavefrontErrorCalculator d6c4c566). Its side was read off a small field's chief ray, which on a decentered or tilted lens is mostly the axial chief ray's departure: for one sign of some y errors it fell behind the image, and the axial field's RMS was some percent off (0.6588 waves for the triplet's last element tilted 0.2° about x, 0.6785 about y, at 0.486 µm). Analyses judged on the y axis alone were affected too; the verification is measured again.
- **Compensators beside the focus**, `--adjust`, repeatable: further air spaces (`space:S`), an element's or group's tilt and decenter in its cell (`tilt:F-L`, `decenter:F-L`), and the image plane's tilt (`image-tilt`), each with an optional travel. The focus is found first, then all are refined together by damped least squares on the wavefront. Each adjustment's move is predicted by its own quadratic model, reported with its travel, recorded in the Monte Carlo and the JSON report (`adjustments`), and kept within its travel by `allocate`.
- Image tilt is evaluated as each field's shift of focus to the tilted plane, about the image center: exact to first order for an RMS wavefront. With every field on the y axis a tilt about y has nothing to act on and stays at zero.
- On the triplet's commercial set with the last element's tilt and the image tilt as compensators, Koch's mean W² was 2.436 against 2.433 ± 0.054 from 300 traced lenses, the 97.7 % level 2.124 against 2.121, and the model check 0.6 %.
- The verification's figures are measured again in a later commit.

## Subtrees, not submodules (2026-10-14)

- AberrationCalculator (11876ee) and WavefrontErrorCalculator (04461f5c) are carried in `external/` as `git subtree`s, as WavefrontErrorCalculator, GhostAnalysis and LensSplitter carry AberrationCalculator. A plain clone, Visual Studio's clone or a ZIP download builds as it is; with submodules a plain clone had empty `external/` folders and did not build.
- `docs/building.md` says how to move them on with `git subtree pull`; CI no longer checks out submodules.

## Irregularity; power on drawings past what a test plate reads (2026-10-14)

- **`TIRR`, irregularity**, in fringes: the departure from the best-fitting sphere, half astigmatism (Fringe Z5) and half spherical (Fringe Z9 less its constant), each a peak to valley of fringes × λ/4 of sag over the same aperture as power. It is traced by AberrationCalculator's real ray trace, which now traces a surface's Zernike sag departure (its PR #7), and analyzed and allocated like every other tolerance.
- The default set carries it on every refracting surface and mirror at Smith 1985's grades: 10, 2, 1 and 0.25 fringes. Allocation keeps it between 0.25 and 10.
- Drawings state the allocated irregularity, where they had stated a quarter of the power: the triplet's least-cost set had drawn 6 fringes of irregularity beside 30 of power, which nothing analyzed. A set without `TIRR` gets `defaultIrregularity` (1 fringe) with a warning. The settings `irregularityRatio` and `minimumIrregularity` are gone.
- Power of more than `maximumPowerFringes` (10) on a curved surface is drawn as a radius tolerance, measured with a spherometer rather than counted on a test plate: the change of radius the allocated fringes allow, the smaller way, rounded down. ISO 10110 writes `R 21.249 ±0.15 CX` and `3/-(2)`; the US form puts the ± in the radius column and a dash under POWER, a column made wider for it.
- `--export` and `--export-trials` refuse a set with `TIRR`: a .zmx surface cannot state the departure.
- Every figure in the verification measured on the default set is measured again with it.

## The model in W against the model in W² (2026-10-14)

- Verification section 3.3: Koch's model in W and the module's in W², built from the same traced data, against 4000 traced lenses on six cases. The model in W² put the 97.7 % level within 2.1 % in every case; the model in W put it 11 to 107 % high on the lenses at the commercial grade, and came within 1.5 % only where the tolerances are small against the design's own error. `verification/WModel` runs it, and `verification/run.sh` includes it.

## The on-axis exit pupil of a perturbed lens; the module's scope (2026-10-14)

- WavefrontErrorCalculator 3f4b6b09 (its PR #4). On axis, a decentered or tilted lens's chief ray crosses the axis at an all but arbitrary place, and that crossing had been taken for the exit pupil. Where it fell near the image, the reference sphere shrank to nothing and W jumped by tens of waves from one focus to the next. The refocus could then stop in a spike: one traced lens of 300 on the biconvex singlet was recorded at 2.98 waves where its best focus gives 1.29. The exit pupil on axis is now the paraxial one, as WavefrontErrorCalculator's documentation always said. Centered lenses are unchanged bit for bit, and the triplet's traced verification run (4000 lenses, seed 11) is unchanged.
- The README, the user guide (section 3) and the theory (section 1) state the scope: lenses designed rotationally symmetric about one axis, with fields along y. The errors make the lenses traced non-symmetric, which the module handles; a design non-symmetric to begin with is outside what it was made and checked for.

## Drawing layout and UTF-8 output (2026-10-14)

- A thickness whose text is wider than the element is thick had both extension lines running through its text; such text now sits clear of their overshoot.
- Text is measured with Helvetica's and Helvetica-Bold's own widths, from Adobe's metrics, where every capital had counted as 667/1000 em. The ASME notes, all capitals, ran into the sheet's frame; they now wrap short of it.
- Tests: no dimension or extension line crosses text, and all text keeps 1 mm clear of the frame.
- `tol` writes UTF-8 to the console and to redirected files, and restores the console's code page on exit. On Windows a report saved to a file lost σ and √, which the console's code page (1252 or 437) lacks.

## Examples (2026-10-14)

- `examples/` holds what `tol` prints and writes on the test triplet, from `nominal` to `drawings`: eight reports, the JSON report, two tolerance files, the drawing settings, and every element drawn in ISO 10110 and in ASME style as PDF, DXF and SVG. `examples/run.sh` regenerates them.

## The boresight is not biased (2026-10-14)

- The verification reported Koch's mean W² for the boresight 8 % low against 1000 traced lenses, as a weakness of the departures' slopes. With more lenses it agrees: 32 000 traced lenses gave 0.08314 ± 0.00041 against 0.08325. The 1000 were a run of high draws. No code changed.
- `verification/run.sh` traces 4000 lenses for the EFL and boresight checks, not 1000, and the verification's section 5 and limitations are corrected.

## An untraceable pair no longer stops the model (2026-10-13)

- A pair of tolerances that leaves the lens untraceable at both maxima, though each traces alone, is traced again at half and then a quarter of its maxima. Its cross term is taken there, against the model's own terms at the same errors, for the criterion and for the compensator's move. Before, the whole model failed: on Laikin 9-3 under `--cross all`, after 58 minutes.
- A pair untraceable even at a quarter is left out, its cross term zero, with a warning naming it.
- `QuadraticModel.PartlyTraced` and `QuadraticModel.Untraced` list these pairs.

## MTF floors (2026-10-13)

- **`--criterion mtf:field:frequency:s|t`**: the geometric MTF at one of the lens's fields, a frequency in cycles per lens unit, sagittal or tangential. `--limit` is the least MTF; it also works with `--also mtf:…:limit`.
  - The MTF is WavefrontErrorCalculator's: Kidger's direct summation over the same rays as the RMS wavefront, every wavelength weighted, with no diffraction factor.
  - The lens is refocused for the sharpest image by the RMS wavefront, and the MTF is measured there.
- **W = √(1 − MTF)**, so W² = 1 − MTF. The sensitivity table, the contributions and the error budget show MTF drops, and the levels are printed as MTF floors, "97.7 % above".
- **The spread comes from a spot synthesis, not Koch's moments.** Each ray's landing point moves linearly with every error, using slopes from the sensitivity run, and Kidger's sum of the moved spot is taken for 20 000 lenses drawn.
  - Quadratics in the MTF, and in its logarithm, missed traced lenses badly because an MTF saturates.
  - Against 1000 traced lenses, in five cases on the triplet, the synthesis put the 97.7 % floor within 0.006 and the yield within 1.5 points.
- **The model check for an MTF** compares the synthesis with 12 traced lenses, as a fraction of their MTFs' scatter, and warns above 25 %.
- `ICriterion` gains `Quoted`, `FromQuoted` and `IsFloor`, for a criterion quoted in other terms than W.

## Cross terms from the wavefront, and documentation (2026-10-13)

- **`--cross wavefront` is the default.** An RMS wavefront's cross terms come from the slopes of its pupil samples between each tolerance's two ends, with no pair traced (theory, section 4.3). A tolerance whose ends change which samples are vignetted, by more than 10 % of those that arrive, keeps its traced pairs.
  - On 17 lenses the model check was better than with traced pairs on 14, the same on two, and 0.1 point worse on one. Where every pair was also traced, the 97.7 % level differed by 2 % at most.
  - Above 80 tolerances the old default, now `--cross auto`, traced only the x and y of each decenter and tilt, and was 29 % low on a 160° fisheye.
  - The triplet's 861 traced pairs became none; the fisheye's 80 minutes became 11 seconds.
  - The compensator's cross terms are traced only where the criterion's are. On the triplet, leaving out the others moved the predicted travel by 0.02 %.
- **Joint yield** (`--joint`): every criterion held to one higher yield, chosen so that the goal's fraction of lenses meets them all.
- **Least cost** finds its multipliers by Newton's method on the binding constraints: three criteria allocate in about 20 s instead of 5½ minutes.
- **One set of traced lenses for every criterion.** Each lens is perturbed and refocused once, and every criterion is measured on it, in the sensitivity run, the pair traces, the model check and the Monte Carlo. The model check runs on each criterion.
- **Fixes.**
  - Under `--preset optiland` the wavefront vector kept its mean, so its cross terms were wrong. It now always removes the mean, as the criterion does.
  - A space of no thickness, two surfaces in contact, got a thickness tolerance of zero range and stopped the run. It now gets none.
- **Documentation.** A user guide, the theory with summaries of the papers, building and testing, references, and verification, in `docs/`. `verification/run.sh` reruns the verification. The plan and the literature digest moved out of the repository.

## Several criteria (2026-10-12)

- `--criterion efl|bfl|boresight` judges a lens by its departure from the nominal lens: the paraxial effective or back focal length, or where the axial object point's image falls. A departure's cross terms come from its slopes, with no pair traced.
- `--also <criterion>:<limit>` adds a criterion with its own limit, repeatable. `statistics` predicts each and the fraction of lenses within every limit; `allocate` meets every limit.
- Allocation keeps to Burge's base, the tolerance any shop holds without effort, and rounds index and Abbe tolerances to the grades glass is sold in.
- Thickness tolerances are capped at a fifth of the thickness.
- Cemented partners share one diameter.
- Drawings: per-element title, part number, description and notes; coating specifications written out as notes; bevels sized from the diameter as Burge gives them.

## Tested on LensHH-LT's sample lenses (2026-10-12)

The eleven sample designs shipped with LensHH-LT were run through every command.

- Wide-angle fields: AberrationCalculator's ray–surface intersection and stop aimer were fixed ([AC #4](https://github.com/jaruiz6363/AberrationCalculator/pull/4), [AC #5](https://github.com/jaruiz6363/AberrationCalculator/pull/5)), and every field of Laikin 9-3 and the 160° fisheye now traces. A field that still cannot be traced is left out of the criterion with a warning.
- Every pair's cross term is evaluated for sets of up to 80 tolerances.
- The model check: `statistics` and `allocate` trace 12 lenses against the quadratic model, and warn when it is off.
- Monte Carlo focus moves and the travel limit are measured from the refocused nominal.
- Export writes F-number and object-NA apertures and object-height fields.
- Drawings: a flat land where a surface cannot reach the edge, edge margins drawn back from a negative edge, mirrors reported as not drawn.
- An independent review of the M2–M4 code found eight defects, all fixed and tested: errors in parallel loops crashing the tool, failed trials polluting the focus range, `normal:0` hanging, an allocation reported feasible while missing its goal, a silent NaN model, a negative-only range printed with the wrong sign, index and mount tolerances losing their bias on drawings, and doc comments that disagreed with the code.

## M4: element drawings (2026-10-12)

- `tol drawings` draws one sheet per piece of glass from a tolerance set, as PDF, DXF and SVG, to ISO 10110 (`--standard iso`) or US practice after ASME Y14.18 (`--standard asme`).
- Values not in the set come from a settings file; `tol settings` writes one with every default.

## M3: tolerance files, travel, budgets, reports (2026-10-12)

- `--tolerances <file>` reads a tolerance set, biased ranges and per-tolerance distributions included; `--write-tolerances` writes one.
- `--travel <mm>` limits the refocus. Koch predicts the travel from the compensator's own quadratic model, and allocation keeps lenses within it.
- Error budgets after Sinclair (`--tree kind|element`).
- `--report <file.json>` writes everything, for drawings and other programs.
- The spread of W² is drawn on the quadratic model (`--shape sampled`, the default), beside Pearson III, gamma and Koch's normal.
- `--polar` draws each x-y pair as one error in a random direction.

## M2: statistics and allocation (2026-10-12)

- `tol statistics` predicts the spread of the criterion with Koch's second-moment method; `--montecarlo <n>` checks it against n traced lenses.
- `tol allocate` sizes every tolerance so that a given fraction of lenses meets a limit, by equal effect (Hilbert, Smith) or least cost (Adams), rounded down to preferred numbers, and refitted.
- Smith 1985's laser recording lens is reproduced in the tests.

## M1: sensitivity (2026-10-12)

- `tol sensitivity` evaluates a grade's default set at both ends of each range, refocusing after each, and ranks the results.
- Surface power, wedge and decenter; thickness; index and Abbe number; element decenter and tilt.
- `tol evaluate` shows one perturbed lens field by field, and `--export` writes it as a .zmx.
- As a cross-check, the same perturbed lenses were computed in OpticStudio: the chief ray agrees to 2.5×10⁻⁷ mm at the field edge, and each field's RMS within its grid.

## M0: repository (2026-10-12)

- The solution, the two submodules with one AberrationCalculator build, and CI on Linux and Windows.
