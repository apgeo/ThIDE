"""The core of the player: read the plan, check it, find its modules, let each play its section.

It knows no feature. What it owns of the plan is what every module shares: the version, the list
of features, the coordinate frame and the timeline.

A render takes two stages when a module of the plan asks for it (something is written over the
picture) or when it is asked for here. The first stage renders the scene as numbered PNG frames
into the frames folder; the second, in a Blender of its own, lays what is to be written over those
frames in the video editor and writes the output. A first stage that was stopped goes on where it
stopped when it is started again: the frames that are there are kept, as long as they were
rendered from the same scene.

Options, after "--" on Blender's command line:
    --plan PATH       the plan to play (default: scene-plan.json beside the script)
    --modules DIR     an extra module folder; may be given more than once
    --build-only      build the scene and stop before rendering
    --stage STAGE     frames: the first stage only; compose: the second stage only, from the
                      frames that are there (a text was changed, the scene was not);
                      all (the default): everything the plan needs
    --frames DIR      the frames folder (default: "frames" beside the plan)
    --keep-frames     keep the frames when the output is written; with it a render takes two
                      stages whatever the plan says
"""

import argparse
import hashlib
import json
import os
import subprocess
import sys
import traceback

import bpy

from . import PLAN_VERSION, PLAYER_VERSION, api
from .registry import Registry

PLAN_FILE = "scene-plan.json"
FRAMES_FOLDER = "frames"
EXIT_FAILED = 64

# The sections the core plays itself; every other section belongs to a module.
CORE_SECTIONS = ("version", "generator", "name", "features", "moduleFolders", "frame", "timeline")

# The keys of the core's own sections (see api.check_keys).
FRAME_KEYS = {"units": None, "upAxis": None, "origin": None, "crs": None}
TIMELINE_KEYS = {"fps": None, "frameCount": None}


def main(plan_dir, plan_text=None, files=None, script=None):
    """Plays the plan and reports a failure the way the caller expects it: one THIDE:error line
    and a non-zero exit code.

    plan_dir    the folder the plan's relative paths start from
    plan_text   the plan, when the script carries it; otherwise it is read from plan_dir
    files       files the script carries, as {plan path: base64 text}
    script      the script Blender was started with; the second stage is started with it again
    """
    try:
        play(plan_dir, plan_text, files, script)
    except api.PlanError as error:
        fail(str(error))
    except Exception as error:
        traceback.print_exc()
        fail("the player failed: %s: %s" % (type(error).__name__, error))


def fail(message):
    print(flush=True)  # Blender may have left a line of its own unfinished
    api.report("error", message)
    sys.exit(EXIT_FAILED)


def play(plan_dir, plan_text=None, files=None, script=None):
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
    check_sections(plan, modules)
    warn_of_unplayed_sections(plan, modules)

    # Two stages when a module needs the second, or when the caller asks for the frames.
    staged = args.stage != "all" or args.keep_frames or any(
        module.two_stage(section_of(plan, module)) for module in modules if hasattr(module, "two_stage"))
    frames_dir = os.path.abspath(args.frames) if args.frames else os.path.join(plan_dir, FRAMES_FOLDER)

    if args.stage != "compose":
        api.report("phase", "scene")
        ctx = api.Context(plan, plan_dir, new_scene(plan), files)
        if staged and not args.build_only:
            ctx.frames = api.Frames(frames_dir, lambda: rendered_from(plan, modules, ctx, ctx.inputs))
        for name, module in zip(plan["features"], modules):
            if hasattr(module, "build"):
                run(name, module.build, ctx, section_of(plan, module))
        if args.build_only:
            api.report("built", "1")
            return
        for name, module in zip(plan["features"], modules):
            if hasattr(module, "render"):
                run(name, module.render, ctx, section_of(plan, module))
        if staged and args.stage == "all" and ctx.frames.count is not None:
            if script is None:
                compose(plan, plan_dir, modules, files, frames_dir, args.keep_frames)
            else:
                # The second stage reports its own end, or its own error.
                compose_in_a_blender_of_its_own(script, args, frames_dir)
                return
    else:
        compose(plan, plan_dir, modules, files, frames_dir, args.keep_frames)
    api.report("done", "1")


