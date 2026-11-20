"""Feature "camera_track": the camera and its timed poses.

Every pose comes from the plan. Nothing is worked out here but the frame a time falls on; Blender
interpolates between the poses the way the plan says.
"""

import bpy

from thide_player import api

SECTION = "camera"
KEYS = {
    "sensorWidth": None, "lens": None, "clipStart": None, "clipEnd": None, "fStop": None,
    "interpolation": None, "track": [{"t": None, "eye": None, "target": None, "lens": None}],
}

INTERPOLATIONS = {"linear": "LINEAR", "bezier": "BEZIER", "constant": "CONSTANT"}


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

    # The camera looks at an empty that has poses of its own. Tracking keeps the horizon level.
    target = bpy.data.objects.new("CameraTarget", None)
    scene.collection.objects.link(target)
    api.stamp(target, "camera-target", "CameraTarget")
    track = cam.constraints.new(type="TRACK_TO")
    track.target = target
    track.track_axis = "TRACK_NEGATIVE_Z"
    track.up_axis = "UP_Y"
    if camera.get("fStop"):
        data.dof.use_dof = True
        data.dof.focus_object = target
        data.dof.aperture_fstop = camera["fStop"]

    keys = camera["track"]
    moves = len(keys) > 1
    zooms = any("lens" in key for key in keys)
    for key in keys:
        frame = ctx.frame_at(key["t"])
        cam.location = key["eye"]
        target.location = key["target"]
        if zooms:
            data.lens = key.get("lens", camera["lens"])
        if moves:
            cam.keyframe_insert(data_path="location", frame=frame)
            target.keyframe_insert(data_path="location", frame=frame)
            if zooms:
                data.keyframe_insert(data_path="lens", frame=frame)
    for block in (cam, target, data):
        api.set_interpolation(block, INTERPOLATIONS[interpolation])
