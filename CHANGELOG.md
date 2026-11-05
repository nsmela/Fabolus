# Changelog

Notable changes to Fabolus. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.1.0] - 2026-11-05

Adds error reports for unexpected failures, a rotation opacity slider and a startup check. Fixes
the crash on exit in the installer and self-contained builds, and lets the parting split work on
the bodies that could not be split in 1.0.0.

### Added

- Unexpected failures now show an error instead of closing the app without a word (#65). A
  failure on the UI thread leaves the app running; one on a background thread is reported and
  then closes it. Each writes a crash report to `%LOCALAPPDATA%\Fabolus\logs`, with file paths,
  file names and mesh names removed, since scans are often named after the patient.
- The rotation view has a **Mesh opacity** slider, from 20% to 100%, with a matching preference
  (#96). Below 100% the bed, the axes and the far side of the mesh show through while aligning;
  overlapping surfaces mix their overhang colours, so it stays solid by default.
- Parting split (experimental): a third flange option, **Halfway between the skins**, for
  parting lines that wind or climb too much for the loft (#91). It parts larynx_large, which
  never split before.
- `FabolusStartupCheck.exe`, a small diagnostic launcher shipped with each release (#95). It
  starts Fabolus, times how long it takes to open, and saves a log - where the folder is
  (network drive, redirected folder, inside a zip), download marks, signatures, security
  software, and when each module loaded - with a plain-language reading of what slowed it.
  The log holds no paths, user names or patient data. The README now lists the usual causes
  of a slow start.
- Closing the app with meshes changed since they were imported or last exported now asks
  before discarding them, naming the meshes.

### Changed

- The 3D viewport runs on HelixToolkit 3 (3.1.2), up from 2.25. Helix 3 moves its maths onto
  `System.Numerics`; nothing about what the viewport shows or how it is used changes.
- Exports default to the Documents folder instead of `%LOCALAPPDATA%`. Preferences that still
  hold the old default move to the new one.
- A parting split exported as separate files writes one lettered file per half, biggest first,
  and the Export tab lists the files it wrote (#88).
- Preferences opens as a dialog over the main window, which waits until it closes (#80).
  Settings still apply as they change. Closing the main window now ends the app.
- Routine package updates, including MahApps.Metro 2.4.11, CommunityToolkit.Mvvm 8.4.2 and
  Microsoft.Extensions 10.0.14.

### Fixed

- The installer and self-contained builds no longer crash on every exit (#80). The publish left
  out a Windows Forms assembly the viewport needs when it unloads.
- Parting cuts no longer leave thin specks of mould behind, and a cut that leaves the mould
  whole plus specks now reports that it did not separate instead of passing as a split (#88).
- Bodies tilted before the split, such as flat_vulva at 30-60°, now split in two (#88). Part of
  the parting line doubled back on itself and folded the cutter.
- larynx_small now splits, where it always came out as one piece (#93).
- A saved mould's body is the same on every opening, so its parting line no longer changes
  between runs (#93).
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
  files, which the save dialog never showed. Declining no longer marks the mesh as saved.

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

[Unreleased]: https://github.com/nsmela/Fabolus/compare/1.1.0...HEAD
[1.1.0]: https://github.com/nsmela/Fabolus/compare/1.0.0...1.1.0
[1.0.0]: https://github.com/nsmela/Fabolus/compare/0.9.3...1.0.0
[0.9.3] and earlier: https://github.com/nsmela/Fabolus/releases
