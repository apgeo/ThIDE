<#
.SYNOPSIS
  Publish ThIDE (self-contained, single-file, win-x64) and wrap it in a setup.exe with Inno Setup.

.DESCRIPTION
  Local counterpart to the "Build Windows installer" step in .github/workflows/release.yml.
  Produces publish/win-x64/ and then build/windows/Output/ThIDE-Setup-<Version>.exe.

  Requires Inno Setup 6 (ISCC.exe). Install it once with:  choco install innosetup -y
  If ISCC isn't on PATH the script probes the default Program Files locations.

.EXAMPLE
  pwsh build/windows/build-installer.ps1 -Version 0.3.0

.EXAMPLE
  # Skip the publish step and just repackage an existing publish/win-x64 folder
  pwsh build/windows/build-installer.ps1 -Version 0.3.0 -SkipPublish
#>
[CmdletBinding()]
param(
  [string]$Version = "0.0.0",
  [string]$Configuration = "Release",
  [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

# Repo root = two levels up from this script (build/windows/).
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$Project  = Join-Path $RepoRoot "ThIDE\ThIDE.csproj"
$PublishDir = Join-Path $RepoRoot "publish\win-x64"
$Iss = Join-Path $PSScriptRoot "ThIDE.iss"
$OutDir = Join-Path $PSScriptRoot "Output"

function Find-ISCC {
  $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
    if (-not $base) { continue }
    foreach ($v in @("Inno Setup 6", "Inno Setup 5")) {
      $p = Join-Path $base "$v\ISCC.exe"
      if (Test-Path $p) { return $p }
    }
  }
  return $null
}

$iscc = Find-ISCC
if (-not $iscc) {
  throw "ISCC.exe (Inno Setup) not found. Install it with 'choco install innosetup -y' or from https://jrsoftware.org/isdl.php"
}

if (-not $SkipPublish) {
  Write-Host "==> Publishing $Project (win-x64, self-contained, single-file)..." -ForegroundColor Cyan
  # -m:1 mirrors the known-good build (the solution OOMs MSBuild under parallel build).
  dotnet publish $Project -m:1 -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -o $PublishDir
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
}

if (-not (Test-Path (Join-Path $PublishDir "ThIDE.exe"))) {
  throw "Publish output not found at $PublishDir. Run without -SkipPublish first."
}

Write-Host "==> Building installer with $iscc ..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" "/DSourceDir=$PublishDir" "/O$OutDir" $Iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)." }

$setup = Join-Path $OutDir "ThIDE-Setup-$Version.exe"
Write-Host "==> Done: $setup" -ForegroundColor Green
