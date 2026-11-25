# For the test of a scene opened in Blender's window: after the player has built the scene, says
# how each 3-D view of the window looks at it, and closes Blender.
#
#   blender --factory-startup --python render.py --python window_views.py -- \
#       --build-only --views out.json
#
# Only bpy and the standard library are used.

import json
import sys

import bpy

path = sys.argv[sys.argv.index("--views") + 1]
views = []
for window in bpy.context.window_manager.windows:
    for area in window.screen.areas:
        if area.type == "VIEW_3D":
            views.append(area.spaces.active.region_3d.view_perspective)
with open(path, "w", encoding="utf-8") as stream:
    json.dump({
        "background": bpy.app.background,
        "views": views,
        "camera": bpy.context.scene.camera.name if bpy.context.scene.camera else None,
        "objects": len(bpy.context.scene.objects),
    }, stream)
sys.stdout.flush()
# The window is not asked to close itself: at start-up it has not begun to listen yet.
sys.exit(0)
