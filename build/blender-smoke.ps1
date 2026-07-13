# Runs the generated-script ⇄ real-Blender smoke loop (GeneratedScriptBlenderSmokeTests):
# generates a matrix of Blender-Python scripts from SceneSpecs and executes each one in the
# locally installed Blender, so emitter/API breakage (4.2 → 5.x churn) is caught before users.
#
#   pwsh build/blender-smoke.ps1                      # locate Blender automatically
#   pwsh build/blender-smoke.ps1 -BlenderPath <exe>   # pin a specific build
#
# Per-case scripts + logs land in $env:TEMP/thide-blender-smoke/<case>/, with a summary in
# $env:TEMP/thide-blender-smoke/report.md (printed below) — designed so a failing case can be
# pasted straight into an AI-assisted fix loop: patch ScriptGenerator → rerun → read report.
param(
    [string]$BlenderPath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$env:THIDE_BLENDER_SMOKE = '1'
if ($BlenderPath) { $env:THIDE_BLENDER_PATH = $BlenderPath }

# -m:1: parallel MSBuild OOMs on this solution (see .claude/CLAUDE.md).
dotnet test (Join-Path $repoRoot 'tests/Therion.Blender.Tests/Therion.Blender.Tests.csproj') -m:1 `
    --filter 'FullyQualifiedName~GeneratedScriptBlenderSmoke' --nologo
$testExit = $LASTEXITCODE

$report = Join-Path ([IO.Path]::GetTempPath()) 'thide-blender-smoke/report.md'
if (Test-Path $report) {
    Write-Host ''
    Get-Content $report | Write-Host
}
exit $testExit
