<#
.SYNOPSIS
    Builds the three Fabolus release artifacts.

.DESCRIPTION
    Produces, into the output directory:

      Fabolus-<version>-win-x64.zip                 framework-dependent, needs the
                                                    .NET 8 Desktop Runtime (x64)
      Fabolus-<version>-win-x64-self-contained.zip  self-contained, no prerequisites
      Fabolus-<version>-setup.exe                   Inno Setup installer wrapping the
                                                    self-contained payload

    This script is the single source of build logic. The GitHub Actions release
    workflow calls it unchanged, so a local run and a CI run produce identical
    artifacts.

.EXAMPLE
    pwsh ./build/publish.ps1
    pwsh ./build/publish.ps1 -Version 0.9.4 -SkipInstaller
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [string] $OutputDir,
    [switch] $SkipInstaller,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'

# ------------------------------------------------------------------
#  Paths
# ------------------------------------------------------------------
$repoRoot   = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\Fabolus.Wpf\Fabolus.Wpf.csproj'
$solution   = Join-Path $repoRoot 'Fabolus.sln'
$issScript  = Join-Path $PSScriptRoot 'installer\Fabolus.iss'

if (-not (Test-Path $appProject)) {
    throw "Could not find the application project at '$appProject'."
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot 'artifacts'
}

# ------------------------------------------------------------------
#  Version: parameter wins, otherwise read <Version> from the csproj
# ------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Version)) {
    $projectXml = [xml](Get-Content -LiteralPath $appProject -Raw)
    $Version = $projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "No -Version was supplied and no <Version> element exists in '$appProject'."
    }
    Write-Host "Version not supplied; using <Version> from the project file: $Version"
}
$Version = $Version.Trim().TrimStart('v', 'V')

function Write-Step([string] $Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

Write-Host 'Fabolus publish' -ForegroundColor Green
Write-Host "  version       : $Version"
Write-Host "  configuration : $Configuration"
Write-Host "  runtime       : $Runtime"
Write-Host "  output        : $OutputDir"

# ------------------------------------------------------------------
#  Clean output
# ------------------------------------------------------------------
Write-Step 'Preparing output directory'
if (Test-Path $OutputDir) { Remove-Item -LiteralPath $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$stageRoot = Join-Path $OutputDir 'stage'
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

# ------------------------------------------------------------------
#  Tests
# ------------------------------------------------------------------
if ($SkipTests) {
    Write-Step 'Skipping tests (-SkipTests)'
} else {
    Write-Step 'Running tests'
    dotnet test $solution -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit code $LASTEXITCODE); aborting publish." }
}

# ------------------------------------------------------------------
#  Publish helpers
# ------------------------------------------------------------------
function Invoke-Publish {
    param(
        [Parameter(Mandatory)] [string] $Destination,
        [Parameter(Mandatory)] [bool]   $SelfContained,
        [string[]] $ExtraArgs = @()
    )

    if ($SelfContained) { $selfContainedArg = 'true' } else { $selfContainedArg = 'false' }

    # Platform is passed explicitly: publishing targets the csproj rather than the solution,
    # and MSBuild would otherwise default to AnyCPU regardless of <Platforms>x64</Platforms>.
    $arguments = @(
        'publish', $appProject,
        '-c', $Configuration,
        '-r', $Runtime,
        '-p:Platform=x64',
        '--self-contained', $selfContainedArg,
        "-p:Version=$Version",
        '-p:PublishSingleFile=false',
        '-o', $Destination,
        '--nologo'
    ) + $ExtraArgs

    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }

    $exe = Join-Path $Destination 'Fabolus.exe'
    if (-not (Test-Path $exe)) {
        throw "Publish completed but '$exe' is missing. Check <AssemblyName> in the project file."
    }
}

function New-Zip {
    param(
        [Parameter(Mandatory)] [string] $SourceDir,
        [Parameter(Mandatory)] [string] $ZipPath
    )

    if (Test-Path $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $SourceDir,
        $ZipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
}

# ------------------------------------------------------------------
#  1. Framework-dependent zip
# ------------------------------------------------------------------
Write-Step "Publishing framework-dependent build ($Runtime)"
$fdDir = Join-Path $stageRoot 'framework-dependent'
Invoke-Publish -Destination $fdDir -SelfContained $false

$fdZip = Join-Path $OutputDir "Fabolus-$Version-$Runtime.zip"
Write-Host "    packing $([System.IO.Path]::GetFileName($fdZip))"
New-Zip -SourceDir $fdDir -ZipPath $fdZip

# ------------------------------------------------------------------
#  2. Self-contained zip
# ------------------------------------------------------------------
Write-Step "Publishing self-contained build ($Runtime)"
$scDir = Join-Path $stageRoot 'self-contained'
Invoke-Publish -Destination $scDir -SelfContained $true -ExtraArgs @('-p:PublishReadyToRun=true')

$scZip = Join-Path $OutputDir "Fabolus-$Version-$Runtime-self-contained.zip"
Write-Host "    packing $([System.IO.Path]::GetFileName($scZip))"
New-Zip -SourceDir $scDir -ZipPath $scZip

# ------------------------------------------------------------------
#  3. Installer (wraps the self-contained payload -> no prerequisites)
# ------------------------------------------------------------------
function Find-InnoSetupCompiler {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    # Machine-wide installs land in HKLM, winget's per-user install in HKCU.
    $uninstallKeys = @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )
    foreach ($key in $uninstallKeys) {
        try {
            $installLocation = (Get-ItemProperty -Path $key -ErrorAction Stop).InstallLocation
            if ($installLocation) {
                $candidate = Join-Path $installLocation 'ISCC.exe'
                if (Test-Path $candidate) { return $candidate }
            }
        } catch { }
    }

    $roots = @(
        [Environment]::GetFolderPath('ProgramFilesX86'),
        [Environment]::GetFolderPath('ProgramFiles'),
        (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs')
    )
    foreach ($root in $roots) {
        if (-not $root) { continue }
        $candidate = Join-Path $root 'Inno Setup 6\ISCC.exe'
        if (Test-Path $candidate) { return $candidate }
    }

    return $null
}

if ($SkipInstaller) {
    Write-Step 'Skipping installer (-SkipInstaller)'
} else {
    Write-Step 'Building installer'
    $iscc = Find-InnoSetupCompiler
    if (-not $iscc) {
        throw @"
Inno Setup 6 was not found, so the installer cannot be built.

Install it once with:

    winget install JRSoftware.InnoSetup

then re-run this script. To build only the two zips for now, pass -SkipInstaller.
"@
    }

    Write-Host "    compiler: $iscc"
    & $iscc "/DAppVersion=$Version" "/DPayloadDir=$scDir" "/DOutputDir=$OutputDir" $issScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed (exit code $LASTEXITCODE)." }

    $setupExe = Join-Path $OutputDir "Fabolus-$Version-setup.exe"
    if (-not (Test-Path $setupExe)) { throw "Inno Setup reported success but '$setupExe' is missing." }
}

# ------------------------------------------------------------------
#  Clean up and report
# ------------------------------------------------------------------
Write-Step 'Cleaning staging directory'
Remove-Item -LiteralPath $stageRoot -Recurse -Force

Write-Host ''
Write-Host "Artifacts in $OutputDir" -ForegroundColor Green
Get-ChildItem -LiteralPath $OutputDir -File |
    Sort-Object Name |
    ForEach-Object { '{0,-52} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) } |
    Write-Host
