# Examples

What `tol` prints and writes, on one lens from start to finish: the typical session of the [user guide](../docs/user-guide.md), section 1, run on the in-repo Cooke triplet (`tests/Tolerancing.Tests/TestData/cooke_triplet.zmx`) at the commercial grade. Then the same, as far as the drawings, on an aspheric singlet ([below](#an-aspheric-singlet)). Everything here was written by the tool. `run.sh` regenerates it in a few minutes; on Windows, run it from Git Bash.

## Reports

What each command prints, with the command at the top.

| File | Command | What it shows |
|---|---|---|
| [1-nominal.txt](reports/1-nominal.txt) | `tol nominal` | First-order data, and the RMS wavefront error of each field (user guide 5.1) |
| [2-sensitivity.txt](reports/2-sensitivity.txt) | `tol sensitivity --top 15` | The 15 tolerances that matter most, each at both ends of its range, refocused (5.2) |
| [3-allocate.txt](reports/3-allocate.txt) | `tol allocate --limit 2 --method cost` | Every tolerance sized by least cost so that 97.7 % of lenses are within 2 waves RMS; the error budget and Koch's prediction (5.5, 10, 11) |
| [4-statistics.txt](reports/4-statistics.txt) | `tol statistics --tolerances … --montecarlo 1000 --plots plots` | The allocated set's prediction, checked against 1000 lenses traced in full, and drawn as SVG and PNG: [monte-carlo.html](plots/monte-carlo.html) (5.4, 10.5) |
| [5-several-criteria.txt](reports/5-several-criteria.txt) | `tol allocate --limit 2.2 --also efl:0.25 --also boresight:0.2` | One set meeting three criteria: RMS wavefront, focal length within 0.25 mm and boresight within 0.2° (6.2) |
| [6-mtf-floor.txt](reports/6-mtf-floor.txt) | `tol statistics --criterion mtf:0:20:t --limit 0.3` | The allocated set judged by its tangential MTF at 20 cycles/mm on axis (6.3) |
| [7-drawings-iso.txt](reports/7-drawings-iso.txt), [8-drawings-asme.txt](reports/8-drawings-asme.txt) | `tol drawings` | The elements drawn, with the warnings on thin edges (12) |
| [cooke_triplet.json](reports/cooke_triplet.json) | `tol allocate --report` | The allocation's JSON report: everything above, for drawings and other programs (13) |

## Tolerance files

| File | What it is |
|---|---|
| [cooke_triplet.tol](tolerances/cooke_triplet.tol) | The set allocated to 2 waves, which the statistics and drawings above read (user guide 4.3) |
| [cooke_triplet_three_criteria.tol](tolerances/cooke_triplet_three_criteria.tol) | The set allocated to the three criteria |

## Drawings

One drawing per element, from the 2-wave set, in both notations (user guide, section 12). Each comes as PDF, DXF (R12, on layers) and SVG, and `drawings.pdf` holds every sheet.

| Element | ISO 10110 | ASME Y14.18 / MIL-PRF-13830B |
|---|---|---|
| OPT-01, lens 1-2, N-SK16 | [PDF](drawings/iso/OPT-01.pdf) · [SVG](drawings/iso/OPT-01.svg) · [DXF](drawings/iso/OPT-01.dxf) | [PDF](drawings/asme/OPT-01.pdf) · [SVG](drawings/asme/OPT-01.svg) · [DXF](drawings/asme/OPT-01.dxf) |
| OPT-02, lens 3-4, F2 | [PDF](drawings/iso/OPT-02.pdf) · [SVG](drawings/iso/OPT-02.svg) · [DXF](drawings/iso/OPT-02.dxf) | [PDF](drawings/asme/OPT-02.pdf) · [SVG](drawings/asme/OPT-02.svg) · [DXF](drawings/asme/OPT-02.dxf) |
| OPT-03, lens 6-7, N-SK16 | [PDF](drawings/iso/OPT-03.pdf) · [SVG](drawings/iso/OPT-03.svg) · [DXF](drawings/iso/OPT-03.dxf) | [PDF](drawings/asme/OPT-03.pdf) · [SVG](drawings/asme/OPT-03.svg) · [DXF](drawings/asme/OPT-03.dxf) |
| Every sheet | [drawings.pdf](drawings/iso/drawings.pdf) | [drawings.pdf](drawings/asme/drawings.pdf) |

The drawings use the default settings, [drawing-settings.json](settings/drawing-settings.json), as `tol settings` writes them: no title-block company, project or author, no coatings, and the default cosmetic and material classes. Edit a copy and pass it with `--settings` to fill them in (user guide 12.2).

The triplet is a test lens, not a product: its first and last elements have edges of 0.96 and 0.52 mm, which `tol drawings` warns are thin to make.

## An aspheric singlet

The same session, as far as the drawings, on a lens with an asphere: `verification/lenses/aspheric_singlet.zmx`, an N-BK7 singlet whose front surface is an even asphere (k = −0.55, A4 = −1.2 × 10⁻⁶).

| File | What it shows |
|---|---|
| [9-asphere-allocate.txt](reports/9-asphere-allocate.txt) | `tol allocate --limit 0.17 --method cost`: every tolerance sized so that 97.7 % of lenses are within 0.17 waves RMS. The lens is forgiving, and most tolerances go to their loosest |
| [aspheric_singlet.tol](tolerances/aspheric_singlet.tol) | The allocated set |
| [aspheric_singlet_edited.tol](tolerances/aspheric_singlet_edited.tol) | The set edited by hand: the asphere's rotationally invariant irregularity, `TRSI 1 0.5`, and its conic, `TCON 1 0.005`, added at the end (user guide 4.1, 4.3) |
| [10-asphere-statistics.txt](reports/10-asphere-statistics.txt) | `tol statistics --montecarlo 1000`: the edited set's prediction, checked against 1000 lenses traced in full. Koch's 97.7 % level is 0.1687 waves, the traced lenses' 0.1688 |
| [11-asphere-drawings-iso.txt](reports/11-asphere-drawings-iso.txt), [12-asphere-drawings-asme.txt](reports/12-asphere-drawings-asme.txt) | `tol drawings` |

| Element | ISO 10110 | ASME Y14.18 / MIL-PRF-13830B |
|---|---|---|
| OPT-01, lens 1-2, N-BK7 | [PDF](drawings/aspheric_singlet/iso/OPT-01.pdf) · [SVG](drawings/aspheric_singlet/iso/OPT-01.svg) · [DXF](drawings/aspheric_singlet/iso/OPT-01.dxf) | [PDF](drawings/aspheric_singlet/asme/OPT-01.pdf) · [SVG](drawings/aspheric_singlet/asme/OPT-01.svg) · [DXF](drawings/aspheric_singlet/asme/OPT-01.dxf) |

What the asphere's drawing shows (user guide, section 12):

- **Sag table** beside the view: h, z, ±Δz in µm and ±Δs in mrad from the vertex to the clear aperture, the most the surface's power, irregularity and TRSI move and tilt it.
- **Power as a radius tolerance.** The allocation leaves 50 fringes of power on the asphere, more than a test plate is read to, so it is drawn on the radius, `R 25.641 ±0.5`, with a dash for power: `3/-(1.5/0.5)`, where 0.5 is the TRSI as ISO 10110-5's C. The sag table follows the radius as drawn.
- **Centering** as ISO 10110-6's `4/σ(L)`: `4/4.8'(0.100)`, the surface tilt and the asphere's decenter.
- **Conic** with its tolerance in the asphere's note: `k = -0.550000 ±0.005`.
