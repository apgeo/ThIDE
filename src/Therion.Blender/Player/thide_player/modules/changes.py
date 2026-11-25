# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "changes": what changes while the clip runs. Things appear and go, fade, take a tint,
models are stretched and moved and revealed part by part, lights are turned up and down, the
world changes its colour and its strength.

A change names its target, by the id or by the role the other modules stamp on what they make,
and gives each property that changes its keys: at "t" seconds the property has the key's value.
Between two keys it moves as the later key's "easing" says: evenly ("linear", the default), eased
in and out ("smooth": 3x² − 2x³) or not at all until the key's time ("constant"). Before its
first key a property has that key's value, and after its last one it keeps it. A key may lie
after the clip's last frame: the clip then ends on the way to it. A key may also lie before the
clip's first frame ("t" below 0): the clip then begins on the way from it, part of the way through
a move or a fade. A change without a target is of the scene itself.

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
    reveal         the threshold a model or a line is shown by, part by part: the parts whose
                   reveal value lies below it are there, the rest is not. The values are on the
                   vertices of the mesh, one number each, under the name "revealFront" gives
                   ("attribute", by default "reveal": a vertex property of a PLY file, _REVEAL in
                   a GLB file, the "reveal" of a line's strips). "revealFront" also says how the
                   edge looks: "edge" is how far behind the threshold a part is wholly there, and
                   "glow" makes what has just appeared shine in a "color", by "strength", over
                   "width". Edge and width are in the units of the values.
    worldColor     the colour of the world (no target)
    worldStrength  the strength of the world (no target): how strongly it lights the scene, and
                   how bright the camera sees it, unless the two are set apart
    worldBackdrop  how bright the camera sees the world, set apart from how strongly it lights
                   the scene (no target): a sky that stays behind a cave while its light goes
                   out, or one that lights a scene and is not seen. The look's world says the
                   same for the whole clip ("backdrop")
    worldRotation  how far the world's image is turned about the vertical, in degrees, as
                   "rotation" of the look's world has it (no target; a world of the kind "image")

Two keys of a property may fall on one frame: the first is where the value was going, the second
(its easing is "constant") the value it has from that frame on. So a move can end in a jump
without the frame before it showing anything but the move. A third key on the frame (its easing
"constant" too) is the value the move to the next key starts from, which no frame shows: the
frame has the second key's value, and the frame after it is one frame along a move that began at
the third's.

A colour moves from key to key as light mixes (straight in the plan's linear RGB), or, for a key
with "mix": "srgb", as a screen mixes two colours (straight in sRGB): half-way from red to green
is then the dull yellow a picture editor shows, not the bright one of two lamps. Such a move is
keyed frame by frame.

Nothing here touches the size of a text or a marker, so a marker goes on pulsing, and keeps the
size its distance from the camera gives it, while it fades. Showing and hiding is the object's
own switch; a fade, an opacity and a tint are numbers on the object (custom properties, keyed)
that its material reads, so objects that share a material change one by one. Every curve is one
Blender works out by itself: a saved scene needs no Python to play them.

A reveal is a number on the object too (the threshold, keyed) and two things that read it. The
material lets nothing through of what lies beyond the threshold, fades the edge in and makes it
glow. A modifier takes away every face that lies wholly beyond the threshold, so that a ray does
not have to pass through the hundred hidden walls of a cave to reach the one that is there, and
so that the draft, which draws no material, shows the reveal too.

Runs after the modules that make what it changes: "look", "textured_materials", "labels",
"annotations". The draft look (Blender's solid view) draws no material. There a model shows its
tint in the colours the draft gives it, and shows how much of it is there by that share of its
faces; a reveal is a cut along the faces of the mesh, without its soft edge or its glow; of a
text, a marker or a photo a fade is a cut, and an opacity or a tint is not shown.
"""

import math

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
    "worldBackdrop": [{"t": None, "value": None, "easing": None}],
    "worldRotation": [{"t": None, "value": None, "easing": None}],
    "reveal": [{"t": None, "value": None, "easing": None}],
    "revealFront": {"attribute": None, "edge": None, "glow": {"color": None, "strength": None, "width": None}},
}]

# What a target can change, and what the scene itself can.
OF_A_TARGET = ("visible", "opacity", "tint", "scale", "offset", "energy", "reveal")
OF_THE_SCENE = ("worldColor", "worldStrength", "worldBackdrop", "worldRotation")

EASINGS = ("linear", "smooth", "constant")
FADE_EASINGS = ("linear", "smooth")
MIXES = ("linear", "srgb")

# The numbers on an object that its material reads.
FADE = "thide_fade"                    # 0 to 1 while it appears or goes
OPACITY = "thide_opacity"
TINT = "thide_tint"                    # the colour
TINT_STRENGTH = "thide_tint_strength"
REVEAL = "thide_reveal"                # the threshold
REVEAL_EDGE = "thide_reveal_edge"
REVEAL_GLOW = "thide_reveal_glow"      # the colour
REVEAL_GLOW_STRENGTH = "thide_reveal_glow_strength"
REVEAL_GLOW_WIDTH = "thide_reveal_glow_width"

# The mesh attribute a revealed thing's values are kept in, whatever its file calls them, and the
# threshold of a thing that is all there: above every value.
REVEAL_VALUES = "thide_reveal"
REVEAL_ATTRIBUTE = "reveal"
ALL_THERE = 1.0e18

# The nodes a material is given, by name: a material is changed once.
SEE_THROUGH = "ThideSeeThrough"
TINTED = "ThideTint"
REVEALED = "ThideReveal"

# What takes away the faces that lie beyond a reveal's threshold: a modifier, and the nodes it runs.
REVEAL_CUT = "ThideRevealCut"

# What shows a model's changes in the draft look: a modifier, and the nodes it runs.
DRAFT_CHANGES = "ThideDraftChanges"


def build(ctx, changes):
    if not changes:
        return
    scene = ctx.scene
    taken = {}                       # (object, property) -> the change that has it
    see_through, tinted, revealed = set(), set(), set()  # the materials that read the numbers
    faded, coloured, shown = [], [], []  # the objects whose numbers change
    soft = False                     # whether a reveal has an edge or a glow
    world = {}                       # the world's tracks, each with where the plan has it
    tracks = 0
    for index, change in enumerate(changes):
        where = "changes[%d]" % index
        named = [name for name in OF_A_TARGET + OF_THE_SCENE if change.get(name) is not None]
        for name in named:
            if not change[name]:
                raise api.PlanError("%s.%s: at least one key is needed" % (where, name))
        tracks += len(named)
        if change.get("revealFront") is not None and "reveal" not in named:
            raise api.PlanError('%s.revealFront: the edge of a reveal goes with the keys of the reveal ("reveal")' % where)
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
            if "reveal" in named:
                if not obj.get("thide_model") or obj.type != "MESH":
                    raise api.PlanError(
                        '%s.reveal: "%s" is not a model or a line; a text or a marker is shown and hidden as a whole ("visible")'
                        % (where, obj.name))
                front = front_of(change.get("revealFront"), where + ".revealFront")
                keep_values(obj, front["attribute"], where + ".reveal")
                materials = materials_of(obj, where + ".reveal")
                revealed.update(materials)
                see_through.update(materials)
                shown.append(obj)
                soft = soft or front["edge"] > 0.0 or front["glow"] is not None
                glow = front["glow"] or {"color": [1.0, 1.0, 1.0], "strength": 0.0, "width": 0.0}
                obj[REVEAL_EDGE] = front["edge"]
                obj[REVEAL_GLOW] = glow["color"]
                obj[REVEAL_GLOW_STRENGTH] = glow["strength"]
                obj[REVEAL_GLOW_WIDTH] = glow["width"]
                key_values(ctx, obj, custom(REVEAL), change["reveal"], where + ".reveal",
                           lambda key, at: finite(key.get("value"), at + ".value"))

    if world:
        change_world(ctx, world)
    for material in see_through:
        make_see_through(material)
    for material in tinted:
        make_tinted(material)
    for material in revealed:
        make_revealed(material)
    for obj in shown:
        cut_beyond_the_threshold(obj)
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
        if worn & revealed and REVEAL not in obj:
            # All of it is there, with no edge and no glow.
            obj[REVEAL] = ALL_THERE
            obj[REVEAL_EDGE] = 0.0
            obj[REVEAL_GLOW] = [1.0, 1.0, 1.0]
            obj[REVEAL_GLOW_STRENGTH] = 0.0
            obj[REVEAL_GLOW_WIDTH] = 0.0
    if scene.render.engine == "BLENDER_WORKBENCH":
        show_in_draft(faded, coloured)
        if soft:
            api.warn("the draft shows a reveal as a cut along the faces of the mesh, without its soft edge or its glow")
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

# The checks of a key's value, which every module that reads numbers shares.
number, unit, amount, colour, vector = api.number, api.unit, api.amount, api.colour, api.vector


def stretch(key, where):
    value = key.get("value")
    if not isinstance(value, list) or len(value) != 3:
        raise api.PlanError("%s.value: a scale is three positive numbers" % where)
    value = [number(part, where + ".value") for part in value]
    if min(value) <= 0.0:
        raise api.PlanError("%s.value: a scale is three positive numbers" % where)
    return value


def finite(value, where):
    value = number(value, where)
    if value in (float("inf"), float("-inf")):
        raise api.PlanError("%s: a number is needed" % where)
    return value


def front_of(front, where):
    """The edge of a reveal as the plan gives it, checked, with what it leaves out filled in."""
    front = front or {}
    if not isinstance(front, dict):
        raise api.PlanError("%s: the edge of a reveal is an object" % where)
    attribute = front.get("attribute", REVEAL_ATTRIBUTE)
    if not isinstance(attribute, str) or not attribute.strip():
        raise api.PlanError("%s.attribute: the attribute needs a name" % where)
    edge = amount(front.get("edge", 0.0), where + ".edge")
    glow = front.get("glow")
    if glow is not None:
        if not isinstance(glow, dict):
            raise api.PlanError("%s.glow: a glow is an object" % where)
        width = amount(glow.get("width", 1.0), where + ".glow.width")
        if width <= 0.0:
            raise api.PlanError("%s.glow.width: a glow reaches some way behind the threshold" % where)
        glow = {"color": colour(glow.get("color", [1.0, 0.55, 0.15]), where + ".glow.color"),
                "strength": amount(glow.get("strength", 3.0), where + ".glow.strength"), "width": width}
    return {"attribute": attribute, "edge": edge, "glow": glow}


def custom(name):
    """The path of a custom property, as an animation curve names it."""
    return '["%s"]' % name


# ---- keys ----

def points_of(ctx, keys, where, value_of, mixed=False):
    """The keys of a track as points: the frame each falls on, its value (value_of gives it,
    checked), how the value is reached from the point before and, for a colour, how it is mixed
    on the way. Up to three points may share a frame: the second one is a jump, and the third
    the value the way on starts from. A point may lie before the clip's first frame."""
    points = []
    for index, key in enumerate(keys):
        at = "%s[%d]" % (where, index)
        frame = ctx.frame_at(finite(key.get("t"), at + ".t"))
        if points and frame < points[-1][0]:
            raise api.PlanError("%s.t: keys are in rising order of time" % at)
        easing = key.get("easing", "linear")
        if easing not in EASINGS:
            raise api.PlanError('%s.easing: unknown easing "%s"' % (at, easing))
        mix = key.get("mix", "linear") if mixed else "linear"
        if mix not in MIXES:
            raise api.PlanError('%s.mix: unknown mix "%s"' % (at, mix))
        if points and frame == points[-1][0]:
            if len(points) > 2 and points[-3][0] == frame:
                raise api.PlanError("%s.t: no more than three keys fall on one frame" % at)
            if easing != "constant":
                raise api.PlanError(
                    '%s.easing: a later key on a frame is a jump; its easing is "constant"' % at)
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
    frame short, at the value it has there, and the second point is the key of the frame. A third
    point on the frame is where the way on starts from: the frame keeps the second's value, and
    the move to the next key gets a key of its own on the frame after, one frame along the way.
    A colour that is mixed as a screen mixes gets a key on every frame of its way that is seen,
    from the first frame up to last_frame."""
    keys, moves = [], []

    def add(frame, value, move):
        if keys:
            moves.append(move)
        keys.append((frame, value))

    start = origin = None  # where the way on starts: its frame, and the value it starts from
    index = 0
    while index < len(points):
        frame, value, easing, mix = points[index]
        group = [points[index]]
        while index + len(group) < len(points) and points[index + len(group)][0] == frame:
            group.append(points[index + len(group)])
        index += len(group)
        jump = len(group) > 1
        shown = group[1][1] if jump else value
        if not keys:
            if jump and frame > 1:
                add(frame - 1, value, HELD)
        else:
            # The frames that show the way here: from the one after a frame whose own value is
            # not where the way starts, and up to the one before a jump.
            first = start + 1 if keys[-1][1] != origin else start
            end = frame - 1 if jump else frame
            span = float(frame - start)
            if easing == "constant":
                if first > start and first <= end:
                    add(first, origin, HELD)
            elif first <= end and end > start:
                def on_the_way(at):
                    return (at - start) / span

                if mix == "srgb" and origin != value:
                    seen = [at for at in range(first, end) if 1 <= at <= last_frame]
                    for between in ([first] if first > start and first not in seen else []) + seen + [end]:
                        if between > keys[-1][0]:
                            add(between, mixed_on_screen(origin, value, eased(on_the_way(between), easing)),
                                HELD if between == first and first > start else EVEN)
                else:
                    if first > start:
                        add(first, between_values(origin, value, eased(on_the_way(first), easing)), HELD)
                    if end > first:
                        low, high = on_the_way(first), on_the_way(end)
                        if easing != "smooth":
                            add(end, between_values(origin, value, high), EVEN)
                        elif low == 0.0:
                            add(end, between_values(origin, value, eased(high, easing)),
                                ("eased", origin, between_values(origin, value, high * high)))
                        else:
                            # A part of the eased way is a cubic too: its handles lie on its
                            # slopes at either end.
                            third = (high - low) / 3.0
                            add(end, between_values(origin, value, eased(high, easing)),
                                ("eased",
                                 between_values(origin, value, eased(low, easing) + slope(low) * third),
                                 between_values(origin, value, eased(high, easing) - slope(high) * third)))
        if jump:
            add(frame, shown, HELD)
        elif not keys or keys[-1][0] != frame:
            add(frame, value, HELD)
        start, origin = frame, group[2][1] if len(group) > 2 else shown
    return keys, moves


def slope(share):
    """How fast the eased share of a way grows at a share of its time."""
    return 6.0 * share * (1.0 - share)


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
        if frames and frame <= frames[-1][0]:
            raise api.PlanError("%s.t: keys are in rising order of time, each on a frame of its own" % at)
        frames.append((frame, ctx.frame_at(seconds + fade), easing))
    # A fade that the next key cuts short ends there.
    ends = [min(end, following[0]) for (_, end, _), following in zip(frames, frames[1:] + [(float("inf"), 0, "")])]

    # Before the first key: the other way round. A first key before the clip's first frame is
    # a change the clip begins after, or part of the way through.
    before = min(1, frames[0][0])
    hidden = {before: keys[0]["value"]}
    fades = [(before, 1.0, "constant")]
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
    """Keys the world's colour, its strength, how bright it is seen and how far its image is
    turned, and the colour the draft shows for it."""
    from thide_player.modules import look

    world = ctx.scene.world
    background = None
    if world is not None and world.node_tree is not None:
        background = next((node for node in world.node_tree.nodes if node.type == "BACKGROUND"), None)

    def numbers(name, what):
        keys, where = tracks[name]
        if background is None:
            raise api.PlanError(
                '%s: the scene has no world to change %s of; give the look a world, '
                'and list "changes" after "look" in "features"' % (where, what))
        return curve_of(points_of(ctx, keys, where, lambda key, at: amount(key.get("value"), at + ".value")), ctx.frame_count)

    colours = strengths = seen = None
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
    if "worldBackdrop" in tracks:
        seen = numbers("worldBackdrop", "the backdrop")
        look.set_apart(world)  # before the strength is keyed: it is then a number of its own
    apart = world is not None and look.is_set_apart(world)
    if "worldStrength" in tracks:
        strengths = numbers("worldStrength", "the strength")
        if apart:
            insert(world.node_tree, look.strength_of(world, look.WORLD_LIGHT), "default_value", strengths)
        else:
            insert(world.node_tree, background.inputs["Strength"], "default_value", strengths)
    if seen is not None:
        insert(world.node_tree, look.strength_of(world, look.WORLD_SEEN), "default_value", seen)
    if "worldRotation" in tracks:
        keys, where = tracks["worldRotation"]
        mapping = None
        if world is not None and world.node_tree is not None:
            mapping = next((node for node in world.node_tree.nodes if node.type == "MAPPING"), None)
        if mapping is None:
            raise api.PlanError(
                '%s: the world has no image to turn; give look.world the kind "image", '
                'and list "changes" after "look" in "features"' % where)
        turns = curve_of(points_of(ctx, keys, where, lambda key, at: finite(key.get("value"), at + ".value")), ctx.frame_count)
        # Turning the direction the image is looked up by one way turns the image the other way.
        insert(world.node_tree, mapping.inputs["Rotation"], "default_value",
               with_each_value(turns, lambda degrees: [0.0, 0.0, -math.radians(degrees)]))

    # The draft's background, as the "look" module sets it: the colour by the strength the
    # camera sees.
    if background is None or background.inputs["Color"].is_linked:
        return  # a sky or an image: the draft shows one colour for it throughout
    colour_now = list(background.inputs["Color"].default_value[:3])
    shown = seen if apart else strengths
    if apart and seen is None:
        strength_now = look.strength_of(world, look.WORLD_SEEN).default_value
    else:
        strength_now = background.inputs["Strength"].default_value
    if shown is None:
        if colours is not None:
            insert(world, world, "color",
                   with_each_value(colours, lambda value: [min(1.0, channel * strength_now) for channel in value]))
        return
    # A product of two curves is not a curve of the same kind: it gets a key on every frame
    # that is seen.
    first = min(curve[0][0][0] for curve in (colours, shown) if curve)
    last = max(curve[0][-1][0] for curve in (colours, shown) if curve)
    drawn = []
    for frame in range(max(1, min(first, ctx.frame_count)), max(1, min(last, ctx.frame_count)) + 1):
        strength = value_at(shown, frame)
        value = value_at(colours, frame) if colours else colour_now
        drawn.append((frame, [min(1.0, channel * strength) for channel in value]))
    insert(world, world, "color", (drawn, [EVEN] * (len(drawn) - 1)))


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
        api.warn("the draft shows a fade of a text, a marker or a photo as a cut, and no change of its opacity or tint")
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
    # The colour is that of the material's own shader, behind what lets light through it.
    for wrapper in (SEE_THROUGH, api.CUT_OUT):
        if shader.name == wrapper:
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


# ---- a reveal ----

def keep_values(obj, name, where):
    """Keeps the reveal values of a mesh under the one name the material and the modifier read:
    a number for every vertex, from the attribute the plan names. glTF spells an attribute of
    one's own with an underscore and in capitals, so that spelling is looked for too."""
    mesh = obj.data
    source = mesh.attributes.get(name) or mesh.attributes.get("_" + name.upper())
    if source is None:
        raise api.PlanError(
            '%s: "%s" has no reveal values: its mesh has no attribute "%s" (a number for every vertex)'
            % (where, obj.name, name))
    if source.domain != "POINT" or source.data_type not in ("FLOAT", "INT"):
        raise api.PlanError(
            '%s: the attribute "%s" of "%s" is not a number for every vertex' % (where, source.name, obj.name))
    if source.name == REVEAL_VALUES and source.data_type == "FLOAT":
        return
    values = [0.0] * len(mesh.vertices)
    source.data.foreach_get("value", values)
    kept = mesh.attributes.get(REVEAL_VALUES)
    if kept is not None:
        mesh.attributes.remove(kept)
    mesh.attributes.new(REVEAL_VALUES, "FLOAT", "POINT").data.foreach_set("value", values)


def make_revealed(material):
    """Makes the material let nothing through of what lies beyond the threshold of the object it
    is on, fade in over the edge behind it and glow there. How far the threshold is past a place
    is the object's number less the value the mesh has there."""
    surface_of(material, "reveal")
    tree = material.node_tree
    if REVEALED in tree.nodes:
        return
    make_see_through(material)
    see_through = tree.nodes[SEE_THROUGH]
    link = tree.links.new

    def math(operation, first, second, clamp=False):
        node = tree.nodes.new("ShaderNodeMath")
        node.operation = operation
        node.use_clamp = clamp
        for socket, value in zip(node.inputs, (first, second)):
            if isinstance(value, float):
                socket.default_value = value
            else:
                link(value, socket)
        return node.outputs[0]

    def of_the_object(name):
        return socket_of(object_number(tree, name).outputs, "Fac")

    values = tree.nodes.new("ShaderNodeAttribute")
    values.attribute_type = "GEOMETRY"
    values.attribute_name = REVEAL_VALUES
    values.name = REVEALED
    past = math("SUBTRACT", of_the_object(REVEAL), socket_of(values.outputs, "Fac"))

    # There by the share of the edge the threshold is past it: an edge of nothing is a cut.
    there = math("DIVIDE", past, math("MAXIMUM", of_the_object(REVEAL_EDGE), 1.0e-9), clamp=True)
    share = see_through.inputs[0].links[0].from_socket
    link(math("MULTIPLY", share, there), see_through.inputs[0])

    # The glow: all of it at the threshold, none of it a width behind.
    glow = math("SUBTRACT", 1.0, math("DIVIDE", past, math("MAXIMUM", of_the_object(REVEAL_GLOW_WIDTH), 1.0e-9)), clamp=True)
    shine = tree.nodes.new("ShaderNodeEmission")
    link(socket_of(object_number(tree, REVEAL_GLOW).outputs, "Color"), shine.inputs["Color"])
    link(of_the_object(REVEAL_GLOW_STRENGTH), shine.inputs["Strength"])
    lit = tree.nodes.new("ShaderNodeMixShader")
    link(math("MULTIPLY", glow, math("MINIMUM", of_the_object(REVEAL_GLOW_STRENGTH), 1.0)), lit.inputs[0])
    link(see_through.inputs[2].links[0].from_socket, lit.inputs[1])
    link(shine.outputs[0], lit.inputs[2])
    link(lit.outputs[0], see_through.inputs[2])


def cut_beyond_the_threshold(obj):
    """Takes away, at every frame, the faces of a mesh that lie wholly beyond the threshold: a
    modifier whose one number follows the object's. A face with one corner this side of the
    threshold stays, for the material to cut."""
    tree = bpy.data.node_groups.get(REVEAL_CUT)
    if tree is None:
        tree = bpy.data.node_groups.new(REVEAL_CUT, "GeometryNodeTree")
        interface = tree.interface
        interface.new_socket(name="Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
        interface.new_socket(name=REVEAL, in_out="INPUT", socket_type="NodeSocketFloat")
        interface.new_socket(name="Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
        nodes, link = tree.nodes, tree.links.new
        given = {socket.name: socket for socket in nodes.new("NodeGroupInput").outputs}
        result = nodes.new("NodeGroupOutput")

        def enabled(sockets):
            return next(socket for socket in sockets if socket.enabled)

        values = nodes.new("GeometryNodeInputNamedAttribute")
        values.data_type = "FLOAT"
        values.inputs["Name"].default_value = REVEAL_VALUES
        beyond = nodes.new("FunctionNodeCompare")
        beyond.data_type = "FLOAT"
        beyond.operation = "GREATER_EQUAL"
        link(enabled(values.outputs), beyond.inputs[0])
        link(given[REVEAL], beyond.inputs[1])
        # Said of the vertices: a face is beyond the threshold when every corner of it is.
        corners = nodes.new("GeometryNodeFieldOnDomain")
        corners.data_type = "BOOLEAN"
        corners.domain = "POINT"
        link(beyond.outputs[0], enabled(corners.inputs))
        remove = nodes.new("GeometryNodeDeleteGeometry")
        remove.domain = "FACE"
        link(given["Geometry"], remove.inputs["Geometry"])
        link(enabled(corners.outputs), remove.inputs["Selection"])
        link(remove.outputs["Geometry"], result.inputs["Geometry"])
    threshold = next(item.identifier for item in tree.interface.items_tree
                     if item.item_type == "SOCKET" and item.in_out == "INPUT" and item.name == REVEAL)
    modifier = obj.modifiers.new(REVEAL_CUT, "NODES")
    modifier.node_group = tree
    modifier[threshold] = ALL_THERE
    follow(obj, 'modifiers["%s"]["%s"]' % (modifier.name, threshold), None, REVEAL)

