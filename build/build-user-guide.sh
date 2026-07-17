#!/usr/bin/env bash
# Builds the ThIDE User Guide (docs/user-guide/*.md) into a single navigable document.
#
# Collates the Markdown pages in reading order and runs Pandoc to produce a PDF with a
# bookmark outline + table of contents. Default output: ThIDE/Assets/ThIDE-User-Guide.pdf,
# which the app bundles (globbed AvaloniaResource) and opens from Help -> User Guide.
#
# Requirements:
#   - pandoc          https://pandoc.org/                        (always)
#   - a PDF engine    TeX (texlive) or wkhtmltopdf/weasyprint    (for the default pdf format)
# If no PDF engine is available, run with FORMAT=html for a self-contained HTML build.
#
# Usage:
#   build/build-user-guide.sh                 # PDF into ThIDE/Assets/
#   FORMAT=html build/build-user-guide.sh     # self-contained HTML
#   OUTPUT=/tmp/guide.pdf build/build-user-guide.sh
set -euo pipefail

FORMAT="${FORMAT:-pdf}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(dirname "$script_dir")"
guide="$repo/docs/user-guide"

command -v pandoc >/dev/null 2>&1 || { echo "pandoc not found — install from https://pandoc.org/"; exit 1; }
[ -d "$guide" ] || { echo "User-guide folder not found: $guide"; exit 1; }

# Reading order: README -> numbered pages -> meta page.
pages=()
[ -f "$guide/README.md" ] && pages+=("$guide/README.md")
while IFS= read -r f; do pages+=("$f"); done < <(find "$guide" -maxdepth 1 -name '[0-9][0-9]-*.md' | sort)
[ -f "$guide/about-this-guide.md" ] && pages+=("$guide/about-this-guide.md")
[ "${#pages[@]}" -gt 0 ] || { echo "No Markdown pages found in $guide"; exit 1; }

ext="pdf"; [ "$FORMAT" = "html" ] && ext="html"
output="${OUTPUT:-$repo/ThIDE/Assets/ThIDE-User-Guide.$ext}"
mkdir -p "$(dirname "$output")"

echo "Building $output from ${#pages[@]} pages..."

# Ordered page basenames (matching the input order) for the link-rewriting Lua filter, which
# turns cross-page .md links into internal anchors and other repo links into GitHub URLs.
names=()
for f in "${pages[@]}"; do names+=("$(basename "$f" .md)"); done
GUIDE_PAGES="$(IFS=,; printf '%s' "${names[*]}")"
export GUIDE_PAGES

common=(--from=gfm --standalone --toc --toc-depth=2 --metadata "title=ThIDE User Guide" \
        "--resource-path=$guide" "--lua-filter=$script_dir/user-guide-links.lua")

# The version stamp is authored in README.md ("**For ThIDE x.y.z** — ...") and records the app
# version the guide's content was last checked against — deliberately NOT the version or date of
# this build. Lift it onto the title page so page 1 of the PDF says which ThIDE it describes.
# (|| true: a missing stamp must warn, not abort the release build under `set -e`.)
stamp=""
if [ -f "$guide/README.md" ]; then
    stamp="$(grep -m1 -E '^> *\*\*For ThIDE ' "$guide/README.md" | sed -e 's/^> *//' -e 's/\*\*//g' || true)"
fi
if [ -n "$stamp" ]; then
    common+=(--metadata "subtitle=$stamp")
else
    echo "warning: no '**For ThIDE <version>**' stamp in README.md — the title page will not name a version." >&2
fi

# Optional extra pandoc args (CI can pass a broad Unicode font via -V mainfont=...).
read -ra extra <<< "${PANDOC_EXTRA:-}"

if [ "$FORMAT" = "html" ]; then
    pandoc "${pages[@]}" "${common[@]}" --embed-resources -o "$output"
else
    # xelatex: the guide uses Unicode glyphs (arrows, box symbols) that pdflatex cannot typeset.
    # colorlinks colors real hyperlinks; toccolor=black keeps the table of contents plain (not red).
    pandoc "${pages[@]}" "${common[@]}" --number-sections --pdf-engine=xelatex \
        -V documentclass=report -V geometry:margin=1in \
        -V colorlinks=true -V toccolor=black \
        "${extra[@]}" -o "$output" \
        || { echo "pandoc failed — PDF needs a Unicode TeX engine (xelatex) or wkhtmltopdf; or run with FORMAT=html."; exit 1; }
fi

echo "OK: $output"
