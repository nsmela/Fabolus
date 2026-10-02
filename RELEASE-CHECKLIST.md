# Public Release Checklist

Tasks to bring the repo and app to a public release. Grouped by priority.
**P0** items are the true release gate (legal, clinical safety, and a buildable repo). Check items off as they land.

---

## P0 — Blocking (legal, safety & buildability)

- [ ] **Add a LICENSE file — here and in GeometryEngine.** Neither repo has one, so both default to *all rights reserved* — nobody may legally use or fork them. Pick a license (MIT/Apache-2.0 for permissive, GPL for copyleft) and add it at each repo root. It must suit both repos and be compatible with the dependency licenses below.
- [ ] **Add a medical disclaimer.** This tool prepares boluses/moulds for radiation therapy and currently has no disclaimer anywhere. Add "not a medical device / not regulator-cleared / no warranty / verify clinically before use / use at own risk" to the top of the README, the docs site landing page (`docs/index.md`, since the user guide gives clinical instructions), **and** an in-app About dialog. Highest-risk omission for a clinical tool.
- [ ] **Add `THIRD-PARTY-NOTICES.md` covering everything the installer ships.** Fabolus's own packages: `HelixToolkit.Wpf.SharpDX` (MIT), `MahApps.Metro` (MIT), `CommunityToolkit.Mvvm` (MIT), `Microsoft.Extensions.*` (MIT), `BasicResults`. Arriving through GeometryEngine: Manifold and oneTBB (Apache-2.0, native), libigl and Eigen (MPL-2.0, compiled into `geometryengine_native.dll`), `Clipper2` (Boost), `NetTopologySuite` (BSD-3), `geometry3Sharp` (Boost). GeometryEngine's own `THIRD-PARTY-NOTICES.md` already documents its share — reference or fold it in rather than re-deriving it. Confirm the license texts end up in the installer and both zips.
- [ ] **Make the repo buildable outside this machine.** `Fabolus.Core` references GeometryEngine as a sibling checkout (`GeometryEngineRoot`, default `..\..\..\GeometryEngine\`), and `Fabolus.sln` hard-codes `..\GeometryEngine\…` paths. `release.yml` only checks out Fabolus, so a tag push currently fails in `build/publish.ps1`. Either check out `nsmela/GeometryEngine` alongside in every workflow (pinned to a known commit/tag) or consume GeometryEngine as a package. Make GeometryEngine public too, or outside contributors cannot build at all. Document the setup in the README.
- [ ] **Verify the tracked test meshes are not real patient data (PHI).** The 15 `tests/files/*.stl` files are named after patient anatomy (`chin_bolus`, `ear_bolus`, `eye_bolus`, `nose_bolus`, `scalp_bolus`, `larynx_bolus`, …), and `chin_legacy_smooth.3mf` is anatomy-derived too — 3MF can also carry metadata, so inspect it. Confirm all are synthetic/de-identified before the repo is public; replace any that are not.

## P1 — Release infrastructure & hygiene

- [ ] **Add a CI workflow (build + test).** Only `release.yml` and `docs.yml` exist — nothing builds or runs the test suite (43 test files across `Fabolus.Core.Tests` and `Fabolus.Wpf.Tests`) on push/PR. Add `ci.yml` that checks out GeometryEngine (see P0), restores, builds, and runs `dotnet test`.
- [ ] **Write the release-prep docs.** None of `SECURITY.md`, `CONTRIBUTING.md`, issue/PR templates, or `dependabot.yml` exist on any branch — an earlier draft was never committed and is gone. Write them; `CONTRIBUTING.md` must cover the GeometryEngine checkout.
- [ ] **Remove dev clutter.** Remove or move the dev-scratch `src/Fabolus.Core/notes.md` and `src/Fabolus.Wpf/notes.md`. Delete the tracked `tests/files/desktop.ini`.
- [ ] **End-to-end release-build test on a clean machine.** Run `build/publish.ps1`, confirm all three artifacts build, the installer works, the GeometryEngine native DLLs and their license texts are present, and the self-contained zip runs with **no .NET installed**.

## P2 — Polish & professionalism

- [ ] **Code-sign the installer/exe** (or at minimum document the SmartScreen "unknown publisher" warning in the README).
- [ ] **Bump 0.9.4 → 1.0.0**, tag it, and add a `CHANGELOG.md`.
- [ ] **Link the docs site from the README.** The user guide (`docs/user-guide/`) already covers the import → smooth → channels → mould workflow; the README just needs a short summary, screenshots, and a link to it.
- [ ] **Confirm no telemetry/outbound network calls** and state the privacy posture ("all processing is local, no data leaves your machine").
- [ ] **Verify the full test suite is green in CI** and that safety-critical geometry (volume preservation, mould subtraction) has coverage.
- [ ] **Optional: align `Microsoft.Extensions.DependencyInjection`/`.Hosting` with the runtime.** They are on **10.0.9** while the app targets **net8.0**. 10.x officially supports net8.0, so this is not a bug — pin to 8.x only if you want package versions to match the LTS runtime.

---

_Generated 2026-09-08; revised 2026-10-02 after GeometryEngine replaced Geometry.MeshLib. P0 items are the release gate — especially the licenses, the medical disclaimer, and a repo that builds outside this machine._
