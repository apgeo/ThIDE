# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "labels": texts that turn to face the camera, and glowing balls that mark places.

Where each one stands, how large it is and what it says comes from the plan; nothing is laid out
here. Runs after "camera_track": the texts turn to the camera, and a marker that is sized by its
distance measures that distance from the camera. A text that follows a marker (a lead's caption)
is the marker's child: it grows with the ball and stays where it stands relative to it.
"""

import math

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
        material = api.glowing("LabelText", colour, texts.get("strength", 1.5))
        for index, item in enumerate(texts["items"]):
            text = add_text(ctx.scene, camera, item, material, colour)
            if item.get("follows") is not None:
                followers.append((text, item["follows"], "labels.texts.items[%d].follows" % index))

    if markers.get("items"):
        colour = rgba(markers["color"])
        material = api.glowing("LeadMarker", colour, markers.get("strength", 2.0))  # as the generated scripts name it
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


def add_text(scene, camera, item, material, colour):
    obj = api.text_facing(camera, item["id"], item["text"], item["size"], material)
    api.stand(scene, obj, item["at"], colour, item.get("role", "label"), item["id"])
    return obj


def add_marker(scene, item, material, colour):
    obj = api.ball(item["id"], item["radius"], material)
    api.stand(scene, obj, item["at"], colour, item.get("role", "marker"), item["id"])
    return obj


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
