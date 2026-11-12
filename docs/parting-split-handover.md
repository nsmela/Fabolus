# Handover: parting split safety, flange quality and line consistency

Written 2026-11-12. Read this with [`parting-strategy-report.md`](parting-strategy-report.md), which
holds the facts and ruled-out approaches for the ridge and the routing. This file is about the work
on the split itself: what was measured, what was decided, what is on which branch, and what is next.

This note lives on its own branch, `claude/parting-handover`, so it stays out of the change PRs.

---

## Where things stand

Three changes are done. Two are open pull requests; the third is pushed and has no PR yet.

| # | change | branch | PR | state |
|---|---|---|---|---|
| 0 | Scorecard diagnostic | `claude/parting-scorecard` | [#109](https://github.com/nsmela/Fabolus/pull/109) | open against `dev`, CI passing |
| 1 | Check a split before it is returned | `claude/parting-split-verification` | [#110](https://github.com/nsmela/Fabolus/pull/110) | open against `dev`, CI passing |
| 2 | Split along the line the user adjusted | `claude/parting-edited-line` | none yet | pushed at `9acc9a8`, tests passing locally |

The branches are stacked: 2 on 1 on 0 on `main` at `3bfbb56` (Release 1.1.0). Each PR is against
`dev`, so until the one below it merges, a PR shows the commits beneath it too. Merge in order.

`dev` did not exist on origin. It was created from `main` on 2026-11-12, as CONTRIBUTING describes.

**The first thing to do** is open the PR for change 2. A description is at the end of this file.

**Branch new work from `claude/parting-edited-line`**, not from this branch, so the next change does
not carry this note with it.

Test counts on the tip of change 2:

- `Fabolus.Core.Tests`, diagnostics excluded: 368 passed, 0 failed, 11 skipped (1.1.0: 335 / 0 / 11)
- `Fabolus.Wpf.Tests`: 262 passed, 0 failed, 9 skipped (1.1.0: 256 / 0 / 9)
- Scorecard against the run before it: 0 cells better, 0 worse

---

## The brief, in short

Improve the Parting Split across every body in `tests/files/3mf` so that it is:

1. **Safer**: a verified-good split or a clear `Result` error, never a silently wrong split.
2. **Better in flange quality**: flat, smooth and separable.
3. **Cleaner in its parting line**: smooth, centred, and the same for the same body every time.

Process the owner asked for, and which still applies:

- One change per pull request against `dev`, comments that say why, a CHANGELOG entry and a
  user-guide update where behaviour the user sees changes.
- After every change, re-run the scorecard and show before/after for **every** body. Reject a change
  that improves one body and regresses another unless the owner approves the trade.
- Stop and get a go-ahead before each behaviour change. Changes 0, 1 and 2 were approved one at a
  time; nothing below is approved yet.
- Look at pictures at every step.

Constraints:

- Do not change `PartingMeshParameters` or `PartingLineParameters` record defaults. Saved projects
  replay through `SplitCommand` and `CutCommand` and must rebuild the same split. Add new options and
  set them in `PartingSplitViewModel`'s recipe.
- Fix flange smoothness at the cause (launch slopes, ring heights), not by smoothing or remeshing
  the finished flange. Do not remesh the thickened cutter.
- `RidgeDetection` and the `PartingStrategy` path stay free of the geometry engine.
- Run `RidgeAllAssets` after any change to `CompleteBand`, and keep `RidgeRegionTests` green.
- No mocks of `IGeometryEngine`; test on the real engine. No patient meshes.
- If a fix belongs in GeometryEngine, say so and stop.

Out of scope unless the owner says otherwise: the hybrid (one parting mesh per rim), the
larynx_small rim attribution gap, and a redesign of the three-step wizard.

---

## Decisions the owner made

Do not re-ask these.

| question | answer |
|---|---|
| Halves that do not pull apart, or a flange that doubles back | **Warn only.** The split is returned with a report. |
| Cutter crosses itself, cutter not a clean closed solid, seal breached | **Refuse**, even when the halves pass every check on the result. |
| The same, after being shown it leaves larynx_large unsplittable and the bisector refused on 7 of 8 bodies | **Keep refusing.** |
| PR base | Create `dev` from `main`; every PR against `dev`. |
| Piece count | Exactly two substantial pieces. `PartingStrategy` reports cuts, not pieces; this was stated as my reading and not objected to. |

Tolerances were stated and not objected to. They are constants with their reasons beside them in
`PartingSeparability` and `PartingSplitVerifier`:

- single-valued: under 1 mm² of mating face turned over, and under 1 mm² doubled over (0.5 mm grid,
  sheets more than 0.3 mm apart)
- pull-apart: overlap under 1 mm³ at 0.5, 2, 5, 15 and 40 mm along the axis
- volume: within 10 ppm of the mould, and never less than 1 mm³
- mould unchanged: no vertex more than 0.01 mm off, measured more than 1 mm from the cutter
- a half: at least 5% of the mould
- a stored line: within 0.05 mm of the body

---

## Building and testing from a worktree

`Fabolus.Core.csproj` finds GeometryEngine at `..\..\..\GeometryEngine\`, which from
`.claude/worktrees/<name>` is a folder that does not exist. Pass the root explicitly, and build the
test projects, not the solution:

```powershell
dotnet test tests/Fabolus.Core.Tests/Fabolus.Core.Tests.csproj -c Release -p:Platform=x64 "-p:GeometryEngineRoot=<path to GeometryEngine>\" --filter "Category!=Diagnostics"
```

On 2026-11-12 the sibling `GeometryEngine` checkout was on local `main` at `2acd7fd` with
uncommitted edits, not at the pin in `build/geometryengine.sha` (`123a129`). Everything here was
built against a detached worktree of the pin:

```powershell
git -C <GeometryEngine> worktree add --detach <somewhere> (Get-Content build/geometryengine.sha)
```

Do not run a build while a test run is using the same output folder; the DLLs are locked and the
build fails or the run dies.

---

## The scorecard

`tests/Fabolus.Core.Tests/Diagnostics/PartingScorecard.cs`. Eight bodies × the three sweeps the view
offers, under the view's recipe. About three and a half minutes.

```powershell
$env:FABOLUS_DIAGNOSTICS = "1"
$env:FABOLUS_SCORECARD_DIR = "<after>"
dotnet test tests/Fabolus.Core.Tests/Fabolus.Core.Tests.csproj -c Release -p:Platform=x64 "-p:GeometryEngineRoot=<GE>\" --filter "FullyQualifiedName~PartingScorecard.Score"

$env:FABOLUS_SCORECARD_BASELINE = "<before>"
dotnet test tests/Fabolus.Core.Tests/Fabolus.Core.Tests.csproj -c Release -p:Platform=x64 "-p:GeometryEngineRoot=<GE>\" --no-build --filter "FullyQualifiedName~PartingScorecard.CompareWithBaseline"
```

It writes, per body, one JSON file and contact sheets (`<body>-line.png`, `<body>-flange.png`,
`<body>-<sweep>-separability.png` for a sweep that fails a separability gate), plus `scorecard.md`
for the run and `scorecard-delta.md` for the comparison.

**Always run it on the branch before your change first**, into its own folder, and keep that folder.
The runs made so far were in a temporary directory and are not preserved; regenerating one takes
three minutes.

Two smaller diagnostics sit beside it:

- `PartingScorecard.WhereTurningTheBodyChangesTheLine`: which stage of the trace first differs when
  a body is turned rigidly.
- `EditedLineCommit`: draws the flange a recipe rebuilds with and without an edit in it.

What the numbers mean:

- **Mating face**: the flange outside the body and inside the mould. Slopes and dihedrals are over
  this, not the whole flange.
- **turn / steer**: the angle between successive segments of the line, in 3D and in the body's
  tangent plane. Steer removes the turning a faceted body forces on any line held on it.
- **ripple / bend**: dihedral across edges that run out from the line (faces side by side round the
  loop), and across edges that run along it (one face further out than the other).
- **steep area by cause**: per face past 45°. "seal" is inside the body; "line climb" is where the
  nearest stretch of line itself climbs 45° or more; "neither" is the rest. It is a heuristic.

---

## What was found

### Gates, at 1.1.0 and now

Verdict under the owner's policy, and what Apply returns. Before change 1 every row but one
returned a split; after it, Apply matches the verdict everywhere.

| body | Loft (default) | Sweep | Bisector |
|---|---|---|---|
| chin | split, 1 warning (flange doubles back, 7 mm²) | refused: cutter crosses itself (6) | refused: cutter not a clean solid |
| ear | split | split | refused: cutter not a clean solid |
| eye | split | split | refused: cutter not a clean solid |
| larynx_large | refused: cutter crosses itself (4066). 1.1.0 returned 99.6% + 0.1% as a split | refused: seal breached. 1.1.0 errored | refused: cutter crosses itself (41). 1.1.0 returned 57.5 / 42.5, which overlaps 2173 mm³ when pulled |
| larynx_small | refused: seal breached (2 points) | refused: seal breached | refused: cutter not a clean solid |
| nose | split | split | refused: cutter not a clean solid |
| scalp | split | split | refused: cutter not a clean solid |
| standard | split | refused: cutter crosses itself (88) | split |

Volume was conserved to under 1 mm³ and the mould's surface away from the cut was unchanged in all
24 combinations.

### Flange quality, default loft, mating face

| body | slope median / p90 / max ° | % past 45° | ripple p99 ° | bend p99 ° | line's steepest climb ° |
|---|---|---|---|---|---|
| chin | 21 / 38 / 83 | 6.9 | 23.5 | 14.2 | 80 |
| ear | 5 / 10 / 50 | 0.2 | 21.5 | 3.1 | 25 |
| eye | 13 / 30 / 73 | 3.2 | 50.3 | 6.0 | 64 |
| larynx_large | 32 / 81 / 90 | 37.9 | 55.4 | 63.9 | 85 |
| larynx_small | 26 / 51 / 90 | 15.7 | 25.8 | 49.2 | 88 |
| nose | 17 / 31 / 44 | 0 | 20.4 | 3.5 | 44 |
| scalp | 28 / 44 / 59 | 9.5 | 8.4 | 2.8 | 59 |
| standard | 8 / 15 / 36 | 0 | 11.5 | 1.3 | 35 |

A surface through a curve is at least as steep as the curve, so the last column is a floor on the
maximum. Scalp, nose, standard and chin are already at or near it; only the area past 45° and the
p90 can move there. Ripple's median is 1 to 4° everywhere, so the p99 is individual creased radials.

### Parting line

| body | steer max / RMS ° | off-centre max | turned 30° max mm | back-steps / footprint crossings |
|---|---|---|---|---|
| chin | 42 / 8.5 | 0.32 | 0.91 | 1 / 1 |
| ear | 42 / 6.0 | 0.08 | 0.81 | 0 / 0 |
| eye | 68 / 10.1 | 0.23 | 4.62 | 0 / 0 |
| larynx_large (outer, hole) | 85 / 8.3, 98 / 15.7 | 0.32, 0.36 | 1.00 | 322 / 8, 2 / 1 |
| larynx_small (outer, hole) | 65 / 9.6, 36 / 9.2 | 0.33, 0.07 | 5.24 | 2 / 0, 0 / 0 |
| nose | 20 / 6.8 | 0.07 | 0.29 | 0 / 0 |
| scalp | 15 / 2.6 | 0.04 | 0.01 | 0 / 0 |
| standard | 21 / 4.4 | 0.05 | 0.00 | 0 / 0 |

Traced twice, opened again, and saved then reopened all reproduce the line exactly on every body.

### Facts established: do not re-derive

- **larynx_large has no pull direction.** The best of 4000 directions for the bisector, 46° off the
  axis, still leaves 1161 mm² of mating face facing away. The surface hooks at the two horns, where
  the line climbs 85 mm, and round the tracheostomy hole.
- **The bisector's cutter is closed.** It has no open edge on any body. What fails it is 2 to 38
  edges where more than two faces meet, plus crossings on nose (11), scalp (19), larynx_large (41)
  and larynx_small (260). Whether those come from the bisector surface or from `ExtrudeByNormals`
  has not been measured.
- **The line depends on orientation, and it is not rounding.** A turn of a millionth of a degree
  changes nothing; a 30° turn moves the line up to 5.2 mm. `RidgeDetection` and `ThicknessParting`
  scale thresholds by the axis-aligned bounding-box diagonal (`MinLengthFraction`, `MaxGapFraction`,
  `MaxRegionWidthFraction`, `MinLoopFraction`, the contour lift). Under the turn the ridge contours
  already differ by up to 3.9 mm, then the crease offset drops different walks (28 against 19 on
  larynx_small). Whether the diagonal is the whole cause is not yet shown.
- **Chin's loft folds because its line's footprint crosses itself** once at the notch. A direction
  7.4° off the fitted axis sees the whole chin flange.
- **An edited line sits on its body.** Across every edit the view offers, on four bodies, no point
  was more than 0.0004 mm off the surface.
- **`Evaluators.SeparateComponents` returns surface shells, not solids.** An enclosed cavity the cut
  never reaches is its own "component". The volume check catches the case this would otherwise let
  through.
- **Old recipes are deliberately unchecked.** A project saved with a wrong split reopens with that
  wrong split. That is the price of `Verification` defaulting to `None`.

### Ruled out

- Making the check the record default: a project saved earlier could refuse to reopen.
- A second copy of the separability measurement in the diagnostic: the table and the check could
  drift, so the scorecard reads the production numbers.
- Replaying the edit operations instead of storing the edited line: it means replaying a
  shortest-path search per section and trusting it to land where it did.
- Snapping a stored line onto a changed body: it yields a line nobody approved.
- Judging "facing against the axis" with one sign for the whole flange: a hole patch wound the other
  way reads as all turned over while lying flat. It is judged a sheet at a time.

---

## What is next

Ranked. None is approved; each needs a go-ahead, its own PR, and a before/after scorecard.

| # | change | targets | notes |
|---|---|---|---|
| 3 | Orientation-independent ridge scale, as a new option set in the view recipe | turned 30° / 60° max deviation: 0.3 to 5.2 mm → 0.05 mm (proposed tolerance) | Touches the ridge: run `RidgeAllAssets`, keep `RidgeRegionTests` green. Changing the scale moves every body's line slightly in its normal pose, so it must be behind an option or saved projects replay differently. `RidgeDetectionOptions.Default` is hard-coded in `PartingMeshFeature` today and would need threading through `PartingLineParameters`. |
| 4 | Loft: stop radials crossing where the line's footprint steps back | turned-over area on chin (7.3 mm²) and larynx_small (7.9 mm²) → 0 | Two candidates to measure against each other: refit the axis to minimise back-steps, or untangle the correspondence in `GenerateMouldLoftFlangeMesh`. |
| 5 | Loft ring heights: ease the outer ring further round the loop, spend the difference along the radial | % past 45° and p90 on scalp, chin, larynx_small, eye | The maximum cannot go below the line's own climb. |
| 6 | Loft launch: smooth the bleed-ring direction and outer-ring position round the loop | ripple p99 on eye (50°), chin (23.5°), ear (21.5°), nose (20.4°) | |
| 7 | Line steering, inside the centring | steer max / RMS on eye, chin, larynx | Straightening as a default is ruled out in `PartingLineStraightening`: it moves the line off centre. |
| 8 | The bisector's cutter: shared edges and crossings | cutter gates on the bisector | First step is a measurement: do the faults exist in the bisector surface, or appear when it is thickened? Fixing the shared edges alone brings the bisector back on chin, ear and eye only. If the cause is in the level set, it belongs in GeometryEngine: say so and stop. |

The owner was told that item 8 may deserve to move up, since under the kept policy it decides
whether the bisector is usable and whether larynx_large can be split at all. They chose the edited
line first and have not ranked the rest.

---

## Not verified

- **Nothing has been exercised in the running app.** The step 3 note, the Apply dialog and the
  refusal messages are covered through the view model and the XAML build only.
- **An edited line on a body with a hole.** `PartingLineEdit.ToPartingLine` keeps only the rims that
  could be put into editable form, in wall order. On a body where a rim cannot be edited, the edited
  line has fewer loops than the traced one. Not looked at; larynx_small's rims are out of scope.
- Pull-apart is tested along the resolved axis only. The other-direction figure uses face normals,
  which is necessary for separability and not sufficient.
- The line and flange sheets for nose and standard were read as numbers, not looked at.
- `Fabolus.StartupCheck.Tests` was not run locally.

---

## Gotchas

- `gh pr edit` fails on this repository with a "Projects (classic) is being deprecated" GraphQL
  error. Edit a PR body with `gh api -X PATCH repos/nsmela/Fabolus/pulls/<n> -F body=@<file>`.
- New `.cs` files written with LF are normalised to CRLF by `.gitattributes` on commit; the warning
  is harmless.
- The WPF tests for the parting view run on an STA thread through `UiThread.Run` and wait for
  `CanEditLine`, because the line is traced off the UI thread after a debounce.
- `PartingSplitViewModel` has two line recipes now: `TracedLineParameters` for the trace an edit is
  made on, and `LineParameters`, which adds the edit and is what every stage and the commit use.

---

## Description for the change 2 pull request

Open it against `dev` from `claude/parting-edited-line`.

**Title:** Split along the parting line the user adjusted

> Stacked on #110. Until that merges, this PR shows the commits beneath it; the change here is
> `9acc9a8`.
>
> ## What this changes
>
> A parting line adjusted by hand is now the line the split is made along, and it is saved with the
> split.
>
> ## Why
>
> The edit lived only in the view. The preview was built from it, so the parting mesh in step two
> and the halves in step three followed the edit, but Apply Split replays the recipe, and the recipe
> held a source and a direction and nothing of the edit. Apply traced the automatic line again and
> cut along that.
>
> Driven through the view model on chin: after one press of Smooth, the previewed line and the line
> the recipe rebuilt were 0.3 mm apart at the first point. Rebuilding the flange from each recipe
> shows the same thing downstream: without the edit in the recipe the flange lies on the traced line
> and up to 1.17 mm off the edited one; with it, the reverse.
>
> ## How
>
> - `PartingLineParameters.Edited` carries the adjusted line as its points. The default is null, so
>   a recipe written before this traces its line as it did.
> - `GeneratePartingLineFromBody` returns the stored line ahead of any trace. That is the one place
>   the preview, Apply and a replay all come through.
> - The view takes the recipe's own answer at every stage instead of the line on screen, so the
>   preview is the first replay of what will be committed.
> - A stored line is only accepted on a body it still lies on, within 0.05 mm. Past that it is
>   refused with `PartingLine.EditedLineOffBody` and the user is asked to set the line again. It is
>   not snapped onto the new surface: that would be a line nobody approved.
> - An edit used to outlive its body: opening the view on another mould left the last one's edited
>   line in force. It is dropped when the body changes and kept when the same mould is reopened.
>
> ## Before and after
>
> No geometry on the automatic line changes. The scorecard against the run before this change: 0
> cells better, 0 worse, on all eight bodies and three sweeps.
>
> | body | smoothing moved the line by | flange from the recipe with the edit | flange from the recipe without it |
> |---|---|---|---|
> | chin | 1.17 mm | on the edited line (0.000 mm), 1.171 mm off the traced | on the traced line, 1.171 mm off the edited |
> | scalp | 0.56 mm | on the edited line, 0.563 mm off the traced | on the traced line, 0.563 mm off the edited |
> | standard | 0.79 mm | on the edited line, 0.792 mm off the traced | on the traced line, 0.792 mm off the edited |
>
> ## Tests
>
> - `PartingLineEditCommitTests` (WPF, real engine, on the UI thread): after an edit the recipe
>   rebuilds the line the preview was built from, point for point; with no edit the recipe carries
>   none; Reset takes the edit out; a different mould drops it; the same mould keeps it. The first
>   of these failed before the fix.
> - `EditedPartingLineTests`: the recipe rebuilds exactly the stored line; a split from the recipe
>   is cut along the edited line as the preview was, and differently without it; every edit the view
>   offers leaves the line within 0.0004 mm of the body; a stored line on a moved body is refused;
>   the line survives a 3MF round trip.
> - `EditedLineCommit` (diagnostic): draws the flange each recipe rebuilds.
> - `Fabolus.Core.Tests`: 368 passed, 0 failed, 11 skipped. `Fabolus.Wpf.Tests`: 262 / 0 / 9.
>
> ## Not verified
>
> - Not exercised in the running app.
> - An edited line on a body with a rim that cannot be put into editable form has fewer loops than
>   the traced one. That is how the preview already behaved; it is now also what is committed.
