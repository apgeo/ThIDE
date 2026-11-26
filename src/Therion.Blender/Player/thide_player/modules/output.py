# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "output": what the render writes (a video, numbered frames or one still), and the
render itself, with its progress reported frame by frame. With "render": false the output is set
up and nothing is rendered: the plan is for a scene that is saved and rendered later.

In a render of two stages the first stage writes the scene as PNG frames into the frames folder
instead, and leaves alone the frames that are there already, so a render that was stopped goes on
where it stopped. The second stage puts those frames into the video editor, under whatever other
modules lay over them, and writes the output from there.
"""

import os

import bpy

from thide_player import api

SECTION = "output"
KEYS = {"kind": None, "container": None, "directory": None, "baseName": None, "render": None}
# Where the output goes and what it is called changes no frame of the first stage.
IN_FRAMES = False

# The channel of the video editor the frames lie on (the first of them, when they take more than
# one: api.Frames.channels); everything else lies over them.
FRAMES_CHANNEL = 1

CONTAINERS = {"mp4": ("MPEG4", ".mp4"), "mkv": ("MKV", ".mkv"), "webm": ("WEBM", ".webm")}


def build(ctx, output):
    if not output:
        raise api.PlanError('the plan has no "output"')
    scene = ctx.scene
    directory = ctx.path(output.get("directory", "."))
    base = output["baseName"]
    kind = output.get("kind")
    if kind == "video":
        set_video(scene, output, directory, base)
    elif kind == "frames":
        set_png(scene)
        scene.render.filepath = os.path.join(directory, base + "_####")
    elif kind == "still":
        set_png(scene)
        scene.render.filepath = os.path.join(directory, base + ".png")
    else:
        raise api.PlanError('output.kind: unknown kind "%s"' % kind)


def set_png(scene):
    image = scene.render.image_settings
    if hasattr(image, "media_type"):
        api.set_enum(image, "media_type", ("IMAGE",))
    image.file_format = "PNG"
    image.color_mode = "RGBA" if scene.render.film_transparent else "RGB"


def set_video(scene, output, directory, base):
    container = output.get("container", "mp4")
    if container not in CONTAINERS:
        raise api.PlanError('output.container: unknown container "%s"' % container)
    ffmpeg_format, extension = CONTAINERS[container]
    image = scene.render.image_settings
    # Blender 5 has the movie formats behind a media type. Not every build can encode video.
    if hasattr(image, "media_type"):
        api.set_enum(image, "media_type", ("VIDEO",))
    if api.set_enum(image, "file_format", ("FFMPEG",)) is None:
        api.warn("This Blender build has no FFMPEG video encoder; rendering a PNG frame sequence instead of %s."
                 % (base + extension))
        set_png(scene)
        scene.render.filepath = os.path.join(directory, base + "_####")
        return
    scene.render.ffmpeg.format = ffmpeg_format
    scene.render.ffmpeg.codec = "WEBM" if container == "webm" else "H264"
    scene.render.ffmpeg.constant_rate_factor = "HIGH"
    scene.render.ffmpeg.audio_codec = "NONE"
    scene.render.filepath = os.path.join(directory, base + extension)


def render(ctx, output):
    if not output.get("render", True):
        return
    scene = ctx.scene
    if ctx.frames is not None and ctx.frames.from_other_plans:
        return  # the frames are those of other plans: whoever names them has rendered them
    if scene.camera is None:
        raise api.PlanError('there is no camera to render through; the plan\'s "features" need "camera_track"')
    if ctx.frames is not None:
        render_frames(ctx, output)
        return
    os.makedirs(os.path.dirname(scene.render.filepath), exist_ok=True)
    api.report("phase", "render")
    keep_render_data(ctx, output["kind"] != "still" and ctx.frame_count > 1)
    render_reporting(scene, ctx.frame_count, still=output["kind"] == "still")
    api.report("output", scene.render.filepath)


def render_frames(ctx, output):
    """The first of two stages: the scene as PNG frames in the frames folder. A frame that is
    there is not rendered again; Blender marks the one it is at with an empty file."""
    scene = ctx.scene
    count = 1 if output["kind"] == "still" else ctx.frame_count
    first, last = ctx.frame_range or (1, count)
    there = ctx.frames.begin(count, first, last)
    wanted = last - first + 1
    set_png(scene)
    scene.render.filepath = ctx.frames.pattern
    scene.render.use_file_extension = True
    scene.render.use_overwrite = False
    scene.render.use_placeholder = True
    scene.frame_start, scene.frame_end = first, last
    api.report("phase", "render")
    if ctx.frame_range:
        api.report("frame-range", "%d-%d" % (first, last))
    if there:
        api.report("resumed", "%d/%d" % (there, wanted))
    if there < wanted:
        keep_render_data(ctx, wanted - there > 1)
        render_reporting(scene, count, still=False)
    else:
        api.report("frames", count)
    missing = ctx.frames.missing()
    if missing:
        raise api.PlanError("the render stopped with %d of %d frames to go" % (len(missing), wanted))
    api.report("frames-folder", ctx.frames.folder)


def keep_render_data(ctx, several):
    """Cycles keeps the scene's render data from one frame to the next in a render of several
    frames (it builds what does not move once); the frames are the same pictures."""
    scene = ctx.scene
    keep = several and ctx.persistent_data and scene.render.engine == "CYCLES"
    scene.render.use_persistent_data = keep
    if keep:
        api.report("persistent-data", "1")


def render_reporting(scene, total, still):
    """Renders, and reports every frame as it is written."""
    # Blender has changed what it passes to handlers; take anything.
    def frame_written(*_):
        api.report("frame", "%d/%d" % (bpy.context.scene.frame_current, total))

    def cancelled(*_):
        api.report("render-cancel", "1")

    bpy.app.handlers.render_write.append(frame_written)
    bpy.app.handlers.render_cancel.append(cancelled)
    api.report("frames", total)
    try:
        if still:
            bpy.ops.render.render(write_still=True)
        else:
            bpy.ops.render.render(animation=True)
    finally:
        bpy.app.handlers.render_write.remove(frame_written)
        bpy.app.handlers.render_cancel.remove(cancelled)


# ---- the second stage ----

def compose(ctx, output):
    """Lays the frames of the first stage into the video editor, as large as they are. The scene's
    picture has their size already."""
    if not output:
        raise api.PlanError('the plan has no "output"')
    scene = ctx.scene
    frames = ctx.frames
    count = 1 if output.get("kind") == "still" else ctx.frame_count
    frames.expect(count, "output")
    build(ctx, output)
    frames.lay(scene, FRAMES_CHANNEL)


def finish(ctx, output):
    """Writes the output from the video editor, when every module has put its strips there. A
    scene that goes on after its last frame (a card at the end) shows that frame meanwhile."""
    scene = ctx.scene
    still = output["kind"] == "still"
    if still:
        scene.frame_start = scene.frame_end = 1
    ctx.frames.hold(scene)
    os.makedirs(os.path.dirname(scene.render.filepath), exist_ok=True)
    render_reporting(scene, scene.frame_end, still)
    api.report("output", scene.render.filepath)
