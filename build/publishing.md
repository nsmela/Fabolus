# Publishing Fabolus

How releases are built. Everything here is driven by [`publish.ps1`](publish.ps1) — the
GitHub Actions workflow calls that same script, so a local run and a CI run produce
identical artifacts.

## Quick start

```powershell
pwsh ./build/publish.ps1
```

Runs the tests, builds all three artifacts into `artifacts/`, and prints their sizes.
Takes a few minutes, most of it compressing the self-contained payload.

## The three artifacts

| Artifact | Contents | User needs |
|---|---|---|
| `Fabolus-<version>-win-x64.zip` | Framework-dependent build. Extract and run `Fabolus.exe`. | [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) |
| `Fabolus-<version>-win-x64-self-contained.zip` | Same app with the .NET runtime bundled in. | Nothing |
| `Fabolus-<version>-setup.exe` | Inno Setup installer wrapping the **self-contained** payload. | Nothing |

The installer wraps the self-contained build deliberately: an installer should have no
prerequisites, and people who want the small download already have the first zip.

Approximate sizes at 0.9.4: 34 MB, 104 MB, 76 MB. The installer is smaller than the zip it
contains because Inno compresses with LZMA2 rather than deflate.

## Versioning

`<Version>` in [`Fabolus.Wpf.csproj`](../src/Fabolus.Wpf/Fabolus.Wpf.csproj) is the single
source of truth. `AssemblyVersion`, `FileVersion` and `InformationalVersion` are all derived
from it — do not set them separately.

- Running the script with no `-Version` reads that value.
- Passing `-Version 0.9.5` overrides it for that run, including the metadata compiled into
  the exe.
- CI passes the git tag, so a tagged build always stamps itself with the tag.

The version appears in every artifact filename, in the exe's file properties, and in the
installer's Add/Remove Programs entry.

## Cutting a release

1. Bump `<Version>` in `Fabolus.Wpf.csproj` and commit.
2. Tag it and push:

```bash
git tag 0.9.5 && git push origin 0.9.5
```

3. [`release.yml`](../.github/workflows/release.yml) picks up any `x.y.z` tag, installs
   Inno Setup on the runner, runs `publish.ps1`, and opens a **draft** release with all
   three files attached and a download table in the body.
4. Review the draft, write the release notes, publish.

`workflow_dispatch` runs the same build against a version you type, uploads the artifacts,
and creates no release — useful for checking the pipeline without tagging.

## Prerequisites for a local build

- .NET SDK capable of targeting `net8.0-windows`.
- [Inno Setup 6.3+](https://jrsoftware.org/isinfo.php), for the installer only:

```bash
winget install JRSoftware.InnoSetup
```

winget installs it **per user**, into `%LOCALAPPDATA%\Programs\Inno Setup 6`, not Program
Files. The script looks in both, plus the HKLM and HKCU uninstall keys and `PATH`. If it
still can't find `ISCC.exe` it stops with instructions rather than silently skipping the
installer.

## Script parameters

| Parameter | Default | Notes |
|---|---|---|
| `-Version` | `<Version>` from the csproj | Leading `v` is stripped, so `v0.9.5` works |
| `-Configuration` | `Release` | |
| `-Runtime` | `win-x64` | Fabolus is x64-only today |
| `-OutputDir` | `<repo>/artifacts` | Wiped at the start of every run |
| `-SkipInstaller` | off | Build just the two zips, no Inno Setup needed |
| `-SkipTests` | off | Skips the test gate. Don't use for a real release |

## How the installer behaves

Per-user by default: `PrivilegesRequired=lowest` puts it in `%LOCALAPPDATA%\Programs\Fabolus`
with no UAC prompt. The user can still choose a machine-wide install from the wizard.

This is not just convenience. `AppPreferencesStore` calls `ConfigurationManager.Save()` on
every preference change, which writes `Fabolus.dll.config` **next to the exe**. Under
`C:\Program Files` that throws for a non-admin user. Keeping the install directory writable
sidesteps it. See *Known rough edges* below.

The `AppId` GUID in [`Fabolus.iss`](installer/Fabolus.iss) must never change — it is what
makes a new version upgrade the existing install in place instead of sitting alongside it.

### Existing installations

Inno matches installs by `AppId`, so an upgrade reuses the existing directory and Add/Remove
entry automatically. It does not, on its own, say anything about what it is replacing, and
it will happily let an older build overwrite a newer one. The `[Code]` section adds that:

| Situation | Behaviour |
|---|---|
| Nothing installed | Normal install, no extra prompts |
| Older version installed | Upgrades. No prompt; the Ready page reports "will be upgraded to X" |
| Same version installed | Confirmation prompt before reinstalling over it |
| **Newer** version installed | Confirmation prompt warning that this downgrades, defaulting to No |

Declining a prompt exits with code 1 and leaves the existing install untouched.

It checks HKCU first, then HKLM in both registry views, so it finds the previous install
whether it was per-user or machine-wide.

**Silent installs skip all prompts** and always proceed, downgrades included — a scripted
run is taken at its word, and this keeps CI and automated deployments from hanging on a
dialog. If you need a silent downgrade to be refused, that check belongs in the calling
script.

`CloseApplications=yes` lets the Restart Manager close a running Fabolus instead of the
install failing part-way on locked files.

Uninstall removes the program files, the generated `Fabolus.dll.config`, the Start Menu
group, and the Add/Remove Programs entry.

## Platform

Every project declares `<Platforms>x64</Platforms>` and the solution has exactly two
configurations, `Debug|x64` and `Release|x64`. Fabolus depends on 64-bit native libraries
through MeshLib, so there is no meaningful AnyCPU or x86 build.

`publish.ps1` passes `-p:Platform=x64` explicitly. It publishes the csproj rather than the
solution, and MSBuild would otherwise default to AnyCPU regardless of what `<Platforms>`
says.

One consequence worth knowing: setting a platform adds a directory to the output path
(`bin/x64/Release/net8.0` instead of `bin/Release/net8.0`). Anything that walks up from
`AppContext.BaseDirectory` by a fixed number of levels will break. `GeometryEngineFixture`
searches upward for a `files` folder instead of counting levels, for exactly this reason.

## Verifying a build

Beyond the automated test gate, worth checking by hand when something changes:

- The exe is named `Fabolus.exe`, not `Fabolus.Wpf.exe`.
- Its file properties report the expected version.
- The framework-dependent zip has **no** `hostfxr.dll` or `PresentationFramework.dll`; the
  self-contained one has both. That's the actual difference between them.
- Neither zip contains `cs/`, `de/`, `fr/` … satellite folders.
- Install the setup exe, confirm no elevation prompt, launch from the Start Menu, change a
  preference, restart the app, and confirm the preference stuck.
- Run the installer a second time and confirm you get one Add/Remove entry, not two.

## Known rough edges

Things a future change should probably address, none of which block a release today:

- **Preferences live next to the exe.** Moving them to `%APPDATA%\Fabolus\` would remove the
  writability constraint and let a machine-wide install work properly.
- **net8.0-windows leaves support in November 2026.** The framework-dependent zip's audience
  shrinks as .NET 8 ages out.
- **CUDA natives ship in every build.** `MRCuda-*.dll` and `MeshLibC2Cuda.dll` come from
  MeshLib and are dead weight on machines without an NVIDIA card. Excluding them needs
  testing that MeshLib doesn't probe for them at startup.
