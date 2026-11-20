"""Feature "labels": texts that turn to face the camera, and glowing balls that mark places.

Where each one stands, how large it is and what it says comes from the plan; nothing is laid out
here. Runs after "camera_track": the texts turn to the camera, and a marker that is sized by its
distance measures that distance from the camera. A text that follows a marker (a lead's caption)
is the marker's child: it grows with the ball and stays where it stands relative to it.
"""

import math

import bmesh
import bpy
import mathutils

from thide_player import api

SECTION = "labels"
KEYS = {
    "texts": {
        "color": None, "strength": None,
        "items": [{"id": None, "role": None, "text": None, "at": None, "size": None, "follows": None}],
    },
    "markers": {
        "color": None, "strength": None,
        "pulse": {"period": None, "scale": None},
        "byDistance": {"factor": None, "minRadius": None, "maxRadius": None},
        "items": [{"id": None, "role": None, "at": None, "radius": None}],
    },
}


def build(ctx, labels):
    texts = (labels or {}).get("texts") or {}
    markers = (labels or {}).get("markers") or {}
    if not texts.get("items") and not markers.get("items"):
        return
    camera = ctx.scene.camera
    if camera is None:
        raise api.PlanError('labels turn to the camera; list "camera_track" before "labels" in "features"')

    followers, placed = [], {}
    if texts.get("items"):
        colour = rgba(texts["color"])
        material = glowing("LabelText", colour, texts.get("strength", 1.5))
        for index, item in enumerate(texts["items"]):
            text = add_text(ctx.scene, camera, item, material, colour)
            if item.get("follows") is not None:
                followers.append((text, item["follows"], "labels.texts.items[%d].follows" % index))

    if markers.get("items"):
        colour = rgba(markers["color"])
        material = glowing("LeadMarker", colour, markers.get("strength", 2.0))  # as the generated scripts name it
        pulse = markers.get("pulse")
        sizing = markers.get("byDistance")
        # A pulse lasts a whole number of frames: the frame its period falls on.
        pulse_frames = max(1, ctx.frame_at(pulse["period"]) - 1) if pulse else 0
        for item in markers["items"]:
            marker = add_marker(ctx.scene, item, material, colour)
            placed[item["id"]] = (marker, item["at"])
            if sizing:
                size_by_distance(marker, camera, item["radius"], sizing, pulse, pulse_frames)
            elif pulse:
                keep_pulsing(marker, pulse, pulse_frames)

    for text, marker_id, where in followers:
        if marker_id not in placed:
            raise api.PlanError('%s: there is no marker "%s"' % (where, marker_id))
        follow(text, *placed[marker_id])
    api.report("labels", len(texts.get("items", [])) + len(markers.get("items", [])))


def rgba(colour):
    return (colour[0], colour[1], colour[2], 1.0)


def glowing(name, colour, strength):
    """A material that emits its colour, so a label reads in a cave no light reaches."""
    material = bpy.data.materials.new(name)
    material.diffuse_color = colour  # in Blender's solid view
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    emission = tree.nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = colour
    emission.inputs["Strength"].default_value = strength
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    tree.links.new(emission.outputs["Emission"], output.inputs["Surface"])
    return material


def add_text(scene, camera, item, material, colour):
    curve = bpy.data.curves.new(item["id"], type="FONT")
    curve.body = item["text"]
    curve.size = item["size"]
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.materials.append(material)
    obj = bpy.data.objects.new(item["id"], curve)
    place(scene, obj, item, colour, "label")
    facing = obj.constraints.new(type="TRACK_TO")
    facing.target = camera
    facing.track_axis = "TRACK_Z"
    facing.up_axis = "UP_Y"
    return obj


def add_marker(scene, item, material, colour):
    ball = bmesh.new()
    bmesh.ops.create_icosphere(ball, subdivisions=2, radius=item["radius"])
    mesh = bpy.data.meshes.new(item["id"])
    ball.to_mesh(mesh)
    ball.free()
    mesh.materials.append(material)
    obj = bpy.data.objects.new(item["id"], mesh)
    place(scene, obj, item, colour, "marker")
    return obj


def place(scene, obj, item, colour, role):
    obj.location = item["at"]
    # Where it stands is known at once, not only when the scene is next worked out: a marker
    # sized by its distance from the camera measures from its own place the first time already.
    obj.matrix_world = mathutils.Matrix.Translation(item["at"])
    # What the solid view and the draft look show: they draw no glow. Which of the object's and
    # the material's colour they take differs between Blender versions, so both are set.
    obj.color = colour
    scene.collection.objects.link(obj)
    api.stamp(obj, item.get("role", role), item["id"])


def follow(text, marker, marker_at):
    """Makes the text the marker's child. The parent inverse undoes the marker's place, where the
    text's own location was given, so that what moves and scales the marker moves and scales the
    text about the marker's middle."""
    text.parent = marker
    text.matrix_parent_inverse = mathutils.Matrix.Translation(marker_at).inverted()


def keep_pulsing(marker, pulse, frames):
    scale = pulse.get("scale", 1.5)
    marker.scale = (1.0, 1.0, 1.0)
    marker.keyframe_insert(data_path="scale", frame=1)
    marker.scale = (scale, scale, scale)
    marker.keyframe_insert(data_path="scale", frame=1 + frames)
    for curve in api.fcurves(marker):
        curve.modifiers.new(type="CYCLES")


def size_by_distance(marker, camera, radius, sizing, pulse, pulse_frames):
    """A driver scales the marker with its distance from the camera. Its expression holds numbers
    only and is of the kind Blender works out by itself, so it runs without Python being allowed
    to run in the file: in a background render and in a saved scene. A pulse is part of it."""
    expression = "min(%r, max(%r, d * %r)) / %r" % (
        float(sizing["maxRadius"]), float(sizing["minRadius"]), float(sizing["factor"]), float(radius))
    if pulse:
        swing = (pulse.get("scale", 1.5) - 1.0) / 2.0
        expression += " * (%r - %r * cos((frame - 1) * %r))" % (1.0 + swing, swing, 2.0 * math.pi / pulse_frames)
    for axis in range(3):
        driver = marker.driver_add("scale", axis).driver
        driver.type = "SCRIPTED"
        distance = driver.variables.new()
        distance.name = "d"
        distance.type = "LOC_DIFF"
        distance.targets[0].id = camera
        distance.targets[1].id = marker
        driver.expression = expression
