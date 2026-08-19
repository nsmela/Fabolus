# Fabolus
**Fabolus** is a Windows-based app designed to assist radiation therapy prepare bolus meshes for 3D printing. It's specialty is to smooth the bolus surface without sacrificing volume and to design a sacrifical mould for curing silicone into the prescribed bolus shape.

This application can import a STL file, smooth it, add airchannels, and create an encompasing mould around the bolus and subtracting the air channels.

## Download and install

Grab the latest files from the [Releases page](https://github.com/nsmela/Fabolus/releases). Fabolus is Windows 10 or later, 64-bit only.

| File | Who it's for | Requires |
|---|---|---|
| `Fabolus-<version>-setup.exe` | **Most people.** Installs to your user folder, adds a Start Menu shortcut, and uninstalls cleanly. No admin rights needed. | Nothing |
| `Fabolus-<version>-win-x64.zip` | Portable use if you already have .NET. Extract anywhere and run `Fabolus.exe`. Smallest download. | [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) |
| `Fabolus-<version>-win-x64-self-contained.zip` | Portable use on a machine with no .NET installed. Everything is bundled, so the download is much larger. | Nothing |

## Building a release

All three artifacts are produced by one script:

```powershell
pwsh ./build/publish.ps1
```

They land in `artifacts/`. Building the installer needs [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`); pass `-SkipInstaller` to build just the two zips without it. Pushing a `x.y.z` tag runs the same script in GitHub Actions and attaches the results to a draft release.

Screenshots:

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
