# Changelog

Notable changes to Fabolus. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `FabolusStartupCheck.exe`, a small diagnostic launcher shipped with each release (#95). It
  starts Fabolus, times how long it takes to open, and saves a log - where the folder is
  (network drive, redirected folder, inside a zip), download marks, signatures, security
  software, and when each module loaded - with a plain-language reading of what slowed it.
  The log holds no paths, user names or patient data. The README now lists the usual causes
  of a slow start.
- Closing the app with meshes changed since they were imported or last exported now asks
  before discarding them, naming the meshes.

### Changed

- Exports default to the Documents folder instead of `%LOCALAPPDATA%`. Preferences that still
  hold the old default move to the new one.

### Fixed

- Clicking another tab while a view is still opening no longer crashes the app (#89). The
  parting split view takes a few seconds to open on some meshes, such as flat_vulva, and
  leaving it during that time threw. The tab bar is now disabled until the view has opened.
- Uninstalling now removes the preferences and crash reports in `%LOCALAPPDATA%\Fabolus`, not
  just the program files. The build docs give the current preferences location.
- The mesh manager's "Orphaned Vertices" check now reports vertices no triangle uses, with
  their count; it always read "No".
- The export panel no longer shows the mould's volume and surface area as the bolus's when the
  bolus inside a mould cannot be rebuilt from its history; it shows the bolus volume as
  unavailable instead.
- Saving a project (3MF) writes to a temporary file first and only then replaces the old one,
  so a save that fails part-way leaves the previous project intact rather than truncated.
- Exporting a parting split as separate pieces asks before replacing existing `_A`, `_B`, …
  files, which the save dialog never showed.

## [1.0.0] - 2026-10-14

A rewrite: a new architecture, a new geometry engine, and the first release intended
for public use.

### Added

- Decals: emboss patient IDs and alignment labels into the bolus or the mould.
- Automatic air-channel placement at trapped-air pockets, alongside hand-placed straight and
  angled channels.
- Convex, concave and contoured mould shapes, mould troughs, and a cut/split tool
  (experimental).
- Parting split (experimental): split a mould into pieces along a parting line traced on the
  body inside it, with the line editable before the cut. Off by default; turn on "Split view
  (for moulds)" in preferences. The split is kept in the project's history, so a reopened
  project rebuilds it.
- A dark theme, now the default; the light theme is a preference.
- Smoothing cross-section and distance heat-map displays, print-orientation overhang preview,
  and a wireframe overlay.
- 3MF project export that keeps the full editing history, so a project can be reopened and
  adjusted.
- Per-feature preferences with import and export of preference profiles.
- An About window with the version, the medical disclaimer, and the licences.
- A documentation site with a user guide and architecture manual.
- Installer and portable builds, produced by `build/publish.ps1` and the release workflow.
- MIT licence, third-party notices, a medical disclaimer (in the README, docs, installer and
  app), and contributor, security and CI setup.

### Changed

- All geometry now comes from [GeometryEngine](https://github.com/nsmela/GeometryEngine),
  replacing MeshLib and geometry3Sharp.
- Long-running tools stay responsive while they work.
- Runs on .NET 10 (LTS). The portable zip now needs the .NET 10 Desktop Runtime; the
  installer and the self-contained zip still need nothing.

## [0.9.3] and earlier

See the [GitHub releases](https://github.com/nsmela/Fabolus/releases).

[Unreleased]: https://github.com/nsmela/Fabolus/compare/1.0.0...HEAD
[1.0.0]: https://github.com/nsmela/Fabolus/compare/0.9.3...1.0.0
[0.9.3] and earlier: https://github.com/nsmela/Fabolus/releases
