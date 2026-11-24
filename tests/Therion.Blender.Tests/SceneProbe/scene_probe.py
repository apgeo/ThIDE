# Scene probe: reports what a generated Blender script built, as data a test can assert on.
#
# Run it after the generated script, in the same Blender session:
#
#   blender -b --factory-startup --python render.py --python scene_probe.py -- --report out.json
#
# or on its own, to read a rendered movie back:
#
#   blender -b --factory-startup --python scene_probe.py -- --movie clip.mp4 --report out.json
#
# Options (after "--"):
#   --report PATH       where to write the JSON report (required)
#   --frames 1,15,30    frames to sample (default: --samples frames spread over the frame range)
#   --samples N         number of evenly spaced frames, first and last included (default 5)
#   --pixels WxH        also render each sampled frame with one flat colour per role and report
#                       each role's share of the pixels; this replaces the scene's engine and
#                       object colours, so it runs last
#   --sheet PATH        also write a contact sheet (PNG) of the sampled frames, for a person or an
#                       agent to look at
#   --sheet-cell WxH    size of one cell of the sheet (default 320x180)
#   --sheet-samples N   render samples for the sheet (default 16)
#   --movie PATH        report a movie file's size, frame count and frame rate; nothing else
#
# At every sampled frame the report also has what can change of each mesh, text and curve (hidden
# or not, its scale, and the numbers the player's "changes" module puts on it for its material:
# fade, opacity, tint), and the world's colour; and for every material, whether its links make it
# read those numbers.
#
# Objects are told apart by the names the generator gives them (CaveModel, Terrain, Camera,
# stn:<station>, cmp:<n>, lead:<station>, leadtxt:<station>, overlay:<kind>), or by the role the player stamps on
# what it makes (the custom property "thide_role").
#
# Only bpy, mathutils and the standard library are used: the Blender packaged by a Linux
# distribution runs on the system Python and has no numpy.

import argparse
import array
import json
import os
import sys

import bpy
import mathutils
from bpy_extras.object_utils import world_to_camera_view
from mathutils.bvhtree import BVHTree

PROBE_VERSION = 1

ROLE_BY_NAME = {"CaveModel": "model", "Terrain": "terrain", "Camera": "camera", "CameraTarget": "camera-target"}
# "leadtxt:" must be tested before "lead:".
ROLE_BY_PREFIX = (
    ("stn:", "station-label"),
    ("cmp:", "component-label"),
    ("leadtxt:", "lead-text"),
    ("lead:", "lead-marker"),
    ("overlay:", "overlay"),
)
# "label" and "marker" are what the player calls a text and a marker whose plan gives no role.
ANNOTATION_ROLES = ("station-label", "component-label", "lead-text", "lead-marker", "overlay", "label", "marker")
GEOMETRY_TYPES = ("MESH", "FONT", "CURVE", "SURFACE")

# One primary colour per role: every channel is 0 or 1, so a pixel classifies by thresholding at
# one half whatever the colour management does to it. Black is the eighth and last one.
ROLE_COLOURS = {
    "model": (1.0, 0.0, 0.0),
    "terrain": (0.0, 0.0, 0.0),
    "station-label": (0.0, 1.0, 0.0),
    "component-label": (0.0, 1.0, 1.0),
    "lead-marker": (0.0, 0.0, 1.0),
    "lead-text": (1.0, 0.0, 1.0),
    "overlay": (1.0, 1.0, 0.0),
    "other": (1.0, 1.0, 1.0),
}
ROLE_BY_BITS = {tuple(c > 0.5 for c in colour): role for role, colour in ROLE_COLOURS.items()}

MAX_SAMPLED_VERTICES = 2000
MAX_UV_SAMPLES = 64

# 3x5 digits for the frame number stamped on each cell of the contact sheet.
DIGITS = {
    "0": "111101101101111", "1": "010110010010111", "2": "111001111100111", "3": "111001111001111",
    "4": "101101111001001", "5": "111100111001111", "6": "111100111101111", "7": "111001001001001",
    "8": "111101111101111", "9": "111101111001111",
}


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="scene_probe")
    parser.add_argument("--report", required=True)
    parser.add_argument("--frames", default="")
    parser.add_argument("--samples", type=int, default=5)
    parser.add_argument("--pixels", default="")
    parser.add_argument("--sheet", default="")
    parser.add_argument("--sheet-cell", default="320x180")
    parser.add_argument("--sheet-samples", type=int, default=16)
    parser.add_argument("--movie", default="")
    # The script that built the scene may have options of its own after the "--".
    return parser.parse_known_args(argv)[0]


