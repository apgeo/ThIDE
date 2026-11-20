# For the player tests: after the player has built a scene, reports for each of its models which
# colour attribute Blender's solid view shows, and the colours it holds.
#
#   blender -b --factory-startup --python render.py --python draft_colours.py -- --build-only --colours out.json

import json
import sys

import bpy

bpy.context.view_layer.update()
report = {}
for obj in bpy.context.scene.objects:
    if obj.get("thide_role") != "model":
        continue
    mesh = obj.data
    shown = mesh.color_attributes.active_color
    entry = {"shown": shown.name if shown else None, "attributes": [a.name for a in mesh.color_attributes]}
    if shown:
        heights = [(obj.matrix_world @ vertex.co).z for vertex in mesh.vertices]
        lowest = min(range(len(heights)), key=heights.__getitem__)
        highest = max(range(len(heights)), key=heights.__getitem__)
        entry["lowest"] = [round(c, 5) for c in shown.data[lowest].color[:3]]
        entry["highest"] = [round(c, 5) for c in shown.data[highest].color[:3]]
        # One vertex in between, and how far up it stands (0 at the lowest, 1 at the highest).
        span = (heights[highest] - heights[lowest]) or 1.0
        between = min(range(len(heights)), key=lambda i: abs((heights[i] - heights[lowest]) / span - 0.3))
        entry["between"] = [round(c, 5) for c in shown.data[between].color[:3]]
        entry["betweenAt"] = (heights[between] - heights[lowest]) / span
        entry["different"] = len({tuple(round(c, 4) for c in value.color[:3]) for value in shown.data})
    report[obj.name] = entry

with open(sys.argv[sys.argv.index("--colours") + 1], "w", encoding="utf-8") as stream:
    json.dump(report, stream, indent=1)
