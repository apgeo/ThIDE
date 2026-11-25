# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "changes": what changes while the clip runs. Things appear and go, fade, take a tint,
models are stretched and moved, lights are turned up and down, the world changes its colour and
its strength.

A change names its target, by the id or by the role the other modules stamp on what they make,
and gives each property that changes its keys: at "t" seconds the property has the key's value.
Between two keys it moves as the later key's "easing" says: evenly ("linear", the default), eased
in and out ("smooth": 3x² − 2x³) or not at all until the key's time ("constant"). Before its
first key a property has that key's value, and after its last one it keeps it. A key may lie
after the clip's last frame: the clip then ends on the way to it. A change without a target is of
the scene itself.

    visible        true or false at "t". "fade" is how long, from "t" on, the target takes to
                   appear or to go, evenly or eased ("easing": "smooth"). Before the first key the
                   target is the other way round. A light is shown and hidden, but does not fade.
    opacity        how much of the target is there, from 0 to 1, over what its material lets
                   through
    tint           a "color" laid over the target's own colours by "strength" (0 none, 1 all of
                   it); light and shadow fall on it as before
    scale          a model's stretch along its own axes, as "scale" of the plan's models has it
    offset         how far a model is moved from where the plan's models put it, in metres
    energy         how strong a light is, as "energy" of the look's lights has it
    worldColor     the colour of the world (no target)
    worldStrength  the strength of the world (no target)

Two keys of a property may fall on one frame: the first is where the value was going, the second
(its easing is "constant") the value it has from that frame on. So a move can end in a jump
without the frame before it showing anything but the move.

