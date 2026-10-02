# Contributing to Fabolus

Thanks for your interest. Bug reports, fixes and improvements are all welcome.

Fabolus prepares meshes that end up on patients, so please read the
[medical disclaimer](DISCLAIMER.txt) and keep that in mind: correctness of the geometry comes
before features.

## Never share patient data

Do not attach real patient meshes, or anything exported from a treatment planning system, to
issues or pull requests, and never commit them. Test meshes in `tests/files/` must be
synthetic or fully de-identified. If a bug only shows up with a patient's mesh, describe it
and we will work out how to reproduce it without the data.

## Getting set up

You need Windows 10 or later, the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0),
and [GeometryEngine](https://github.com/nsmela/GeometryEngine), Fabolus's geometry library,
checked out beside this repository:

```
<any folder>/
├── Fabolus/
└── GeometryEngine/
```

```powershell
git clone https://github.com/nsmela/Fabolus.git
git clone https://github.com/nsmela/GeometryEngine.git
git -C GeometryEngine checkout (Get-Content Fabolus/build/geometryengine.sha)
```

[`build/geometryengine.sha`](build/geometryengine.sha) is the GeometryEngine commit CI builds
against. If you work on both repositories at once, keep GeometryEngine on a branch there and
update that file in the same Fabolus pull request once the GeometryEngine change has merged.

To build against a GeometryEngine checkout somewhere else, pass
`-p:GeometryEngineRoot=<path to GeometryEngine>\` when building an individual project.
`Fabolus.sln` refers to `..\GeometryEngine\` directly, so the solution itself only builds in
the side-by-side layout.

## Building and testing

```powershell
dotnet build Fabolus.sln -c Release -p:Platform=x64
dotnet test Fabolus.sln -c Release -p:Platform=x64
```

Close any running `Fabolus.exe` built from `src/Fabolus.Wpf/bin` first: the WPF tests build
into the same folder, and a running copy locks it.

GeometryEngine's own tests use a custom console runner rather than `dotnet test`:

```powershell
dotnet run -c Release --project ../GeometryEngine/tests/GeometryEngine.Tests
```

[`docs/architecture/07-testing-strategy.md`](docs/architecture/07-testing-strategy.md)
describes how the tests are organised.

## Pull requests

- Open pull requests against the **`v1`** branch.
- Keep a pull request to one change, and describe what it changes and why.
- Add or update tests for behaviour changes, especially anything that alters geometry (volume,
  smoothing, mould shells, booleans).
- Match the style of the code around your change: naming, layout, and the habit of commenting
  *why* rather than *what*. The [architecture manual](docs/architecture/01-system-architecture.md)
  explains the structure: vertical feature slices, the command-replay pipeline, and the
  GeometryEngine boundary.
- CI must pass. It builds the solution and runs both test projects.

## Reporting bugs and requesting features

Use the issue templates. For a bug, the version (shown in the About window), what you did, and
what happened are the most useful things to include.

## Licence

By contributing, you agree that your contributions are licensed under the project's
[MIT License](LICENSE).
