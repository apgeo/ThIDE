# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""What a feature module may use.

A feature module is one Python file named after its feature ("camera_track" is camera_track.py):

    SECTION = "camera"            the plan section it plays; the core passes it to every function
    KEYS = {...}                  optional: every key of the section it knows (see check_keys);
                                  the core refuses a plan with any other before anything is built
    def build(ctx, section)       builds its part of the scene
    def render(ctx, section)      optional; runs after every module has built, and not at all
                                  when the scene is only built (--build-only)

Modules run in the order the plan's "features" lists them, and share one thing: the scene. A module
that needs what another one made finds it there (scene.camera, with_role()). It raises PlanError
when the plan asks for something it cannot do.

A render may take two stages, each in a Blender of its own. The first is the one above, except
that what it renders is the scene as numbered PNG frames in one folder (ctx.frames). The second
starts from an empty scene whose video editor is to hold those frames with whatever is laid over
them, and writes the plan's output. A module takes part in the second stage with:

    def two_stage(section)        optional: true when its section needs the second stage
    def compose(ctx, section)     optional: puts its strips into the video editor
    def finish(ctx, section)      optional; runs after every module has composed
    IN_FRAMES = False             optional: its section changes nothing in the frames of the first
                                  stage, so a change to it alone does not render them again
"""

import atexit
import base64
import hashlib
import json
import math
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


def place(obj, item, where):
    """Places an object as the plan places a model: stretched along its own axes by "scale",
    turned about its own origin by "rotation" and moved by "translation", in that order."""
    import mathutils  # Blender's own: this file is also read where there is no Blender

    obj.location = item.get("translation", (0.0, 0.0, 0.0))
    obj.scale = item.get("scale", (1.0, 1.0, 1.0))
    rotation = item.get("rotation")
    if rotation:
        axis = mathutils.Vector(rotation["axis"])
        if axis.length == 0.0:
            raise PlanError("%s.rotation.axis: an axis needs a direction" % where)
        turn = mathutils.Quaternion(axis.normalized(), math.radians(rotation["degrees"]))
        obj.rotation_euler = turn.to_euler(obj.rotation_mode)


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
    channelbag of its action slot from 4.4. From 4.4 two datablocks may share one action, each
    with a slot of its own (a world and its node tree do), and the action's own list is then
    that of its first slot only: the slot is asked first."""
    data = getattr(idblock, "animation_data", None)
    if not data or not data.action:
        return []
    slot = getattr(data, "action_slot", None)
    if slot is not None:
        curves = []
        try:
            for layer in data.action.layers:
                for strip in layer.strips:
                    bag = strip.channelbag(slot)
                    if bag:
                        curves = list(bag.fcurves) + curves
            return curves
        except Exception:
            pass
    return list(getattr(data.action, "fcurves", None) or [])


def set_interpolation(idblock, mode):
    for curve in fcurves(idblock):
        for point in curve.keyframe_points:
            point.interpolation = mode


def strips(scene):
    """The strips of the scene's video editor. Blender 5.0 calls them "strips"; before that they
    were "sequences"."""
    editor = scene.sequence_editor or scene.sequence_editor_create()
    return editor.strips if hasattr(editor, "strips") else editor.sequences


def effect_strip(scene, name, kind, channel, first, last):
    """A new strip that needs no file (a text, a colour), from frame first to frame last. Blender
    5.0 takes its length, earlier versions the frame after its last."""
    try:
        return strips(scene).new_effect(name=name, type=kind, channel=channel, frame_start=first, length=last - first + 1)
    except TypeError:
        return strips(scene).new_effect(name=name, type=kind, channel=channel, frame_start=first, frame_end=last + 1)


def image_strip(scene, name, path, channel, first):
    """A new strip that shows an image file as large as it is."""
    return strips(scene).new_image(name=name, filepath=path, channel=channel, frame_start=first, fit_method="ORIGINAL")


def scratch_dir():
    """A temporary folder that is removed when Blender exits."""
    path = tempfile.mkdtemp(prefix="thide-player-")
    atexit.register(shutil.rmtree, path, True)
    return path


