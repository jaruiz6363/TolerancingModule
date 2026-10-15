# References

The papers and other sources the module's methods come from, cited by author and year in the other documents. [theory.md](theory.md) says what the module takes from each.

## Tolerancing

**Adams 1988.** G. Adams, "Selection of tolerances," *Proc. SPIE* **892**, *Simulation and Modeling of Optical Systems*, 173 (1988).
Inverse sensitivity; cost-optimal allocation by Lagrange multipliers, with closed forms for one worst-case or RSS constraint (eqs. 15, 17) and the conditions for several (eqs. 19, 20). Used for least-cost allocation.

**Ginsberg 1979.** R. H. Ginsberg, "Outline of tolerancing (from performance specification to toleranced drawings)," *Proc. SPIE* **181**, *Contemporary Optical Systems and Components Specifications*, 2 (1979).

**Ginsberg 1981.** R. H. Ginsberg, "Outline of tolerancing (from performance specification to toleranced drawings)," *Optical Engineering* **20**(2), 175–180 (1981).
A revision of Ginsberg 1979. The eleven-step process from specification to drawings, the sensitivity table and error budget formats, and tables of tight and loose tolerances. Used for the workflow, the table formats and the element tolerances' range.

**Grey 1970.** D. S. Grey, "Tolerance sensitivity and optimization," *Applied Optics* **9**(3), 523 (1970).
The second-order change of a merit function (eq. 3), and why a nonzero linear term means unequal plus and minus tolerances.

**Grey 1978.** D. S. Grey, "The inclusion of tolerance sensitivities in the merit function for lens optimization," *Proc. SPIE* **147**, *Computer-Aided Optical Design*, 63 (1978).
Tilt and decenter sensitivity from one nominal trace, OPD = Q·dN.

**Heshmaty-Manesh and Haig 1986.** D. Heshmaty-Manesh and G. Y. Haig, "Lens tolerancing by desk-top computer," *Applied Optics* **25**(8), 1268 (1986).
A change table corrected for non-linearity, then Monte Carlo; biased distributions. Used for the refit after allocation and for biased ranges.

**Hilbert 1979.** R. S. Hilbert, "Semi-automatic modulation transfer function (MTF) tolerancing," *Proc. SPIE* **193**, *Optical Systems Engineering*, 34 (1979).
CODE V's default tolerance set, equal-drop allocation, plus and minus sensitivities, the 97.7 % "probable worst case", compensator ranges. Used for the default set's kinds, equal effect and the 97.7 % level.

**Koch 1978.** D. G. Koch, "A statistical approach to lens tolerancing," *Proc. SPIE* **147**, *Computer-Aided Optical Design*, 71 (1978).
The second-moment method: the mean and variance of linear and quadratic performance changes from the moments of the errors (eqs. 12–21), polar x–y errors (eqs. 23–31), and moment tables. The module's analytic engine. Eqs. 25–27 have misprints, given in theory.md, section 5.5.

**Kojima 1979.** T. Kojima, "Yield estimation for mass-produced lenses based on computer simulation of the centering tolerance," *Proc. SPIE* **193**, *Optical Systems Engineering*, 141 (1979).
Monte Carlo of element and group shifts and tilts, with cemented groups moving rigidly.

**Sinclair, Tritchew and Jennison 1990.** R. L. Sinclair, S. Tritchew and P. Jennison, "Tolerancing methodology for an IR optical telescope," *Proc. SPIE* **1309**, *Infrared Imaging Systems: Design, Analysis, Modeling, and Testing*, 233 (1990).
A hierarchical budget tree with RSS and linear nodes. Used for the error budget trees.

**Smith 1985.** W. J. Smith, "Fundamentals of establishing an optical tolerance budget," *Proc. SPIE* **531**, *Geometrical Optics*, 196 (1985).
RSS budgets, the change table, aberration and fringe conversions, four shop grades (Table 3), and a worked example. Used for the shop grades, the trial-budget procedure, and his laser-disc lens as a regression test.

**Starke and Wise 1980.** J. P. Starke and C. M. Wise, "Modulation-transfer-function-based optical sensitivity and tolerancing programs," *Applied Optics* **19**(11), 1768 (1980).
Linear synthesis of Zernike changes for fast Monte Carlo. The idea behind cross terms from wavefront slopes.

**Williamson 1989.** D. M. Williamson, "Compensator selection in the tolerancing of a microlithographic lens," *Proc. SPIE* **1049**, *Recent Trends in Optical Systems Design II*, 178 (1989).
Choosing compensators, and splitting irregularity on drawings.

## Course material

