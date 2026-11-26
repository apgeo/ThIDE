# For the player tests: after the player has built a scene, reports every object of every
# annotation at the frames asked for, with where the camera is then.
#
#   blender -b --factory-startup --python render.py --python annotation_parts.py -- \
#       --build-only --parts out.json --part-frames 1,5,9
#
# Of each object: the annotation it belongs to (thide_id), what it is of it (thide_part), its role,
# where its own origin is in the scene, the way its own axes point there, its scale, whether it is
# hidden, and the numbers the "changes" module puts on it. Of a line: the middles of its two ends.
# Of a picture: its four corners, its colours in the solid view, and the image its material shows.
#
# Only bpy, mathutils and the standard library are used.

import json
import sys

import bpy
import mathutils


def option(name, default=None):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


def vec(v):
    return [round(float(c), 6) for c in v]


def middle(points):
    total = mathutils.Vector((0.0, 0.0, 0.0))
    for point in points:
        total += point
    return total / len(points)


def image_of(obj):
    for slot in obj.material_slots:
        tree = slot.material.node_tree if slot.material else None
        for node in tree.nodes if tree else []:
            if node.type == "TEX_IMAGE" and node.image is not None:
                return {"name": node.image.name, "size": [node.image.size[0], node.image.size[1]],
                        "packed": node.image.packed_file is not None,
                        "emitted": any(link.to_node.type == "EMISSION" for link in node.outputs["Color"].links)}
    return None


def describe(obj, depsgraph):
    evaluated = obj.evaluated_get(depsgraph)
    world = evaluated.matrix_world
    axes = world.to_3x3().normalized()
    part = {
        "name": obj.name,
        "id": obj.get("thide_id"),
        "part": obj.get("thide_part"),
        "role": obj.get("thide_role"),
        "type": obj.type,
        "location": vec(world.translation),
        "x": vec(axes.col[0]),
        "y": vec(axes.col[1]),
        "z": vec(axes.col[2]),
        "scale": vec(world.to_scale()),
        "hidden": bool(evaluated.hide_render),
        "fade": evaluated.get("thide_fade"),
        "opacity": evaluated.get("thide_opacity"),
        "shadow": bool(getattr(obj, "visible_shadow", True)),
        "lights": bool(getattr(obj, "visible_diffuse", True)),
        "materials": [slot.material.name for slot in obj.material_slots if slot.material],
        "constraints": [constraint.type for constraint in obj.constraints],
    }
    if obj.type == "FONT":
        part["text"] = obj.data.body
        part["size"] = obj.data.size
        part["alignY"] = obj.data.align_y
    if obj.type == "MESH":
        vertices = [world @ vertex.co for vertex in obj.data.vertices]
        part["vertices"] = len(vertices)
        part["faces"] = len(obj.data.polygons)
        # Where its mesh lies about its own origin, along its own axes.
        local = [vertex.co for vertex in obj.data.vertices]
        if local:
            part["bounds"] = [vec([min(c[i] for c in local) for i in range(3)]), vec([max(c[i] for c in local) for i in range(3)])]
        if obj.get("thide_part") == "line":
            # A ring of six about each of its two points.
            part["ends"] = [vec(middle(vertices[:6])), vec(middle(vertices[6:12]))]
            part["width"] = round(2.0 * (vertices[0] - middle(vertices[:6])).length, 6)
            # Every point of it, as a ring of six about each.
            part["rings"] = [vec(middle(vertices[at:at + 6])) for at in range(0, len(vertices) - 5, 6)]
        if obj.get("thide_part") == "picture":
            mesh = obj.data
            pictures = [polygon for polygon in mesh.polygons if polygon.material_index == 0]
            corners = [world @ mesh.vertices[index].co for polygon in pictures for index in polygon.vertices]
            # Of the picture itself, without its frame: the corners that lie farthest out along
            # its own axes.
            def far(right, up):
                return max(corners, key=lambda c: right * (c - world.translation).dot(axes.col[0]) + up * (c - world.translation).dot(axes.col[1]))
            part["corners"] = {"bottomLeft": vec(far(-1, -1)), "bottomRight": vec(far(1, -1)),
                               "topLeft": vec(far(-1, 1)), "topRight": vec(far(1, 1))}
            part["tiles"] = len(pictures)
            part["frameFaces"] = len(mesh.polygons) - len(pictures)
            part["image"] = image_of(obj)
            shown = mesh.color_attributes.active_color
            part["shown"] = shown.name if shown else None
            if shown is not None:
                # The colour the solid view has for the tile under each of five places of the
                # picture: (share of its width, share of its height).
                def tile_at(u, v):
                    place = world.translation + axes.col[0] * ((u - 0.5) * part_width) + axes.col[1] * ((v - 0.5) * part_height)
                    nearest = min(pictures, key=lambda polygon: (world @ polygon.center - place).length)
                    return [round(c, 4) for c in shown.data[nearest.loop_indices[0]].color[:3]]
                part_width = (far(1, -1) - far(-1, -1)).length
                part_height = (far(-1, 1) - far(-1, -1)).length
                part["tileColours"] = {"left": tile_at(0.2, 0.3), "right": tile_at(0.8, 0.3), "top": tile_at(0.5, 0.9),
                                       "topLeft": tile_at(0.1, 0.9), "bottomRight": tile_at(0.9, 0.1)}
    return part


scene = bpy.context.scene
frames = [int(f) for f in option("--part-frames", "1").split(",") if f.strip()]
objects = sorted((obj for obj in scene.objects if obj.get("thide_part")), key=lambda obj: obj.name)
anchors = sorted((obj for obj in scene.objects if obj.get("thide_role") == "annotation-anchor"), key=lambda obj: obj.name)
report = {"frames": []}
for frame in frames:
    scene.frame_set(frame)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    camera = scene.camera.evaluated_get(depsgraph)
    report["frames"].append({
        "frame": frame,
        # A camera looks along its own -Z: "back" is the way out of the picture, towards whoever
        # looks at it; "up" is the picture's top.
        "camera": {"location": vec(camera.matrix_world.translation),
                   "up": vec(camera.matrix_world.to_3x3().normalized().col[1]),
                   "back": vec(camera.matrix_world.to_3x3().normalized().col[2])},
        "parts": [describe(obj, depsgraph) for obj in objects],
        "anchors": [{"name": obj.name, "of": obj.get("thide_annotation"), "parent": obj.parent.name if obj.parent else None,
                     "location": vec(obj.evaluated_get(depsgraph).matrix_world.translation)} for obj in anchors],
    })

with open(option("--parts"), "w", encoding="utf-8") as stream:
    json.dump(report, stream, indent=1, ensure_ascii=False)
print("THIDE:parts=" + option("--parts"), flush=True)