def size_of(text):
    width, height = text.lower().split("x")
    return int(width), int(height)


def vec(v):
    return [round(float(c), 6) for c in v]


def role_of(obj):
    if obj.get("thide_role"):
        return obj["thide_role"]
    if obj.name in ROLE_BY_NAME:
        return ROLE_BY_NAME[obj.name]
    for prefix, role in ROLE_BY_PREFIX:
        if obj.name.startswith(prefix):
            return role
    return "light" if obj.type == "LIGHT" else "other"


def world_corners(obj):
    return [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]


def bounds_of(corners):
    low = [min(c[i] for c in corners) for i in range(3)]
    high = [max(c[i] for c in corners) for i in range(3)]
    return low, high


def frames_to_sample(scene, args):
    if args.frames:
        return [int(f) for f in args.frames.split(",") if f.strip()]
    first, last = scene.frame_start, scene.frame_end
    count = max(1, args.samples)
    if last <= first or count == 1:
        return [first]
    frames = {first + round((last - first) * i / (count - 1)) for i in range(count)}
    return sorted(frames)


def describe_scene(scene):
    render = scene.render
    image = render.image_settings
    info = {
        "frameStart": scene.frame_start,
        "frameEnd": scene.frame_end,
        "fps": round(render.fps / render.fps_base, 4),
        "resolution": [render.resolution_x, render.resolution_y],
        "resolutionPercentage": render.resolution_percentage,
        "engine": render.engine,
        "camera": scene.camera.name if scene.camera else None,
        "filmTransparent": bool(render.film_transparent),
        "fileFormat": image.file_format,
        "mediaType": getattr(image, "media_type", None),
        "outputPath": render.filepath,
        "world": scene.world.name if scene.world else None,
    }
    if render.engine == "CYCLES":
        info["samples"] = scene.cycles.samples
        info["device"] = scene.cycles.device
    elif hasattr(scene, "eevee") and render.engine.startswith("BLENDER_EEVEE"):
        info["samples"] = scene.eevee.taa_render_samples
    return info


def describe_object(obj, depsgraph):
    evaluated = obj.evaluated_get(depsgraph)
    info = {
        "name": obj.name,
        "role": role_of(obj),
        "type": obj.type,
        "parent": obj.parent.name if obj.parent else None,
        "location": vec(evaluated.matrix_world.translation),
        "hideRender": bool(evaluated.hide_render),
        "animated": obj.animation_data is not None and obj.animation_data.action is not None,
        "constraints": [
            {"type": c.type, "target": c.target.name if getattr(c, "target", None) else None}
            for c in obj.constraints
        ],
        "materials": [slot.material.name for slot in obj.material_slots if slot.material],
    }
    if obj.type in GEOMETRY_TYPES:
        low, high = bounds_of(world_corners(evaluated))
        info["boundsMin"] = vec(low)
        info["boundsMax"] = vec(high)
    if obj.type == "MESH":
        info["vertices"] = len(obj.data.vertices)
        info["polygons"] = len(obj.data.polygons)
        info["colourAttributes"] = [a.name for a in obj.data.color_attributes]
        info["uvMaps"] = [layer.name for layer in obj.data.uv_layers]
        info["visibleShadow"] = bool(getattr(obj, "visible_shadow", True))
        if obj.data.uv_layers:
            info["uvSamples"] = uv_samples(obj)
    elif obj.type == "FONT":
        info["text"] = obj.data.body
        info["textSize"] = round(obj.data.size, 6)
    elif obj.type == "LIGHT":
        info["lightType"] = obj.data.type
        info["energy"] = round(obj.data.energy, 6)
        # A light shines along its own -Z axis.
        info["direction"] = vec(evaluated.matrix_world.to_3x3().normalized() @ mathutils.Vector((0.0, 0.0, -1.0)))
        if obj.data.type == "AREA":
            info["lightSize"] = round(obj.data.size, 6)
    elif obj.type == "CAMERA":
        info["lens"] = round(obj.data.lens, 6)
        info["depthOfField"] = bool(obj.data.dof.use_dof)
    return info


