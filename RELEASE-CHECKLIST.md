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
- [ ] **Verify the tracked test meshes are not real patient data (PHI).** *Needs you.* The 15 `tests/files/*.stl` files are named after patient anatomy (`chin_bolus`, `ear_bolus`, `eye_bolus`, `nose_bolus`, `scalp_bolus`, `larynx_bolus`, …), and `chin_legacy_smooth.3mf` is anatomy-derived too (3MF can also carry metadata). Confirm all are synthetic or de-identified; replace any that are not. If any were real, removing them from history needs a history rewrite, not just a delete.

## P1 — Release infrastructure & hygiene

- [x] **Add a CI workflow (build + test).** [`ci.yml`](.github/workflows/ci.yml) runs on pushes to `main`/`v1` and on every PR, and uploads the test results.
- [x] **Write the release-prep docs.** `SECURITY.md`, `CONTRIBUTING.md`, bug/feature issue forms, a PR template, and `dependabot.yml` (monthly, grouped, targeting `v1`).
- [x] **Remove dev clutter.** Removed the two `notes.md` files and `tests/files/desktop.ini`; `desktop.ini` and `Thumbs.db` are now ignored.
- [ ] **Turn on the GitHub settings these files rely on.** *Needs you.* In the repo's settings:
  - **Pages:** deploy from the `gh-pages` branch. The docs are built there, but `nsmela.github.io/Fabolus` returns 404, and the README, About window and issue forms all link to it.
  - **Private vulnerability reporting:** enable it (Security → Settings). `SECURITY.md` points to it.
  - **Default branch:** consider making `v1` the default. Contributors land on `main`, which has none of this.
- [ ] **End-to-end release-build test on a clean machine.** *Needs you.* Already verified locally: `publish.ps1` builds all three artifacts, and the zips contain the licences. Still to do: on a machine with **no .NET installed**, run the installer (check the disclaimer page), run the self-contained zip, and open About.
- [ ] **Plan the move off .NET 8.** .NET 8 leaves support on **10 November 2026**, weeks after a 1.0 would ship. Retarget Fabolus and GeometryEngine to .NET 10 (LTS), ideally before 1.0.

## P2 — Polish & professionalism

- [ ] **Code-sign the installer/exe.** *Needs a certificate.* Until then, the README explains the SmartScreen "unknown publisher" warning.
- [ ] **Bump 0.9.4 → 1.0.0 and tag it.** *Needs you.* [`CHANGELOG.md`](CHANGELOG.md) is started; move its *Unreleased* section under the version when you tag.
- [x] **Link the docs site from the README.** Done, together with an import → export summary, a privacy statement, building from source, and the licence.
- [x] **Confirm no telemetry/outbound network calls.** No HTTP or socket use in Fabolus or GeometryEngine source. The README and About window say so.
- [ ] **Verify the full test suite is green in CI** after the first run of `ci.yml`, and that safety-critical geometry (volume preservation, mould subtraction) has coverage.
- [ ] **Optional: align `Microsoft.Extensions.DependencyInjection`/`.Hosting` with the runtime.** They are on **10.0.9** while the app targets **net8.0**. 10.x officially supports net8.0, so this is not a bug, and it goes away with the .NET 10 move above.

---

_Generated 2026-09-08; revised 2026-10-02 after GeometryEngine replaced Geometry.MeshLib; work landed 2026-10-02. What remains in P0 is the patient-data check._
