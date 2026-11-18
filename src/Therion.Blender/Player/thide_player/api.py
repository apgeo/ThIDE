"""What a feature module may use.

A feature module is one Python file named after its feature ("camera_track" is camera_track.py):

    SECTION = "camera"            the plan section it plays; the core passes it to both functions
    KEYS = {...}                  optional: every key of the section it knows (see check_keys);
                                  the core refuses a plan with any other before anything is built
    def build(ctx, section)       builds its part of the scene
    def render(ctx, section)      optional; runs after every module has built, and not at all
                                  when the scene is only built (--build-only)

Modules run in the order the plan's "features" lists them, and share one thing: the scene. A module
that needs what another one made finds it there (scene.camera, with_role()). It raises PlanError
when the plan asks for something it cannot do.
"""

import atexit
import base64
import os
import shutil
import tempfile

from . import PLAYER_VERSION


class PlanError(Exception):
    """The plan cannot be played. The message says which part of it, and why."""


def report(key, value):
    """Prints one line of the THIDE: protocol, which the program that started Blender reads."""
    print("THIDE:" + key + "=" + str(value).replace("\r", " ").replace("\n", " "), flush=True)


def warn(message):
    report("warning", message)


def check_keys(value, keys, where):
    """Refuses a key the player does not know: a plan written for a newer player would otherwise
    be played as if that key were not there. keys describes what may be there: a dict of the known
    keys, each with the keys of its own value (a dict, or a list of one dict for a list of
    objects) or None when its value holds no keys. A value of another type is left to the module
    that reads it."""
    if isinstance(keys, list):
        if isinstance(value, list):
            for index, item in enumerate(value):
                check_keys(item, keys[0], "%s[%d]" % (where, index))
    elif isinstance(keys, dict) and isinstance(value, dict):
        for key, item in value.items():
            if key not in keys:
                raise PlanError("%s.%s is not known to player %s; a newer player may be needed"
                                % (where, key, PLAYER_VERSION))
            if keys[key] is not None:
                check_keys(item, keys[key], where + "." + key)


def stamp(obj, role, ident):
    """Marks an object the player made, so that other modules, tests and people can find it."""
    obj["thide_role"] = role
    obj["thide_id"] = ident


def with_role(scene, role):
    return [obj for obj in scene.objects if obj.get("thide_role") == role]


def set_enum(obj, prop, candidates):
    """Sets obj.prop to the first candidate this Blender accepts and returns it, or None when it
    accepts none. Blender renames enum values between releases, and the values an enum lists can
    include some it refuses to be set to, so assigning is the only probe that can be trusted."""
    for candidate in candidates:
        try:
            setattr(obj, prop, candidate)
            return candidate
        except TypeError:
            continue
    return None


def fcurves(idblock):
    """The animation curves of a datablock: on the action itself up to Blender 4.3, under the
    channelbag of its action slot from 4.4."""
    data = getattr(idblock, "animation_data", None)
    if not data or not data.action:
        return []
    curves = getattr(data.action, "fcurves", None)
    if curves:
        return list(curves)
    curves = []
    try:
        for layer in data.action.layers:
            for strip in layer.strips:
                bag = strip.channelbag(data.action_slot)
                if bag:
                    curves = list(bag.fcurves) + curves
    except Exception:
        return []
    return curves


def set_interpolation(idblock, mode):
    for curve in fcurves(idblock):
        for point in curve.keyframe_points:
            point.interpolation = mode


class Context:
    """What the core hands every module."""

    def __init__(self, plan, plan_dir, scene, files=None):
        self.plan = plan
        self.plan_dir = plan_dir
        self.scene = scene
        self.fps = plan["timeline"]["fps"]
        self.frame_count = plan["timeline"]["frameCount"]
        self._files = files or {}
        self._unpacked = {}
        self._unpack_dir = None

    def frame_at(self, seconds):
        """The frame a time of the plan falls on. Time 0 is frame 1."""
        return 1 + int(round(seconds * self.fps))

    def path(self, name):
        """Where a path of the plan leads: plan paths are relative to the plan's folder."""
        return os.path.normpath(os.path.join(self.plan_dir, name))

    def input_file(self, name, where):
        """The file a plan path names, for reading. A single-file script carries its files
        inside it; those are unpacked to a temporary folder that is removed when Blender exits."""
        if name in self._files:
            if name not in self._unpacked:
                if self._unpack_dir is None:
                    self._unpack_dir = tempfile.mkdtemp(prefix="thide-player-")
                    atexit.register(shutil.rmtree, self._unpack_dir, True)
                path = os.path.join(self._unpack_dir, "%d-%s" % (len(self._unpacked), os.path.basename(name)))
                with open(path, "wb") as stream:
                    stream.write(base64.b64decode(self._files[name]))
                self._unpacked[name] = path
            return self._unpacked[name]
        path = self.path(name)
        if not os.path.isfile(path):
            raise PlanError("%s: file not found: %s" % (where, path))
        return path
