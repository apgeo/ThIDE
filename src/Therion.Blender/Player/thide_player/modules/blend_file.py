"""Feature "blend_file": saves the scene the plan built as a Blender file.

The file is for editing the scene by hand, and for rendering it later, in parts or on another
machine:

    blender -b scene.blend -s 1 -e 100 -a

It is saved when every module has built its part, whatever the order of "features", and before a
module that is listed after this one renders: list "blend_file" before "output" and the file is
there while the render runs. A scene that is only built (--build-only) is not saved.
"""

import os

import bpy

from thide_player import api

SECTION = "blend"
KEYS = {"path": None}


def build(ctx, blend):
    if not blend:
        return
    name = str(blend.get("path", ""))
    if not name.lower().endswith(".blend"):
        raise api.PlanError('blend.path: "%s" is not the name of a .blend file' % name)
    # A plan may come from anywhere; the file it has saved stays in its own folder.
    if os.path.isabs(name) or not is_inside(ctx.path(name), ctx.plan_dir):
        raise api.PlanError('blend.path: "%s" does not lie in the plan\'s folder' % name)


def render(ctx, blend):
    if not blend:
        return
    path = ctx.path(blend["path"])
    folder = os.path.dirname(path)
    os.makedirs(folder, exist_ok=True)
    scene = ctx.scene

    # The file names the render's output from where the file itself lies ("//"), as long as the
    # output stays in the plan's folder: the folder can then be moved and rendered elsewhere.
    output = scene.render.filepath
    if is_inside(output, ctx.plan_dir):
        scene.render.filepath = bpy.path.relpath(output, start=folder)
    try:
        bpy.ops.wm.save_as_mainfile(filepath=path, copy=True, check_existing=False)
    finally:
        scene.render.filepath = output
    api.report("blend", path)


def is_inside(path, folder):
    if not os.path.isabs(path):
        return False
    try:
        return os.path.relpath(path, folder).split(os.sep)[0] != os.pardir
    except ValueError:  # on another drive
        return False
