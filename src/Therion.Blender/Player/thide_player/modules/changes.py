# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "changes": what changes while the clip runs. Things appear and go, fade, take a tint,
models are stretched, the world changes its colour.

A change names its target, by the id or by the role the other modules stamp on what they make,
and gives each property that changes its keys: at "t" seconds the property has the key's value.
Between two keys it moves as the later key's "easing" says: evenly ("linear", the default), eased
in and out ("smooth": 3x² − 2x³) or not at all until the key's time ("constant"). Before its
first key a property has that key's value, and after its last one it keeps it. A change without a
target is of the scene itself.

    visible     true or false at "t". "fade" is how long, from "t" on, the target takes to appear
                or to go. Before the first key the target is the other way round.
    opacity     how much of the target is there, from 0 to 1, over what its material lets through
    tint        a "color" laid over the target's own colours by "strength" (0 none, 1 all of it);
                light and shadow fall on it as before
    scale       a model's stretch along its own axes, as "scale" of the plan's models has it
    worldColor  the colour of the world (no target)

Nothing here touches the size of a text or a marker, so a marker goes on pulsing, and keeps the
size its distance from the camera gives it, while it fades. Showing and hiding is the object's
own switch; a fade, an opacity and a tint are numbers on the object (custom properties, keyed)
that its material reads, so objects that share a material change one by one. Every curve is one
Blender works out by itself: a saved scene needs no Python to play them.

