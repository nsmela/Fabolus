# Public Release Checklist

Tasks to bring the repo and app to a public release. Grouped by priority.
**P0** items are the true release gate (legal & clinical safety). Check items off as they land.

---

## P0 — Blocking (legal & safety)

- [ ] **Add a LICENSE file.** There is none, so the repo currently defaults to *all rights reserved* — nobody may legally use or fork it. Pick a license (MIT/Apache-2.0 for permissive, GPL for copyleft) and add it at the repo root. Must be compatible with the dependency licenses below.
- [ ] **Add a medical disclaimer.** This tool prepares boluses/moulds for radiation therapy and currently has no disclaimer anywhere. Add "not a medical device / not regulator-cleared / no warranty / verify clinically before use / use at own risk" to the top of the README **and** an in-app About dialog. Highest-risk omission for a clinical tool.
- [ ] **Confirm third-party redistribution rights and add `THIRD-PARTY-NOTICES.md`.** Dependencies: `MeshLib`, `geometry3Sharp` (Boost), `HelixToolkit.Wpf.SharpDX` (MIT), `MahApps.Metro` (MIT), `Clipper2` (Boost), `NetTopologySuite` (BSD). **Verify `MeshLib`'s license permits redistribution in a distributed binary/installer** — mesh libraries are often dual/commercially licensed. Then generate a NOTICES file listing each package + license.
- [ ] **Verify the 15 tracked `tests/files/*.stl` are not real patient data (PHI).** They are named after patient anatomy (`chin_bolus`, `ear_bolus`, `eye_bolus`, `nose_bolus`, `scalp_bolus`, `larynx_bolus`, …). Confirm they are synthetic/de-identified before the repo is public; replace any that are not.

## P1 — Release infrastructure & hygiene

- [ ] **Add a CI workflow (build + test).** Only `release.yml` exists — nothing builds or runs the 32 test files on push/PR. Add `ci.yml` that restores, builds, and runs `dotnet test`.
- [ ] **Land the release-prep docs.** The `.claude/worktrees/gifted-archimedes-d9eb42` worktree already contains `SECURITY.md`, `CONTRIBUTING.md`, `THIRD-PARTY-NOTICES.md`, issue/PR templates, `dependabot.yml`, and `ci.yml` — none are on `v1`. Review and merge (or recreate) them.
- [ ] **Remove scratch/dead projects and local clutter.** Delete `src/Fabolus.CrashTest`, `src/Test3MF`, `src/TestProp` (on disk, not in the solution). Drop the stale `packages/` folder (old HelixToolkit 2.10 NuGet cache). Remove or move the dev-scratch `src/**/notes.md` files. Remove the orphaned root `Fabolus.Tests` project.
- [ ] **Fix dependency version mismatch.** `Microsoft.Extensions.DependencyInjection`/`.Hosting` are pinned to **10.0.9** while everything targets **net8.0** (a next-major package on an LTS runtime). Pin to the matching 8.x.
- [ ] **End-to-end release-build test on a clean machine.** Run `build/publish.ps1`, confirm all three artifacts build, the installer works, and the self-contained zip runs with **no .NET installed**.

## P2 — Polish & professionalism

- [ ] **Code-sign the installer/exe** (or at minimum document the SmartScreen "unknown publisher" warning in the README).
- [ ] **Bump 0.9.4 → 1.0.0**, tag it, and add a `CHANGELOG.md`.
- [ ] **Expand the README for end users** — add a short getting-started/workflow section (import → smooth → channels → mould) tied to the screenshots.
- [ ] **Confirm no telemetry/outbound network calls** and state the privacy posture ("all processing is local, no data leaves your machine").
- [ ] **Verify the full test suite is green in CI** and that safety-critical geometry (volume preservation, mould subtraction) has coverage.

---

_Generated 2026-09-08. P0 items are the release gate — especially the license, the medical disclaimer, and confirming MeshLib's redistribution rights._
