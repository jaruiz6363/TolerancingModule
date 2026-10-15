# Verification

How the module's results have been checked, what was found, and where they are known to be weak. Every number here can be reproduced: the tests hold the first kind, and `verification/run.sh` reruns the rest ([building.md](building.md)).

The figures in sections 3 to 8 were measured on October 14, 2026, at module commit 51cf46a, with AberrationCalculator 11876ee and WavefrontErrorCalculator d6c4c566. Irregularity is in the default set, each field is judged with its mirror image at −y (section 3.4), and every lens built with errors keeps the design's stop and its on-axis exit pupil (section 10). Section 8.2, and the Laikin run in section 11, are from October 13, before these, and say so. Section 7's spot synthesis against traced lenses and the fisheye's Monte Carlo in section 8.1 are from October 15, at commit 03f70aa, whose changes since do not touch the RMS wavefront or the MTF. Section 3 traces every pair (`--cross all`); the rest use the default unless stated. Monte Carlo runs use the seeds given, on the in-repo Cooke triplet (`tests/Tolerancing.Tests/TestData/cooke_triplet.zmx`) at the commercial grade unless stated otherwise.

Contents:

1. [What is checked, and how](#1-what-is-checked-and-how)
2. [The optics, checked without another program](#2-the-optics-checked-without-another-program)
3. [Koch's prediction against traced lenses](#3-kochs-prediction-against-traced-lenses)
4. [Allocated sets, built](#4-allocated-sets-built)
5. [Departure criteria](#5-departure-criteria)
6. [Several criteria](#6-several-criteria)
7. [MTF floors](#7-mtf-floors)
8. [Many lenses: the model check and the cross terms](#8-many-lenses-the-model-check-and-the-cross-terms)
9. [Smith 1985's example](#9-smith-1985s-example)
10. [Defects verification found](#10-defects-verification-found)
11. [Known limitations](#11-known-limitations)

## 1. What is checked, and how

| Level | What it checks | Where |
|---|---|---|
| Algebra | Each distribution's moments against sampling; Koch's mean, variance and skewness against 400 000 draws on the model itself; allocation on synthetic models with known answers | Tests: `DistributionTests`, `KochTests`, `AllocationTests` |
| Optics | The libraries' own verification, against published tables, closed forms and independent methods; each error applied to the lens against an exact answer | The libraries' repositories; tests: `TiltDecentreTests`, `ToleranceTests`, `IrregularityTests` (section 2) |
| Cross-check | The same perturbed lenses computed in a commercial program, OpticStudio | Tests: `OpticStudioComparisonTests` (section 2.3) |
| A published example | Smith 1985's laser-disc lens and his change table | Tests: `SmithTests` (section 9) |
| Statistics | Koch's prediction against thousands of lenses traced in full | `verification/run.sh` (sections 3 to 6), and smaller runs in `LensStatisticsTests` |
| MTF floors | The criterion against the MTF it is built on; the spot synthesis and an allocated set against traced lenses | Tests: `MtfCriterionTests`; `verification/run.sh` (section 7) |
| Breadth | The model check and the cross terms on 17 lenses of different kinds | `verification/run.sh` (section 8) |

The traced Monte Carlo is the reference throughout. Each of its lenses has every error drawn at once, is refocused, and is traced in full by WavefrontErrorCalculator, so it assumes nothing that Koch's method assumes.

## 2. The optics, checked without another program

The module's optics are not its own: AberrationCalculator traces the lens and WavefrontErrorCalculator computes the wavefront. Both were verified in their own repositories by evidence that needs no other program. The module then checks its own part, how each error is put on the lens and how the lens is refocused, against exact answers. Agreement with a commercial program comes last, as a cross-check made after the fact (section 2.3); nothing was built or tuned to it.

### 2.1 The libraries

- **AberrationCalculator** ([its verification](https://github.com/jaruiz6363/AberrationCalculator/blob/main/docs/verification.md)): Buchdahl's published tables reproduced entry by entry; closed-form surfaces whose aberrations are known exactly, such as a paraboloid's; coefficients recovered from real traced rays; an independent implementation written from the same monograph; and Forbes's series trace, from a separate paper with no shared code.
- **WavefrontErrorCalculator** ([its verification](https://github.com/jaruiz6363/WavefrontErrorCalculator/blob/main/docs/verification.md)): the wavefront computed three independent ways, by the optical path, by Rayces's relation between wave and ray aberration, and by Hopkins's surface contributions, agreeing to 10⁻⁹ wave or better; and cases with exact answers: a paraboloid on axis, a sphere imaging its center of curvature, the aplanatic points of a refracting sphere and confocal paraboloids, all to 10⁻⁶ wave. Its geometric MTF is checked against closed forms and its own diffraction OTF (section 7). Agreement with several other programs follows there too, as cross-checks.

### 2.2 How the module applies each error

Each check has an answer known in advance, and the tests hold it.

| Check | Expected | Test |
|---|---|---|
| Power: the sag at the semi-diameter after `TFRN` | Changed by exactly λ/2 a fringe, conic and aspheric terms included (to 10⁻¹³ mm) | `FringesChangeTheSagAtTheSemiDiameterByHalfAWavelengthEach` |
| An index change on a singlet | The focal length the thick-lens formula gives (to 10⁻⁹) | `AnIndexChangeMovesTheFocalLengthAsTheThickLensFormulaSays` |
| A sphere turned about its own center of curvature | The same sphere: the wavefront does not change (to 10⁻⁹ wave) | `ASphereTurnedAboutItsCentreOfCurvatureChangesNothing` |
| The whole lens, stop included, moved 0.05 mm across | Every ray lands 0.05 mm over (to 10⁻¹⁰ mm) | `TheWholeLensMovedOverMovesTheImageOver` |
| A move and its inverse; two tilts about one pivot | The identity; one tilt of their sum (to 10⁻¹²) | `AMoveAndItsInverseCancel`, `TiltsAboutOnePivotAddLikeAnglesInOnePlane` |
| An x decenter or tilt of a symmetric lens, either sign | The same wavefront (to 10⁻¹⁰) | `AnXDecentreOfASymmetricLensIsTheSameEitherWay` |
| Under `--fields xy`, the same error in x and in y | The same cost (to about 10⁻⁶) | `AdjustmentTests` |
| Irregularity (`TIRR`, `TRSI`) | The peak to valley stated, sampled over the aperture, the vertex in place, the paraxial lens untouched | `IrregularityTests`, `AsphereFormTests` |
| An adjusted thickness | Everything beyond it stays where it was | `AnAdjustedThicknessKeepsEverythingBeyondItInPlace` |
| Boresight in degrees | A 0.05 mm shift over the focal length | `BoresightIsTheLineOfSightsAngleInDegrees` |

The refocus is checked by what it must do: the compensator finds the minimum of the criterion it is given, and the traced Monte Carlo, which refocuses every lens in full, is the reference for everything statistical (sections 3 to 8).

### 2.3 Agreement with a commercial program

As a cross-check, the same perturbed lenses were also computed in OpticStudio, on October 12, 2026. The module was set to the conventions OpticStudio documents (WavefrontErrorCalculator's `zemax` preset: its reference sphere and pupil), so that both programs report the same defined quantity. Neither program is taken as right. The comparison was made after the module was written; agreement between two programs written separately is further evidence, and a disagreement would be investigated on its merits. The tests hold these numbers too, so a change in the tracing that broke the agreement would be seen.

*A rotationally symmetric case:* the triplet with 10 fringes on surface 3 at 0.6328 µm and its air space 0.2 mm longer, the radius set in OpticStudio to the one `TFRN` gives, and the back focus optimized there on its RMS wavefront.

| Quantity | Module | OpticStudio |
|---|---|---|
| Polychromatic RMS wavefront, waves | 1.539334 | 1.5393338 |
| Refocused back focus, mm | 43.160286549 | 43.160286518 |
| Criterion after refocus, waves | 1.192196 | 1.1921957 |

*Tilts and decenters:* wedge in x and y, surface decenters, an element tilted about its vertex, and an element decentered and tilted.

| Quantity | Agreement |
|---|---|
| Axial chief ray at the image | To 1×10⁻¹² mm |
| Chief ray at the field edge | To 2.5×10⁻⁷ mm, where the errors moved it 0.28 mm |
| A tilt about x alone | To the last digit printed, sign included; the module's tilt signs follow OpticStudio's operands, so a tolerance means the same in both |
| RMS for each field and wavelength | Within 2×10⁻⁴, the limit of OpticStudio's 1024² grid |

*Random lenses:* Monte Carlo lenses from an allocated set were exported refocused (`--export-trials`) and opened in OpticStudio. Every field and wavelength checked agreed within the same 2×10⁻⁴. Glass errors were left out, because a .zmx names glass by catalog and cannot carry a changed index.

## 3. Koch's prediction against traced lenses

### 3.1 Moments and levels

Every cross term evaluated (`--cross all`), 4000 lenses traced each.

| | Koch | Traced, 4000 lenses |
|---|---|---|
| **Commercial set** (48 tolerances, uniform errors; seed 11) | | |
| Mean of W² | 3.466 | 3.478 ± 0.024 |
| σ of W² | 1.458 | 1.497 |
| Skewness of W² | 0.81 | 0.85 |
| 97.7 % level, waves | 2.626 | 2.677 |
| **Biased set** (10 tolerances: thicknesses made long, a normal glass, end-point decenters; seed 21) | | |
| Mean of W² | 1.8793 | 1.8789 ± 0.0024 |
| σ of W² | 0.1547 | 0.1547 |
| Skewness of W² | 0.15 | 0.16 |
| 97.7 % level, waves | 1.481 | 1.483 |

The mean agrees within half a standard error in both. On the commercial set the 97.7 % level is 1.9 % low, the model's σ 2.6 % low and its skewness 0.81 against 0.85; on the biased set the level is within 0.2 %. The biased set is in `verification/run.sh`.

### 3.2 The shape of the spread

The moments are right; the shape chosen for the rest sets the 97.7 % level. On the same two sets:

| Set | Traced | Sampled (default) | Pearson III | Gamma | Normal |
|---|---|---|---|---|---|
| Commercial | 2.677 | 2.626 | 2.632 | 2.636 | 2.526 |
| Biased | 1.483 | 1.481 | 1.483 | 1.484 | 1.479 |

- **Normal** (Koch's own) is 5.6 % low on the commercial set, whose spread has a long tail.
- **Sampled, Pearson III and gamma** are within 2 % on both. Sampled draws the spread from the model itself and assumes no shape, which is why it is the default. When each field was judged on one side only, the biased set's end-point decenters changed W² linearly, which gave a spread flatter than any shape built from moments, and every moment-fitted shape was 4 to 6 % high there; sampled was within 1.0 %. Judged on both sides, the linear terms cancel.

### 3.3 The model in W against the model in W²

Koch 1978 fitted his quadratic to the performance measure itself; the module fits it to W², the mean square (theory, section 3.1). To see what that choice is worth, both models were built from the same traced data, on six cases:

- the sensitivity run;
- every pair at both maxima.

They differ only in what each parabola is fitted to:

- **The model in W:** Koch's, with a parabola in W through each tolerance's ends and the refocused nominal, and each cross term the pair's W less the nominal and both ends' changes.
- **The model in W²:** the module's, with every pair traced (`--cross all`) and with its default cross terms from the wavefront slopes.

Each model was drawn 200 000 times on the module's own error draws, and set against 4000 lenses traced in full (seed 3). "Lens by lens" is the RMS, over those 4000 lenses, of each model's W less the traced W at the same errors. Measured by `verification/WModel`.

| Case | Tolerances | Traced 97.7 % level | Model in W | Model in W², every pair | Model in W², default | Lens by lens: W, W², default |
|---|---|---|---|---|---|---|
| Cooke triplet, commercial | 48 | 2.608 | 2.983 (+14 %) | 2.627 (+0.7 %) | 2.625 (+0.7 %) | 0.143, 0.017, 0.015 |
| Cooke triplet, allocated to 2 waves ([examples](../examples/tolerances/cooke_triplet.tol)) | 48 | 1.908 | 1.965 (+3.0 %) | 1.913 (+0.2 %) | 1.912 (+0.2 %) | 0.023, 0.005, 0.006 |
| Biconvex singlet | 15 | 1.326 | 1.326 | 1.326 | 1.326 | 0.0000, 0.0000, 0.0001 |
| Cemented doublet | 22 | 0.312 | 0.356 (+14 %) | 0.309 (−1.0 %) | 0.309 (−1.0 %) | 0.018, 0.0007, 0.0006 |
| Cassegrain | 21 | 1.546 | 3.291 (+113 %) | 1.510 (−2.4 %) | 1.481 (−4.2 %) | 1.001, 0.187, 0.120 |
| Cooke triplet with vignetting | 48 | 3.493 | 3.935 (+13 %) | 3.509 (+0.5 %) | 3.501 (+0.2 %) | 0.228, 0.038, 0.031 |

The traced mean of W² against each model's, for the same cases: 3.455 ± 0.023 against 3.723 (W) and 3.467 (W²); 2.238 ± 0.010 against 2.265 and 2.244; 1.7003 ± 0.0005 against 1.7005 and 1.7005; 0.0499 ± 0.0003 against 0.0552 and 0.0498; 0.756 ± 0.010 against 3.077 and 0.741, 0.739 by default; 5.513 ± 0.045 against 5.849 and 5.531.

The allocated set is the examples' least-cost set; the rest are the commercial default set. Both have irregularity.

- **The model in W² is within 1.0 % of the traced 97.7 % level on five cases out of six.** On the Cassegrain it is 2.4 % low with every pair traced and 4.2 % low with the default cross terms, and its mean of W² 1.5 and 1.8 standard errors low; lens by lens it is 0.12 waves off, where before irregularity was in the set it was 0.013. Irregularity on a mirror counts twice in the wavefront, and the quadratic follows it less well here than the other errors. Confirm a Cassegrain's 97.7 % level with `--montecarlo` (section 11).
- **The model in W is pessimistic in five cases out of six**, by 13 to 14 % on the lenses at the commercial grade and by 113 % on the Cassegrain. There a well-corrected nominal (0.04 waves) leaves W as near |x| as the model can get, a V, which no parabola in W follows, and 2.6 % of its draws come out with W below zero. It comes within 3 % only where the tolerances are small against the design's own error, on the singlet and the allocated set, where W ≈ √(Q₀ + A x²) is nearly a parabola too.
- **Lens by lens the model in W² is 4 to 30 times closer**, except on the singlet, where both are within 10⁻⁴ waves.

- **A tolerance set allocated on the model in W would be tighter than it needs to be**, often much tighter: its pessimism is the cost of fitting the wrong quantity. The model in W errs the other way, optimistically, for one dominant error on a well-corrected lens, where at half a tolerance it puts W at half the truth; no case here was of that kind.

This is also the check that found the exit-pupil fault in WavefrontErrorCalculator (section 10): a traced singlet lens at 2.98 waves where both models put 1.29.

### 3.4 The fields judged

The commercial set, 1000 lenses traced each (seed 3), with each field judged as `--fields` says (theory, section 2.3):

| Fields | Koch mean W² | Traced mean W² | Koch 97.7 % level | Traced 97.7 % level |
|---|---|---|---|---|
| The file's alone (`file`), on the +y side | 3.403 | 3.333 ± 0.072 | 3.072 | 3.074 |
| With their mirror images at −y (`symmetric`, the default) | 3.466 | 3.428 ± 0.046 | 2.622 | 2.606 |
| With their images on the x axis too (`xy`) | 3.469 | 3.434 ± 0.046 | 2.625 | 2.607 |

- **Each agrees with its own traced lenses**, within one standard error in the mean and 0.7 % in the 97.7 % level. The three ask different questions.
- **Judged on one side, the spread is wider**: a y decenter is then a first-order change, better at one sign and worse at the other, and the 97.7 % level is 3.07 waves where the whole field's is 2.62.
- **`xy` and `symmetric` agree within 0.2 %** on a lens designed rotationally symmetric, as they should: with the design's stop kept and the on-axis exit pupil on its side (section 10), an error in x costs what the same error in y costs, and the fields on the x axis add nothing new. The tests check that to 10⁻⁶ waves for decenters, element tilts, surface tilts, wedges and the image tilt (`XyFieldTests`).

### 3.5 Compensators beside the focus

The commercial set with further compensators (`--adjust`), 1000 lenses traced each (seed 3):

| Compensators | Koch mean W² | Traced mean W² | Koch 97.7 % level | Traced 97.7 % level | Predicted moves for 95.4 % | Traced moves, range |
|---|---|---|---|---|---|---|
| Focus, the last element's tilt and the image tilt | 2.447 | 2.442 ± 0.030 | 2.147 | 2.135 | element ±1.17°, image ±1.35° about x | element −1.58° to +1.55°, image −1.68° to +1.78° |
| Focus and the last element's decenter | 2.110 | 2.108 ± 0.027 | 2.033 | 2.019 | ±0.20 mm in x, ±0.19 mm in y | −0.25 to +0.27 mm |
| The first, with `xy` fields | 2.449 | 2.444 ± 0.030 | 2.147 | 2.135 | element ±1.17°, image ±1.35° about x and about y | image about y −1.94° to +1.65° |

- **The mean agrees within 0.2 standard errors, and the 97.7 % level within 0.7 %**, in all three.
- **Against the focus alone** (section 3.4, 2.62 waves) the tilts bring the 97.7 % level to 2.15 and the decenter to 2.03.
- **The image tilt about y** has nothing to act on with the fields on the y axis alone, and stays at zero; with `xy` fields it moves as the tilt about x does, ±1.35° for 95.4 % of lenses.

## 4. Allocated sets, built

The triplet allocated to 2 waves at 97.7 %, then built: 1000 lenses traced from the allocated set (seed 5).

| Allocation | Adams cost | Koch 97.7 % level | Koch yield at 2 waves | Traced 97.7 % level | Traced yield at 2 waves |
|---|---|---|---|---|---|
| Equal effect | 10.03 | 1.911 | 99.2 % | 1.920 | 99.2 % |
| Least cost | 7.87 | 1.910 | 99.2 % | 1.929 | 99.0 % |
| Least cost, focus within ±0.5 mm | 10.42 | 1.915 | 99.2 % | 1.917 | 99.1 % |

- Every set meets its goal when built. Koch's 97.7 % level is 0.1 to 1.0 % below the traced one, and the yields Koch predicts, 99.2 %, are within 0.2 points of the traced ones. Before each lens kept the design's stop and judged its fields on both sides, Koch's level was 2 to 3 % below.
- Least cost is 22 % cheaper than equal effect, for the same yield.
- With the travel limit, Koch predicts ±0.38 mm of travel for 95.4 % of lenses and ±0.57 mm for 99.7 %. Of the 1000 traced lenses, 1 (0.1 %) ran out of the ±0.5 mm, where the goal allowed 2.3 %. Holding the focus within the travel raised the cost from 7.87 to 10.42.
- The mean of W² agrees within a standard error in all three: 2.400 against 2.413 ± 0.018, 2.244 against 2.259 ± 0.021, and 2.309 against 2.323 ± 0.020.


## 5. Departure criteria

4000 lenses traced (seed 3), refocused for the sharpest image.

| Criterion | Koch mean W² | Traced mean W² | Koch 97.7 % level | Traced 97.7 % level | Cross terms' share |
|---|---|---|---|---|---|
| EFL change, mm | 0.2297 | 0.2292 ± 0.0049 | 1.071 | 1.067 | 95 % |
| Boresight, mm | 0.0834 | 0.0830 ± 0.0012 | 0.525 | 0.533 | 82 % |

- Both agree, within 0.4 standard errors in the mean and 2 % in the 97.7 % level. Irregularity leaves both models as they were, Koch's means to every digit shown: it has no paraxial effect, and the on-axis chief ray meets each surface near its vertex, where the irregularity's slopes are zero.
- **The boresight is not biased.** With 1000 lenses this check, before irregularity was in the set, found its mean W² 8 % low, 0.0907 ± 0.0025, and this file called that a weakness of the slopes. It was a run of high draws. Without the compensator, which leaves the boresight's model all but unchanged and traces far faster, 32 000 lenses (seed 101) gave 0.08314 ± 0.00041 against Koch's 0.08325, and a 97.7 % level of 0.528 against 0.531. W² is skewed (skewness 1.4), so 1000 lenses can stray this far. Traced with the decenters, each other kind of tolerance (thickness, fringes, glass, surface tilt, element tilt) left Koch within 1.5 standard errors of 4000 lenses. So the slopes' linear departure holds over the commercial grade's ranges.
- Cross terms are most of the variance for both. Without the free cross terms of departures, these criteria could not be predicted at all.

## 6. Several criteria

The triplet allocated by least cost to 2.2 waves RMS, EFL within 0.25 mm and boresight within 0.2 mm at the image (`boresight-mm`); 1000 lenses traced from each set (seed 7).

**Each criterion at 97.7 % on its own:**

| | RMS, waves | EFL, mm | Boresight, mm | Within every limit at once |
|---|---|---|---|---|
| Koch, 97.7 % level | 2.117 | 0.2322 | 0.1709 | 97.1 % |
| Traced, 97.7 % level | 2.097 | 0.2272 | 0.1748 | 96.8 % |

The EFL limit comes nearest to binding (cost 14.36).

**97.7 % within every limit at once** (`--joint`):

| Method | Each criterion held to | Cost | Within every limit, Koch | Within every limit, traced |
|---|---|---|---|---|
| Equal effect | 98.03 % | 16.99 | 98.8 % | 98.2 % |
| Least cost | 99.20 % | 16.65 | 99.2 % | 98.3 % |

- Equal effect leaves boresight and RMS well inside their limits, so only EFL binds and the marginal yield barely moves.
- Least cost balances all three, so they fail on nearly separate lenses, and the marginal yield must rise to the union bound of 1 − 2.3 %/3 = 99.2 %.
- Least cost is still the cheaper of the two.
- Both joint sets meet the goal when built, at 98.2 % and 98.3 %; Koch put them 0.6 and 0.9 points higher. With each criterion at 97.7 % on its own, 96.8 % of traced lenses were within every limit at once, where Koch put 97.1 %.

## 7. MTF floors

**The MTF itself** is WavefrontErrorCalculator's and is verified there (its method.md, section 11.1), against closed forms and against its own diffraction OTF:
- the sum on one point and two points;
- a defocused paraboloid's 2J₁(x)/x;
- a perfect lens's MTF against Haines and King's Table I;
- the diffraction OTF at λ/100, which Kidger 1978 used to check his own programs, within 0.0012 on the Cooke triplet, on axis and off.

It has not been compared with another program's geometric MTF, since programs disagree on it and none is an independent reference. The module's tests (`MtfCriterionTests`) check that the criterion is that MTF with no diffraction factor, that W² = 1 − MTF, that the intercepts the synthesis moves are the rays the MTF sums, and the parsing.

**The spot synthesis against traced lenses.** 2000 lenses traced and refocused (seed 21), at the commercial grade unless stated; the MTFs are tangential (T) or sagittal (S), at cycles/mm (October 15, 2026, irregularity included).

| Lens, MTF | Nominal | Mean MTF, synthesis | Mean MTF, traced | 97.7 % above, synthesis | 97.7 % above, traced | Yield, synthesis | Yield, traced | Model check |
|---|---|---|---|---|---|---|---|---|
| Triplet, on axis, T 20, floor 0.3 | 0.575 | 0.513 | 0.516 ± 0.003 | 0.253 | 0.250 | 94.6 % | 94.7 % | 5.5 % |
| Triplet, 10°, T 10, floor 0.3 | 0.585 | 0.468 | 0.471 ± 0.004 | 0.219 | 0.212 | 80.2 % | 80.6 % | 6.0 % |
| Triplet, 14°, S 10, floor 0.3 | 0.597 | 0.508 | 0.507 ± 0.004 | 0.221 | 0.224 | 90.7 % | 90.3 % | 4.1 % |
| Triplet, on axis, T 20, precision grade, floor 0.5 | 0.575 | 0.591 | 0.592 ± 0.002 | 0.435 | 0.440 | 89.0 % | 88.9 % | 2.2 % |
| Triplet with vignetting, 14°, T 10, floor 0.2 | 0.509 | 0.360 | 0.361 ± 0.004 | 0.121 | 0.115 | 84.0 % | 83.7 % | 8.0 % |

- The mean MTF agrees within one standard error in every case.
- The 97.7 % floor agrees within 0.007 and the yield within 0.4 points, with no lean either way: the synthesis is the more optimistic in three cases and the more cautious in two.
- **One sample shared by five cases.** An earlier run of these cases traced 1000 lenses on one seed (9) and found the synthesis up to 3.1 points optimistic in yield, in four of the five. All five draw the same errors from that seed, so one unlucky sample moved them together; a check that took out the irregularity, on the same seed, seemed to close the gap and was read as the irregularity's doing. With 2000 lenses on another seed the gap is gone, irregularity and all. With the lens traced one error at a time and in pairs, the synthesis followed the irregularity's effect on the spot to the fourth decimal, and the summed refocus agreed with each lens's own to 0.009 mm.
- The model check is the synthesis against 12 traced lenses, as a fraction of their MTFs' scatter (theory, section 9).

At the precision grade the mean MTF is slightly above the nominal: some errors raise this MTF, and the synthesis follows that as the traced lenses do.

**Allocated to an MTF, built.** The triplet allocated to an MTF of 0.3 on axis at 20 cycles/mm, tangential, for 97.7 % of lenses; 1000 lenses traced from each set (seed 5).

| Allocation | Adams cost | 97.7 % above, synthesis | 97.7 % above, traced | Yield at 0.3, synthesis | Yield at 0.3, traced | Model check |
|---|---|---|---|---|---|---|
| Least cost | 9.03 | 0.345 | 0.347 | 99.0 % | 99.0 % | 7.7 % |
| Equal effect | 12.14 | 0.346 | 0.329 | 98.9 % | 98.8 % | 10.8 % |

- Both sets meet the goal when built, and least cost is 26 % cheaper.
- The equal-effect floor from the synthesis is 0.017 high: the built set is still within its goal, at 98.8 %.
- Before irregularity was in the set, the equal-effect set opened the middle element's decenter to ±0.1 mm, where the spot moves nonlinearly, and the model check warned (26.5 %); it now holds it to ±0.06 mm, and the check passes.

## 8. Many lenses: the model check and the cross terms

Every lens at the commercial grade, with the default cross terms, from the wavefront slopes, and with the traced cross terms of `--cross auto`: every pair up to 80 tolerances, the x–y pairs beyond. The model check traces 12 lenses against the model (theory, section 9). The LensHH-LT samples are the designs shipped with LensHH-LT. The rest are written for the tests or for verification, and are in the repository.

| Lens | Tolerances | Cross terms' share | 97.7 % level, auto | 97.7 % level, default | Model check, auto | Model check, default |
|---|---|---|---|---|---|---|
| Biconvex singlet | 15 | 0 % | 1.326 | 1.326 | 0.0 % | 0.0 % |
| Aspheric singlet | 17 | 29 % | 0.1770 | 0.1770 | 0.1 % | 0.2 % |
| Cemented doublet | 22 | 68 % | 0.3103 | 0.3102 | 0.3 % | 0.2 % |
| Cassegrain | 21 | 78 % | 1.515 | 1.487 | **30.4 %** | 14.7 % |
| Cooke triplet | 48 | 49 % | 2.626 | 2.622 | 1.3 % | 0.9 % |
| Cooke triplet, F-number aperture | 48 | 49 % | 2.665 | 2.661 | 1.3 % | 0.9 % |
| Cooke triplet, wider field | 48 | 26 % | 3.511 | 3.494 | 2.1 % | 1.4 % |
| Cooke triplet, cemented | 55 | 72 % | 2.340 | 2.338 | 1.5 % | 0.9 % |
| Smith 1985 laser lens | 72 | 85 % | 1.121 | 1.119 | 14.8 % | 10.0 % |
| LensHH-LT Cooke triplet | 47 | 75 % | 1.864 | 1.866 | 1.2 % | 1.3 % |
| LensHH-LT Cooke triplet, vignetting | 47 | 73 % | 2.597 | 2.590 | 2.7 % | 1.0 % |
| LensHH-LT Petzval | 61 | 78 % | 3.255 | 3.257 | 2.1 % | 1.9 % |
| LensHH-LT Heliar | 62 | 72 % | 2.872 | 2.865 | 7.1 % | 2.0 % |
| LensHH-LT Eggert patent | 71 | 69 % | 0.934 | 0.935 | 3.0 % | 2.4 % |
| LensHH-LT USP 4,338,001 | 79 | 93 % | 3.949 | 3.950 | 2.5 % | 1.6 % |
| LensHH-LT fisheye 160° f/2 | 126 | 85 % | 5.328 | 7.385 | **90.7 %** | 7.5 % |
| LensHH-LT Laikin 9-3 | 142 | 17 % | 6.310 | 6.497 | 3.5 % | 0.2 % |

The model check is the RMS of the 12 lenses' differences from the model; `tol` warns above 15 %. The cross terms' share is from the default run. The default runs took 5 s or less each, except the cemented triplet's 15 s, Laikin's 19 s and the fisheye's 23 s.

### 8.1 What the sweep shows

- **Cross terms are most of the variance on real lenses**: 68 to 93 % on every multi-element design here, except the in-repo air-spaced triplets' 26 to 49 % and Laikin's 17 %. A model without them would understate the spread by half or more.
- **Above 80 tolerances `auto` traces only the x–y pairs**, and on the fisheye and Laikin that leaves out every cross term between different tolerances. The fisheye's 97.7 % level falls from 7.39 to 5.33 waves. With every pair traced it was 7.42 (section 8.2, before irregularity was in the set), so `auto` is about 28 % low there. This is why the default is the slopes.
- **The slopes agree with the traced pairs** within 1.9 % on every lens of 80 tolerances or fewer, the largest difference being the Cassegrain's.
- **The slopes' model check is better on 14 of the 17 lenses**, the same on the biconvex singlet, and 0.1 point worse on the aspheric singlet and the LensHH-LT Cooke triplet. Where tracing failed it most:

  | Lens | Model check, auto | Model check, default |
  |---|---|---|
  | Fisheye 160° f/2 | 90.7 % (pairs only) | 7.5 % |
  | Cassegrain | 30.4 % (every pair traced) | 14.7 % |
  | Smith 1985 laser lens | 14.8 % (every pair traced) | 10.0 % |
  | LensHH-LT Heliar | 7.1 % (every pair traced) | 2.0 % |
  | Laikin 9-3 | 3.5 % (pairs only) | 0.2 % |

  On Smith's lens, tracing every pair at both maxima puts each cross term at a corner where the lens is far from quadratic. The slopes average over each tolerance's range instead.
- **Irregularity is the hardest tolerance for the quadratic on the Cassegrain and Smith's lens.** Before it was in the set, their default model checks were 1.3 % and 6.9 %; with it they are 14.7 % and 10.0 %, still under the 15 % at which `tol` warns. On the Cassegrain the 97.7 % level from the slopes is 4.2 % below 4000 traced lenses (section 3.3).
- **The fisheye's model check, with the slopes, fell from 25.3 % to 7.5 %** when each lens came to keep the design's stop and its on-axis exit pupil; Laikin's 97.7 % level fell from 7.21 to 6.50 waves.
- **The fisheye against its own traced lenses.** 1000 lenses at the commercial grade (seed 3, October 15, 2026): Koch's mean W² 17.04 against 16.10 ± 0.41 traced, and its 97.7 % level 7.385 waves against 7.114, 3.8 % high, the cautious side. The predicted spread follows the traced one over its whole range. The lens farthest from quadratic here is predicted well with the slopes' cross terms.


### 8.2 Cross terms from the slopes against traced pairs

Every pair traced against `--cross wavefront`, on four lenses (October 13, 2026, before and after the vignetting allowance, and before irregularity was in the default set):

| Lens | Tolerances | Pairs traced | Time, traced → slopes | 97.7 % level, traced → slopes | Model check, traced → slopes |
|---|---|---|---|---|---|
| Cooke triplet (in-repo) | 42 | 861 → 0 | 26 s → 3 s | 3.058 → 3.054 | 2.0 % → 0.9 % |
| Cooke triplet with vignetting | 41 | 820 → 0 | 18 s → 15 s | 2.602 → 2.609 | 13.8 % → 6.3 % |
| Fisheye 160° f/2 | 110 | 5995 → 1900 | 80 min → 6½ min | 7.424 → 7.415 | 19.5 % → 19.2 % |
| Cassegrain | 19 | 171 → 0 | 0.4 s → 0.1 s | 1.450 → 1.452 | 4.7 % → 1.3 % |

The fisheye row is under the strict rule that preceded the 10 % vignetting allowance, so 19 of its tolerances still had their pairs traced. Under the allowance, the sweep's fisheye run with slopes took 11 s.

The test `WavefrontCrossTermTests` keeps a smaller version of this check: the triplet with a vignetting rear aperture, 14 tolerances, cross terms within 15 % of the traced ones and the 97.7 % level within 0.5 %.

## 9. Smith 1985's example

Smith's worked example is a 14 mm, NA 0.42 laser recording lens at 0.82 µm (his Table 1). The module reproduces it: the focal length and NA, a 0.7 mm field, and on axis the 0.04 waves P-V he gives.

His change table (Table 2) is not the module's criterion: each entry is a P-V OPD estimated from third-order spherical aberration, coma and astigmatism by his rules of thumb. The tests compare it with 3.5 times the RMS each change adds, Smith's own RMS = P-V/3.5, at the edge of the field, refocused as his lens is.

- Radius, thickness and index changes fall within 2.5 times his, with a median ratio of 1.5. The module's are larger because it keeps every aberration, where he kept three.
- The surface tilts rank in his order except one neighboring pair (Spearman 0.98), and come out 2 to 3 times his values. The tilts are traced exactly (section 2.2), and agree with a commercial program's (section 2.3), so the factor is his third-order coma estimate's.

At the commercial grade, with every pair traced, its model check is 14.8 %, just under the 15 % at which `tol` warns; with the default slopes it is 10.0 % (section 8).

## 10. Defects verification found

Verification is meant to find faults, and found these. Each is fixed and kept as a test.

| Found | Defect | Fix |
|---|---|---|
| October 14, 2026, adding `xy` fields (section 3.4) | The same decenter cost differently in x and in y. WavefrontErrorCalculator measured the stop's radius on each lens built with errors, from its axial marginal ray, which a decenter ahead of the stop moves to first order in y and to second in x: the stop grew or shrank with the error's direction | Each lens keeps the design's stop radius: WavefrontErrorCalculator d6c4c566, its PR #6, with `StopHeightTests` there; the module passes the nominal radius. `XyFieldTests` |
| October 14, 2026, the same | The axial field alone still cost differently for an element tilted in x and in y. WavefrontErrorCalculator read which side of the image the on-axis exit pupil lies on from a small field's chief ray, which on a perturbed lens is mostly the axial chief ray's departure from the axis; for one sign of some y errors the exit pupil went behind the image. On the triplet, the last element tilted 0.2° about x gave 0.6588 waves on axis at 0.486 µm, against 0.6785 for the same tilt about y | The small field's chief ray is taken relative to the axial field's: the same PR, with `AxialExitPupilTests`. Centered lenses are unchanged. It changed analyses judged on the y axis alone too, and every figure in sections 3 to 8 was measured again |
| October 14, 2026, the model in W against W² (section 3.3) | WavefrontErrorCalculator took the exit pupil on axis from where a decentered or tilted lens's chief ray crosses the axis, which can fall on the image. There the reference sphere shrank to nothing, W jumped by tens of waves from one focus to the next, and the refocus stopped in a spike: a traced singlet lens at 2.98 waves, 1.29 at its best focus | On axis, the paraxial exit pupil, as its documentation said: WavefrontErrorCalculator 3f4b6b09, its PR #4, with `PerturbedTests` there. Centered lenses are unchanged, and so is the triplet's traced run of section 3.1 |
| October 13, 2026, the sweep | A space of zero thickness (USP 4,338,001's stop touching its first surface) was given a thickness tolerance of ±0, and no model could be fitted | A space of no thickness gets no tolerance. `ThicknessCapTests` |
| October 13, 2026, writing the user guide | Under `--preset optiland`, whose RMS is about zero, the vector the slopes come from kept its mean, so `--cross wavefront` was wrong | The vector always removes the mean, as the criterion does. `WavefrontCrossTermTests` |
| October 12, 2026, the LensHH-LT sweep | Wide-angle fields could not be traced; untraceable fields were fatal; Monte Carlo focus moves were measured from the file's thickness; drawings failed on deep menisci | Fixed in AberrationCalculator and the module. `RobustnessTests` |
| October 12, 2026, an independent review of the statistics, allocation and writers | Eight defects, among them errors in parallel loops crashing the tool, an allocation reported feasible while missing its goal, and a silent NaN model | Fixed. `ReviewFindingTests` |

## 11. Known limitations

- **`--cross auto` above 80 tolerances.** Pairs only can leave out most of the variance: about 28 % low on the fisheye (section 8.1). The default no longer does this, but `auto` is kept for comparison.
- **Tracing every pair can meet pairs the lens cannot be traced with.** On Laikin 9-3 with `--cross all`, seven pairs, among them 10 fringes on surface 5 with an index change in element 12, left the lens untraceable at both maxima. This used to fail the whole model after 58 minutes. Those pairs are now traced nearer nominal (theory, section 4.1), and the run completed: 97.7 % level 7.230 waves against 7.162 from the default slopes, model check 5.5 % against 0.5 %, in 68 minutes (October 13, 2026, before irregularity was in the default set). The default traces almost no pairs and seldom meets them.
- **The slopes are a linear approximation.** They leave out the wavefront's own curvature in each error, dotted with the nominal lens's residual (theory, section 4.3), and the compensator's cross terms for the pairs not traced. On the lenses here the first moved the 97.7 % level by 2 % at most, and on the triplet the second moved the predicted travel by 0.02 %.
- **Lenses pushed far from their design.** The fisheye at the commercial grade fails the model check with `--cross auto` (90.7 %), and Smith's lens with every pair traced is just under the warning (14.8 %). Where `tol` warns, the Monte Carlo is the answer. With the default cross terms the fisheye passes (7.5 %), and its 97.7 % level is 3.8 % above 1000 traced lenses (section 8.1).
- **Irregularity is the least quadratic tolerance on some lenses.** On the Cassegrain, whose mirrors double it in the wavefront, the 97.7 % level from the default slopes is 4.2 % below 4000 traced lenses and the model check 14.7 %, close to the warning; on Smith's lens the check is 10.0 % (sections 3.3 and 8). For a mirror system, confirm the level with `--montecarlo`.
- **Irregularity's orientation is fixed**: its astigmatism lies along x and y on every surface, so the astigmatism of two surfaces adds as if aligned. A shop's surfaces are oriented at random; the prediction leans to the cautious side, by an amount not measured here.
- **MTF floors** have been checked on the Cooke triplet, with and without vignetting, and on other lenses only against 400 traced lenses during development. The MTF is geometric, so it does not apply near the diffraction limit (user guide, section 6.3). Before irregularity was in the set, a set allocated by equal effect drew the model check's warning (section 7). Against 2000 traced lenses in each of five cases, the synthesis puts the yield within 0.4 points (section 7).
- **Drawings** have been checked against the standards' rules and for internal consistency, but not yet reviewed by an optical shop.
