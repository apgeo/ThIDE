#!/bin/sh
# Renders the scene of this folder with Blender 4.2 or newer.
#
#   sh render.sh            the scene as planned (scene-plan.json)
#   sh render.sh --draft    a quick draft of it (scene-plan.draft.json), when the bundle has one
#   sh render.sh --compose  the output again from the frames that are there: after a text of the
#                           plan was changed (a title, a caption), nothing is rendered again
#
# A scene with texts over the picture is rendered in two stages: its frames first, into "frames"
# beside this script, then the output from them. A render that was stopped goes on where it
# stopped when this script is started again.
#
# What it writes goes to the folder the plan names, "out" beside this script. Blender is looked
# for on the PATH, and in /Applications on a Mac; to use another one, name it in BLENDER:
#   BLENDER=/opt/blender-4.5/blender sh render.sh
# Anything else after the options above goes to the player (see thide_player/core.py).
set -e
case "$0" in */*) HERE=${0%/*} ;; *) HERE=. ;; esac
HERE=$(CDPATH= cd -- "$HERE" && pwd)
if [ -z "$BLENDER" ]; then
    if command -v blender >/dev/null 2>&1; then
        BLENDER=blender
    elif [ -x /Applications/Blender.app/Contents/MacOS/Blender ]; then
        BLENDER=/Applications/Blender.app/Contents/MacOS/Blender
    else
        echo "Blender was not found. Install Blender 4.2 or newer, or name it in BLENDER:" >&2
        echo "  BLENDER=/path/to/blender sh render.sh" >&2
        exit 1
    fi
fi
PLAN="$HERE/scene-plan.json"
STAGE=all
while [ "$1" = "--draft" ] || [ "$1" = "--compose" ]; do
    if [ "$1" = "--compose" ]; then
        STAGE=compose
    else
        PLAN="$HERE/scene-plan.draft.json"
        if [ ! -f "$PLAN" ]; then
            echo "This bundle has no draft (scene-plan.draft.json)." >&2
            exit 1
        fi
    fi
    shift
done
exec "$BLENDER" -b --factory-startup --python-exit-code 64 --python "$HERE/render.py" -- --plan "$PLAN" --stage "$STAGE" "$@"