A colour moves from key to key as light mixes (straight in the plan's linear RGB), or, for a key
with "mix": "srgb", as a screen mixes two colours (straight in sRGB): half-way from red to green
is then the dull yellow a picture editor shows, not the bright one of two lamps. Such a move is
keyed frame by frame.

Nothing here touches the size of a text or a marker, so a marker goes on pulsing, and keeps the
size its distance from the camera gives it, while it fades. Showing and hiding is the object's
own switch; a fade, an opacity and a tint are numbers on the object (custom properties, keyed)
that its material reads, so objects that share a material change one by one. Every curve is one
Blender works out by itself: a saved scene needs no Python to play them.

Runs after the modules that make what it changes: "look", "textured_materials", "labels". The
draft look (Blender's solid view) draws no material. There a model shows its tint in the colours
the draft gives it, and shows how much of it is there by that share of its faces; of a text or a
marker a fade is a cut, and an opacity or a tint is not shown.
"""

import bpy

from thide_player import api

SECTION = "changes"
KEYS = [{
    "target": {"id": None, "role": None},
    "visible": [{"t": None, "value": None, "fade": None, "easing": None}],
    "opacity": [{"t": None, "value": None, "easing": None}],
    "tint": [{"t": None, "color": None, "strength": None, "easing": None, "mix": None}],
    "scale": [{"t": None, "value": None, "easing": None}],
    "offset": [{"t": None, "value": None, "easing": None}],
    "energy": [{"t": None, "value": None, "easing": None}],
    "worldColor": [{"t": None, "value": None, "easing": None, "mix": None}],
    "worldStrength": [{"t": None, "value": None, "easing": None}],
}]

# What a target can change, and what the scene itself can.
OF_A_TARGET = ("visible", "opacity", "tint", "scale", "offset", "energy")
OF_THE_SCENE = ("worldColor", "worldStrength")

EASINGS = ("linear", "smooth", "constant")
FADE_EASINGS = ("linear", "smooth")
MIXES = ("linear", "srgb")

# The numbers on an object that its material reads.
FADE = "thide_fade"                    # 0 to 1 while it appears or goes
OPACITY = "thide_opacity"
TINT = "thide_tint"                    # the colour
TINT_STRENGTH = "thide_tint_strength"

# The nodes a material is given, by name: a material is changed once.
SEE_THROUGH = "ThideSeeThrough"
TINTED = "ThideTint"

# What shows a model's changes in the draft look: a modifier, and the nodes it runs.
DRAFT_CHANGES = "ThideDraftChanges"


def build(ctx, changes):
    if not changes:
        return
    scene = ctx.scene
    taken = {}                       # (object, property) -> the change that has it
    see_through, tinted = set(), set()  # the materials that read the numbers
    faded, coloured = [], []         # the objects whose numbers change
    world = {}                       # the world's tracks, each with where the plan has it
    tracks = 0
    for index, change in enumerate(changes):
        where = "changes[%d]" % index
        named = [name for name in OF_A_TARGET + OF_THE_SCENE if change.get(name) is not None]
        for name in named:
            if not change[name]:
                raise api.PlanError("%s.%s: at least one key is needed" % (where, name))
        tracks += len(named)
        target = change.get("target")
        if target is None:
            for name in named:
                if name not in OF_THE_SCENE:
                    raise api.PlanError('%s.%s: a change of "%s" needs a target' % (where, name, name))
                if name in world:
                    raise api.PlanError('%s.%s: %s changes "%s" already' % (where, name, world[name][1], name))
                world[name] = (change[name], "%s.%s" % (where, name))
            continue

        objects = targets(scene, target, where + ".target")
        for name in named:
            if name not in OF_A_TARGET:
                raise api.PlanError('%s.%s: a change of "%s" has no target' % (where, name, name))
            for obj in objects:
                if (obj.name, name) in taken:
                    raise api.PlanError('%s.%s: %s changes "%s" of "%s" already'
                                        % (where, name, taken[(obj.name, name)], name, obj.name))
                taken[(obj.name, name)] = where
        for obj in objects:
            light = obj.type == "LIGHT"
            for name in named:
                if light and name not in ("visible", "energy"):
                    raise api.PlanError('%s.%s: "%s" is a light; of a light only "visible" and "energy" change'
                                        % (where, name, obj.name))
            if "visible" in named and change_visibility(ctx, obj, change["visible"], where + ".visible"):
                see_through.update(materials_of(obj, where + ".visible"))
                faded.append(obj)
            if "opacity" in named:
                see_through.update(materials_of(obj, where + ".opacity"))
                faded.append(obj)
                key_values(ctx, obj, custom(OPACITY), change["opacity"], where + ".opacity",
                           lambda key, at: unit(key.get("value"), at + ".value"))
            if "tint" in named:
                tinted.update(materials_of(obj, where + ".tint"))
                coloured.append(obj)
                key_values(ctx, obj, custom(TINT), change["tint"], where + ".tint",
                           lambda key, at: colour(key.get("color"), at + ".color"), mixed=True)
                key_values(ctx, obj, custom(TINT_STRENGTH), change["tint"], where + ".tint",
                           lambda key, at: unit(key.get("strength"), at + ".strength"))
            if "scale" in named:
                if not obj.get("thide_model"):
                    raise api.PlanError(
                        '%s.scale: "%s" is not a model; the size of a text or a marker is its own section\'s'
                        % (where, obj.name))
                key_values(ctx, obj, "scale", change["scale"], where + ".scale", stretch)
            if "offset" in named:
                if not obj.get("thide_model"):
                    raise api.PlanError(
                        '%s.offset: "%s" is not a model; where a text or a marker stands is its own section\'s'
                        % (where, obj.name))
                # Beside the place the plan's models give it, which stays as it is.
                key_values(ctx, obj, "delta_location", change["offset"], where + ".offset",
                           lambda key, at: vector(key.get("value"), at + ".value"))
            if "energy" in named:
                if not light:
                    raise api.PlanError('%s.energy: "%s" is not a light' % (where, obj.name))
                key_values(ctx, obj.data, "energy", change["energy"], where + ".energy",
                           lambda key, at: amount(key.get("value"), at + ".value"))

    if world:
        change_world(ctx, world)
    for material in see_through:
        make_see_through(material)
    for material in tinted:
        make_tinted(material)
    # Every object that wears one of these materials has the numbers it reads, changed or not.
    for obj in scene.objects:
        worn = {slot.material for slot in obj.material_slots if slot.material}
        if worn & see_through:
            for name in (FADE, OPACITY):
                if name not in obj:
                    obj[name] = 1.0
        if worn & tinted:
            if TINT not in obj:
                obj[TINT] = [1.0, 1.0, 1.0]
            if TINT_STRENGTH not in obj:
                obj[TINT_STRENGTH] = 0.0
    if scene.render.engine == "BLENDER_WORKBENCH":
        show_in_draft(faded, coloured)
    api.report("changes", tracks)


# ---- targets ----

def targets(scene, target, where):
    """The objects a target names: the one with the id, or every one with the role."""
    if not isinstance(target, dict):
        raise api.PlanError('%s: a target is an "id" or a "role"' % where)
    ident, role = target.get("id"), target.get("role")
    if (ident is None) == (role is None):
        raise api.PlanError('%s: a target is an "id" or a "role"' % where)
    if ident is not None:
        found = [obj for obj in scene.objects if obj.get("thide_id") == ident]
        if not found:
            raise api.PlanError('%s.id: nothing in the scene has the id "%s"' % (where, ident))
    else:
        found = api.with_role(scene, role)
        if not found:
            raise api.PlanError('%s.role: nothing in the scene has the role "%s"' % (where, role))
    return sorted(found, key=lambda obj: obj.name)


def materials_of(obj, where):
    materials = [slot.material for slot in obj.material_slots if slot.material]
    if not materials:
        if obj.type == "LIGHT":
            raise api.PlanError('%s: "%s" is a light and has nothing to fade; key its "energy"' % (where, obj.name))
        raise api.PlanError(
            '%s: "%s" has no material to change; give it one, and list "changes" after the modules that make materials'
            % (where, obj.name))
    return materials


# ---- values ----

def number(value, where):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or value != value:
        raise api.PlanError("%s: a number is needed" % where)
    return float(value)


def unit(value, where):
    value = number(value, where)
    if not 0.0 <= value <= 1.0:
        raise api.PlanError("%s: a number from 0 to 1 is needed" % where)
    return value


def amount(value, where):
    value = number(value, where)
    if value < 0.0 or value == float("inf"):
        raise api.PlanError("%s: a number that is not negative is needed" % where)
    return value


def colour(value, where):
    if not isinstance(value, list) or len(value) != 3:
        raise api.PlanError("%s: a colour is three numbers from 0 to 1" % where)
    return [unit(channel, where) for channel in value]


def vector(value, where):
    if not isinstance(value, list) or len(value) != 3:
        raise api.PlanError("%s: three numbers are needed" % where)
    return [number(part, where) for part in value]


def stretch(key, where):
    value = key.get("value")
    if not isinstance(value, list) or len(value) != 3:
        raise api.PlanError("%s.value: a scale is three positive numbers" % where)
    value = [number(part, where + ".value") for part in value]
    if min(value) <= 0.0:
        raise api.PlanError("%s.value: a scale is three positive numbers" % where)
    return value


def custom(name):
    """The path of a custom property, as an animation curve names it."""
    return '["%s"]' % name


# ---- keys ----

def points_of(ctx, keys, where, value_of, mixed=False):
    """The keys of a track as points: the frame each falls on, its value (value_of gives it,
    checked), how the value is reached from the point before and, for a colour, how it is mixed
    on the way. Two points may share a frame: the second one is a jump."""
    points = []
    for index, key in enumerate(keys):
        at = "%s[%d]" % (where, index)
        frame = ctx.frame_at(number(key.get("t"), at + ".t"))
        if frame < 1 or (points and frame < points[-1][0]):
            raise api.PlanError("%s.t: keys are in rising order of time" % at)
        easing = key.get("easing", "linear")
        if easing not in EASINGS:
            raise api.PlanError('%s.easing: unknown easing "%s"' % (at, easing))
        mix = key.get("mix", "linear") if mixed else "linear"
        if mix not in MIXES:
            raise api.PlanError('%s.mix: unknown mix "%s"' % (at, mix))
        if points and frame == points[-1][0]:
            if len(points) > 1 and points[-2][0] == frame:
                raise api.PlanError("%s.t: no more than two keys fall on one frame" % at)
            if easing != "constant":
                raise api.PlanError(
                    '%s.easing: the second key on a frame is a jump; its easing is "constant"' % at)
        points.append((frame, value_of(key, at), easing, mix))
    return points


def key_values(ctx, owner, path, keys, where, value_of, mixed=False):
    """Keys a property of owner at the times of the plan's keys, eased as they say."""
    insert(owner, owner, path, curve_of(points_of(ctx, keys, where, value_of, mixed), ctx.frame_count))


# How a curve gets from one of its keys to the next: not at all, evenly, or as a Bézier piece
# with these two values at its handles (which lie a third of the way in from either end).
HELD, EVEN = ("held",), ("even",)


def curve_of(points, last_frame):
    """The points of a track as the keys of an animation curve, each on a frame of its own, and
    the moves between them.

    Of two points on one frame the first is only where the value was going: its move is cut one
    frame short, at the value it has there, and the second point is the key of the frame. A
    colour that is mixed as a screen mixes gets a key on every frame of its way, up to
    last_frame, the last frame anyone sees."""
    keys, moves = [], []

    def add(frame, value, move):
        if keys:
            moves.append(move)
        keys.append((frame, value))

    index = 0
    while index < len(points):
        frame, value, easing, mix = points[index]
        jump = points[index + 1] if index + 1 < len(points) and points[index + 1][0] == frame else None
        index += 2 if jump else 1
        if not keys:
            if jump and frame > 1:
                add(frame - 1, value, HELD)
        else:
            start, origin = keys[-1]
            end = frame - 1 if jump else frame
            if easing != "constant" and end > start:
                span = float(frame - start)
                if mix == "srgb" and origin != value:
                    for between in list(range(start + 1, min(end, last_frame + 1))) + [end]:
                        add(between, mixed_on_screen(origin, value, eased((between - start) / span, easing)), EVEN)
                elif easing == "smooth":
                    share = (end - start) / span
                    add(end, between_values(origin, value, eased(share, easing)),
                        ("eased", origin, between_values(origin, value, share * share)))
                else:
                    add(end, between_values(origin, value, (end - start) / span), EVEN)
        if jump:
            add(frame, jump[1], HELD)
        elif not keys or keys[-1][0] != frame:
            add(frame, value, HELD)
    return keys, moves


def eased(share, easing):
    return share * share * (3.0 - 2.0 * share) if easing == "smooth" else share


def between_values(start, end, share):
    if isinstance(start, list):
        return [a + (b - a) * share for a, b in zip(start, end)]
    return start + (end - start) * share


def to_screen(channel):
    return 12.92 * channel if channel <= 0.0031308 else 1.055 * channel ** (1.0 / 2.4) - 0.055


def from_screen(channel):
    return channel / 12.92 if channel <= 0.04045 else ((channel + 0.055) / 1.055) ** 2.4


def mixed_on_screen(start, end, share):
    """A colour between two of the plan's (linear RGB), the share of the way that a screen shows
    between them: straight in sRGB."""
    return [from_screen(to_screen(a) + (to_screen(b) - to_screen(a)) * share) for a, b in zip(start, end)]


def value_at(curve, frame):
    """What a curve of curve_of gives at a frame, as Blender works it out."""
    keys, moves = curve
    if frame <= keys[0][0]:
        return keys[0][1]
    for (start, origin), (end, value), move in zip(keys, keys[1:], moves):
        if frame < end:
            if move is HELD:
                return origin
            share = (frame - start) / float(end - start)
            if move is EVEN:
                return between_values(origin, value, share)
            # A Bézier piece whose handles lie at the thirds of its time is a cubic of the share.
            weights = ((1 - share) ** 3, 3 * share * (1 - share) ** 2, 3 * share * share * (1 - share), share ** 3)
            corners = (origin, move[1], move[2], value)
            if isinstance(origin, list):
                return [sum(w * c[i] for w, c in zip(weights, corners)) for i in range(len(origin))]
            return sum(w * c for w, c in zip(weights, corners))
    return keys[-1][1]


def with_each_value(curve, change):
    """The curve with every value, at the keys and at the handles, put through change."""
    keys, moves = curve
    return ([(frame, change(value)) for frame, value in keys],
            [move if len(move) == 1 else (move[0], change(move[1]), change(move[2])) for move in moves])


def insert(block, owner, path, curve):
    """Keys owner.path as a curve of curve_of says. block is the datablock the animation curves
    belong to: owner itself, or the node tree of a socket."""
    keys, moves = curve
    for frame, value in keys:
        if path.startswith("["):
            owner[path[2:-2]] = value
        else:
            setattr(owner, path, value)
        owner.keyframe_insert(data_path=path, frame=frame)
    full = path if block is owner else owner.path_from_id(path)
    for animated in api.fcurves(block):
        if animated.data_path == full:
            shape(animated, moves)


def shape(animated, moves):
    """Sets how an animation curve moves between its keys. An eased move is a Bézier piece whose
    handles lie a third of the way in from its ends, at the values the move has for them: level
    with the ends they give exactly 3x² − 2x³."""
    keys = sorted(animated.keyframe_points, key=lambda point: point.co.x)
    for left, right, move in zip(keys, keys[1:], moves):
        if move is HELD:
            left.interpolation = "CONSTANT"
        elif move is EVEN:
            left.interpolation = "LINEAR"
        else:
            first, second = move[1], move[2]
            if isinstance(first, list):
                first, second = first[animated.array_index], second[animated.array_index]
            span = right.co.x - left.co.x
            left.interpolation = "BEZIER"
            left.handle_right_type = "FREE"
            right.handle_left_type = "FREE"
            left.handle_right = (left.co.x + span / 3.0, first)
            right.handle_left = (right.co.x - span / 3.0, second)
    if keys:
        keys[-1].interpolation = "CONSTANT"  # it keeps its last value
    animated.update()


# ---- showing and hiding ----

def change_visibility(ctx, obj, keys, where):
    """Shows and hides an object at the keys' times. A key with a fade starts at its time: the
    object takes that long to appear, or to go before it is hidden. Returns whether anything
    fades, which is what needs the material."""
    frames = []
    for index, key in enumerate(keys):
        at = "%s[%d]" % (where, index)
        if not isinstance(key.get("value"), bool):
            raise api.PlanError("%s.value: true or false is needed" % at)
        if index and key["value"] == keys[index - 1]["value"]:
            raise api.PlanError("%s.value: a key says the opposite of the key before it" % at)
        seconds = number(key.get("t"), at + ".t")
        fade = number(key.get("fade", 0.0), at + ".fade")
        if fade < 0.0:
            raise api.PlanError("%s.fade: a fade takes no less than no time" % at)
        easing = key.get("easing", "linear")
        if easing not in FADE_EASINGS:
            raise api.PlanError('%s.easing: a fade is "linear" or "smooth"; without a fade the change is a cut' % at)
        frame = ctx.frame_at(seconds)
        if frame < 1 or (frames and frame <= frames[-1][0]):
            raise api.PlanError("%s.t: keys are in rising order of time, each on a frame of its own" % at)
        frames.append((frame, ctx.frame_at(seconds + fade), easing))
    # A fade that the next key cuts short ends there.
    ends = [min(end, following[0]) for (_, end, _), following in zip(frames, frames[1:] + [(float("inf"), 0, "")])]

    hidden = {1: keys[0]["value"]}  # before the first key: the other way round
    fades = [(1, 1.0, "constant")]
    for key, (frame, _, easing), end in zip(keys, frames, ends):
        if key["value"]:
            hidden[frame] = False
            fades += [(frame, 0.0, "constant"), (end, 1.0, easing)] if end > frame else [(frame, 1.0, "constant")]
        else:
            hidden[end] = True
            if end > frame:
                fades += [(frame, 1.0, "constant"), (end, 0.0, easing)]

    for frame in sorted(hidden):
        for switch in ("hide_render", "hide_viewport"):
            setattr(obj, switch, hidden[frame])
            obj.keyframe_insert(data_path=switch, frame=frame)
    if not any(end > frame for (frame, _, _), end in zip(frames, ends)):
        return False

    # Where two points fall on one frame, the value is the later one's and the way there the
    # earlier one's: a fade that ends where the next one starts still runs to its end.
    points = []
    for frame, value, easing in fades:
        if points and points[-1][0] == frame:
            points[-1] = (frame, value, points[-1][2], "linear")
        else:
            points.append((frame, value, easing, "linear"))
    materials_of(obj, where)  # before anything is keyed on a thing that cannot fade
    insert(obj, obj, custom(FADE), curve_of(points, ctx.frame_count))
    return True


# ---- the world ----

def change_world(ctx, tracks):
    """Keys the world's colour and its strength, and the colour the draft shows for the two."""
    world = ctx.scene.world
    background = None
    if world is not None and world.node_tree is not None:
        background = next((node for node in world.node_tree.nodes if node.type == "BACKGROUND"), None)
    colours = strengths = None
    if "worldColor" in tracks:
        keys, where = tracks["worldColor"]
        if background is None or background.inputs["Color"].is_linked:
            raise api.PlanError(
                '%s: the world has no colour of its own to change; give look.world the kind "color", '
                'and list "changes" after "look" in "features"' % where)
        colours = curve_of(
            points_of(ctx, keys, where, lambda key, at: colour(key.get("value"), at + ".value"), mixed=True), ctx.frame_count)
        insert(world.node_tree, background.inputs["Color"], "default_value",
               with_each_value(colours, lambda value: value + [1.0]))
    if "worldStrength" in tracks:
        keys, where = tracks["worldStrength"]
        if background is None:
            raise api.PlanError(
                '%s: the scene has no world to change the strength of; give the look a world, '
                'and list "changes" after "look" in "features"' % where)
        strengths = curve_of(
            points_of(ctx, keys, where, lambda key, at: amount(key.get("value"), at + ".value")), ctx.frame_count)
        insert(world.node_tree, background.inputs["Strength"], "default_value", strengths)

    # The draft's background, as the "look" module sets it: the colour by the strength.
    if background.inputs["Color"].is_linked:
        return  # a sky: the draft shows one colour for it throughout
    colour_now = list(background.inputs["Color"].default_value[:3])
    strength_now = background.inputs["Strength"].default_value
    if strengths is None:
        insert(world, world, "color",
               with_each_value(colours, lambda value: [min(1.0, channel * strength_now) for channel in value]))
        return
    # A product of two curves is not a curve of the same kind: it gets a key on every frame
    # that is seen.
    first = min(curve[0][0][0] for curve in (colours, strengths) if curve)
    last = max(curve[0][-1][0] for curve in (colours, strengths) if curve)
    shown = []
    for frame in range(min(first, ctx.frame_count), min(last, ctx.frame_count) + 1):
        strength = value_at(strengths, frame)
        value = value_at(colours, frame) if colours else colour_now
        shown.append((frame, [min(1.0, channel * strength) for channel in value]))
    insert(world, world, "color", (shown, [EVEN] * (len(shown) - 1)))


# ---- the draft ----

def show_in_draft(faded, coloured):
    """Shows what it can of the changes in Blender's solid view, which draws no material: a
    model in the draft's colours takes its tint there, and is there by the share of its faces
    that its fade and its opacity leave. A modifier does both, from the numbers on the object;
    nothing runs but Blender's own nodes. Anything else is said once."""
    drafted, left, labels = [], [], False
    for obj in sorted(set(faded) | set(coloured), key=lambda obj: obj.name):
        if not obj.get("thide_model"):
            labels = True
        elif obj.type != "MESH" or obj.data.color_attributes.active_color is None:
            left.append(obj.name)
        else:
            drafted.append(obj)
    if drafted:
        tree, inputs = draft_nodes()
        for obj in drafted:
            modifier = obj.modifiers.new(DRAFT_CHANGES, "NODES")
            modifier.node_group = tree
            modifier[inputs["Colours"]] = obj.data.color_attributes.active_color.name
            for name, start, parts in ((FADE, 1.0, 0), (OPACITY, 1.0, 0), (TINT_STRENGTH, 0.0, 0),
                                       (TINT, (1.0, 1.0, 1.0, 1.0), 3)):
                modifier[inputs[name]] = start
                if name in obj:
                    path = 'modifiers["%s"]["%s"]' % (modifier.name, inputs[name])
                    for part in range(parts) if parts else (None,):
                        follow(obj, path, part, name)
    if labels:
        api.warn("the draft shows a fade of a text or a marker as a cut, and no change of its opacity or tint")
    for name in left:
        api.warn('the draft shows no change of opacity or tint of "%s": it has no colours the solid view shows' % name)


def follow(obj, path, part, name):
    """Makes a number of a modifier follow a number on its object: a driver that only passes a
    value on, which Blender works out without Python."""
    driver = (obj.driver_add(path) if part is None else obj.driver_add(path, part)).driver
    driver.type = "SUM"
    source = driver.variables.new()
    source.type = "SINGLE_PROP"
    source.targets[0].id = obj
    source.targets[0].data_path = custom(name) if part is None else "%s[%d]" % (custom(name), part)


def draft_nodes():
    """The nodes that show a model's changes in the draft, made once, and the names of their
    inputs on a modifier. They mix the tint over the colours the solid view shows, and keep the
    share of the faces that fade times opacity says: which ones is settled by their numbers, so
    a model that goes loses the same faces at every frame."""
    tree = bpy.data.node_groups.get(DRAFT_CHANGES)
    if tree is None:
        tree = bpy.data.node_groups.new(DRAFT_CHANGES, "GeometryNodeTree")
        interface = tree.interface
        interface.new_socket(name="Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
        for name, kind in (("Colours", "NodeSocketString"), (TINT, "NodeSocketColor"), (TINT_STRENGTH, "NodeSocketFloat"),
                           (FADE, "NodeSocketFloat"), (OPACITY, "NodeSocketFloat")):
            interface.new_socket(name=name, in_out="INPUT", socket_type=kind)
        interface.new_socket(name="Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
        nodes, link = tree.nodes, tree.links.new
        given = {socket.name: socket for socket in nodes.new("NodeGroupInput").outputs}
        result = nodes.new("NodeGroupOutput")

        own = nodes.new("GeometryNodeInputNamedAttribute")
        own.data_type = "FLOAT_COLOR"
        mix = nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MIX"
        mix.clamp_factor = True
        store = nodes.new("GeometryNodeStoreNamedAttribute")
        store.data_type = "FLOAT_COLOR"
        store.domain = "POINT"
        link(given["Colours"], own.inputs["Name"])
        link(given["Colours"], store.inputs["Name"])
        link(socket_of(own.outputs, "Attribute"), socket_of(mix.inputs, "A_Color"))
        link(given[TINT], socket_of(mix.inputs, "B_Color"))
        link(given[TINT_STRENGTH], socket_of(mix.inputs, "Factor_Float"))
        link(given["Geometry"], store.inputs["Geometry"])
        link(socket_of(mix.outputs, "Result_Color"), store.inputs["Value"])

        there = nodes.new("ShaderNodeMath")
        there.operation = "MULTIPLY"
        link(given[FADE], there.inputs[0])
        link(given[OPACITY], there.inputs[1])
        lot = nodes.new("FunctionNodeRandomValue")
        lot.data_type = "FLOAT"
        gone = nodes.new("FunctionNodeCompare")
        gone.data_type = "FLOAT"
        gone.operation = "GREATER_EQUAL"
        link(next(socket for socket in lot.outputs if socket.enabled), gone.inputs[0])
        link(there.outputs[0], gone.inputs[1])
        remove = nodes.new("GeometryNodeDeleteGeometry")
        remove.domain = "FACE"
        link(store.outputs["Geometry"], remove.inputs["Geometry"])
        link(gone.outputs[0], remove.inputs["Selection"])
        link(remove.outputs["Geometry"], result.inputs["Geometry"])
    inputs = {item.name: item.identifier for item in tree.interface.items_tree
              if item.item_type == "SOCKET" and item.in_out == "INPUT"}
    return tree, inputs


# ---- materials ----

def object_number(tree, name):
    """A node that gives a number of the object the material is on."""
    node = tree.nodes.new("ShaderNodeAttribute")
    node.attribute_type = "OBJECT"
    node.attribute_name = name
    return node


def socket_of(sockets, identifier):
    return next(socket for socket in sockets if socket.identifier == identifier)


def surface_of(material, what):
    """The input of the material's output that its surface goes into, linked."""
    tree = material.node_tree if material.use_nodes else None
    output = None
    if tree is not None:
        outputs = [node for node in tree.nodes if node.type == "OUTPUT_MATERIAL"]
        output = next((node for node in outputs if node.is_active_output), outputs[0] if outputs else None)
    if output is None or not output.inputs["Surface"].is_linked:
        raise api.PlanError('the material "%s" has no surface to %s' % (material.name, what))
    return output.inputs["Surface"]


def make_see_through(material):
    """Puts the material's surface behind a mix with nothing at all, by fade × opacity of the
    object it is on."""
    surface = surface_of(material, "make see-through")
    tree = material.node_tree
    if SEE_THROUGH in tree.nodes:
        return
    solid = surface.links[0].from_socket
    share = tree.nodes.new("ShaderNodeMath")
    share.operation = "MULTIPLY"
    tree.links.new(socket_of(object_number(tree, FADE).outputs, "Fac"), share.inputs[0])
    tree.links.new(socket_of(object_number(tree, OPACITY).outputs, "Fac"), share.inputs[1])
    nothing = tree.nodes.new("ShaderNodeBsdfTransparent")
    mix = tree.nodes.new("ShaderNodeMixShader")
    mix.name = SEE_THROUGH
    tree.links.new(share.outputs[0], mix.inputs[0])
    tree.links.new(nothing.outputs[0], mix.inputs[1])
    tree.links.new(solid, mix.inputs[2])
    tree.links.new(mix.outputs[0], surface)
    if hasattr(material, "use_transparent_shadow"):
        material.use_transparent_shadow = True  # EEVEE: its shadow fades with it


def make_tinted(material):
    """Lays the object's tint over whatever gives the material's shader its colour."""
    surface = surface_of(material, "tint")
    tree = material.node_tree
    if TINTED in tree.nodes:
        return
    shader = surface.links[0].from_node
    if shader.name == SEE_THROUGH:
        shader = shader.inputs[2].links[0].from_node
    own = shader.inputs.get("Base Color") or shader.inputs.get("Color")
    if own is None:
        raise api.PlanError('the material "%s" has no colour to tint' % material.name)
    mix = tree.nodes.new("ShaderNodeMix")
    mix.name = TINTED
    mix.data_type = "RGBA"
    mix.blend_type = "MIX"
    mix.clamp_factor = True
    under = socket_of(mix.inputs, "A_Color")
    if own.is_linked:
        tree.links.new(own.links[0].from_socket, under)
    else:
        under.default_value = own.default_value
    tree.links.new(socket_of(object_number(tree, TINT_STRENGTH).outputs, "Fac"), socket_of(mix.inputs, "Factor_Float"))
    tree.links.new(socket_of(object_number(tree, TINT).outputs, "Color"), socket_of(mix.inputs, "B_Color"))
    tree.links.new(socket_of(mix.outputs, "Result_Color"), own)
