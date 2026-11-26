# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "segments": a movie made of several scenes, one after the other.

The plan of such a movie has no scene of its own. Each of its segments names the plan of a scene
that lies beside it, and the folder its frames have inside the movie's frames folder:

    { "id": "s02", "name": "Main passage", "plan": "scene-plan.s02.json", "frames": "s02",
      "start": 11, "frameCount": 630, "in": { "kind": "dissolve", "seconds": 1 } }

The first stage of the movie is the first stage of every segment's plan, each in a Blender of its
own. A segment whose frames are there, rendered from the same scene, is not rendered again: a
movie that was stopped goes on with the segment it stopped in, and a movie of which one scene was
changed renders that scene again and no other.

The second stage lays the frames of the segments into the video editor at the times the plan gives
them, and joins each to the one before it as its "in" says:

    cut        (or no "in") the segment starts on the frame after the last of the one before
    dip        as a cut, with a colour ("color", black unless it says otherwise) that comes over
               the last frames of the one and goes from the first frames of the other; "seconds"
               is the time of both halves together
    dissolve   the segment starts "seconds" before the other ends: both have frames for that
               time, and the picture goes evenly from the one to the other

Times are seconds of the movie; the plan's "timeline" is the movie's, and the texts of its "texts"
section, in the movie's time, lie over everything.
"""

import os

from thide_player import api

SECTION = "segments"
KEYS = [{"id": None, "name": None, "plan": None, "frames": None, "start": None, "frameCount": None,
         "in": {"kind": None, "seconds": None, "color": None}}]

KINDS = ("cut", "dip", "dissolve")
BLACK = (0.0, 0.0, 0.0)


def two_stage(segments):
    return True


def build(ctx, segments):
    check(ctx, segments)
    if ctx.frames is None:
        api.warn("a movie of several scenes has no scene of its own to build; "
                 "name the plan of one of its segments (--plan)")


def frames(ctx, segments, folder):
    return MovieFrames(ctx, check(ctx, segments), folder)


def render(ctx, segments):
    ctx.frames.render()


def check(ctx, segments):
    """The segments as the plan gives them, checked: each with the path of its plan, the first
    frame it has in the movie ("first"), how many it has ("count") and how it begins ("kind",
    "over": the frames of a dissolve; "out" and "into": the frames of the two halves of a dip)."""
    if not isinstance(segments, list) or not segments:
        raise api.PlanError('"segments" must list the scenes of the movie')
    parts, ids, folders = [], set(), set()
    for index, segment in enumerate(segments):
        where = "segments[%d]" % index
        if not isinstance(segment, dict):
            raise api.PlanError("%s: a segment is an object" % where)
        ident = segment.get("id")
        if not isinstance(ident, str) or not ident or ident in ids:
            raise api.PlanError("%s.id: every segment needs an id of its own" % where)
        ids.add(ident)
        folder = beside(segment.get("frames"), where + ".frames")
        if folder in folders:
            raise api.PlanError("%s.frames: every segment needs a frames folder of its own" % where)
        folders.add(folder)
        count = segment.get("frameCount")
        if isinstance(count, bool) or not isinstance(count, int) or count < 1:
            raise api.PlanError("%s.frameCount must be a whole number of at least 1" % where)
        start = api.number(segment.get("start"), where + ".start")
        if start < 0:
            raise api.PlanError("%s.start: a time is in seconds from the start of the movie, not negative" % where)
        part = {"id": ident, "name": segment.get("name") or ident, "frames": folder, "count": count,
                "plan": ctx.path(beside(segment.get("plan"), where + ".plan")),
                "first": ctx.frame_at(start), "kind": "cut", "over": 0, "out": 0, "into": 0, "colour": BLACK}
        joined(ctx, part, segment.get("in"), parts[-1] if parts else None, where)
        parts.append(part)
    end = parts[-1]["first"] + parts[-1]["count"] - 1
    if end != ctx.frame_count:
        raise api.PlanError(
            "segments: the last segment ends on frame %d and the movie has %d (timeline.frameCount)"
            % (end, ctx.frame_count))
    return parts


def joined(ctx, part, how, before, where):
    """Checks how a segment begins, and that it begins where that puts it."""
    if how is not None:
        if not isinstance(how, dict) or how.get("kind") not in KINDS:
            raise api.PlanError('%s.in.kind: a segment begins with a "cut", a "dip" or a "dissolve"' % where)
        part["kind"] = how["kind"]
    if before is None:
        if part["kind"] != "cut" or part["first"] != 1:
            raise api.PlanError("%s: the first segment starts the movie, on its first frame and with a cut" % where)
        return
    length = 0
    if part["kind"] != "cut":
        length = ctx.frame_at(api.number(how.get("seconds"), where + ".in.seconds")) - 1
        if length < 1:
            raise api.PlanError("%s.in.seconds: a %s takes at least one frame" % (where, part["kind"]))
    if part["kind"] == "dip":
        part["out"], part["into"] = length // 2, length - length // 2
        if how.get("color") is not None:
            part["colour"] = api.colour(how["color"], where + ".in.color")
    elif part["kind"] == "dissolve":
        part["over"] = length
    end = before["first"] + before["count"] - 1
    if part["first"] != end + 1 - part["over"]:
        raise api.PlanError(
            "%s.start: the segment starts on frame %d; after the one before it, it starts on frame %d"
            % (where, part["first"], end + 1 - part["over"]))
    # What joins two segments lies inside both, and clear of what joined the one before.
    taken = before["over"] + before["into"]
    if part["over"] + part["out"] > before["count"] - taken or part["over"] + part["into"] > part["count"]:
        raise api.PlanError("%s.in.seconds: the %s is longer than the segments it joins" % (where, part["kind"]))


def beside(name, where):
    """A path of the plan that stays inside the folder it starts from."""
    if not isinstance(name, str) or not name.strip():
        raise api.PlanError("%s: a path is needed" % where)
    path = os.path.normpath(name)
    if os.path.isabs(name) or path.split(os.sep)[0] == os.pardir or path == os.curdir:
        raise api.PlanError('%s: "%s" is not a path beside the plan' % (where, name))
    return path


class MovieFrames:
    """The frames of a movie of several scenes: one frames folder for every segment, inside the
    movie's. What a module asks of api.Frames in the second stage, it may ask of this."""

    # Two channels for the frames, so that two segments can show at once, and one for the colour
    # of a dip.
    channels = 3
    from_other_plans = True

    def __init__(self, ctx, parts, folder):
        self.folder = folder
        self.count = None
        self._ctx = ctx
        self._parts = parts
        self._frames = None

    def _each(self):
        """Every segment with its frames (api.Frames), which know the segment's plan."""
        if self._frames is None:
            self._frames = []
            for index, part in enumerate(self._parts):
                frames = self._ctx.others.frames(part["plan"], os.path.join(self.folder, part["frames"]))
                timeline = frames.plan["timeline"]
                if timeline["fps"] != self._ctx.fps or timeline["frameCount"] != part["count"]:
                    raise api.PlanError(
                        "segments[%d]: its plan has %d frames at %d fps, and the movie takes %d at %d fps"
                        % (index, timeline["frameCount"], timeline["fps"], part["count"], self._ctx.fps))
                self._frames.append(frames)
        return list(zip(self._parts, self._frames))

    def render(self):
        """The first stage: every segment's, one after the other."""
        for index, part in enumerate(self._parts):
            api.report("segment", "%d/%d %s" % (index + 1, len(self._parts), part["name"]))
            self._ctx.others.render(part["plan"], os.path.join(self.folder, part["frames"]))
        self.count = self._ctx.frame_count

    def expect(self, count, where):
        for index, (part, frames) in enumerate(self._each()):
            frames.expect(part["count"], "segments[%d]" % index)
        self.count = count

    def picture(self):
        sizes = [frames.picture() for _, frames in self._each()]
        for index, size in enumerate(sizes):
            if size[:2] != sizes[0][:2]:
                raise api.PlanError(
                    "segments[%d]: its frames are %dx%d and those of the first segment %dx%d; a movie has one size"
                    % ((index,) + size[:2] + sizes[0][:2]))
        return sizes[0][0], sizes[0][1], any(size[2] for size in sizes)

    def lay(self, scene, channel):
        """Puts the frames of every segment into the video editor, each a strip "Frames.<id>"
        at its time, on two channels in turn, and joins them."""
        from thide_player.modules.texts import as_shown

        strips = []
        for index, (part, frames) in enumerate(self._each()):
            strip = api.image_strip(scene, "Frames." + part["id"], frames.path(1), channel + index % 2, part["first"])
            for number in range(2, part["count"] + 1):
                strip.elements.append(os.path.basename(frames.path(number)))
            # A strip that is partly there lets the one below it through by as much.
            strip.blend_type = "CROSS"
            strips.append(strip)
        for index, part in enumerate(self._parts):
            first = part["first"]
            if part["kind"] == "dissolve":
                # Of the two strips the upper one lets the lower one through: it comes when it
                # is the segment that begins, and goes when it is the one that ends.
                rising = index % 2 == 1
                upper = strips[index] if rising else strips[index - 1]
                key(upper, [(first - 1, 0.0 if rising else 1.0), (first + part["over"], 1.0 if rising else 0.0)])
            elif part["kind"] == "dip":
                strip = api.effect_strip(scene, "Dip." + part["id"], "COLOR", channel + 2,
                                         first - part["out"], first + part["into"] - 1)
                strip.color = as_shown(part["colour"])
                strip.blend_type = "CROSS"
                # The colour alone on the last frame of the one segment and the first of the other.
                keys = [(first - 1, 1.0), (first, 1.0)]
                if part["out"] > 0:
                    keys.insert(0, (first - 1 - part["out"], 0.0))
                keys.append((first + part["into"], 0.0))
                key(strip, keys)
        api.set_interpolation(scene, "LINEAR")

    def hold(self, scene):
        """A movie that goes on after its last frame (a card at the end) shows that frame
        meanwhile."""
        part, frames = self._each()[-1]
        strip = api.strips(scene)["Frames." + part["id"]]
        last = os.path.basename(frames.path(part["count"]))
        for _ in range(scene.frame_end - (part["first"] + len(strip.elements) - 1)):
            strip.elements.append(last)

    def remove(self):
        for _, frames in self._each():
            frames.remove()
        try:
            os.rmdir(self.folder)
        except OSError:
            pass


def key(strip, keys):
    for frame, opacity in keys:
        strip.blend_alpha = opacity
        strip.keyframe_insert("blend_alpha", frame=frame)
