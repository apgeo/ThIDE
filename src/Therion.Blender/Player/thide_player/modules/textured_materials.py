# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "textured_materials": materials that drape an image over a model and let the scene
show through it: a terrain over a cave, with an aerial picture or a map on it.

Each material is given to the models it names. Its image lies on a model by the model's own UV
map; without an image, or on a model without a UV map, the material has its plain colour. How
solid it is may differ between its two sides: a terrain that is see-through from above and not
there at all from below never comes between the camera and a passage. A model can be kept from
casting shadows, so the sun still lights what lies under it.

A material may also be opened by a cut-away ("cutaway"): a hole straight down through the
models that wear it, so that a terrain shows the cave under it. The hole is everything within
"radius" metres of the cut-away's "lines" (polylines, or single points, in the plan's frame; only
east and north count), measured across the ground. It is soft over its "edge" (metres inside the
radius) and may have a "rim": a band of "width" metres just outside the hole where the terrain
takes a "color" by "strength", so that the opening reads. "open" (0 to 1) is how much of the
radius is open: 0 is no hole, 1 the whole of it, and a change keys it ("cutaway") so that the cut
opens while the clip runs. The mesh stays as it is: the material lets the scene through. How far
every place is from the lines is worked out once, on a grid, and kept in an image inside the scene
(packed, so a saved scene carries it), which the material reads at every point it draws: the same
in Cycles and in EEVEE. The draft look draws no material: there the faces within the hole are
taken away by a modifier, without the soft edge or the rim.