Runs after the modules that make what it changes: "look", "textured_materials", "labels". The
draft look (Blender's solid view) draws no material: there a fade is a cut, and an opacity or a
tint is not shown.
"""

import bpy

from thide_player import api

SECTION = "changes"
KEYS = [{
    "target": {"id": None, "role": None},
    "visible": [{"t": None, "value": None, "fade": None}],
    "opacity": [{"t": None, "value": None, "easing": None}],
    "tint": [{"t": None, "color": None, "strength": None, "easing": None}],
    "scale": [{"t": None, "value": None, "easing": None}],
    "worldColor": [{"t": None, "value": None, "easing": None}],
}]

# What a target can change, and what the scene itself can.
OF_A_TARGET = ("visible", "opacity", "tint", "scale")
OF_THE_SCENE = ("worldColor",)

EASINGS = ("linear", "smooth", "constant")

# The numbers on an object that its material reads.
FADE = "thide_fade"                    # 0 to 1 while it appears or goes
OPACITY = "thide_opacity"
TINT = "thide_tint"                    # the colour
TINT_STRENGTH = "thide_tint_strength"

# The nodes a material is given, by name: a material is changed once.
SEE_THROUGH = "ThideSeeThrough"
TINTED = "ThideTint"


def build(ctx, changes):
    if not changes:
        return
    scene = ctx.scene
    taken = {}                       # (object, property) -> the change that has it
    see_through, tinted = set(), set()  # the materials that read the numbers
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
            if "worldColor" in named:
                change_world(ctx, change["worldColor"], where + ".worldColor")
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
            if "visible" in named and change_visibility(ctx, obj, change["visible"], where + ".visible"):
                see_through.update(materials_of(obj, where + ".visible"))
            if "opacity" in named:
                see_through.update(materials_of(obj, where + ".opacity"))
                key_values(ctx, obj, custom(OPACITY), change["opacity"], where + ".opacity",
                           lambda key, at: unit(key.get("value"), at + ".value"))
            if "tint" in named:
                tinted.update(materials_of(obj, where + ".tint"))
                key_values(ctx, obj, custom(TINT), change["tint"], where + ".tint",
                           lambda key, at: colour(key.get("color"), at + ".color"))
                key_values(ctx, obj, custom(TINT_STRENGTH), change["tint"], where + ".tint",
                           lambda key, at: unit(key.get("strength"), at + ".strength"))
            if "scale" in named:
                if not obj.get("thide_model"):
                    raise api.PlanError(
                        '%s.scale: "%s" is not a model; the size of a text or a marker is its own section\'s'
                        % (where, obj.name))
                key_values(ctx, obj, "scale", change["scale"], where + ".scale", stretch)

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
    if (see_through or tinted) and scene.render.engine == "BLENDER_WORKBENCH":
        api.warn("the draft shows a fade as a cut, and no change of opacity or tint")
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


def colour(value, where):
    if not isinstance(value, list) or len(value) != 3:
        raise api.PlanError("%s: a colour is three numbers from 0 to 1" % where)
    return [unit(channel, where) for channel in value]


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

def points_of(ctx, keys, where, value_of):
    """The keys of a track as points: the frame each falls on, its value (value_of gives it,
    checked) and how the value is reached from the point before."""
    points = []
    for index, key in enumerate(keys):
        at = "%s[%d]" % (where, index)
        frame = ctx.frame_at(number(key.get("t"), at + ".t"))
        if frame < 1 or (points and frame <= points[-1][0]):
            raise api.PlanError("%s.t: keys are in rising order of time, each on a frame of its own" % at)
        easing = key.get("easing", "linear")
        if easing not in EASINGS:
            raise api.PlanError('%s.easing: unknown easing "%s"' % (at, easing))
        points.append((frame, value_of(key, at), easing))
    return points


def key_values(ctx, owner, path, keys, where, value_of):
    """Keys a property of owner at the times of the plan's keys, eased as they say."""
    insert(owner, owner, path, points_of(ctx, keys, where, value_of))


def insert(block, owner, path, points):
    """Keys owner.path at each point (frame, value, how the value is reached from the point
    before). block is the datablock the curves belong to: owner itself, or the node tree of a
    socket."""
    for frame, value, _ in points:
        if path.startswith("["):
            owner[path[2:-2]] = value
        else:
            setattr(owner, path, value)
        owner.keyframe_insert(data_path=path, frame=frame)
    full = path if block is owner else owner.path_from_id(path)
    for curve in api.fcurves(block):
        if curve.data_path == full:
            ease(curve, [easing for _, _, easing in points])


def ease(curve, easings):
    """Sets how a curve moves between its keys. A smooth move is a Bézier piece whose handles lie
    level with its ends, a third of the way in from each: exactly 3x² − 2x³."""
    keys = sorted(curve.keyframe_points, key=lambda point: point.co.x)
    for left, right, easing in zip(keys, keys[1:], easings[1:]):
        if easing == "smooth":
            span = right.co.x - left.co.x
            left.interpolation = "BEZIER"
            left.handle_right_type = "FREE"
            right.handle_left_type = "FREE"
            left.handle_right = (left.co.x + span / 3.0, left.co.y)
            right.handle_left = (right.co.x - span / 3.0, right.co.y)
        else:
            left.interpolation = "CONSTANT" if easing == "constant" else "LINEAR"
    if keys:
        keys[-1].interpolation = "CONSTANT"  # it keeps its last value
    curve.update()


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
        frame = ctx.frame_at(seconds)
        if frame < 1 or (frames and frame <= frames[-1][0]):
            raise api.PlanError("%s.t: keys are in rising order of time, each on a frame of its own" % at)
        frames.append((frame, ctx.frame_at(seconds + fade)))
    # A fade that the next key cuts short ends there.
    ends = [min(end, following[0]) for (_, end), following in zip(frames, frames[1:] + [(float("inf"), 0)])]

    hidden = {1: keys[0]["value"]}  # before the first key: the other way round
    fades = [(1, 1.0, "constant")]
    for key, (frame, _), end in zip(keys, frames, ends):
        if key["value"]:
            hidden[frame] = False
            fades += [(frame, 0.0, "constant"), (end, 1.0, "linear")] if end > frame else [(frame, 1.0, "constant")]
        else:
            hidden[end] = True
            if end > frame:
                fades += [(frame, 1.0, "constant"), (end, 0.0, "linear")]

    for frame in sorted(hidden):
        for switch in ("hide_render", "hide_viewport"):
            setattr(obj, switch, hidden[frame])
            obj.keyframe_insert(data_path=switch, frame=frame)
    if not any(end > frame for (frame, _), end in zip(frames, ends)):
        return False

    # Where two points fall on one frame, the value is the later one's and the way there the
    # earlier one's: a fade that ends where the next one starts still runs to its end.
    points = []
    for frame, value, easing in fades:
        if points and points[-1][0] == frame:
            points[-1] = (frame, value, points[-1][2])
        else:
            points.append((frame, value, easing))
    insert(obj, obj, custom(FADE), points)
    return True


# ---- the world ----

def change_world(ctx, keys, where):
    world = ctx.scene.world
    background = None
    if world is not None and world.node_tree is not None:
        background = next((node for node in world.node_tree.nodes if node.type == "BACKGROUND"), None)
    if background is None or background.inputs["Color"].is_linked:
        raise api.PlanError(
            '%s: the world has no colour of its own to change; give look.world the kind "color", '
            'and list "changes" after "look" in "features"' % where)
    socket = background.inputs["Color"]
    strength = background.inputs["Strength"].default_value
    points = points_of(ctx, keys, where, lambda key, at: colour(key.get("value"), at + ".value"))
    insert(world.node_tree, socket, "default_value", [(frame, value + [1.0], easing) for frame, value, easing in points])
    # The draft's background, as the "look" module sets it.
    insert(world, world, "color",
           [(frame, [min(1.0, channel * strength) for channel in value], easing) for frame, value, easing in points])


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
