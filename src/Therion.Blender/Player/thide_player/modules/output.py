"""Feature "output": what the render writes (a video, numbered frames or one still), and the
render itself, with its progress reported frame by frame. With "render": false the output is set
up and nothing is rendered: the plan is for a scene that is saved and rendered later.
"""

import os

import bpy

from thide_player import api

SECTION = "output"

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
    if scene.camera is None:
        raise api.PlanError('there is no camera to render through; the plan\'s "features" need "camera_track"')
    os.makedirs(os.path.dirname(scene.render.filepath), exist_ok=True)
    total = ctx.frame_count

    # Blender has changed what it passes to handlers; take anything.
    def frame_written(*_):
        api.report("frame", "%d/%d" % (bpy.context.scene.frame_current, total))

    def cancelled(*_):
        api.report("render-cancel", "1")

    bpy.app.handlers.render_write.append(frame_written)
    bpy.app.handlers.render_cancel.append(cancelled)
    api.report("phase", "render")
    api.report("frames", total)
    try:
        if output["kind"] == "still":
            bpy.ops.render.render(write_still=True)
        else:
            bpy.ops.render.render(animation=True)
    finally:
        bpy.app.handlers.render_write.remove(frame_written)
        bpy.app.handlers.render_cancel.remove(cancelled)
    api.report("output", scene.render.filepath)