Runs after "models" and "look". The draft look (Blender's solid view) cannot draw one model
see-through: there such a model is left out, and a solid one is drawn in its plain colour.
"""

import math

import bpy

from thide_player import api

SECTION = "texturedMaterials"
KEYS = [{
    "id": None, "models": None, "color": None, "roughness": None, "image": None, "opacity": None,
    "backOpacity": None, "shadow": None,
    "cutaway": {"lines": None, "radius": None, "edge": None, "open": None,
                "rim": {"color": None, "width": None, "strength": None}},
}]

# The number on an object that says how open its cut-away is (0 to 1): keyed by a change.
CUTAWAY = "thide_cutaway"
# The image of the distances from the lines, and the nodes that read it, by name.
CUTAWAY_FIELD = "ThideCutawayField"
CUTAWAY_NODE = "ThideCutaway"
# What takes the faces of the hole away in the draft: a modifier, its nodes, and the distance of
# every vertex from the lines it reads.
CUTAWAY_DRAFT = "ThideCutawayDraft"
CUTAWAY_DISTANCE = "thide_cutaway_distance"
# The grid of distances: at most this many pixels a side, and at least this fine a pixel.
FIELD_SIDE = 2048
FIELD_PIXEL = 0.05

DRAFT_COLOURS = "ThideDraft"  # the colour attribute the draft look shows


def build(ctx, materials):
    scene = ctx.scene
    draft = scene.render.engine == "BLENDER_WORKBENCH"
    models = {obj["thide_id"]: obj for obj in scene.objects if obj.get("thide_model")}
    for index, spec in enumerate(materials or []):
        where = "texturedMaterials[%d]" % index
        image = None
        if spec.get("image"):
            image = bpy.data.images.load(ctx.input_file(spec["image"], where + ".image"))
        cut = None
        if spec.get("cutaway") is not None:
            cut = cutaway_of(spec["cutaway"], where + ".cutaway")
            cut["field"] = distance_field(cut, spec["id"])
        for name in spec.get("models", []):
            obj = models.get(name)
            if obj is None:
                raise api.PlanError('%s.models: there is no model "%s"' % (where, name))
            draped = image is not None and bool(obj.data.uv_layers)
            if image is not None and not draped:
                api.warn('the model "%s" has no UV map; "%s" is drawn in its plain colour' % (name, spec["id"]))
            obj.data.materials.clear()
            obj.data.materials.append(make_material(spec, image if draped else None, cut))
            if spec.get("shadow") is False and hasattr(obj, "visible_shadow"):
                obj.visible_shadow = False
            if cut is not None:
                obj[CUTAWAY] = cut["open"]
            if draft:
                show_in_draft(obj, spec, name)
                if cut is not None and not obj.hide_render:
                    cut_in_draft(obj, cut)


def make_material(spec, image, cut=None):
    material = bpy.data.materials.new(spec["id"])
    colour = rgba(spec["color"])
    material.diffuse_color = colour  # in Blender's solid view
    material.use_nodes = True
    tree = material.node_tree
    shader = next(node for node in tree.nodes if node.type == "BSDF_PRINCIPLED")
    shader.inputs["Roughness"].default_value = spec.get("roughness", 1.0)
    shader.inputs["Base Color"].default_value = colour
    if image is not None:
        texture = tree.nodes.new("ShaderNodeTexImage")
        texture.image = image
        texture.extension = "EXTEND"  # the edge of the image, not its other side, beyond the edge
        coordinates = tree.nodes.new("ShaderNodeTexCoord")
        tree.links.new(coordinates.outputs["UV"], texture.inputs["Vector"])
        tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])

    # alpha = front + (back - front) x backfacing. The same in Cycles and EEVEE, which culling
    # the back faces would not be.
    front = spec.get("opacity", 1.0)
    back = spec.get("backOpacity", front)
    alpha = None
    if front < 1.0 or back < 1.0:
        geometry = tree.nodes.new("ShaderNodeNewGeometry")
        sides = tree.nodes.new("ShaderNodeMath")
        sides.operation = "MULTIPLY_ADD"
        sides.inputs[1].default_value = back - front
        sides.inputs[2].default_value = front
        tree.links.new(geometry.outputs["Backfacing"], sides.inputs[0])
        alpha = sides.outputs["Value"]
    if cut is not None:
        there = cut_out(tree, shader, cut)
        if alpha is not None:
            both = tree.nodes.new("ShaderNodeMath")
            both.operation = "MULTIPLY"
            tree.links.new(alpha, both.inputs[0])
            tree.links.new(there, both.inputs[1])
            alpha = both.outputs[0]
        else:
            alpha = there
        if hasattr(material, "use_transparent_shadow"):
            material.use_transparent_shadow = True  # EEVEE: no shadow of the hole
    if alpha is not None:
        tree.links.new(alpha, shader.inputs["Alpha"])
    return material


def show_in_draft(obj, spec, name):
    if spec.get("opacity", 1.0) < 1.0:
        obj.hide_render = True
        obj.hide_viewport = True
        api.warn('the draft leaves "%s" out: the solid view cannot draw a model see-through' % name)
        return
    mesh = obj.data
    attribute = mesh.color_attributes.new(DRAFT_COLOURS, "FLOAT_COLOR", "POINT")
    attribute.data.foreach_set("color", rgba(spec["color"]) * len(mesh.vertices))
    mesh.color_attributes.active_color = attribute


def rgba(colour):
    return (colour[0], colour[1], colour[2], 1.0)


# ---- the cut-away ----

def cutaway_of(cut, where):
    """The cut-away as the plan gives it, checked, with what it leaves out filled in."""
    if not isinstance(cut, dict):
        raise api.PlanError("%s: a cut-away is an object" % where)
    lines = cut.get("lines")
    if not isinstance(lines, list) or not lines:
        raise api.PlanError("%s.lines: a cut-away needs at least one line (a list of points)" % where)
    points = 0
    checked = []
    for index, line in enumerate(lines):
        at = "%s.lines[%d]" % (where, index)
        if not isinstance(line, list) or not line:
            raise api.PlanError("%s: a line is a list of one point or more" % at)
        checked.append([api.vector(point, "%s[%d]" % (at, k))[:2] for k, point in enumerate(line)])
        points += len(line)
    radius = api.amount(cut.get("radius"), where + ".radius")
    if radius <= 0.0:
        raise api.PlanError("%s.radius: a cut-away reaches some way from its lines" % where)
    edge = api.amount(cut.get("edge", 0.0), where + ".edge")
    rim = cut.get("rim")
    if rim is not None:
        if not isinstance(rim, dict):
            raise api.PlanError("%s.rim: a rim is an object" % where)
        rim = {"color": api.colour(rim.get("color", [0.03, 0.025, 0.02]), where + ".rim.color"),
               "width": api.amount(rim.get("width", 3.0), where + ".rim.width"),
               "strength": api.unit(rim.get("strength", 0.6), where + ".rim.strength")}
        if rim["width"] <= 0.0 or rim["strength"] == 0.0:
            rim = None
    return {"lines": checked, "radius": radius, "edge": edge,
            "open": api.unit(cut.get("open", 1.0), where + ".open"), "rim": rim, "points": points}


def distance_field(cut, name):
    """How far every place near the lines is from them, across the ground, on a grid: an image
    (one float a pixel, the same in its three colours) and where it lies. The grid reaches as far
    from the lines as the hole and its rim do and a pixel more; beyond that a place is as far as
    the reach, which is all the material needs to know."""
    radius, edge = cut["radius"], cut["edge"]
    reach = radius + (cut["rim"]["width"] if cut["rim"] else 0.0)
    xs = [p[0] for line in cut["lines"] for p in line]
    ys = [p[1] for line in cut["lines"] for p in line]
    # A pixel a quarter of the edge (of the radius for a hard edge, and at most that), and coarse
    # enough for the grid to stay within FIELD_SIDE a side.
    pixel = max(min(radius, edge if edge > 0.0 else radius) / 4.0, FIELD_PIXEL)
    span = max(max(xs) - min(xs), max(ys) - min(ys)) + 2.0 * reach
    pixel = max(pixel, span / (FIELD_SIDE - 3))
    x0, y0 = min(xs) - reach - pixel, min(ys) - reach - pixel
    width = int(math.ceil((max(xs) + reach + pixel - x0) / pixel)) + 1
    height = int(math.ceil((max(ys) + reach + pixel - y0) / pixel)) + 1
    far = reach * reach
    best = [far] * (width * height)

    def segments():
        for line in cut["lines"]:
            if len(line) == 1:
                yield line[0], line[0]
            for a, b in zip(line, line[1:]):
                yield a, b

    # Pixel (i, j) has its centre at x0 + (i + 0.5) pixel, y0 + (j + 0.5) pixel, as an image
    # texture reads it; row 0 is the south.
    for (ax, ay), (bx, by) in segments():
        dx, dy = bx - ax, by - ay
        length = dx * dx + dy * dy
        i0 = max(0, int((min(ax, bx) - reach - x0) / pixel))
        i1 = min(width - 1, int((max(ax, bx) + reach - x0) / pixel) + 1)
        j0 = max(0, int((min(ay, by) - reach - y0) / pixel))
        j1 = min(height - 1, int((max(ay, by) + reach - y0) / pixel) + 1)
        for j in range(j0, j1 + 1):
            py = y0 + (j + 0.5) * pixel - ay
            row = j * width
            for i in range(i0, i1 + 1):
                px = x0 + (i + 0.5) * pixel - ax
                if length > 0.0:
                    t = (px * dx + py * dy) / length
                    t = 0.0 if t < 0.0 else 1.0 if t > 1.0 else t
                    ex, ey = px - t * dx, py - t * dy
                else:
                    ex, ey = px, py
                d = ex * ex + ey * ey
                if d < best[row + i]:
                    best[row + i] = d
    pixels = []
    for d in best:
        d = math.sqrt(d)
        pixels.extend((d, d, d, 1.0))
    image = bpy.data.images.new("%s:%s" % (CUTAWAY_FIELD, name), width, height, alpha=False, float_buffer=True,
                                is_data=True)
    image.colorspace_settings.name = "Non-Color"
    image.pixels.foreach_set(pixels)
    # Inside the scene, so that a saved scene carries it; packed as it is, in floats.
    image.pack()
    return {"image": image, "x0": x0, "y0": y0, "pixel": pixel, "width": width, "height": height,
            "reach": reach, "values": best}


def cut_out(tree, shader, cut):
    """The nodes that open the hole: how much of the material is there at the point drawn, from
    0 in the hole to 1 beyond its soft edge; and the rim on the colour of the shader. The point is
    where it lies in the frame, so the hole goes straight down whatever the model's place."""
    field = cut["field"]
    link = tree.links.new

    def math_node(operation, *values, clamp=False):
        node = tree.nodes.new("ShaderNodeMath")
        node.operation = operation
        node.use_clamp = clamp
        for socket, value in zip(node.inputs, values):
            if isinstance(value, (int, float)):
                socket.default_value = value
            else:
                link(value, socket)
        return node.outputs[0]

    def smoothstep(share):
        # share × share × (3 − 2 share), for a share from 0 to 1
        return math_node("MULTIPLY", math_node("MULTIPLY", share, share),
                         math_node("MULTIPLY_ADD", share, -2.0, 3.0))

    where = tree.nodes.new("ShaderNodeNewGeometry")
    onto = tree.nodes.new("ShaderNodeVectorMath")
    onto.operation = "MULTIPLY_ADD"
    side = field["pixel"]
    onto.inputs[1].default_value = (1.0 / (field["width"] * side), 1.0 / (field["height"] * side), 0.0)
    onto.inputs[2].default_value = (-field["x0"] / (field["width"] * side), -field["y0"] / (field["height"] * side), 0.0)
    link(where.outputs["Position"], onto.inputs[0])
    texture = tree.nodes.new("ShaderNodeTexImage")
    texture.name = CUTAWAY_NODE
    texture.image = field["image"]
    texture.interpolation = "Linear"
    texture.extension = "EXTEND"  # beyond the grid every place is as far as its edge: out of reach
    link(onto.outputs["Vector"], texture.inputs["Vector"])
    distance = tree.nodes.new("ShaderNodeSeparateColor")
    link(texture.outputs["Color"], distance.inputs["Color"])
    distance = distance.outputs[0]

    # The radius open now: the object's share of the cut-away's.
    share = tree.nodes.new("ShaderNodeAttribute")
    share.attribute_type = "OBJECT"
    share.attribute_name = CUTAWAY
    opened = math_node("MULTIPLY", share.outputs["Fac"], cut["radius"])
    # There from the inside of the soft edge (the radius less the edge) to the radius, eased:
    # nothing is cut while the radius open is nothing, as every place is farther than that.
    edge = max(cut["edge"], 1.0e-4)
    there = smoothstep(math_node("DIVIDE", math_node("SUBTRACT", distance, math_node("SUBTRACT", opened, edge)),
                                 edge, clamp=True))
    if cut["rim"] is not None:
        rim = cut["rim"]
        own = shader.inputs["Base Color"]
        mix = tree.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MIX"
        mix.clamp_factor = True
        under = next(socket for socket in mix.inputs if socket.identifier == "A_Color")
        if own.is_linked:
            link(own.links[0].from_socket, under)
        else:
            under.default_value = own.default_value
        next(socket for socket in mix.inputs if socket.identifier == "B_Color").default_value = rgba(rim["color"])
        # All of the rim at the hole, none of it a width beyond; it grows in with the opening,
        # so a closed cut leaves no dark line on the ground.
        beyond = math_node("DIVIDE", math_node("SUBTRACT", distance, opened), rim["width"], clamp=True)
        grown = math_node("DIVIDE", opened, max(rim["width"], edge), clamp=True)
        amount = math_node("MULTIPLY", math_node("MULTIPLY", math_node("SUBTRACT", 1.0, smoothstep(beyond)), grown),
                           rim["strength"])
        link(amount, next(socket for socket in mix.inputs if socket.identifier == "Factor_Float"))
        link(next(socket for socket in mix.outputs if socket.identifier == "Result_Color"), own)
    return there


def cut_in_draft(obj, cut):
    """Takes away, in the draft, the faces that lie in the hole: a modifier whose share of the
    radius follows the object's number, over the distance of every vertex from the lines (from
    the grid, where the vertex lies in the frame). The mesh keeps its faces."""
    field = cut["field"]
    mesh = obj.data
    matrix = obj.matrix_basis
    values = []
    for vertex in mesh.vertices:
        at = matrix @ vertex.co
        values.append(sample(field, at.x, at.y))
    kept = mesh.attributes.get(CUTAWAY_DISTANCE)
    if kept is not None:
        mesh.attributes.remove(kept)
    mesh.attributes.new(CUTAWAY_DISTANCE, "FLOAT", "POINT").data.foreach_set("value", values)

    tree = bpy.data.node_groups.get(CUTAWAY_DRAFT)
    if tree is None:
        tree = bpy.data.node_groups.new(CUTAWAY_DRAFT, "GeometryNodeTree")
        interface = tree.interface
        interface.new_socket(name="Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
        interface.new_socket(name="Radius", in_out="INPUT", socket_type="NodeSocketFloat")
        interface.new_socket(name=CUTAWAY, in_out="INPUT", socket_type="NodeSocketFloat")
        interface.new_socket(name="Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
        nodes, link = tree.nodes, tree.links.new
        given = {socket.name: socket for socket in nodes.new("NodeGroupInput").outputs}
        result = nodes.new("NodeGroupOutput")
        values = nodes.new("GeometryNodeInputNamedAttribute")
        values.data_type = "FLOAT"
        values.inputs["Name"].default_value = CUTAWAY_DISTANCE
        opened = nodes.new("ShaderNodeMath")
        opened.operation = "MULTIPLY"
        link(given["Radius"], opened.inputs[0])
        link(given[CUTAWAY], opened.inputs[1])
        inside = nodes.new("FunctionNodeCompare")
        inside.data_type = "FLOAT"
        inside.operation = "LESS_THAN"
        link(next(socket for socket in values.outputs if socket.enabled), inside.inputs[0])
        link(opened.outputs[0], inside.inputs[1])
        # Read on the faces: a face is in the hole when the mean distance of its corners is.
        remove = nodes.new("GeometryNodeDeleteGeometry")
        remove.domain = "FACE"
        link(given["Geometry"], remove.inputs["Geometry"])
        link(inside.outputs[0], remove.inputs["Selection"])
        link(remove.outputs["Geometry"], result.inputs["Geometry"])
    inputs = {item.name: item.identifier for item in tree.interface.items_tree
              if item.item_type == "SOCKET" and item.in_out == "INPUT"}
    modifier = obj.modifiers.new(CUTAWAY_DRAFT, "NODES")
    modifier.node_group = tree
    modifier[inputs["Radius"]] = cut["radius"]
    modifier[inputs[CUTAWAY]] = cut["open"]
    # The share follows the object's number, which a change keys: a driver that only passes it on.
    driver = obj.driver_add('modifiers["%s"]["%s"]' % (modifier.name, inputs[CUTAWAY])).driver
    driver.type = "SUM"
    source = driver.variables.new()
    source.type = "SINGLE_PROP"
    source.targets[0].id = obj
    source.targets[0].data_path = '["%s"]' % CUTAWAY
    api.warn('the draft shows the cut-away of "%s" as a cut along its faces, without its soft edge or its rim'
             % obj.name)


def sample(field, x, y):
    """The distance at a place, as the image texture reads it: between the four nearest pixel
    centres, and as at the edge beyond the grid."""
    u = (x - field["x0"]) / field["pixel"] - 0.5
    v = (y - field["y0"]) / field["pixel"] - 0.5
    width, height, values = field["width"], field["height"], field["values"]
    u = min(max(u, 0.0), width - 1.0)
    v = min(max(v, 0.0), height - 1.0)
    i, j = min(int(u), width - 2), min(int(v), height - 2)
    fu, fv = u - i, v - j

    def at(a, b):
        return math.sqrt(values[b * width + a])

    return ((at(i, j) * (1 - fu) + at(i + 1, j) * fu) * (1 - fv)
            + (at(i, j + 1) * (1 - fu) + at(i + 1, j + 1) * fu) * fv)
