# Agent instruction — the parting strategy report

How a body's parting approach is decided, what the report says, and what has already been ruled out.
Read this before touching `PartingStrategy`, `RidgeDetection`'s band repair, or the Parting Split
scene manager.

---

## What it is

`PartingStrategy.Evaluate(body, options, seamAvailable, seamError, wallThickness)` →
`PartingStrategyReport`.

- Source: [`src/Fabolus.Core/Features/PartingSplit/PartingStrategy.cs`](../src/Fabolus.Core/Features/PartingSplit/PartingStrategy.cs)
- Consumed by: `PartingSplitViewModel.Evaluate` (step one of the wizard) and
  `PartingSplitSceneManager` (rim colouring, via `PartingStrategy.Rims`)
- Exercised by: `PartingStrategySweep` (diagnostics)

It answers two things and nothing else: **which `PartingLineSource` this body wants**, and **how many
parting meshes it takes**. It computes no geometry and commits to nothing.

---

## The rules it encodes

Each is the way it is because the obvious alternative was measured and failed. Do not simplify one
without reading the matching entry under *Ruled out*.

**1. Watertight first.** An open mesh short-circuits to "neither source can be trusted". Genus is
undefined on a surface with a border, and every count below it is then being read off something that
is not a shell.

**2. Separation is asked of the rims *together*, never one at a time.** On a genus-1 body no single
closed curve can separate the surface — the two sides always meet by going round through the hole. It
takes `CutsNeeded` = genus + 1 cuts. A per-contour test therefore condemns rims that are perfectly
correct; it was what wrongly rejected `larynx-large`.

**3. The seam is asked last.** It decides whether the preferred answer is *available*, not which answer
is preferred. A body with a dividing rim whose thickness trace failed is reported as "fix the trace, do
not demote" — the rim says the extrusion border is there to be found, so failing to find it is a fault,
not a routing decision.

**4. Rims are grouped by band group, not by proximity.** Two rims of a body with a hole run close
together where they converge, so distance alone merges them. Each rim's wall is a different connected
group of regions; that group is the rim's identity, carried on `RidgeContour.Rim` and
`RidgeSurfaces.FaceRims`.

---

## The fields worth knowing

| Field | Meaning |
|---|---|
| `Shape`, `Genus`, `EulerCharacteristic`, `IsClosed` | Topology. `Shell` (χ 2) or `Torus` (χ 0) on this asset set. |
| `CutsNeeded` | genus + 1. How many cuts part the body — **the number of parting meshes**. |
| `Combined` | All closed contours walled at once. `Separates` is the real verdict. |
| `Combined.SubstantialPieces` | Pieces ≥ 5% of surface. **Report this, not `Components`.** |
| `Rims` | One entry per rim, with its contour indices and `Kind`. |
| `Rims[].Kind` | `Wall` (2 contours), `SingleRidge` (1), `Merged` (>2), `Unknown` (no wall supplied). |
| `Rims[].Line` | Where that rim's parting line runs, in words. |
| `NeedsHybrid` | `CutsNeeded > 1` — the split must build a mesh per rim. |
| `Recommended`, `Summary` | The verdict and its one-sentence justification. |
| `OverBudget` | More non-dividing contours than the genus accounts for. Diagnostic only now — rule 2 supersedes it. |

`wallThickness` is optional. Without it, `Kind` for a two-contour rim is `Unknown`; the one-contour and
more-than-two cases need no thickness and are decided either way. This is deliberate — it keeps
`Fabolus.Core`'s strategy path free of the MeshLib ray cast. The scene manager relies on it.

---

## How to use it

```csharp
var report = PartingStrategy.Evaluate(
    body.Mesh,
    seamAvailable: traced.IsSuccess,
    seamError: traced.IsFailure ? traced.Error.Description : null,
    wallThickness: wall);          // optional; omit to stay engine-free

if (report.Recommended != PartingLineSource.ExtrusionBorder) { /* body wants a pull direction */ }
if (report.NeedsHybrid)          { /* one parting mesh per rim */ }
foreach (var rim in report.Rims) { /* rim.ContourIndices, rim.Kind, rim.Line */ }
```

To draw the ridge, call `PartingStrategy.Rims(contours)` rather than regrouping — deriving the
grouping twice is how the picture and the report come to disagree.

**The report is advisory.** `PartingSplitViewModel.LineSource` is still hard-coded to
`ExtrusionBorder`. Step one shows the assessment in green when the body agrees and amber when it does
not; nothing gates on it yet.

---

## Facts established — do not re-derive

- **Band width ≈ wall thickness**, ratio 0.97–1.13 on all eight bodies. Three independent measurements
  agree (dihedral curvature, ray-cast thickness, contour-to-contour distance).
- **A rim's two contours sit 0.55–1.07 × wall apart**; anything unpaired measures 1.85 × or more. The
  1.6 tolerance sits in that empty gap.
- **`ridge↔seam` ≈ half the wall**, ratio 0.78–1.21. `standard`'s is now 6.96 mm after the band repair,
  up from 5.29 — a uniform outward shift of about one face ring, not a one-sided drift.
- **All eight bodies route to `ExtrusionBorder`.** Nothing needs `Silhouette`.
- **`larynx-small` is genus 1 and its 3 contours are correct** — it tapers to a knife edge where the
  outer rim and the tracheostomy rim merge.
- **`larynx-large`'s 4 contours are 2 proper wall rims**, ids 0 and 154. It is not a single-ridge body
  and it has no missed rim.
- `RidgeDetection` is pure geometry with **no engine dependency**. Keep it that way.
- `ThicknessParting` remains the parting-line source. The ridge is a display overlay.

---

## Ruled out — do not re-attempt without new evidence

