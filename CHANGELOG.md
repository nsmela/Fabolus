# Changelog

Notable changes to Fabolus. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

The `v1` rewrite: a new architecture, a new geometry engine, and the first release intended
for public use.

### Added

- Decals: emboss patient IDs and alignment labels into the bolus or the mould.
- Automatic air-channel placement at trapped-air pockets, alongside hand-placed straight and
  angled channels.
- Convex, concave and contoured mould shapes, mould troughs, and a cut/split tool
  (experimental).
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

[Unreleased]: https://github.com/nsmela/Fabolus/compare/0.9.3...v1
[0.9.3] and earlier: https://github.com/nsmela/Fabolus/releases
