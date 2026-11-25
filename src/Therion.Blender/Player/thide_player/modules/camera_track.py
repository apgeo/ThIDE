# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "camera_track": the camera and its timed poses.

Every pose comes from the plan. Nothing is worked out here but the frame a time falls on; Blender
interpolates between the poses the way the plan says.

A pose turns the camera one of two ways, the same for every pose of a track. Without an
orientation the camera looks at the pose's target and keeps the horizon level. With one (a unit
quaternion w, x, y, z) the camera is turned so, roll included, which also holds where looking at a
target cannot: straight down or straight up. The target is then only where the camera focuses.

A pose may have a focal length and a clip range of its own; a pose without one has the camera's.
The range a camera draws within moves from pose to pose as the camera does: a flight that comes
in from far away to stand a metre from a wall keeps its depth exact at both ends.
"""

import math

import bpy
import mathutils

from thide_player import api

SECTION = "camera"
KEYS = {
    "sensorWidth": None, "lens": None, "clipStart": None, "clipEnd": None, "fStop": None,
    "interpolation": None,
    "track": [{"t": None, "eye": None, "target": None, "lens": None, "orientation": None,
               "clipStart": None, "clipEnd": None}],
}

INTERPOLATIONS = {"linear": "LINEAR", "bezier": "BEZIER", "constant": "CONSTANT"}

# How far the length of an orientation may be from 1.
UNIT_TOLERANCE = 1e-4


def build(ctx, camera):
    if not camera or not camera.get("track"):
        raise api.PlanError("camera.track: at least one pose is needed")
    interpolation = camera.get("interpolation", "linear")
    if interpolation not in INTERPOLATIONS:
        raise api.PlanError('camera.interpolation: unknown interpolation "%s"' % interpolation)
    scene = ctx.scene

    data = bpy.data.cameras.new("Camera")
    data.sensor_fit = "HORIZONTAL"
    data.sensor_width = camera.get("sensorWidth", 36.0)
    data.lens = camera["lens"]
    data.clip_start = camera["clipStart"]
    data.clip_end = camera["clipEnd"]
    cam = bpy.data.objects.new("Camera", data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    api.stamp(cam, "camera", "Camera")

    keys = camera["track"]
    turns = orientations(keys)

    # The target is an empty that has poses of its own. Without orientations the camera looks at
    # it, and tracking keeps the horizon level; with them it is where the camera focuses.
    target = bpy.data.objects.new("CameraTarget", None)
    scene.collection.objects.link(target)
    api.stamp(target, "camera-target", "CameraTarget")
    if turns is None:
        track = cam.constraints.new(type="TRACK_TO")
        track.target = target
        track.track_axis = "TRACK_NEGATIVE_Z"
        track.up_axis = "UP_Y"
    else:
        cam.rotation_mode = "QUATERNION"
    if camera.get("fStop"):
        data.dof.use_dof = True
        data.dof.focus_object = target
        data.dof.aperture_fstop = camera["fStop"]

    moves = len(keys) > 1
    zooms = any("lens" in key for key in keys)
    clips = any("clipStart" in key or "clipEnd" in key for key in keys)
    for index, key in enumerate(keys):
        frame = ctx.frame_at(key["t"])
        cam.location = key["eye"]
        target.location = key["target"]
        if turns is not None:
            cam.rotation_quaternion = turns[index]
        if zooms:
            data.lens = key.get("lens", camera["lens"])
        if clips:
            near, far = key.get("clipStart", camera["clipStart"]), key.get("clipEnd", camera["clipEnd"])
            if not 0.0 < near < far:
                raise api.PlanError(
                    "camera.track[%d]: a pose draws from a positive clipStart to a greater clipEnd" % index)
            data.clip_start = near
            data.clip_end = far
        if moves:
            cam.keyframe_insert(data_path="location", frame=frame)
            target.keyframe_insert(data_path="location", frame=frame)
            if turns is not None:
                cam.keyframe_insert(data_path="rotation_quaternion", frame=frame)
            if zooms:
                data.keyframe_insert(data_path="lens", frame=frame)
            if clips:
                data.keyframe_insert(data_path="clip_start", frame=frame)
                data.keyframe_insert(data_path="clip_end", frame=frame)
    for block in (cam, target, data):
        api.set_interpolation(block, INTERPOLATIONS[interpolation])


def orientations(keys):
    """The turns of the poses as quaternions, or None when the poses have none. Blender
    interpolates a quaternion one number at a time, and q and -q are the same turn: each turn is
    taken as the one of the two nearer the turn before it, so that the camera goes the short
    way round between two poses."""
    first = "orientation" in keys[0]
    odd = next((index for index, key in enumerate(keys) if ("orientation" in key) != first), None)
    if odd is not None:
        raise api.PlanError("camera.track[%d].orientation: either every pose has an orientation or none has" % odd)
    if not first:
        return None
    turns = []
    for index, key in enumerate(keys):
        where = "camera.track[%d].orientation" % index
        value = key["orientation"]
        if not isinstance(value, list) or len(value) != 4 or not all(
                isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) for v in value):
            raise api.PlanError(where + ": four finite numbers are needed (w, x, y, z)")
        turn = mathutils.Quaternion(value)
        if abs(turn.magnitude - 1.0) > UNIT_TOLERANCE:
            raise api.PlanError(where + ": not a unit quaternion (its length is %g)" % turn.magnitude)
        turn.normalize()
        if turns and turn.dot(turns[-1]) < 0:
            turn.negate()
        turns.append(turn)
    return turns
