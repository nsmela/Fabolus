# Public Release Checklist

Tasks to bring the repo and app to a public release. Grouped by priority.
**P0** items are the true release gate (legal, clinical safety, and a buildable repo). Check items off as they land.

Both `nsmela/Fabolus` and `nsmela/GeometryEngine` are **already public**, so the P0 items are live exposure, not future work.

---

## P0 — Blocking (legal, safety & buildability)

- [x] **Add a LICENSE file — here and in GeometryEngine.** MIT in both repos (`LICENSE` at each root; GeometryEngine's README links it).
- [x] **Add a medical disclaimer.** One wording, in [`DISCLAIMER.txt`](DISCLAIMER.txt): shown by the installer before it installs anything, embedded in the new in-app **About** window (ⓘ button over the viewport), and repeated at the top of the README, on the docs home page, and in the user guide overview.
- [x] **Add `THIRD-PARTY-NOTICES.md` covering everything the installer ships.** Lists every redistributed component, with the MIT, Boost and BSD-3 texts in full. The Apache-2.0 texts ship as GeometryEngine's `LICENSE.*.txt` files, and the MPL-2.0 source locations are included. `LICENSE` and the notices are copied beside `Fabolus.exe`, so all three artifacts carry them (verified in both zips).
- [x] **Make the repo buildable outside this machine.** CI and release workflows check out Fabolus and GeometryEngine side by side, with GeometryEngine pinned in [`build/geometryengine.sha`](build/geometryengine.sha). Verified locally in that layout: the solution builds, all tests pass, and `publish.ps1` produces all three artifacts. The setup is documented in the README and CONTRIBUTING.
- [x] **Verify the tracked test meshes are not real patient data (PHI).** Confirmed by the maintainer (2026-10-05, 2026-10-09): the 15 `tests/files/*.stl` files and `chin_legacy_smooth.3mf` hold no identifiable patient data. They are boluses designed for patient phantoms, and patient boluses with all identifiers removed.

## P1 — Release infrastructure & hygiene

- [x] **Add a CI workflow (build + test).** [`ci.yml`](.github/workflows/ci.yml) runs on pushes to `main`/`v1` and on every PR, and uploads the test results.
- [x] **Write the release-prep docs.** `SECURITY.md`, `CONTRIBUTING.md`, bug/feature issue forms, a PR template, and `dependabot.yml` (monthly, grouped, targeting `v1`).
- [x] **Remove dev clutter.** Removed the two `notes.md` files and `tests/files/desktop.ini`; `desktop.ini` and `Thumbs.db` are now ignored.
- [ ] **Turn on the GitHub settings these files rely on.** *Needs you.* In the repo's settings:
  - **Pages:** deploy from the `gh-pages` branch. The docs are built there, but `nsmela.github.io/Fabolus` returns 404, and the README, About window and issue forms all link to it.
  - **Private vulnerability reporting:** enable it (Security → Settings). `SECURITY.md` points to it.
  - **Default branch:** stays `main`. `v1` is merged into `main` when it is ready, which brings all of this with it.
- [x] **End-to-end release-build test.** `publish.ps1` builds all three artifacts on .NET 10, with the licences in both zips. The maintainer verified the installer works as intended (2026-10-05).
- [x] **Move off .NET 8.** Fabolus and GeometryEngine now target .NET 10 (LTS); .NET 8 leaves support on 10 November 2026. GeometryEngine's change merged as nsmela/GeometryEngine#4, and [`build/geometryengine.sha`](build/geometryengine.sha) pins that merge commit (`39b31c2`), which also carries GeometryEngine's MIT licence.

## P2 — Polish & professionalism

- [ ] **Code-sign the installer/exe.** *Needs a certificate.* Until then, the README explains the SmartScreen "unknown publisher" warning.
- [ ] **Bump 0.9.4 → 1.0.0 and tag it** when `v1` merges into `main`. [`CHANGELOG.md`](CHANGELOG.md) is started; move its *Unreleased* section under the version at the same time.
- [x] **Link the docs site from the README.** Done, together with an import → export summary, a privacy statement, building from source, and the licence.
- [x] **Confirm no telemetry/outbound network calls.** No HTTP or socket use in Fabolus or GeometryEngine source. The README and About window say so.
- [ ] **Verify the full test suite is green in CI** after the first run of `ci.yml`, and that safety-critical geometry (volume preservation, mould subtraction) has coverage.
- [x] **Align `Microsoft.Extensions.DependencyInjection`/`.Hosting` with the runtime.** Resolved by the .NET 10 move: the 10.0.9 packages now match the runtime.

---

_Generated 2026-09-08; revised 2026-10-02 after GeometryEngine replaced Geometry.MeshLib; updated 2026-10-05. All P0 items are done. What remains is the GitHub settings, CI's first green run, and the 1.0.0 tag and code signing when `v1` merges into `main`._
