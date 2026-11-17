"""The core of the player: read the plan, check it, find its modules, let each play its section.

It knows no feature. What it owns of the plan is what every module shares: the version, the list
of features, the coordinate frame and the timeline.

Options, after "--" on Blender's command line:
    --plan PATH       the plan to play (default: scene-plan.json beside the script)
    --modules DIR     an extra module folder; may be given more than once
    --build-only      build the scene and stop before rendering
"""

import argparse
import json
import os
import sys
import traceback

import bpy

from . import PLAN_VERSION, PLAYER_VERSION, api
from .registry import Registry

PLAN_FILE = "scene-plan.json"
EXIT_FAILED = 64

# The sections the core plays itself; every other section belongs to a module.
CORE_SECTIONS = ("version", "generator", "name", "features", "moduleFolders", "frame", "timeline")


def main(plan_dir, plan_text=None, files=None):
    """Plays the plan and reports a failure the way the caller expects it: one THIDE:error line
    and a non-zero exit code.

    plan_dir    the folder the plan's relative paths start from
    plan_text   the plan, when the script carries it; otherwise it is read from plan_dir
    files       files the script carries, as {plan path: base64 text}
    """
    try:
        play(plan_dir, plan_text, files)
    except api.PlanError as error:
        fail(str(error))
    except Exception as error:
        traceback.print_exc()
        fail("the player failed: %s: %s" % (type(error).__name__, error))


def fail(message):
    print(flush=True)  # Blender may have left a line of its own unfinished
    api.report("error", message)
    sys.exit(EXIT_FAILED)


def play(plan_dir, plan_text=None, files=None):
    args = arguments()
    api.report("player", PLAYER_VERSION)
    api.report("blender", ".".join(str(v) for v in bpy.app.version))
    if bpy.app.version < (4, 2, 0):
        raise api.PlanError("Blender 4.2 or newer is needed; this is " + bpy.app.version_string)

    if args.plan or plan_text is None:
        plan_path = os.path.abspath(args.plan or os.path.join(plan_dir, PLAN_FILE))
        plan_dir = os.path.dirname(plan_path)
        try:
            with open(plan_path, encoding="utf-8-sig") as stream:
                plan_text = stream.read()
        except OSError as error:
            raise api.PlanError("the plan cannot be read: %s" % error)
    try:
        plan = json.loads(plan_text)
    except ValueError as error:
        raise api.PlanError("the plan is not valid JSON: %s" % error)
    check(plan)
    api.report("plan-version", plan["version"])

    # Every module is found before anything is built: a plan this player cannot play fails at once.
    folders = [os.path.abspath(folder) for folder in args.modules]
    folders += [folder_beside(plan_dir, folder) for folder in plan.get("moduleFolders", [])]
    registry = Registry(folders)
    modules = [registry.load(name) for name in plan["features"]]
    warn_of_unplayed_sections(plan, modules)

    api.report("phase", "scene")
    ctx = api.Context(plan, plan_dir, new_scene(plan), files)
    for name, module in zip(plan["features"], modules):
        run(name, module.build, ctx, section_of(plan, module))
    if args.build_only:
        api.report("built", "1")
        return
    for name, module in zip(plan["features"], modules):
        if hasattr(module, "render"):
            run(name, module.render, ctx, section_of(plan, module))
    api.report("done", "1")


def run(name, function, ctx, section):
    """Runs one step of a module; a failure that is not a PlanError is reported with its name."""
    try:
        function(ctx, section)
    except api.PlanError:
        raise
    except Exception as error:
        traceback.print_exc()
        raise api.PlanError('the module "%s" failed: %s: %s' % (name, type(error).__name__, error))


def arguments():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="thide_player", allow_abbrev=False)
    parser.add_argument("--plan")
    parser.add_argument("--modules", action="append", default=[])
    parser.add_argument("--build-only", action="store_true")
    # Other scripts of the same Blender run may have options of their own after the "--".
    return parser.parse_known_args(argv)[0]


def check(plan):
    """Checks what the core itself relies on. Each module checks its own section."""
    if not isinstance(plan, dict):
        raise api.PlanError("the plan must be a JSON object")
    version = plan.get("version")
    if not isinstance(version, int) or isinstance(version, bool) or version < 1:
        raise api.PlanError('the plan has no usable "version"')
    if version > PLAN_VERSION:
        raise api.PlanError(
            "the plan is version %d and this player reads up to version %d; use a newer player"
            % (version, PLAN_VERSION))

    features = plan.get("features")
    if not isinstance(features, list) or not features:
        raise api.PlanError('"features" must list the modules the plan needs')
    if len(set(map(str, features))) != len(features):
        raise api.PlanError('"features" names a module twice')
    folders = plan.get("moduleFolders", [])
    if not isinstance(folders, list) or not all(isinstance(folder, str) for folder in folders):
        raise api.PlanError('"moduleFolders" must be a list of folder names')

    frame = plan.get("frame")
    if not isinstance(frame, dict):
        raise api.PlanError('the plan has no "frame"')
    if frame.get("units") != "metres":
        raise api.PlanError('frame.units: only "metres" is understood')
    if frame.get("upAxis") != "Z":
        raise api.PlanError('frame.upAxis: only "Z" is understood (X east, Y north, Z up)')
    if not is_vector(frame.get("origin")):
        raise api.PlanError("frame.origin must be [x, y, z]")

    timeline = plan.get("timeline")
    if not isinstance(timeline, dict):
        raise api.PlanError('the plan has no "timeline"')
    for key in ("fps", "frameCount"):
        value = timeline.get(key)
        if not isinstance(value, int) or isinstance(value, bool) or value < 1:
            raise api.PlanError("timeline.%s must be a whole number of at least 1" % key)


def is_vector(value):
    return (isinstance(value, list) and len(value) == 3
            and all(isinstance(c, (int, float)) and not isinstance(c, bool) for c in value))


def folder_beside(plan_dir, name):
    """A module folder the plan names. A plan is data and may come from anywhere, so it can point
    only at code that lies beside it; any other folder is named by whoever starts Blender."""
    folder = os.path.normpath(os.path.join(plan_dir, name))
    if os.path.isabs(name) or os.path.relpath(folder, plan_dir).split(os.sep)[0] == os.pardir:
        raise api.PlanError('moduleFolders: "%s" is not a folder beside the plan' % name)
    return folder


def section_of(plan, module):
    name = getattr(module, "SECTION", None)
    return plan.get(name) if name else None


def warn_of_unplayed_sections(plan, modules):
    played = {getattr(module, "SECTION", None) for module in modules}
    for name, value in plan.items():
        if name not in CORE_SECTIONS and name not in played and value not in (None, [], {}):
            api.warn('the plan\'s "%s" section is not played: no module in "features" reads it' % name)


def new_scene(plan):
    if bpy.app.background:
        bpy.ops.wm.read_factory_settings(use_empty=True)
    else:
        # In Blender's window, loading the factory file leaves the rest of a start-up script
        # without a context to run operators in. Empty the scene that is open instead.
        bpy.data.batch_remove(list(bpy.data.objects))
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.fps = plan["timeline"]["fps"]
    scene.render.fps_base = 1.0
    scene.frame_start = 1
    scene.frame_end = plan["timeline"]["frameCount"]
    scene.frame_set(1)
    # Where the scene's origin lies in the world, for whoever needs world coordinates back.
    scene["thide_origin"] = [float(c) for c in plan["frame"]["origin"]]
    if plan["frame"].get("crs"):
        scene["thide_crs"] = str(plan["frame"]["crs"])
    return scene