class Frames:
    """The frames of a render in two stages: numbered PNG files in one folder, which the first
    stage writes and the second reads, with a note of what they were rendered from (frames.json).

    The note is what makes a render safe to continue: frames that are there are kept only when
    they were rendered from the same scene, with the same files, by the same Blender."""

    NOTE = "frames.json"
    NAME = "frame_%04d.png"
    PNG_END = b"\x00\x00\x00\x00IEND\xaeB`\x82"

    def __init__(self, folder, describe):
        self.folder = folder
        self._describe = describe  # what the frames are rendered from, as a dict
        self.count = None

    @property
    def pattern(self):
        """The folder and file name as Blender's output path: it puts the frame's number and
        ".png" in place of the hashes."""
        return os.path.join(self.folder, "frame_####")

    def path(self, number):
        return os.path.join(self.folder, self.NAME % number)

    def note(self):
        try:
            with open(os.path.join(self.folder, self.NOTE), encoding="utf-8") as stream:
                note = json.load(stream)
            return note if isinstance(note, dict) else None
        except (OSError, ValueError):
            return None

    def begin(self, count):
        """Makes the folder ready for the first stage to render count frames into, and returns
        how many of them are there already. Frames of another scene are removed, and so is a
        frame that a render which was stopped left unfinished."""
        self.count = count
        os.makedirs(self.folder, exist_ok=True)
        wanted = dict(self._describe(), frames=count)
        if self.note() != wanted:
            self._remove_frames()
            with open(os.path.join(self.folder, self.NOTE), "w", encoding="utf-8") as stream:
                json.dump(wanted, stream, indent=1, sort_keys=True)
            return 0
        there = 0
        for number in range(1, count + 1):
            path = self.path(number)
            if not os.path.isfile(path):
                continue
            if self._is_whole(path):
                there += 1
            else:
                os.remove(path)
        return there

    def expect(self, count, where):
        """For the second stage: checks that every frame is there and was rendered from this
        scene. Frames without a note are taken as they are: they may come from elsewhere."""
        self.count = count
        note = self.note()
        if note is not None:
            now = self._describe()
            files = {name: digest for name, digest in now.get("files", {}).items() if digest is not None}
            then = {name: digest for name, digest in (note.get("files") or {}).items() if name in files}
            if note.get("scene") != now.get("scene") or then != files:
                raise PlanError(
                    "%s: the frames in %s were rendered from another scene; render them again "
                    "(without --stage compose)" % (where, self.folder))
        missing = [number for number in range(1, count + 1) if not self._is_whole(self.path(number))]
        if missing:
            raise PlanError(
                "%s: %d of %d frames are not in %s (the first is %s); render the frames first "
                "(without --stage compose)"
                % (where, len(missing), count, self.folder, os.path.basename(self.path(missing[0]))))

    def missing(self):
        return [number for number in range(1, self.count + 1) if not self._is_whole(self.path(number))]

    def picture(self):
        """The width and height of the frames, and whether they have an alpha channel: read from
        the first one."""
        try:
            with open(self.path(1), "rb") as stream:
                header = stream.read(26)
        except OSError:
            raise PlanError(
                "there are no frames in %s; render the frames first (without --stage compose)" % self.folder)
        if len(header) < 26 or header[:8] != b"\x89PNG\r\n\x1a\n":
            raise PlanError("%s is not a PNG file" % self.path(1))
        width = int.from_bytes(header[16:20], "big")
        height = int.from_bytes(header[20:24], "big")
        return width, height, header[25] in (4, 6)

    def remove(self):
        """Removes the frames and their note, and the folder when nothing else is in it."""
        self._remove_frames()
        try:
            os.remove(os.path.join(self.folder, self.NOTE))
        except OSError:
            pass
        try:
            os.rmdir(self.folder)
        except OSError:
            pass

    def _remove_frames(self):
        try:
            names = os.listdir(self.folder)
        except OSError:
            return
        for name in names:
            if name.startswith("frame_") and name.endswith(".png") and name[6:-4].isdigit():
                try:
                    os.remove(os.path.join(self.folder, name))
                except OSError:
                    pass

    def _is_whole(self, path):
        """A frame that was written to its end. A render that is stopped leaves the frame it was
        at empty or cut short."""
        try:
            with open(path, "rb") as stream:
                stream.seek(0, os.SEEK_END)
                if stream.tell() < 20:
                    return False
                stream.seek(-len(self.PNG_END), os.SEEK_END)
                return stream.read() == self.PNG_END
        except OSError:
            return False


class Context:
    """What the core hands every module."""

    def __init__(self, plan, plan_dir, scene, files=None, frames=None):
        self.plan = plan
        self.plan_dir = plan_dir
        self.scene = scene
        self.fps = plan["timeline"]["fps"]
        self.frame_count = plan["timeline"]["frameCount"]
        # The frames of a render in two stages (Frames); None when the render takes one.
        self.frames = frames
        self._files = files or {}
        self._unpacked = {}
        self._unpack_dir = None
        self.inputs = []  # the plan paths of the files the modules have read

    def frame_at(self, seconds):
        """The frame a time of the plan falls on. Time 0 is frame 1."""
        return 1 + int(round(seconds * self.fps))

    def path(self, name):
        """Where a path of the plan leads: plan paths are relative to the plan's folder."""
        return os.path.normpath(os.path.join(self.plan_dir, name))

    def input_file(self, name, where):
        """The file a plan path names, for reading. A single-file script carries its files
        inside it; those are unpacked to a temporary folder that is removed when Blender exits."""
        if name not in self.inputs:
            self.inputs.append(name)
        if name in self._files:
            if name not in self._unpacked:
                if self._unpack_dir is None:
                    self._unpack_dir = scratch_dir()
                path = os.path.join(self._unpack_dir, "%d-%s" % (len(self._unpacked), os.path.basename(name)))
                with open(path, "wb") as stream:
                    stream.write(base64.b64decode(self._files[name]))
                self._unpacked[name] = path
            return self._unpacked[name]
        path = self.path(name)
        if not os.path.isfile(path):
            raise PlanError("%s: file not found: %s" % (where, path))
        return path

    def digest_of(self, name):
        """The SHA-256 of a file the plan names, or None when it is not there."""
        digest = hashlib.sha256()
        if name in self._files:
            digest.update(base64.b64decode(self._files[name]))
            return digest.hexdigest()
        try:
            with open(self.path(name), "rb") as stream:
                for block in iter(lambda: stream.read(1 << 20), b""):
                    digest.update(block)
        except OSError:
            return None
        return digest.hexdigest()
