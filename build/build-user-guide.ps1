<#
.SYNOPSIS
    Builds the ThIDE User Guide (docs/user-guide/*.md) into a single navigable document.

.DESCRIPTION
    Collates the Markdown pages in reading order (README, then the numbered NN-*.md pages,
    then about-this-guide) and runs Pandoc to produce a PDF with a bookmark outline and a
    table of contents. The default output path is ThIDE/Assets/ThIDE-User-Guide.pdf, which is
    picked up automatically by the app's globbed <AvaloniaResource Include="Assets\**" /> and
    shipped so Help -> User Guide can open it.

    Requirements:
      - pandoc            https://pandoc.org/            (always)
      - a PDF engine      TeX (MiKTeX/TeX Live) or wkhtmltopdf/weasyprint   (for -Format pdf)
    If no PDF engine is available, use -Format html for a self-contained HTML build that needs
    only pandoc.

.PARAMETER Format
    pdf (default) or html.

.PARAMETER Output
    Override the output file path.

.EXAMPLE
    pwsh build/build-user-guide.ps1
    pwsh build/build-user-guide.ps1 -Format html
#>
[CmdletBinding()]
param(
    [ValidateSet('pdf', 'html')]
    [string]$Format = 'pdf',
    [string]$Output
)

$ErrorActionPreference = 'Stop'

# Repo root = parent of this script's /build directory.
$repo  = Split-Path -Parent $PSScriptRoot
$guide = Join-Path $repo 'docs/user-guide'

if (-not (Get-Command pandoc -ErrorAction SilentlyContinue)) {
    Write-Error "pandoc was not found on PATH. Install it from https://pandoc.org/ and retry."
}
if (-not (Test-Path $guide)) {
    Write-Error "User-guide folder not found: $guide"
}

# Reading order: home (README) -> numbered pages (NN-*.md) -> the meta page.
$pages = @()
$readme = Join-Path $guide 'README.md'
if (Test-Path $readme) { $pages += $readme }
$pages += Get-ChildItem -Path $guide -Filter '??-*.md' | Sort-Object Name | Select-Object -ExpandProperty FullName
$about = Join-Path $guide 'about-this-guide.md'
if (Test-Path $about) { $pages += $about }

if ($pages.Count -eq 0) { Write-Error "No Markdown pages found in $guide" }

if (-not $Output) {
    $ext    = if ($Format -eq 'html') { 'html' } else { 'pdf' }
    $Output = Join-Path $repo "ThIDE/Assets/ThIDE-User-Guide.$ext"
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Output) | Out-Null

# Ordered page basenames (matching the input order) for the link-rewriting Lua filter, which
# turns cross-page .md links into internal anchors and other repo links into GitHub URLs.
$env:GUIDE_PAGES = (($pages | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_) }) -join ',')

$common = @(
    '--from=gfm'
    '--standalone'
    '--toc'
    '--toc-depth=2'
    '--metadata', 'title=ThIDE User Guide'
    "--resource-path=$guide"
    "--lua-filter=$(Join-Path $PSScriptRoot 'user-guide-links.lua')"
)

# Optional extra pandoc args (e.g. CI supplies a broad Unicode font via -V mainfont=...).
$extra = @()
if ($env:PANDOC_EXTRA) { $extra = $env:PANDOC_EXTRA -split '\s+' | Where-Object { $_ } }

Write-Host "Building $Output from $($pages.Count) pages..." -ForegroundColor Cyan

if ($Format -eq 'html') {
    # Self-contained HTML inlines CSS/images so the single file is portable.
    & pandoc @pages @common '--embed-resources' '-o' $Output
}
else {
    # LaTeX PDFs get a clickable bookmark outline (hyperref) from the section headings.
    # xelatex is used because the guide contains Unicode glyphs (arrows, box symbols) that
    # pdflatex cannot typeset. colorlinks colors real hyperlinks; toccolor=black keeps the
    # table of contents plain (not red).
    & pandoc @pages @common '--number-sections' '--pdf-engine=xelatex' `
        '-V' 'documentclass=report' `
        '-V' 'geometry:margin=1in' `
        '-V' 'colorlinks=true' `
        '-V' 'toccolor=black' `
        @extra '-o' $Output
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "pandoc failed (exit $LASTEXITCODE). For PDF you need a Unicode TeX engine (xelatex, e.g. MiKTeX/TeX Live) or wkhtmltopdf; or run with -Format html."
}

$size = [math]::Round((Get-Item $Output).Length / 1KB, 1)
Write-Host "OK: $Output ($size KB)" -ForegroundColor Green