**Burge.** J. H. Burge, "Specifying optical components," lecture notes for OPTI 421/521, *Introductory Optomechanical Engineering*, University of Arizona.
Base, precision and high-precision rules of thumb, glass grades, wedge and deviation, bevel widths. Used for the base tolerances, the glass grades and the default chamfer.

**Milby 2009.** E. Milby, "Tolerancing optical systems," report for OPTI 521, University of Arizona (November 30, 2009).
A survey of Smith, Willey, Ginsberg and Magarill, with performance criteria besides image quality. Used for the EFL, BFL and boresight criteria.

## Optics and numerics

**Forbes 1988.** G. W. Forbes, "Optical system assessment for design: numerical ray tracing in the Gaussian pupil," *Journal of the Optical Society of America A* **5**(11), 1943 (1988).
Gauss quadrature over the pupil. The RMS criterion's 8-ring, 24-arm rule.

**Kidger 1974.** M. J. Kidger, "The use of O.T.F. in optical design," *Proc. SPIE* **46**, 49 (1974).
The geometric OTF as (1/n) Σ exp(2πifδ) over the spot (eq. 2). The MTF criterion's summation.

**Kidger 1978.** M. J. Kidger, "The calculation of the optical transfer function using Gaussian quadrature," *Optica Acta* **25**(8), 665 (1978).
Section 2.8: the geometric OTF checked against the diffraction OTF computed at a much shorter wavelength. WavefrontErrorCalculator's check of the geometric MTF.

**Kidger 2002.** M. J. Kidger, *Fundamental Optical Design* (SPIE Press, 2002), sections 4.5.2 and 4.5.3.
The geometric MTF by direct summation, its ray pattern, and the correction for diffraction, which the module does not apply.

**Brent 1973.** R. P. Brent, *Algorithms for Minimization without Derivatives* (Prentice-Hall, 1973), ch. 5.
The refocus's one-dimensional minimizer (`fmin`).

**Press et al.** W. H. Press, S. A. Teukolsky, W. T. Vetterling and B. P. Flannery, *Numerical Recipes in C*, 2nd ed. (Cambridge University Press, 1992), §6.2: the incomplete gamma function by its series and continued fraction, and the complementary error function by its Chebyshev fit.

**Lanczos 1964.** C. Lanczos, "A precision approximation of the gamma function," *SIAM Journal on Numerical Analysis, Series B* **1**, 86 (1964).
ln Γ, with g = 7 and nine coefficients.

**Wilson and Hilferty 1931.** E. B. Wilson and M. M. Hilferty, "The distribution of chi-square," *Proceedings of the National Academy of Sciences* **17**(12), 684 (1931).
The cube-root normal approximation, for the gamma distribution at large shape.

**Steele, Lea and Flood 2014.** G. L. Steele Jr., D. Lea and C. H. Flood, "Fast splittable pseudorandom number generators," *Proc. ACM OOPSLA 2014*, *ACM SIGPLAN Notices* **49**(10), 453 (2014).
SplitMix64, the generator for the sampled spread.

## Standards

| Standard | Used for |
|---|---|
| ISO 10110, *Optics and photonics — Preparation of drawings for optical elements and systems*: parts 1 (general), 2 (stress birefringence), 3 (bubbles and inclusions), 4 (inhomogeneity and striae), 5 (surface form tolerances), 6 (centering tolerances), 7 (surface imperfections) | The ISO drawings |
| ASME Y14.18, *Optical Parts* | The US-practice drawings |
| MIL-PRF-13830B, *Optical Components for Fire Control Instruments; General Specification Governing the Manufacture, Assembly, and Inspection of* | Scratch-dig on the US-practice drawings |

## Software

| Program | Role |
|---|---|
| [AberrationCalculator](https://github.com/jaruiz6363/AberrationCalculator) | The lens model, file readers, glass, and paraxial and real ray tracing. A subtree in `external/` |
| [WavefrontErrorCalculator](https://github.com/jaruiz6363/WavefrontErrorCalculator) | Wavefront error over the pupil, and its conventions; the geometric MTF. A subtree in `external/` |
| Ansys Zemax OpticStudio | An independent program whose results were compared with the module's (see verification.md); the tolerance codes use its operand names, which designers already know |

## Read but not used

These were read while planning the module. They concern producibility, mounts, drawings or particular instruments rather than the methods implemented, and are listed for completeness: Bayar 1981 (mount stress); Betts and Merx 1978 and Sottini 1979 (geodesic lenses); Cross and Woodfin 1984 and Sharma and Jha 1984 (alignment and spacer compensators); Gustafson 1983 (a CODE V drawing template); Kuttner 1979 (producibility); Plummer 1979 (cost against tolerance); Westort (opto-mechanical practice); Yoder (mount drawings).
