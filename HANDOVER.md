# Handover: parting split ported onto v1 / GeometryEngine

*2026-10-09. Notes for whoever picks this up next.*

## Where things stand

The parting-split feature from `feat/split-mesh` (built on MeshLib) is ported onto `v1`, which
replaced MeshLib with GeometryEngine. The port is complete through the WPF view and the
diagnostics. The split the app actually runs - border line, `MouldLoft` flange, thin extruded
cutter, severed components - works end to end on the test scans.

| | |
|---|---|
| Fabolus branch | `claude/parting-split-port` (from `v1` 67680b5), pushed to origin, no PR yet |
| Worktree | `Fabolus/.claude/worktrees/branch-review-0fb9e4` (the name is historical; it has the port branch checked out) |
| Source of the old code | `claude/branch-review-0fb9e4` @ 91b4f83 = `feat/split-mesh` 44eb4e6 + three review fixes |
| GeometryEngine, merged | PR #5 triangulator fix -> `main` 3ec73b8; PR #6 `ISpatialQueries.ShortestPath` -> `main` 5e8f7cd |
| GeometryEngine worktree to build against | `GeometryEngine/.claude/worktrees/surface-geodesic` (d8bcaa1, the same tree as 5e8f7cd) |
| Fabolus GE pin | `build/geometryengine.sha` = 5e8f7cd (was 39b31c2, which lacks `ShortestPath`) |

## Build and test

Build the two test projects, not the `.sln` (it hard-codes sibling paths a worktree breaks):

```
G='C:\Users\nsmel\source\repos\nsmela\GeometryEngine\.claude\worktrees\surface-geodesic\'
dotnet test tests/Fabolus.Core.Tests -p:GeometryEngineRoot="$G"
dotnet test tests/Fabolus.Wpf.Tests  -p:GeometryEngineRoot="$G"
```

- Core: **293-294 passed, 9-10 failed, 32 skipped** (31 diagnostics + 1 skipped on v1); the chin
  offset case is flaky (see open issues).
- Wpf: **173 passed, 9 skipped, 0 failed**.
- Diagnostics run only with `FABOLUS_DIAGNOSTICS=1` (like benchmarks with `FABOLUS_BENCH=1`).
- GeometryEngine's tests: `dotnet run --project tests/GeometryEngine.Tests -- <word>` in its
  worktree (custom runner; the filter matches a word of the humanised test name). 306/306.

## Commits on the port branch

| Commit | What |
|---|---|
| fae0dc4 | Stage 1: parting line, band graph, sectioned line, ridge detection (pure C#) |
| 978cfc4 | Stage 1 converted to `Vec3` / double, to match the rest of Core |
| 8723c45 | Stage 2a: `PartingTools`, `ThicknessParting`, wall thickness on GE primitives |
| 99a1a2d | Stage 2b: feature layer on `MeshRecord`; stage-aware replay |
| 6b39b08 | Free geodesic on GE `ShortestPath` |
| 55976bf | Stage 3: the WPF view, tab behind "Split view (for moulds)" |
| b191507 | View tests hold the `MouldLoft` recipe (level face, still seals) |
| 131c49c | Dark theme from the branch, dark by default (user's choice) |
| 8e1cba3 | Diagnostics + the branch's saved `.3mf` moulds |

## Design decisions worth knowing

- **Double precision throughout Core.** The user asked to match GeometryEngine. Code uses the
  global `Vector3 = Vec3` alias and `Vec3`'s instance API. WPF rendering stays `SharpDX`/float;
  `Fabolus.Wpf/Common/Helpers/VectorConversions.cs` crosses the boundary. In the scene manager and
  handle visuals, `CoreVector3` is `Vec3` and `Vector3` is aliased in-file to `SharpDX.Vector3`.
- **`new PartingTools(engine)`**, not `engine.PartingTools` - GE carries no domain tools.
  Wall thickness is `WallThickness.Measure(engine, mesh, options)`.
- **Replay that keeps its stages** (user's "option 1"). `CommandReplay` records each stage in
  `ReplayStages`; `SplitCommand`/`CutCommand` implement `IStagedMeshCommand` and re-trace on the body
  the replay passed through. Applied without history they refuse (`MetadataErrors.NeedsEarlierStages`).
  `MouldMesh.Create(mesh, record)` / `MouldMesh.FromReplay(mesh, stages)` own the way back to the body.
  A split piece's record is the mould's record plus its `SplitCommand`; `CommandPriority.Split = 30`.
- **GE triangulation** of the wavefront footprint goes through `PartingTools.TriangulateNested`
  (two passes, because GE nests rings even-odd).
- **Diagnostics** open saved moulds via `Diagnostics/SavedMoulds.cs` (through `ImportMesh`, so the
  record and history come back). `FlangeSelfIntersection` locates crossings with its own
  `SelfIntersectingFaces` rather than widening GE.
- **New packages.** Core references Clipper2 directly (GE already uses it; it is in
  `THIRD-PARTY-NOTICES.md`). Core's tests add SkiaSharp for the diagnostics' images; it does not ship.
- **Not carried over**: three `Fabolus.Wpf_*_wpftmp.csproj` build leftovers the branch committed.

## Open issues, most important first

1. **Saved chin and larynx moulds no longer trace a parting line on v1.** Their bodies, rebuilt by
   replaying the saved history, come back with non-manifold edges (chin 6, both larynx 2), and the
   trace refuses them. On the branch they traced. The body has twice the base mesh's triangles, so
   the suspect is v1's smoothing replay (now GE, was MeshLib). This hits real saved projects - start
   here. Reproduce: `FABOLUS_DIAGNOSTICS=1`, run `EditLoopCoverage`.
2. **Known Core failures (none on the app's recipe):**
   - Half-space split (5) - halves not watertight / overlapping; GE boolean quality. Two of these
     also failed on MeshLib.
   - Offset thickening (2-3) - GE `Offset` returns a cutter with ~1,700 crossings on scalp, and is
     **not deterministic**: crossing counts differ run to run, so the chin case is flaky.
   - Wavefront cutter crossings (2) - nose 3, scalp 12; MeshLib's were 0.
3. **`InspectCutContours`** returns `NotImplemented` - GE cannot order intersection contours. It
   only adds detail to an error message.
4. **The app has not been run.** Everything is verified by tests only; nobody has opened the
   parting split view or the dark theme on screen.
5. **No PR yet** for `claude/parting-split-port` - open one against `v1` from
   https://github.com/nsmela/Fabolus/pull/new/claude/parting-split-port. Before it merges: drop or
   move this `HANDOVER.md` (it sits at the repo root and would ship), and add the parting split to
   the Unreleased section of `CHANGELOG.md`.
6. Local `feat/split-mesh` is still at 44eb4e6 - the review fixes live on
   `claude/branch-review-0fb9e4` (91b4f83) and were carried into the port.

## Suggested next steps

1. Investigate issue 1 (smoothing replay -> non-manifold body).
2. Open the port's PR against `v1` (see issue 5).
3. Smoke-test the app: import a scan, build a mould, run the parting split, toggle the theme.
