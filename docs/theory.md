# TolerancingModule theory

This document gives the methods behind the module's numbers. It summarizes the papers they come from, and says where and why the module departs from them. Each section ends with the code that implements it. The [user guide](user-guide.md) says how to use the tool; [references.md](references.md) lists the papers in full.

Contents:

1. [The problem](#1-the-problem)
2. [Sensitivity and compensation](#2-sensitivity-and-compensation)
3. [The quadratic model of W²](#3-the-quadratic-model-of-w)
4. [Cross terms](#4-cross-terms)
5. [Koch's second-moment method](#5-kochs-second-moment-method)
6. [The shape of the spread](#6-the-shape-of-the-spread)
7. [Allocation](#7-allocation)
8. [Several criteria](#8-several-criteria)
9. [Checking the model](#9-checking-the-model)
10. [Error budgets](#10-error-budgets)
11. [The core papers](#11-the-core-papers)
12. [Where the module departs from the papers](#12-where-the-module-departs-from-the-papers)

## Notation

| Symbol | Meaning |
|---|---|
| x_i | The error in tolerance i, in its own units (fringes, mm, degrees) |
| ±t_i | Tolerance i's range; t_i its half-width |
| c_i | The center of a range not centered on zero: its bias |
| W | The criterion: RMS wavefront error, or the length of a departure such as an EFL change |
| Q = W² | The criterion squared, which the statistics are of |
| Q₀ | Q of the nominal lens, refocused |
| A_i, B_i | The quadratic and linear coefficients of tolerance i in the model of Q |
| c_ij | The cross term of tolerances i and j |
| σ_i², ν₄ᵢ, ν₆ᵢ | The second, fourth and sixth moments of error i about its center |
| y | The yield: the fraction of lenses within a limit |
| τ_i | The tightest value a shop can hold tolerance i to |

## 1. The problem

The module treats lenses designed rotationally symmetric about one optical axis (user guide, section 3). A lens is built from parts that are never exactly as designed. Each error is random within its tolerance, so each lens built is different, and its performance W is a random variable. Tolerancing has two halves (Ginsberg 1981):

- **Analysis:** given the tolerances, predict the distribution of W over the lenses built, and so the yield y: the fraction within a limit L.
- **Allocation, or budgeting:** given L and the yield wanted, choose tolerances that achieve it at the least cost.

Worst-case stacking, where every error is at its limit and adds, gives tolerances far tighter than needed, because almost no lens is built that way. The classical alternative is the root sum of squares (RSS) of the effects (Smith 1985; Adams 1988):

$$\Delta W_{\rm RSS} = \sqrt{\textstyle\sum_i (s_i t_i)^2}$$

with s_i the sensitivity of W to error i. RSS assumes W is linear in each error and the errors are independent. Koch 1978 showed it is a special case of a more general second-moment method. If each error has σ_i = k t_i, then σ_W = k·RSS: k = 1 for errors always at their ends, 1/√3 for uniform errors, and about 1/2.5 for a normal truncated at 2.5σ. So ±1 RSS covers about 92 % of uniform errors.

RSS fails for image quality. An RMS wavefront error or an MTF is not linear in the errors. Near a well-corrected design it changes quadratically, and the same error can improve the lens in one direction and degrade it in the other. Linear RSS has no place for the resulting shift of the *mean* (Koch 1978; Hilbert 1979). The module therefore uses Koch's method on a quadratic model, set out in sections 3 to 6.

## 2. Sensitivity and compensation

### 2.1 The sensitivity run

Every method starts from a sensitivity table, Smith's "change table" (Ginsberg 1981; Hilbert 1979; Smith 1985). Each tolerance is applied alone at each end of its range, the lens is refocused, and the criterion and the compensator's move are recorded. Plus and minus are run separately, because a quadratic criterion responds differently to each (Hilbert 1979, Fig. 1; Grey 1978).

The module runs each end on its own copy of the lens, in parallel. Changes are measured from the nominal lens refocused the same way, not from the file as saved. A file not saved at best focus would otherwise charge its own defocus to every tolerance.

*Code:* `Sensitivity.Run`, `SensitivityTable`.

### 2.2 Compensators

A compensator is an adjustment made after assembly, such as focus (Hilbert 1979; Ginsberg 1981; Williamson 1989). The module always refocuses: a thickness, the back focus by default, set to minimize the criterion. For a departure criterion or an MTF it is set to minimize the RMS wavefront, as the lens would be focused in use. Beside the focus it may make other adjustments: further air spaces, an element's or a group's tilt and decenter in its cell, and the image plane's tilt (user guide, section 7).

- **The focus** is searched for first, alone: from where the paraxial image has moved to, by Brent's method (Brent 1973) within a bracket a few depths of focus wide (2λF² each), widened while the minimum sits on its edge. With a travel limit, a lens that needs more is left at the end of the travel; the criterion is unimodal in focus, so that is the best the travel allows. With no other adjustment, this is the whole solve.
- **With other adjustments**, the focus and all of them are then refined together by damped least squares (Levenberg-Marquardt) on the vector of weighted pupil samples whose length is W (section 4.3). Each adjustment acts on the wavefront nearly linearly, so W² is nearly quadratic in the adjustments and a few steps converge. The derivatives are forward differences; a step is kept only if it lowers W. An adjustment whose effect is less than a hundredth of the strongest one's is held where it is, so as not to wander along a valley it does not change, and no step moves an adjustment more than a millimeter or a degree. An adjustment that reaches its travel stops there.
- **The model is unchanged by them.** The compensated W² is the minimum over the adjustments of a function quadratic in errors and adjustments together, which is again quadratic in the errors. So Koch's method applies as it stands, whatever the compensators.
- **Image tilt.** AberrationCalculator traces to an image plane square to the axis. A tilted plane z = (y − y₀) tan θx + (x − x₀) tan θy, about the image center (x₀, y₀) where the axial field's chief ray lands, is evaluated for each field as the shift of focus that brings the plane to where the tilted one meets that field's chief ray. The RMS wavefront is referred to a sphere about the chief ray's image point, so for it this is exact to first order in the tilt; for a spot it leaves out the plane's slope across the spot, which is second order. With every field on the y axis a tilt about y has nothing to act on, and stays at zero.
- **Travel.** Hilbert 1979 and Ginsberg 1981 report the compensator's travel beside the criterion. The module fits each compensator's move, the focus's and each adjustment's, with its own quadratic model (section 3), and predicts its mean and spread over the lenses built by Koch's method. A move is nearly linear in the errors, so its spread is taken as normal. The travel that covers a fraction p of lenses is |mean| + z_{(1+p)/2}·σ, the mean + 2σ of Hilbert's compensator range for p = 95.4 %.

*Code:* `FocusCompensator`, `CompensatorSolver`, `Adjustment` and its kinds, `ImagePlane`, `Brent`, `TravelPrediction`.

### 2.3 The fields judged

A lens file lists its fields on one side of the axis, +y, which for a rotationally symmetric lens says everything: the lens performs alike at y and −y. A decenter or a tilt breaks that symmetry. A lens judged on one side only then sees a decenter in y as a first-order change, an improvement for one sign and a loss for the other, though the whole field degrades either way: the side it is not judged on carries the loss. So the module judges each lens at every field and its mirror image at −y, the weight shared between the two, as OpticStudio's "Y-Symmetric" tolerancing fields do. The nominal lens's criterion is unchanged, and so is every rotationally symmetric error's.

On the Cooke triplet's commercial set this moved the 97.7 % level from 3.07 waves, judged on one side, to 2.62 (verification, section 3.4). Each agrees with its own traced lenses: the two ask different questions, and the symmetric one is the lens's whole field. `--fields file` judges the file's fields alone.

`--fields xy` adds each field's two images on the x axis, (±y, 0), the weight shared four ways, as OpticStudio's "XY-Symmetric" fields. Judged on the y axis alone, a decenter in x costs a lens less than the same decenter in y: it acts on those fields only at second order, across the field. On the x axis the two change places, so with all four fields a lens pays alike for an error in x and in y. AberrationCalculator and WavefrontErrorCalculator trace fields on the y axis, so a field at (x, 0) is traced as the field (0, x) of the lens turned a quarter turn about its axis, every perturbation turned with it.

Every perturbed lens keeps the design's stop: its radius is the height of the nominal lens's axial marginal ray there. Measured instead on each perturbed lens, a y decenter ahead of the stop, which moves the axial marginal ray, would resize the stop to first order and an x decenter would not, and the same error would cost differently in x and y.

*Code:* `NominalLens.MirrorFields`, `QuarterTurn`, `FieldSites`.


## 3. The quadratic model of W²

### 3.1 Why the square

Koch 1978's eq. 17 models a performance change as a quadratic in the errors:

$$\Delta Q = \sum_i (A_i x_i^2 + B_i x_i) + \sum_{i<j} c_{ij} x_i x_j$$

The module applies it to **Q = W², not to W**. The reason is that an RMS wavefront error is the root of a mean square, and a mean square is quadratic in small changes of the lens. The wavefront at each pupil point is linear in small errors, w(x) ≈ w₀ + Σ g_i x_i, so its variance over the pupil is exactly quadratic in x to that order. Refocusing keeps it quadratic, since the minimum of a quadratic over one of its variables is again a quadratic in the rest.

W itself is not quadratic. Near a well-corrected design it goes as √(Q₀ + A x²), which no parabola in x follows. So the statistics are of W², and are read back as W at the end. Against lenses traced in full, on six cases, the model in W² put the 97.7 % level within 1.0 % on five and 4.2 % low on a Cassegrain, and Koch's model in W, built from the same traced data, up to 113 % high (verification, section 3.3).

Fitting W² also does by itself what Smith 1985 does by hand when he subtracts the design's own error in quadrature, OPD_tol = √(OPD_spec² − OPD_design²).

### 3.2 The fit

Each tolerance's term is the parabola through its two ends and the refocused nominal: (lo, Q_lo), (0, Q₀) and (hi, Q_hi). With the slopes to each end,

$$S_{\rm hi} = \frac{Q_{\rm hi} - Q_0}{\rm hi}, \qquad S_{\rm lo} = \frac{Q_{\rm lo} - Q_0}{\rm lo},$$

$$A = \frac{S_{\rm hi} - S_{\rm lo}}{{\rm hi} - {\rm lo}}, \qquad B = S_{\rm hi} - A\,{\rm hi}.$$

- The sensitivity run for the model is at ± the larger end of each range. A range all on one side of nominal, a bias, is still fitted on both sides of nominal.
- The compensator runs without its travel limit while the model is fitted, since a limit would bend the quadratic. The travel is predicted from the compensator's model instead.
- After an allocation, the model is refitted at the allocated values, since the first fit was made where the tolerances started, not where they ended (section 7.6).

### 3.3 Departures

For a departure criterion, W = |d| with d the departure: an EFL or BFL change, or the boresight's (x, y), as the line of sight's angle or as the shift at the image. It is measured from the nominal lens refocused the same way, so d = 0 for the nominal lens. d is linear in small errors, d ≈ Σ g_i x_i, so

$$Q = |d|^2 = \sum_{i,j} x_i x_j\, \mathbf g_i\cdot\mathbf g_j,$$

exactly to second order. Each g_i is the slope of d between the two ends of tolerance i, from the sensitivity run.

*Code:* `QuadraticModel.Build`, `QuadraticModel.Fit`, `QuadraticModel.FitTerm`, `DeviationCriterion`.

### 3.4 An MTF

**The MTF.** The geometric MTF by Kidger's direct summation (Kidger 1974, eq. 2; Kidger 2002, section 4.5). Rays spread evenly over the pupil land at (x_k, y_k) on the image, and

$$M_S(f) = \frac{\left|\sum_k w_k\, e^{-2\pi i f x_k}\right|}{\sum_k w_k}, \qquad M_T(f) \text{ likewise in } y_k,$$

with w_k the ray's wavelength weight. There is no diffraction factor: the MTF is the geometric one alone. It is the limit of the diffraction OTF as λ → 0, and WavefrontErrorCalculator checks it as that, against its own diffraction OTF at λ/100, within 0.0012 on the Cooke triplet (WavefrontErrorCalculator's method.md, section 11.1).

**As a criterion.** Larger is better and the MTF is at most 1, so the module takes

$$W = \sqrt{1 - M}, \qquad Q = W^2 = 1 - M.$$

Q is then a "smaller is better" quantity like the others. The sensitivity run, the error budget and the allocation's steering all read its terms as MTF drops. A floor M_min on the MTF is a limit √(1 − M_min) on W, and the levels are turned back into MTFs only when they are printed.

**Why not a quadratic.** An MTF saturates. Once other errors have spread the spot, a further error changes the MTF less than it would alone. A quadratic in the errors cannot do this. With independent errors, a quadratic's variance is at least the sum of the variances each error gives alone (section 5.3: the cross terms add variance, never remove it). On the triplet at the precision grade, at 14° and 20 cycles/mm, that sum was 1.7 times the variance of 400 lenses traced with every error at once. Quadratics in the MTF and in its logarithm both put the 97.7 % floor up to 0.4 off, some of it below zero.

**The spot synthesis.** What is nearly linear in the errors is where each ray lands. So each ray's coordinate across the lines is moved linearly by every error,

$$y_k(\mathbf x) = y_{0k} + \sum_i g_{ik}\, x_i,$$

with g_ik its slope between tolerance i's two ends in the sensitivity run, which is refocused, so that the refocus is in the slopes. The MTF of any lens is then Kidger's sum of that spot, and the exponentials make the saturation themselves. This is Starke and Wise 1980's linear synthesis applied to the spot rather than to Zernike coefficients.

- The levels and the yield come from the sampled spread (section 6.3): 20 000 lenses drawn, each with its spot synthesized and summed. Koch's moments, which are the quadratic's, are not used.
- The mean and spread printed are those of the sample.
- A ray that does not arrive at nominal is left out. One stopped at either end of a tolerance keeps its nominal place for that tolerance, as a wavefront sample does (section 4.3).
- The quadratic model is still fitted, for the error budget and to steer the allocation. Its cross terms come from finite differences of the synthesis.

Against 2000 traced lenses, the synthesis put the 97.7 % floor within 0.007 in five cases on the test triplet, and the yield within 0.4 points, with no lean either way (verification.md, section 7).

*Code:* `MtfCriterion`, `SpotSynthesis`, `QuadraticModel.Synthesis`; `GeometricMtf` in WavefrontErrorCalculator.

## 4. Cross terms

### 4.1 Traced pairs

Two errors that make the same aberration, such as two wedges that both make coma, add their wavefronts before squaring. Their combined effect on Q is not the sum of their separate effects, and c_ij carries the difference. Koch 1978 measures it as the change with both errors applied, less the two individual changes. With both errors at their maxima,

$$c_{ij} = \frac{Q_{ij} - Q_i - Q_j + Q_0}{t_i\,t_j}.$$

A pair can leave the lens untraceable at both maxima though each error traces alone. The pair is then traced at a fraction s of them, a half and then a quarter, and

$$c_{ij} = \frac{Q_{ij}(s t_i, s t_j) - Q_0 - \Delta_i(s t_i) - \Delta_j(s t_j)}{s^2\, t_i\, t_j},$$

with Δ_i each term's own change, A_i x² + B_i x. Since each term's parabola passes through its ends, s = 1 is the formula above. A pair untraceable even at a quarter is left out, and named.

That costs one more traced lens per pair, n(n−1)/2 for n tolerances. Koch suggested evaluating them mainly for compensators. Cross terms add nothing to the mean of Q, but they do add to its variance (section 5), and on real lenses that can be most of it: 78 % in a Cassegrain, whose two mirrors' tilts and decenters largely cancel. So every pair is wanted, which tracing makes costly on a large set: 3160 pairs for 80 tolerances. The module traces them only on request (`--cross all`, or `auto`: every pair up to 80 tolerances and the x and y of each decenter and tilt beyond). By default it takes them from the slopes, sections 4.2 and 4.3.

### 4.2 Departures: from the slopes

For a departure, section 3.3 gives every cross term from the sensitivity run alone:

$$c_{ij} = 2\,\mathbf g_i\cdot\mathbf g_j,$$

with no pair traced, however many tolerances there are. A pair whose slopes are orthogonal, such as an x tilt and a y tilt acting on a boresight, has none.

### 4.3 RMS wavefront: from the pupil samples

The same idea extends to the RMS wavefront. The criterion is a weighted sum of variances over fields f, wavelengths λ and pupil samples k:

$$W^2 = \sum_{f,\lambda} \frac{w_{f\lambda}}{\sum w} \sum_k \frac{a_k}{\sum a}\,(w_k - \bar w_{f\lambda})^2 = |\mathbf v|^2,$$

with a_k the quadrature weight of sample k (Forbes 1988), and

$$v_k = \sqrt{\frac{w_{f\lambda}}{\sum w}\,\frac{a_k}{\sum a}}\;(w_k - \bar w_{f\lambda}).$$

So W² is the length squared of a vector, as a departure's is. If each error changes the refocused wavefront linearly, v ≈ v₀ + Σ g_i x_i, and

$$Q = |\mathbf v_0|^2 + 2\sum_i x_i\,\mathbf v_0\cdot\mathbf g_i + \sum_{i,j} x_i x_j\,\mathbf g_i\cdot\mathbf g_j,$$

so c_ij = 2 g_i·g_j, with g_i the slope of v between tolerance i's two ends.

- **Refocusing.** The vectors are taken after refocusing, so the slopes are those of the refocused wavefront. In the linear model, refocusing projects every vector off the focus direction f, and the refocused Q's cross term is 2(Pg_i)·(Pg_j), with P the projection: what the refocused slopes give.
- **What it leaves out.** The wavefront's own curvature dotted with the nominal residual, 2 v₀·∂²v/∂x_i∂x_j. A departure has v₀ = 0 and loses nothing; a wavefront with a large nominal error loses most.
- **Vignetting.** A pupil sample that is vignetted has no wavefront. A sample vignetted at either end, or in the nominal lens, is given no slope. A tolerance whose ends change which samples arrive, by more than 10 % of those arriving in the nominal lens, has its pairs traced instead. The samples that come and go also change the weights a_k/Σa of the rest a little.
- **How well it works.** Against every pair traced, it moved the 97.7 % level by 2 % at most, on the 16 lenses where every pair could be traced. Its model check was better on 14 of 17 (verification, section 8), most of all where rim rays clip, because two traced ends can land on different vignetting states and the slopes average over that edge; it was 0.1 point worse on two.

This is related to Starke and Wise 1980's linear synthesis, which adds Zernike changes per unit perturbation to make each Monte Carlo lens without tracing it. Here the pupil samples themselves are the basis, and only the cross terms are synthesized: each tolerance's own A and B still come from the traced ends.

*Code:* `QuadraticModel.Fit`, `QuadraticModel.DeviationCross`, `QuadraticModel.WavefrontSlopes`, `RmsWavefrontCriterion.Vector`, `CrossTerms`.

## 5. Koch's second-moment method

### 5.1 The idea

Koch 1978 computes the mean and variance of a performance change directly from the moments of each error's distribution, with no random lenses. Given the sensitivities, the arithmetic is fast enough for a desk calculator, and the effect of changing a tolerance can be seen at once. The module evaluates it thousands of times inside each allocation.

### 5.2 The moments of the errors

The errors are independent, and each is symmetric about the center of its range. For a range ±t (Koch 1978, Table 1):

| Distribution | σ² | ν₄ | ν₆ |
|---|---|---|---|
| Uniform | t²/3 | t⁴/5 | t⁶/7 |
| End points | t² | t⁴ | t⁶ |
| Normal truncated at ±kσ | see below | see below | see below |

For a unit normal u kept within ±k, with Z = 2Φ(k) − 1, integration by parts gives

$$E[u^2] = 1 - \frac{2k\varphi(k)}{Z}, \quad E[u^4] = 3E[u^2] - \frac{2k^3\varphi(k)}{Z}, \quad E[u^6] = 5E[u^4] - \frac{2k^5\varphi(k)}{Z},$$

scaled by t/k. Koch's printed table has entries that look garbled. The module re-derives every moment and tests it against sampling.

### 5.3 Mean and variance

For zero-mean symmetric errors, Koch's eqs. 20 and 21:

$$\mu_Q = Q_0 + \sum_i A_i\sigma_i^2,$$

$$\sigma_Q^2 = \sum_i \left[A_i^2(\nu_{4i} - \sigma_i^4) + B_i^2\sigma_i^2\right] + \sum_{i<j} c_{ij}^2\,\sigma_i^2\sigma_j^2.$$

How to read them:

- The **A** terms move the mean. A well-corrected lens has small B, so its main response to tolerances is a drop in mean performance, which linear RSS misses entirely.
- The **B** terms, linear, add only to the variance, as in RSS.
- The **cross terms** add only to the variance.

### 5.4 Biases

A range not centered on zero, a bias with center c_i, is taken out first. With x = c + u, the model in u has

$$Q_0' = Q(\mathbf c), \qquad B_i' = B_i + 2A_ic_i + \sum_j c_{ij}c_j,$$

and the same A and cross terms. Koch's general eq. 18 does the same with odd moments. The bias's fixed shift Q(c) − Q₀ is part of the mean, not random, and the budget tree shows it as its own leaf.

### 5.5 Polar errors

A decenter or a wedge has a size and a direction. Drawn as one error, x = ρ cos θ and y = ρ sin θ, with θ uniform on the circle and ρ from a radial distribution (Koch 1978, eqs. 23–27). The moments of ρ must be taken over the radius's density p′(ρ) = ρ p(ρ), not the point's. Uniform over a disc gives a triangular ρ, with ν₂ = t²/2 and ν₄ = t⁴/3. A circular normal gives a Rayleigh ρ.

With S = (A_x + A_y)/2, Δ = (A_x − A_y)/2 and D the pair's cross term, the pair's change of Q is

$$\rho^2\left(S + \Delta\cos 2\theta + \tfrac12 D\sin 2\theta\right) + \rho\,(B_x\cos\theta + B_y\sin\theta).$$

Averaging over θ, the module's re-derivation gives

$$\mu = S\nu_2, \qquad \sigma^2 = \nu_4\left(S^2 + \frac{\Delta^2}{2} + \frac{D^2}{8}\right) + \nu_2\,\frac{B_x^2 + B_y^2}{2} - (S\nu_2)^2.$$

This is Koch's eq. 27, since ν₄(S² + Δ²/2) = ν₄(3A_x² + 2A_xA_y + 3A_y²)/8, with three misprints corrected:

- eq. 25 multiplies the pair's cross term D by ρ where ρ² is meant;
- eq. 26 gives the mean as μ², a stray square;
- eq. 27 has D ν₄/8 where the derivation gives D² ν₄/8.

### 5.6 The third moment

The module also carries Q's third cumulant, for the skewness that two of the spread shapes need (section 6). For one error, the centered change A(u² − σ²) + Bu has

$$\kappa_3 = A^3(\nu_6 - 3\sigma^2\nu_4 + 2\sigma^6) + 3AB^2(\nu_4 - \sigma^4).$$

The cross terms add, with D_i each error's own centered change:

- 6 Σ c_ij B_i B_j σ_i²σ_j², since E[D_i x_i] = B_i σ_i²;
- 3 Σ c_ij²(E[D_i x_i²] σ_j² + E[D_j x_j²] σ_i²);
- 6 Σ over triangles c_ij c_jk c_ki σ_i²σ_j²σ_k².

A polar pair's third moment is averaged over θ by the trapezoid rule on 72 points. That is exact here, since the integrand is a trigonometric polynomial of degree at most 6.

*Code:* `Koch.Predict`, `Distribution`, `RadialDistribution`, `StatisticsModel`.

## 6. The shape of the spread

### 6.1 Koch's normal

From the central limit theorem, Koch took ΔQ as normal once there are four linear or six to eight quadratic parameters, so that 97.7 % of lenses lie within μ + 2σ. That is Hilbert 1979's "probable worst case".

### 6.2 Why it is not enough

Q is a sum of squares, and in practice a few dominant errors shape it. Its spread is anchored near Q₀ with a long upper tail, and a normal understates that tail. The module offers three other shapes:

| Shape | From | How |
|---|---|---|
| Gamma | Mean and variance | Shape k = μ²/σ², scale θ = σ²/μ. Anchored at zero, with a long upper tail |
| Pearson type III | Mean, variance and skewness γ | A gamma of shape 4/γ² and scale σγ/2, shifted to Q's mean, so its tail is as long as the third moment says |
| Sampled | The model itself | Q drawn for 20 000 lenses on the quadratic model. Nothing is assumed beyond the model, and each draw costs microseconds |

On the test triplet the moments agree with traced lenses every time. The shapes differ in their tails. The 97.7 % level, in waves, against 4000 traced lenses each:

| Set | Traced | Sampled | Pearson III | Gamma | Normal |
|---|---|---|---|---|---|
| Commercial grade, uniform errors | 2.68 | 2.63 | 2.63 | 2.64 | 2.53 |
| Biases, normal glass, end-point decenters | 1.483 | 1.481 | 1.483 | 1.484 | 1.479 |

Normal, Koch's own, is 6 % low on the first set, whose spread has a long tail. The others are within 2 % on both. A spread can be of a shape no moments describe: judged on one side of the field only, the second set's end-point decenters changed W² linearly and gave a spread flatter than any shape fitted to moments, which put Pearson III and gamma 4 to 6 % high, though even the skewness agreed. The sampled shape assumes nothing beyond the model, so it is the default.

### 6.3 The sampled shape

The 20 000 draws use a fixed generator for each draw, SplitMix64 (Steele, Lea and Flood 2014) seeded from the draw's number. The same draws then serve every prediction of a model, so a quantile moves smoothly as the tolerances are scaled, which the allocation's searches need. This is common random numbers.

*Code:* `Prediction`, `SpreadShape`, `Koch.SampleQ`, `Special`.

## 7. Allocation

### 7.1 Inverse sensitivity

The oldest allocation divides each performance allowance by the sensitivity: t_i = c/s_i, taking the tightest over all specifications (Adams 1988, eq. 6; Heshmaty-Manesh and Haig 1986). Ginsberg 1981 splits an allowance L equally among N tolerances as L/√N each. Adams's second-order version solves c = s t + s₂ t² for the tightest root, and ignores cross terms. These give a set that meets each specification for each tolerance alone, but not a predicted yield.

### 7.2 Equal effect

Hilbert 1979 scales every tolerance so that each causes the same drop, for example 0.01 in MTF, then clamps to the shop's limits and rounds. Smith 1985 makes each tolerance inversely proportional to its sensitivity. Koch 1978 splits the variance equally, clamps the tolerances that fall out of range to their limits, and rescales the rest by a common factor until μ + 2σ meets the margin.

The module's equal effect combines them:

1. Each tolerance (each x–y pair counted once) is set where it alone raises Q by δ, clamped to its shop limits.
2. The rise is taken along each end's increasing envelope. A tolerance whose criterion falls again past a peak is not given more for it.
3. δ is the largest for which the prediction meets the goal, found by bisection in ln δ.

With several criteria, each rise is a fraction of that criterion's own target Q, and a tolerance is sized by the criterion it uses most of, so none takes more than δ of any one budget.

### 7.3 Least cost: Adams's condition

Adams 1988 takes the cost of a tolerance as τ_i/t_i, its "infeasibility": the tightest a shop can hold, over the value asked for. That follows Willey's cost ∝ 1/t. He minimizes the total, Σ τ_i/t_i, subject to the performance constraints. For one linear constraint his closed forms are:

| Constraint | Solution | Tolerance |
|---|---|---|
| Worst case, Σ \|s_i\| t_i = c (eq. 15) | t_i = √(τ_i/\|s_i\|) · c / Σ_k √(τ_k \|s_k\|) | ∝ \|s\|^−½ |
| RSS, Σ s_i² t_i² = c² (eq. 17) | t_i = ∛(τ_i/s_i²) · c / √(Σ_k s_k^⅔ τ_k^⅔) | ∝ s^−⅔ |

For several constraints r_m ≤ 1, the Lagrange condition is (his eqs. 19 and 20)

$$\frac{\tau_i}{t_i^2} = \sum_m \lambda_m\,\frac{\partial r_m}{\partial t_i},$$

with each λ_m ≥ 0, and zero unless its constraint binds (complementary slackness, the KKT conditions).

### 7.4 Least cost on Koch's prediction

The module applies Adams's condition to Koch's prediction rather than to linear RSS. Each constraint is a criterion's level at the goal's yield, over its limit:

$$r_m(\mathbf t) = \frac{Q_m^{(y)}(\mathbf t)}{L_m^2} \le 1,$$

with Q^(y) the level of Q that a fraction y of lenses are within. A compensator travel limit is one more constraint. Writing h_mi = (∂r_m/∂t_i)/t_i, the condition becomes

$$t_i = \left(\frac{\tau_i}{\sum_m \lambda_m h_{mi}}\right)^{1/3}.$$

For Adams's RSS, h is constant and this is his eq. 17. For Koch's prediction, h varies with t, so the module iterates:

1. Hold h at the present tolerances.
2. Find the multipliers.
3. Recompute the tolerances and h, and repeat until no tolerance changes by more than 10⁻⁹ of itself.

The multipliers are found by Newton's method in ln λ on the binding constraints, ln r_m = 0, started from the previous iteration's.

- A broken constraint enters with the multiplier that alone would give the present tolerances.
- A slack constraint whose multiplier hardly moves anything beside the others leaves, since driving it to zero would only make the step ill-conditioned.
- A backtracking line search keeps every step reducing the worst miss.
- If Newton's method fails, bisection of each multiplier inside the others' is the fallback.

On the triplet with three criteria this took about 20 s, where nested bisection took 5½ minutes, for the same answer.

### 7.5 The sampled shape in the search

A sampled level costs a 20 000-lens draw, and the search needs thousands of levels. So the allocation is solved on the Pearson III shape, which is analytic. Each limit is then moved by how far Pearson's level is from the sampled one at the answer, and the allocation is solved again, until the sampled levels meet the goal, in at most six passes. Last, if the answer still misses the goal, every value is drawn back toward its tightest by one common factor until it meets the goal.

### 7.6 Limits, rounding and refits

- **Shop limits** (Smith 1985, §VI; Burge, OPTI 521). Tighter than τ_i the shop cannot hold, and looser than its base saves nothing. The module takes τ_i from Smith's extra-precise grade, and the loosest value from his low-cost grade or Burge's base, whichever is tighter.
- **Rounding.** Values are rounded down to preferred numbers (1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8), and index and Abbe number to the grades glass is sold in. Rounding down only tightens, so the rounded set still meets the goal.
- **Refits** (Heshmaty-Manesh and Haig 1986). The model was fitted where the tolerances started. After allocating, the sensitivities are rerun at the allocated values and the allocation is repeated on the new model: their correction for non-linearity, made by re-evaluating at the new tolerances.

*Code:* `Allocator`, `Adams`, `ShopLimits`, `Budget`.

## 8. Several criteria

### 8.1 Each criterion a constraint

A lens is often specified by more than image quality: also a focal length, a back focus, a line of sight (Milby 2009; Smith 1985). Each further criterion gets its own quadratic model, from the same traced lenses, and becomes one more constraint r_m ≤ 1 in the allocation. Each lens is refocused once, for the sharpest image, and every criterion is measured on it.

### 8.2 The joint yield

Meeting each criterion at yield y does not put y of the lenses within every limit at once. The same errors drive every criterion, so the joint yield J is neither the product of the separate yields nor known from them. It is bounded:

$$1 - N(1 - y) \le J \le y,$$

the left side being the union bound for N criteria. The module counts J on the sampled draws of section 6.3: the fraction of the 20 000 lenses within every limit at once.

To allocate to a joint yield, every criterion is held to one higher marginal yield y′. It is chosen so that J(y′) equals the goal, and lies between the goal and 1 − (1 − goal)/N. The failure fraction F of the joint failures is nearly proportional to each criterion's own, so y′ is found by a secant search in ln F against ln(1 − y′), bracketed and stopped within 2 % of the target. That takes two to four allocations.

*Code:* `Koch.JointYield`, `Allocator.Allocate`, `Requirement`.

## 9. Checking the model

Koch's method is exact for the quadratic model. Everything else rests on the model being right. The module checks it two ways.

- **Model check.** Twelve lenses are drawn at random and traced in full, and each one's criterion is set beside the model's for the same errors. A dozen lenses compared one by one show a model failure that comparing distributions would need hundreds of lenses to see. A check passes within 15 % RMS. The RMS rather than the worst lens is used because errors of a few lenses partly cancel in the statistics: on a triplet, one lens in twelve was 15.6 % off and the RMS 8.3 %, and Koch agreed with 600 traced lenses to 1 % in mean W².
  - For a departure, the errors are fractions of the spread, since one lens can have almost no departure.
  - For an MTF, the lenses are set against the spot synthesis (section 3.4), and the errors are fractions of the traced MTFs' scatter among the twelve, in the MTF itself. The bar is 25 %. A fraction of W would be too lenient: √(1 − M) moves little while the MTF moves a lot. Before irregularity was in the default set, a set on the triplet that missed by 26.5 % still put the 97.7 % floor within 0.02 of 1000 traced lenses, on the safe side, and sets within 6 % put it within 0.006.
- **Monte Carlo.** Random lenses, every error drawn at once, refocused and traced in full (Heshmaty-Manesh and Haig 1986; Kojima 1979; Hilbert 1979). This checks everything the prediction assumes: the quadratic model, the cross terms evaluated, and the spread's shape. The 1980s papers used about a hundred trials; with today's computers, the module's runs use thousands.
- **The model's own Monte Carlo.** The same draws, but evaluated on the quadratic model rather than traced, check the moment algebra alone. The tests require 400 000 such draws to agree with Koch's mean within four standard errors, his variance within 2 %, and his skewness within 0.03, for every way of drawing the errors.

*Code:* `ModelCheck`, `MonteCarlo.Run`, `MonteCarlo.RunModel`.

## 10. Error budgets

Sinclair 1990 organizes a tolerance budget as a tree. Each node holds a share of the criterion's degradation, with an allowance and a combination rule: RSS for independent random terms, and linear for terms with one common cause. Smith 1985 combines classes of tolerance by the RSS of class.

The module evaluates such trees from Koch's contributions. Each node gets the mean and σ it adds to W², and an equivalent √(mean + 2σ): the criterion that node alone would cause in 97.7 % of lenses, if normal. Children combine by their node's rule. Two leaves sit at the root of every tree: the cross terms' variance, which belongs to no single tolerance, and the biases' fixed shift of the mean.

*Code:* `BudgetTree`, `BudgetNode`, `NodeResult`.

## 11. The core papers

### Koch 1978, "A statistical approach to lens tolerancing"

*Proc. SPIE 147, 71.* The analytic engine of the module.

- Monte Carlo was expensive in 1978, and its results could not be updated without rerunning it. So Koch derived the mean and variance of a performance change from the moments of each error's distribution, for:
  - a linear change: μ = Σ A_i μ_i, σ² = Σ A_i² σ_i² (eqs. 12 and 13);
  - a quadratic change with cross terms (eq. 17), with general mean and variance (eqs. 18 and 19);
  - the special case of zero-mean symmetric errors (eqs. 20 and 21);
  - x–y errors in polar form (eqs. 23–27).
- He tabulates the moments of uniform, end-point and truncated normal errors, and of disc and Rayleigh radii.
- He shows RSS to be the linear special case, with coverage set by k = σ/t.
- He gives a ten-step budgeting procedure that clamps, redistributes and rescales.

What the module takes: all of it, applied to W² rather than W, with the polar case and the moment tables re-derived where the printed equations are wrong. It adds the third moment and other shapes for the spread.

### Hilbert 1979, "Semi-automatic modulation transfer function (MTF) tolerancing"

*Proc. SPIE 193, 34.* The specification of CODE V's MTF tolerancing.

- A default tolerance set by surface, element and cemented group, with minimum and maximum clamps for each kind.
- Allocation by equal MTF drop.
- Plus and minus sensitivities shown separately.
- The summary statistic mean + 2σ at 97.7 %, his "probable worst case".
- Compensators and their ranges, and tables an opto-mechanical designer can use directly.

What the module takes: the default-set taxonomy, equal effect, the 97.7 % level, the compensator range, and RMS wavefront as the criterion where it is appropriate.

### Ginsberg 1981, "Outline of tolerancing (from performance specification to toleranced drawings)"

*Opt. Eng. 20(2), 175.* Revised from SPIE 181 (1979).

- The whole process in eleven steps, from performance specification to drawings, with its feedback loops.
- The sensitivity table: change after compensation, with the compensator's travel.
- The error budget, ordered by element so that it transfers to drawings.
- Equal √N allocation.
- Tables of "fairly tight" and "fairly loose" tolerances.

What the module takes: the workflow its commands follow, the table formats, and the element tolerance range.

### Smith 1985, "Fundamentals of establishing an optical tolerance budget"

*Proc. SPIE 531, 196.*

- RSS statistics, built up from the thickness of a stack of disks.
- The change table, and adjusting a trial budget to meet a goal.
- Conversions between aberrations, fringes and OPD, and Strehl from RMS.
- Four shop grades (his Table 3).
- A complete worked example: a laser-disc recording lens, with prescription, change table and final budget.

What the module takes: the shop grades for the default set and the allocation's limits, the trial-budget procedure, and his example as a regression test. The module reproduces his lens, and compares his change table, which estimates P-V OPD from third-order aberrations, with its own traced changes.

### Adams 1988, "Selection of tolerances"

*Proc. SPIE 892, 173.*

- RSS and worst case.
- Inverse sensitivity, first and second order.
- Cost-optimal allocation: minimize Σ τ_i/t_i subject to performance constraints, by Lagrange multipliers. He gives closed forms for one worst-case or RSS constraint, and the KKT conditions for several.
- A penalty to keep tolerances within shop limits.

What the module takes: the cost function and the Lagrange condition, applied to Koch's prediction instead of linear RSS, with Newton's method for several multipliers.

### Sinclair, Tritchew and Jennison 1990, "Tolerancing methodology for an IR optical telescope"

*Proc. SPIE 1309, 233.*

- A hierarchical budget tree, each node with an allowance, a combination rule (random RSS or systematic linear) and a compensable flag.
- Apportioning optical tolerances to mechanical drawing features.
- An as-built loop: measured values replace nominals, and the analysis is rerun.

What the module takes: the budget tree.

### Heshmaty-Manesh and Haig 1986, "Lens tolerancing by desk-top computer"

*Appl. Opt. 25(8), 1268.*

- A simple loop: a change table, a correction for non-linearity by re-evaluating at the tolerance itself, scaling, then a Monte Carlo yield, and adjustment.
- Criteria with upper and lower limits.
- Biased distributions, such as thickness made on the high side for rework.

What the module takes: the refit after allocation, and biased ranges.

### Starke and Wise 1980, "Modulation-transfer-function-based optical sensitivity and tolerancing programs"

*Appl. Opt. 19(11), 1768.*

- Zernike changes are linear in each perturbation and add.
- So each Monte Carlo lens can be synthesized from one trace per perturbation, an order of magnitude faster than tracing it.

What the module takes: the idea behind `--cross wavefront`, applied to the pupil samples, for cross terms only; and, applied to where the rays land, the spot synthesis from which an MTF's whole spread is drawn (section 3.4).

### Forbes 1988, "Optical system assessment for design: numerical ray tracing in the Gaussian pupil"

*J. Opt. Soc. Am. A 5(11), 1943.*

- Gauss quadrature over the pupil gives the mean square of a smooth wavefront exactly with few rays.

What the module takes: the RMS criterion samples the pupil by an 8-ring, 24-arm Gauss rule, 192 rays, exact for a wavefront polynomial up to ρ³⁰. A grid or rings weighted ray by ray count the rim's rays as heavily as the center's, and overstated the RMS of the test triplet's axis by 15 %.

### Others used in part

| Source | What the module takes |
|---|---|
| Grey 1970, 1978 | Why plus and minus differ: a nonzero linear term means asymmetric tolerances |
| Kojima 1979 | Cemented groups moving rigidly in a Monte Carlo |
| Williamson 1989 | Compensator practice, and splitting irregularity for drawings |
| Burge, OPTI 421/521 | The base tolerances any shop holds, glass grades, chamfer widths, wedge as deviation |
| Milby 2009 | Criteria besides image quality: EFL, BFL, boresight |
| Kidger 1974, 1978, 2002 | The geometric MTF by direct summation over the spot, and its check as the diffraction OTF's limit at short wavelength |
| Brent 1973 | The refocus's minimizer |
| Wilson and Hilferty 1931 | The gamma distribution's quantile at large shape |
| Steele, Lea and Flood 2014 | SplitMix64, the sampled shape's generator |

## 12. Where the module departs from the papers

| Paper's method | The module | Why |
|---|---|---|
| Koch: the model in W | The model in W² | A mean square is quadratic in small changes; its root is not (section 3.1). Against traced lenses the model in W put the 97.7 % level 13 to 113 % high on lenses at the commercial grade, the model in W² within 1.0 % on five of six cases and 4.2 % low on a Cassegrain (verification, section 3.3) |
| Koch: cross terms mainly for compensators | Every pair up to 80 tolerances; free for departures and, optionally, the RMS wavefront | Cross terms can be most of the variance (section 4) |
| Koch: a normal spread | A sampled spread by default; gamma and Pearson III available | Q is a skewed sum of squares; the normal understates its tail (section 6) |
| Koch: eqs. 25–27 as printed | Re-derived | Three misprints (section 5.5) |
| Adams: linear RSS constraints | Koch's prediction at a yield, iterated | The constraint is the yield, not an RSS (section 7.4) |
| Adams: KKT for several constraints | Newton's method in ln λ on the binding set | Fast and robust for several criteria and a travel limit |
| Hilbert, Smith: equal drop | Equal rise of Q, along its increasing envelope, by the criterion each tolerance uses most of | Works with several criteria, and with errors past a criterion's peak |
| All: each criterion at its own yield | Optionally a joint yield | The lenses must meet every limit at once (section 8.2) |
| Monte Carlo as the check | A 12-lens model check as well | It finds a model failure with a dozen lenses (section 9) |
| Starke and Wise: an MTF synthesized from Zernike changes | The spot synthesized from each ray's slopes, and its MTF by Kidger's sum; W² = 1 − MTF for the budget | The rays are what is linear in the errors; an MTF saturates, which a quadratic in the MTF cannot follow (section 3.4) |
| Hilbert: irregularity a fraction of power, on the drawing | Irregularity a tolerance of its own (TIRR), analyzed and allocated like the rest: half astigmatism, half spherical, Williamson 1989's split, at Smith 1985's grades | It changes the wavefront on its own; a ratio to power, when power is loosened because the lens is insensitive to it, gave irregularity no analysis supported (6 fringes on the triplet) |