**Per-contour separation as the test.** Rejects `larynx-large`, whose rims are correct. Superseded by
rule 2.

**Band quality (CoV, width outliers, closed-contour count) as the routing gate.** Ranks `larynx-small`
(CoV 1.68, rims fine) and `larynx-large` (CoV 0.87, rims fine) together and below `standard`. Quality
does not predict partability.

**Band-width ÷ local wall thickness as the wall/knife-edge discriminator.** Distributions overlap:
`standard`'s suspect faces read 0.37/0.62/0.99, `larynx-large`'s 0.26/0.51/1.63. Backwards from what it
should say.

**Filled share of the ridge as a knife-edge proxy.** 75% on `larynx-small`, 80% on `larynx-large`, 81%
on `standard`, 86–88% on the clean bodies. Does not separate.

**Suspect-face cluster size as a gate.** `standard` 3 clusters median 47; `larynx-large` 10 clusters
median 14 but largest 63 — ranges overlap, no safe threshold.

**Lowering the grow thresholds globally to close `standard`'s band.** 0.15/20° repairs `standard` and
wrecks `nose` in the same step; 0.10/15° trips the percolation guard on `eye` and `larynx-small` so they
report no ridge at all.

**Any second pass confined to a zone around the defect.** Four variants, all landing at CoV ≈ 0.27
against the global run's 0.058. Measured cause: at the pinch the relaxation admits only chords across a
ridge that already runs through those vertices — 19 of 31 with both endpoints already on it, none off
it at both ends. Sealing needs reach; the ramp is smooth to 100 mm with no plateau.

**Relaxing `Classify`'s group-spanning rule.** Measured directly: once a face is sealed into its own
region it is filled and classified as band *every time*, at every radius. Nothing downstream refuses
anything.

**Dilating the ridge into soft edges rim-wide.** Reaches only 81% of `standard`'s repair while adding
10–14 percentage points of shaded area to `nose` and `larynx-small`. Does not discriminate.

---

## The band repair, and its gate

`RidgeDetection.CompleteBand` grows the band across a width shortfall, iterating up to
`BandRepairPasses` (4) and re-laying the crease along the band's edge **once** at the end — per-pass
re-laying walks the boundary outward one ring per iteration and carries the band off the seam.

**It is gated on convergence, not on a threshold.** After its passes it re-measures; if any shortfall
remains it restores everything — face mask, regions, areas, ridge edges. Wall-rimmed bodies converge to
zero suspect faces; knife-edge rims never do, because a rim with no width reads as narrow against the
wider band either side and each pass widens it further. A repair that has not finished is working on a
shape it does not model.

Effect, verified across all 23 asset files: **`test bolus standard` is the only model it touches.**
7 untouched, 15 STLs unenterable (no mould metadata — this is expected, not a bug).

---

## Current state

| body | shape | rims | kind | cuts | routes to |
|---|---|---|---|---|---|
| chin, ear, eye, nose, scalp, standard | Shell | 1 | Wall | 1 | ExtrusionBorder |
| larynx-large | Torus | 2 | Wall, Wall | 2 | ExtrusionBorder + hybrid |
| larynx-small | Torus | 1 | **Merged** | 2 | ExtrusionBorder + hybrid |

`standard` after the repair: 2/2 closed contours, band 11.44 mm against a 10.81 mm wall, CoV 0.064,
**0.0% width outliers** (was 0.303 / 22.0%).

---

## Known gaps

**`larynx-small`'s rims cannot be told apart.** Its faces split into three band groups (2106, 556, 72)
but all three contours attribute to group 0 — a chain walks straight across a group boundary where the
walls touch, and the majority vote puts the whole chain on the largest group. So the region shades in
three colours while the curves over it are one. This blocks the hybrid on that body. Fixing it means
attributing chains per-edge rather than per-chain, or splitting merged groups at the knife edge.

**The hybrid is not built.** `NeedsHybrid` is reported; the split still builds one parting mesh.
`larynx-large` decomposes cleanly and is the body to build it against.

**Single-ridge handling is classified but not consumed.** `Kind == SingleRidge` means the contour *is*
the line — no band to bound, and none of the centre-drift problems that came with wall rims. Nothing
reads it yet.

**The silhouette column tests one axis** (+Z). Read a "no" as "not along the obvious axis", not as
"impossible".

---

## Commands

```bash
# unit suite - MUST stay at 250 passed / 5 failed
dotnet test tests/Fabolus.Core.Tests/Fabolus.Core.Tests.csproj --filter "Category!=Diagnostics"
```

```bash
# what each body routes to, and why
dotnet test tests/Fabolus.Core.Tests/Fabolus.Core.Tests.csproj --filter "FullyQualifiedName~PartingStrategySweep" -l "console;verbosity=detailed"
```

```bash
# which models the band repair touches - run after ANY change to CompleteBand
dotnet test tests/Fabolus.Core.Tests/Fabolus.Core.Tests.csproj --filter "FullyQualifiedName~RidgeAllAssets" -l "console;verbosity=detailed"
```

```bash
# full evaluation, images and per-model reports
FABOLUS_RIDGE_REPORT_DIR=<dir> dotnet test tests/Fabolus.Core.Tests/Fabolus.Core.Tests.csproj --filter "FullyQualifiedName~RidgeDetectionEvaluation"
```

The 5 unit failures are pre-existing and unrelated: `HalfSpaceSplitTests` (×3),
`PartingMeshAxisTests`, `SplitMouldFeatureTests`. If you see 6+, you broke something.
`RidgeRegionTests` is the canary — it has caught two real bugs in band handling that nothing else
would have.

**Look at the pictures.** Every wrong conclusion in this work was corrected by an image, and several
were caused by not looking sooner.