def compose(plan, plan_dir, modules, files, frames_dir, keep_frames):
    """The second stage: an empty scene whose video editor gets the frames of the first stage and
    what the modules lay over them, written as the plan's output."""
    api.report("phase", "compose")
    ctx = api.Context(plan, plan_dir, new_scene(plan), files)
    ctx.frames = api.Frames(frames_dir, lambda: rendered_from(plan, modules, ctx, None))
    scene = ctx.scene
    scene.sequence_editor_create()
    # The frames are pictures already: they pass through as they are, with no view transform
    # laid over them a second time.
    api.set_enum(scene.view_settings, "view_transform", ("Standard",))
    try:
        scene.view_settings.look = "None"
    except TypeError:
        pass
    scene.render.use_sequencer = True
    # The picture is as large as the frames are, and keeps their transparency.
    width, height, alpha = ctx.frames.picture()
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = alpha
    for name, module in zip(plan["features"], modules):
        if hasattr(module, "compose"):
            run(name, module.compose, ctx, section_of(plan, module))
    for name, module in zip(plan["features"], modules):
        if hasattr(module, "finish"):
            run(name, module.finish, ctx, section_of(plan, module))
    if keep_frames:
        api.report("frames-kept", frames_dir)
    else:
        ctx.frames.remove()


def compose_in_a_blender_of_its_own(script, args, frames_dir):
    command = [bpy.app.binary_path, "-b", "--factory-startup", "--python-exit-code", str(EXIT_FAILED),
               "--python", script, "--", "--stage", "compose", "--frames", frames_dir]
    if args.plan:
        command += ["--plan", os.path.abspath(args.plan)]
    for folder in args.modules:
        command += ["--modules", os.path.abspath(folder)]
    if args.keep_frames:
        command.append("--keep-frames")
    sys.stdout.flush()
    sys.stderr.flush()
    try:
        code = subprocess.call(command)
    except OSError as error:
        raise api.PlanError("the second stage could not be started: %s" % error)
    if code != 0:
        sys.exit(code)


def rendered_from(plan, modules, ctx, inputs):
    """What the frames of the first stage are rendered from, for the note that is kept with them:
    a digest of every part of the plan that shapes them, and of every file the modules read.
    inputs are the plan paths of those files; None takes those of the note that is there."""
    outside = {"name", "generator"}
    names = set()
    for name, module in zip(plan["features"], modules):
        if getattr(module, "IN_FRAMES", True) is False:
            names.add(name)
            if getattr(module, "SECTION", None):
                outside.add(module.SECTION)
    shaping = {key: value for key, value in plan.items() if key not in outside}
    shaping["features"] = [name for name in plan["features"] if name not in names]
    text = json.dumps(shaping, sort_keys=True, ensure_ascii=True, separators=(",", ":"))
    if inputs is None:
        inputs = list(((ctx.frames.note() if ctx.frames else None) or {}).get("files") or {})
    return {
        "scene": hashlib.sha256(text.encode("ascii")).hexdigest(),
        "files": {name: ctx.digest_of(name) for name in sorted(inputs)},
        "player": PLAYER_VERSION,
        "blender": bpy.app.version_string,
    }


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
    parser.add_argument("--stage", choices=("all", "frames", "compose"), default="all")
    parser.add_argument("--frames")
    parser.add_argument("--keep-frames", action="store_true")
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


def check_sections(plan, modules):
    """Refuses a key that the core or a module does not know, in its own section. A module that
    does not say which keys it knows (KEYS) checks its section itself, or not at all."""
    api.check_keys(plan["frame"], FRAME_KEYS, "frame")
    api.check_keys(plan["timeline"], TIMELINE_KEYS, "timeline")
    for module in modules:
        name, keys = getattr(module, "SECTION", None), getattr(module, "KEYS", None)
        if name and keys is not None and plan.get(name) is not None:
            api.check_keys(plan[name], keys, name)


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
