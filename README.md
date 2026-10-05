# Fabolus

> [!WARNING]
> **Fabolus is not a medical device.** It has not been cleared, approved or certified by any medical device regulator, and it is not intended to diagnose, treat or plan treatment for any patient. It is provided "as is", without warranty of any kind. Every bolus and mould it produces must be independently verified by qualified clinical staff (including its dimensions, thickness, volume and fit) under your institution's own quality-assurance procedures before it is used on a patient. You use Fabolus entirely at your own risk. The full wording is in [DISCLAIMER.txt](DISCLAIMER.txt).

**Fabolus** is a Windows app that helps radiation therapy teams prepare bolus meshes for 3D printing. Its specialty is smoothing the bolus surface without sacrificing volume, and designing a sacrificial mould for casting silicone into the prescribed bolus shape.

It imports a bolus exported from your treatment planning system (STL, 3MF, OBJ, OFF or PLY), smooths it, orients it for printing, adds air channels, and builds a mould around the bolus with the channels subtracted, ready to export for printing.

**Documentation:** the [user guide](https://nsmela.github.io/Fabolus/) walks through the whole workflow, from import to casting, and the [architecture manual](https://nsmela.github.io/Fabolus/architecture/01-system-architecture/) covers how it is built.

**Privacy:** Fabolus runs entirely on your computer. It makes no network connections of its own, collects no telemetry, and never uploads meshes or any other data.

## Download and install

Grab the latest files from the [Releases page](https://github.com/nsmela/Fabolus/releases). Fabolus is Windows 10 or later, 64-bit only.

| File | Who it's for | Requires |
|---|---|---|
| `Fabolus-<version>-setup.exe` | **Most people.** Installs to your user folder, adds a Start Menu shortcut, and uninstalls cleanly. No admin rights needed. | Nothing |
| `Fabolus-<version>-win-x64.zip` | Portable use if you already have .NET. Extract anywhere and run `Fabolus.exe`. Smallest download. | [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0/runtime) |
| `Fabolus-<version>-win-x64-self-contained.zip` | Portable use on a machine with no .NET installed. Everything is bundled, so the download is much larger. | Nothing |

The builds are not code-signed yet, so Windows SmartScreen may warn that the publisher is unknown the first time you run the installer or `Fabolus.exe`. Choose **More info**, then **Run anyway**. Only do this for files downloaded from the Releases page above.

## Building from source

Fabolus needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and its geometry library, [GeometryEngine](https://github.com/nsmela/GeometryEngine), checked out **beside** this repository:

```
<any folder>/
├── Fabolus/
└── GeometryEngine/
```

```powershell
git clone https://github.com/nsmela/Fabolus.git
git clone https://github.com/nsmela/GeometryEngine.git
git -C GeometryEngine checkout (Get-Content Fabolus/build/geometryengine.sha)
dotnet test Fabolus/Fabolus.sln -c Release -p:Platform=x64
```

[build/geometryengine.sha](build/geometryengine.sha) records the GeometryEngine commit CI builds against. To build against a checkout somewhere else, pass `-p:GeometryEngineRoot=<path>\` to `dotnet build` or `dotnet test` for an individual project. See [CONTRIBUTING.md](CONTRIBUTING.md) for more.

## Building a release

All three artifacts are produced by one script:

```powershell
pwsh ./build/publish.ps1
```

They land in `artifacts/`. Building the installer needs [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`); pass `-SkipInstaller` to build just the two zips without it. Pushing a `x.y.z` tag runs the same script in GitHub Actions and attaches the results to a draft release.

See [build/publishing.md](build/publishing.md) for the full release process, script options, and known rough edges.

## Licence

Fabolus is released under the [MIT License](LICENSE). The third-party components it builds on and redistributes keep their own licences, listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Screenshots

<img width="800" height="550" alt="2025-09-08_10-14-45" src="https://github.com/user-attachments/assets/8306ff5e-0518-4c20-bc2e-e50954c238e8" />

<img width="800" height="550" alt="imported" src="https://github.com/user-attachments/assets/b9eb92b3-5191-40fd-b808-65a4aa4d5f90" />

<img width="800" height="550" alt="smoothing applied" src="https://github.com/user-attachments/assets/61004e7e-e7c0-42b9-9042-b3107f069a23" />
<img width="800" height="550" alt="smoothing-distanceheatmap" src="https://github.com/user-attachments/assets/0471b267-2de9-4a28-bdc9-30946c2592ca" />
<img width="800" height="550" alt="smoothing-contouring" src="https://github.com/user-attachments/assets/3a7f4bd2-242a-43c2-88a2-cf3075aeeb36" />
<img width="800" height="550" alt="rotation" src="https://github.com/user-attachments/assets/c7bff6a4-fafd-4dd1-9a08-3d7bf2a30134" />
<img width="800" height="550" alt="rotation-preview" src="https://github.com/user-attachments/assets/273b12c5-0a7e-4207-85f9-d4c6ed1907be" />
<img width="800" height="550" alt="channels" src="https://github.com/user-attachments/assets/2782c3dd-2aba-4952-929a-95c475b78b4c" />
<img width="800" height="550" alt="channels-channel types" src="https://github.com/user-attachments/assets/18f2d77f-271f-4f1b-8e7e-f21c1e69ba3b" />
<img width="800" height="550" alt="mould" src="https://github.com/user-attachments/assets/0649474a-f204-4fd8-b13f-8a29d0943ca2" />
<img width="800" height="550" alt="wiremesh display" src="https://github.com/user-attachments/assets/f04d4154-5d1a-48bc-8cdf-2a664a1019ed" />
<img width="246" height="363" alt="app preferences" src="https://github.com/user-attachments/assets/d644f3e0-389b-4261-b3a6-a7b51f2bc003" />