def first_image(obj):
    for slot in obj.material_slots:
        if slot.material and slot.material.node_tree:
            for node in slot.material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image is not None:
                    return node.image
    return None


def uv_samples(obj):
    """Where some vertices sit, their UV, and the colour of the material's image there (rows of
    image.pixels run bottom-up, as UV v does)."""
    mesh = obj.data
    uv = mesh.uv_layers.active.data
    image = first_image(obj)
    pixels, width, height = None, 0, 0
    if image is not None:
        width, height = image.size  # reading the size loads the image
        if width * height > 0:
            pixels = array.array("f", bytes(4 * 4 * width * height))
            image.pixels.foreach_get(pixels)
    step = max(1, len(mesh.loops) // MAX_UV_SAMPLES)
    samples = []
    for index in range(0, len(mesh.loops), step):
        u, v = uv[index].uv
        sample = {"position": vec(obj.matrix_world @ mesh.vertices[mesh.loops[index].vertex_index].co),
                  "uv": [round(u, 6), round(v, 6)]}
        if pixels is not None:
            x = min(width - 1, max(0, int(u * width)))
            y = min(height - 1, max(0, int(v * height)))
            at = 4 * (y * width + x)
            sample["imageColour"] = [round(c, 4) for c in pixels[at:at + 4]]
        samples.append(sample)
    return samples


# The numbers the player's "changes" module puts on an object for its material to read.
CHANGE_NUMBERS = (("fade", "thide_fade"), ("opacity", "thide_opacity"), ("tint", "thide_tint"),
                  ("tintStrength", "thide_tint_strength"))


def thing_at(obj, depsgraph):
    """What can change of an object, as it is at the current frame."""
    evaluated = obj.evaluated_get(depsgraph)
    info = {
        "name": obj.name,
        "hidden": bool(evaluated.hide_render),
        "hiddenInViewport": bool(evaluated.hide_viewport),
        "scale": [round(float(s), 6) for s in evaluated.matrix_world.to_scale()],
    }
    for key, name in CHANGE_NUMBERS:
        value = evaluated.get(name)
        if value is not None:
            info[key] = round(float(value), 6) if isinstance(value, (int, float)) else vec(value)
    return info


def world_at(scene, depsgraph):
    """The world's own colour and strength at the current frame, and the colour the solid view
    shows; the colour is None when something else gives the background its colour (a sky)."""
    world = scene.evaluated_get(depsgraph).world
    if world is None:
        return None
    info = {"color": None, "strength": None, "viewportColor": vec(world.color)}
    if world.node_tree is not None:
        background = next((node for node in world.node_tree.nodes if node.type == "BACKGROUND"), None)
        if background is not None:
            info["strength"] = round(float(background.inputs["Strength"].default_value), 6)
            if not background.inputs["Color"].is_linked:
                info["color"] = vec(background.inputs["Color"].default_value[:3])
    return info


def object_number_read(socket):
    """The name of the object's number a socket reads through an Attribute node, or None."""
    if socket is None or not socket.is_linked:
        return None
    node = socket.links[0].from_node
    return node.attribute_name if node.type == "ATTRIBUTE" and node.attribute_type == "OBJECT" else None


def describe_material(material):
    """Whether the material reads the numbers of the object it is on: seeThrough when its surface
    is mixed with a transparent one by the object's fade times its opacity, tinted when the
    object's tint is mixed over its shader's colour by the tint's strength. Read from the links
    alone, whatever the nodes are called."""
    info = {"name": material.name, "seeThrough": False, "tinted": False}
    tree = material.node_tree
    outputs = [node for node in tree.nodes if node.type == "OUTPUT_MATERIAL"] if tree is not None else []
    output = next((node for node in outputs if node.is_active_output), outputs[0] if outputs else None)
    if output is None or not output.inputs["Surface"].is_linked:
        return info
    shader = output.inputs["Surface"].links[0].from_node
    if shader.type == "MIX_SHADER" and shader.inputs[0].is_linked and shader.inputs[1].is_linked and shader.inputs[2].is_linked:
        share = shader.inputs[0].links[0].from_node
        read = set()
        if share.type == "MATH" and share.operation == "MULTIPLY":
            read = {object_number_read(share.inputs[0]), object_number_read(share.inputs[1])}
        if read == {"thide_fade", "thide_opacity"} and shader.inputs[1].links[0].from_node.type == "BSDF_TRANSPARENT":
            info["seeThrough"] = True
            shader = shader.inputs[2].links[0].from_node
    colour = shader.inputs.get("Base Color") or shader.inputs.get("Color")
    if colour is not None and colour.is_linked:
        mix = colour.links[0].from_node
        if mix.type == "MIX" and mix.data_type == "RGBA":
            by_identifier = {socket.identifier: socket for socket in mix.inputs}
            info["tinted"] = (object_number_read(by_identifier.get("Factor_Float")) == "thide_tint_strength"
                              and object_number_read(by_identifier.get("B_Color")) == "thide_tint")
    return info


def in_frame(x, y, depth, camera_data):
    return 0.0 <= x <= 1.0 and 0.0 <= y <= 1.0 and camera_data.clip_start <= depth <= camera_data.clip_end


def frame_coverage(scene, camera, corners):
    """Share of the frame covered by the 2-D box around the projected corners; None when a
    corner is behind the camera, where a projection means nothing."""
    xs, ys = [], []
    for corner in corners:
        x, y, depth = world_to_camera_view(scene, camera, corner)
        if depth <= 0.0:
            return None
        xs.append(min(1.0, max(0.0, x)))
        ys.append(min(1.0, max(0.0, y)))
    return round((max(xs) - min(xs)) * (max(ys) - min(ys)), 6)


def sample_frame(scene, frame, model, sampled_vertices, annotations, things):
    scene.frame_set(frame)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    fps = scene.render.fps / scene.render.fps_base
    entry = {"frame": frame, "seconds": round((frame - scene.frame_start) / fps, 6)}
    entry["things"] = [thing_at(obj, depsgraph) for obj in things]
    entry["world"] = world_at(scene, depsgraph)
    if scene.camera is None:
        return entry

    camera = scene.camera.evaluated_get(depsgraph)
    camera_data = camera.data
    eye = camera.matrix_world.translation.copy()
    axes = camera.matrix_world.to_3x3().normalized()
    entry["camera"] = {
        "location": vec(eye),
        "forward": vec(axes @ mathutils.Vector((0.0, 0.0, -1.0))),
        "up": vec(axes @ mathutils.Vector((0.0, 1.0, 0.0))),
        "lens": round(camera_data.lens, 6),
        "sensorWidth": round(camera_data.sensor_width, 6),
        "clipStart": round(camera_data.clip_start, 6),
        "clipEnd": round(camera_data.clip_end, 6),
    }
    target = scene.objects.get("CameraTarget")
    if target is not None:
        target_location = target.evaluated_get(depsgraph).matrix_world.translation
        entry["camera"]["target"] = vec(target_location)
        entry["camera"]["targetDistance"] = round((target_location - eye).length, 6)

    if model is not None:
        model_evaluated = model.evaluated_get(depsgraph)
        corners = world_corners(model_evaluated)
        low, high = bounds_of(corners)
        projected = [world_to_camera_view(scene, camera, c) for c in corners]
        centre = world_to_camera_view(scene, camera, (mathutils.Vector(low) + mathutils.Vector(high)) / 2.0)
        seen = sum(1 for p in (world_to_camera_view(scene, camera, v) for v in sampled_vertices)
                   if in_frame(p.x, p.y, p.z, camera_data))
        info = {
            "cornersInFrame": sum(1 for p in projected if in_frame(p.x, p.y, p.z, camera_data)),
            "centreInFrame": in_frame(centre.x, centre.y, centre.z, camera_data),
            "verticesInFrame": round(seen / len(sampled_vertices), 6) if sampled_vertices else 0.0,
            "farthestCornerDepth": round(max(p.z for p in projected), 6),
            "cameraInsideBounds": all(low[i] <= eye[i] <= high[i] for i in range(3)),
        }
        info["beyondFarClip"] = info["farthestCornerDepth"] > camera_data.clip_end
        # The tree is in the model's own coordinates; the generator leaves the model unscaled.
        tree = BVHTree.FromObject(model, depsgraph)
        nearest = tree.find_nearest(model_evaluated.matrix_world.inverted() @ eye)
        if nearest[0] is not None:
            info["nearestSurface"] = round((model_evaluated.matrix_world @ nearest[0] - eye).length, 6)
        entry["model"] = info

    entry["objects"] = []
    for obj in annotations:
        evaluated = obj.evaluated_get(depsgraph)
        corners = world_corners(evaluated)
        low, high = bounds_of(corners)
        centre = (mathutils.Vector(low) + mathutils.Vector(high)) / 2.0
        x, y, depth = world_to_camera_view(scene, camera, centre)
        info = {
            "name": obj.name,
            "hidden": bool(evaluated.hide_render),
            "origin": vec(evaluated.matrix_world.translation),
            "scale": round(max(abs(s) for s in evaluated.matrix_world.to_scale()), 6),
            "frameX": round(x, 6),
            "frameY": round(y, 6),
            "depth": round(depth, 6),
            "inFrame": in_frame(x, y, depth, camera_data),
            "coverage": frame_coverage(scene, camera, corners),
            "occluded": False,
            "occluder": None,
        }
        ray = centre - eye
        distance = ray.length
        if distance > 1e-6:
            hit, where, _normal, _index, hit_object, _matrix = scene.ray_cast(
                depsgraph, eye, ray / distance, distance=distance)
            if hit and hit_object is not None and hit_object.original.name != obj.name \
                    and (where - eye).length < distance - 1e-4:
                info["occluded"] = True
                info["occluder"] = hit_object.original.name
        entry["objects"].append(info)
    return entry


def set_still_image_output(scene, colour_mode):
    image = scene.render.image_settings
    if hasattr(image, "media_type"):
        image.media_type = "IMAGE"
    image.file_format = "PNG"
    image.color_mode = colour_mode
    scene.render.resolution_percentage = 100
    for switch in ("use_compositing", "use_sequencer"):
        if hasattr(scene.render, switch):
            setattr(scene.render, switch, False)


def read_pixels(path):
    image = bpy.data.images.load(path)
    width, height = image.size
    pixels = array.array("f", bytes(4 * 4 * width * height))
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    return width, height, pixels


def render_still(scene, frame, path):
    scene.frame_set(frame)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def stamp_number(sheet, sheet_width, left, top, number, scale=3):
    """Writes the frame number, white on black, with its top-left corner at (left, top) counted
    from the top of the sheet. Image rows run bottom-up."""
    text = str(number)
    box_width = (len(text) * 4 + 1) * scale
    box_height = 7 * scale
    sheet_height = len(sheet) // (4 * sheet_width)

    def put(x, y_from_top, value):
        row = sheet_height - 1 - y_from_top
        if 0 <= x < sheet_width and 0 <= row < sheet_height:
            at = 4 * (row * sheet_width + x)
            sheet[at], sheet[at + 1], sheet[at + 2], sheet[at + 3] = value, value, value, 1.0

    for dy in range(box_height):
        for dx in range(box_width):
            put(left + dx, top + dy, 0.0)
    for index, digit in enumerate(text):
        bits = DIGITS[digit]
        for cell in range(15):
            if bits[cell] == "1":
                for dy in range(scale):
                    for dx in range(scale):
                        put(left + (1 + index * 4 + cell % 3) * scale + dx,
                            top + (1 + cell // 3) * scale + dy, 1.0)


def write_contact_sheet(scene, frames, path, cell, samples, work_dir):
    width, height = cell
    render = scene.render
    saved = (render.resolution_x, render.resolution_y, render.resolution_percentage, render.filepath,
             render.film_transparent)
    render.resolution_x, render.resolution_y = width, height
    render.film_transparent = False
    set_still_image_output(scene, "RGB")
    if render.engine == "CYCLES":
        scene.cycles.samples = min(scene.cycles.samples, samples)
    elif hasattr(scene, "eevee"):
        scene.eevee.taa_render_samples = min(scene.eevee.taa_render_samples, samples)

    columns = min(len(frames), 4)
    rows = (len(frames) + columns - 1) // columns
    sheet_width = columns * width
    sheet = array.array("f", bytes(4 * 4 * sheet_width * rows * height))
    for index, frame in enumerate(frames):
        cell_path = os.path.join(work_dir, "sheet_%04d.png" % frame)
        render_still(scene, frame, cell_path)
        _w, _h, pixels = read_pixels(cell_path)
        column, row = index % columns, index // columns
        for y in range(height):
            source = 4 * y * width
            target = 4 * (((rows - 1 - row) * height + y) * sheet_width + column * width)
            sheet[target:target + 4 * width] = pixels[source:source + 4 * width]
        stamp_number(sheet, sheet_width, column * width + 4, row * height + 4, frame)

    image = bpy.data.images.new("contact-sheet", sheet_width, rows * height, alpha=False)
    image.pixels.foreach_set(sheet)
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
    (render.resolution_x, render.resolution_y, render.resolution_percentage, render.filepath,
     render.film_transparent) = saved
    return {"path": path, "columns": columns, "rows": rows, "cell": [width, height], "frames": frames}


def measure_pixels(scene, frames, size, work_dir):
    """Renders each frame unlit, one flat colour per role, and counts the pixels of each."""
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "FLAT"
    shading.color_type = "OBJECT"
    for switch in ("show_shadows", "show_cavity", "show_object_outline", "show_specular_highlight",
                   "show_xray", "show_backface_culling"):
        if hasattr(shading, switch):
            setattr(shading, switch, False)
    scene.display.render_aa = "OFF"
    scene.render.film_transparent = True
    scene.render.resolution_x, scene.render.resolution_y = size
    set_still_image_output(scene, "RGBA")
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    for obj in scene.objects:
        colour = ROLE_COLOURS.get(role_of(obj), ROLE_COLOURS["other"])
        obj.color = (colour[0], colour[1], colour[2], 1.0)

    results = []
    for frame in frames:
        path = os.path.join(work_dir, "roles_%04d.png" % frame)
        render_still(scene, frame, path)
        width, height, pixels = read_pixels(path)
        counts = {}
        for at in range(0, len(pixels), 4):
            if pixels[at + 3] < 0.5:
                role = "background"
            else:
                role = ROLE_BY_BITS.get(
                    (pixels[at] > 0.5, pixels[at + 1] > 0.5, pixels[at + 2] > 0.5), "unknown")
            counts[role] = counts.get(role, 0) + 1
        total = float(width * height)
        results.append({
            "frame": frame,
            "size": [width, height],
            "shares": {role: round(count / total, 6) for role, count in sorted(counts.items())},
        })
    return results


def describe_movie(path):
    clip = bpy.data.movieclips.load(path)
    return {
        "path": path,
        "bytes": os.path.getsize(path),
        "size": [clip.size[0], clip.size[1]],
        "frames": clip.frame_duration,
        "fps": round(clip.fps, 4),
    }


def main():
    args = parse_args()
    report_path = os.path.abspath(args.report)
    work_dir = os.path.dirname(report_path)
    os.makedirs(work_dir, exist_ok=True)
    report = {"probe": PROBE_VERSION, "blender": bpy.app.version_string}

    if args.movie:
        report["movie"] = describe_movie(os.path.abspath(args.movie))
    else:
        scene = bpy.context.scene
        frames = frames_to_sample(scene, args)
        scene.frame_set(scene.frame_start)
        depsgraph = bpy.context.evaluated_depsgraph_get()
        objects = sorted(scene.objects, key=lambda o: o.name)
        model = scene.objects.get("CaveModel")
        sampled_vertices = []
        if model is not None and model.type == "MESH":
            vertices = model.data.vertices
            step = max(1, len(vertices) // MAX_SAMPLED_VERTICES)
            sampled_vertices = [model.matrix_world @ vertices[i].co for i in range(0, len(vertices), step)]
        annotations = [o for o in objects if role_of(o) in ANNOTATION_ROLES and o.type in GEOMETRY_TYPES]

        report["scene"] = describe_scene(scene)
        report["objects"] = [describe_object(o, depsgraph) for o in objects]
        report["images"] = [
            {"name": i.name, "path": i.filepath, "size": [i.size[0], i.size[1]], "loaded": bool(i.has_data)}
            for i in bpy.data.images if i.source == "FILE"
        ]
        report["materials"] = [describe_material(m) for m in sorted(bpy.data.materials, key=lambda m: m.name)]
        things = [o for o in objects if o.type in GEOMETRY_TYPES]
        report["frames"] = [sample_frame(scene, f, model, sampled_vertices, annotations, things) for f in frames]
        if args.sheet:
            report["sheet"] = write_contact_sheet(
                scene, frames, os.path.abspath(args.sheet), size_of(args.sheet_cell), args.sheet_samples, work_dir)
        if args.pixels:
            report["pixels"] = measure_pixels(scene, frames, size_of(args.pixels), work_dir)

    with open(report_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=1, ensure_ascii=False)
    print("THIDE:probe=" + report_path, flush=True)


main()
