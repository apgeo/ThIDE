# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "annotations": things that point something out in the scene.

Each annotation has a kind and an anchor ("at"), the point it is about:

    label      a text that turns to face the camera, centred on the anchor
    callout    a text that stands "offset" away from the anchor, with a line from the anchor to it
    marker     a glowing ball on the anchor; "symbol" makes it a "pin", a "cone", a "cube", a
               "ring" (turned to the camera) or an "image" (a small picture of "image", turned to
               the camera, 2 x "radius" wide). "sizeBy" ("distance" or "screen") scales it with its
               distance from the camera, as labels.markers.byDistance does
    outline    a line of "width" that lies along "points", drawn as the plan's lines are
    photo      a picture from an image file, centred on the anchor or "offset" away from it (with
               a line to the anchor when "lineWidth" is given). It faces the camera: it stays
               parallel to the picture and upright in it, wherever the camera is and however
               it is turned, so it is always seen as the rectangle it is. With "facing": "fixed"
               it faces along "normal" and stays so. "frame" puts a border about it. It shows
               its image as a screen would, whatever the light, and lights nothing itself.
               Where a PNG image is clear the photo is not there: a picture cut out of its
               background shows the cave behind it. (The draft's mosaic is whole.)

Where each one stands, how large it is and what it says or shows comes from the plan; nothing is
laid out here. With "model" the anchor is a point of that model, in the model's own coordinates:
the annotation goes where the point goes, also while the model is stretched or moved, and is not
stretched or turned itself.

Nothing here says when an annotation is there: it is there throughout, unless a change shows and
hides it. Every object of one annotation has the annotation's id and role on it, so a change that
names either changes the whole of it: its text and its line, its picture and its frame.

Runs after "camera_track" and after the modules that make models, and before "changes". The
draft look (Blender's solid view) draws no image: there a photo is a mosaic of its colours.
"""

import math
import os

import bpy
import mathutils

from thide_player import api

SECTION = "annotations"
KEYS = [{
    "id": None, "role": None, "kind": None, "at": None, "model": None, "text": None, "size": None,
    "radius": None, "offset": None, "lineWidth": None, "color": None, "strength": None, "image": None,
    "width": None, "height": None, "facing": None, "normal": None, "up": None,
    "frame": {"width": None, "color": None},
    "symbol": None, "sizeBy": None, "factor": None, "minRadius": None, "maxRadius": None, "points": None,
}]

# What every annotation says, and what one of each kind may say beside it.
OF_EVERY_KIND = ("id", "role", "kind", "at", "model")
OF_A_KIND = {
    "label": ("text", "size", "color", "strength"),
    "callout": ("text", "size", "offset", "lineWidth", "color", "strength"),
    "marker": ("radius", "color", "strength", "symbol", "image", "sizeBy", "factor", "minRadius", "maxRadius"),
    "outline": ("points", "width", "color", "strength"),
    "photo": ("image", "width", "height", "facing", "normal", "up", "frame", "offset", "lineWidth", "strength"),
}

TEXT_COLOUR = [0.95, 0.93, 0.82]
MARKER_COLOUR = [1.0, 0.25, 0.1]
FRAME_COLOUR = [0.9, 0.9, 0.9]

DRAFT_COLOURS = "ThideDraft"  # the colour attribute the draft look shows
DRAFT_TILES = 48              # of a photo's mosaic, along its longer side

NOWHERE = (0.0, 0.0, 0.0)


def build(ctx, annotations):
    if not annotations:
        return
    scene = ctx.scene
    if scene.camera is None:
        raise api.PlanError('annotations turn to the camera; list "camera_track" before "annotations" in "features"')
    made = Made(ctx, scene.render.engine == "BLENDER_WORKBENCH")
    models = {obj["thide_id"]: obj for obj in scene.objects if obj.get("thide_model")}
    taken = {obj.get("thide_id") for obj in scene.objects}
    for index, item in enumerate(annotations):
        where = "annotations[%d]" % index
        ident = item.get("id")
        if not isinstance(ident, str) or not ident:
            raise api.PlanError("%s.id: an annotation needs an id" % where)
        if ident in taken:
            raise api.PlanError('%s.id: something else in the scene has the id "%s"' % (where, ident))
        taken.add(ident)
        kind = item.get("kind")
        if kind not in OF_A_KIND:
            raise api.PlanError('%s.kind: unknown kind "%s"' % (where, kind))
        for key in item:
            if key not in OF_EVERY_KIND and key not in OF_A_KIND[kind]:
                raise api.PlanError('%s.%s: a %s has no "%s"' % (where, key, kind, key))
        at = mathutils.Vector(api.vector(item.get("at"), where + ".at"))
        anchor = anchor_of(scene, models, item, at, where)
        parts = PARTS[kind](made, item, where)
        for part in parts:
            put(scene, part, at, anchor, item.get("role", kind), ident)
        if kind == "marker" and item.get("sizeBy") is not None:
            size_marker(made, item, parts[0].obj, where)
    if made.mosaics:
        api.warn("the draft shows a photo as a mosaic of its colours, %d tiles along its longer side" % DRAFT_TILES)
    api.report("annotations", len(annotations))


class Part:
    """One object of an annotation: what it is of it ("text", "line", "ball", "picture"), where it
    stands from the anchor, the colour the solid view shows it in, and how it is turned when it
    does not turn to the camera."""

    def __init__(self, obj, name, colour, offset=NOWHERE, turn=None):
        self.obj = obj
        self.name = name
        self.colour = colour
        self.offset = mathutils.Vector(offset)
        self.turn = turn


class Made:
    """What the annotations share: the camera, the materials and the images, each made once."""

    def __init__(self, ctx, draft):
        self.ctx = ctx
        self.camera = ctx.scene.camera
        self.draft = draft
        self.mosaics = 0
        self._glows = {}
        self._images = {}
        self._pictures = {}

    def glow(self, colour, strength):
        key = (tuple(colour), strength)
        if key not in self._glows:
            self._glows[key] = api.glowing("Annotation", rgba(colour), strength)
        return self._glows[key]

    def image(self, name, where):
        if name not in self._images:
            image = bpy.data.images.load(self.ctx.input_file(name, where))
            # Reading the size loads the file: one that is no image has none.
            if image.size[0] < 1 or image.size[1] < 1:
                bpy.data.images.remove(image)
                raise api.PlanError('%s: "%s" cannot be read as an image' % (where, name))
            self._images[name] = image
        return self._images[name]

    def picture(self, name, image, strength):
        key = (name, strength)
        if key not in self._pictures:
            self._pictures[key] = showing(os.path.basename(name), image, strength)
        return self._pictures[key]


def put(scene, part, at, anchor, role, ident):
    """Puts one object of an annotation into the scene: at its place from the anchor, or, when
    the anchor is a point of a model, wherever that point is, at every frame."""
    obj = part.obj
    if anchor is None:
        api.stand(scene, obj, at + part.offset, rgba(part.colour), role, ident)
    else:
        api.stand(scene, obj, part.offset, rgba(part.colour), role, ident)
        follow = obj.constraints.new(type="COPY_LOCATION")
        follow.target = anchor
        follow.use_offset = True  # its own place is where it stands from the anchor
        obj.constraints.move(len(obj.constraints) - 1, 0)  # before it turns to the camera
    if part.turn is not None:
        obj.rotation_euler = part.turn.to_euler(obj.rotation_mode)
    obj["thide_part"] = part.name
    # It points something out; it is not a thing of the cave that a lamp throws a shadow of.
    if hasattr(obj, "visible_shadow"):
        obj.visible_shadow = False


def anchor_of(scene, models, item, at, where):
    """For an annotation of a model: an empty that rides the model at the anchor, which the
    annotation's objects follow. None for a point of the scene."""
    name = item.get("model")
    if name is None:
        return None
    model = models.get(name)
    if model is None:
        raise api.PlanError(
            '%s.model: there is no model or line "%s"; list "annotations" after the modules that make them' % (where, name))
    anchor = bpy.data.objects.new(item["id"] + ".anchor", None)
    anchor.empty_display_size = 0.2
    scene.collection.objects.link(anchor)
    anchor.parent = model
    anchor.location = at  # in the model's own coordinates
    anchor["thide_role"] = "annotation-anchor"
    anchor["thide_annotation"] = item["id"]
    return anchor


# ---- values ----

def rgba(colour):
    return (colour[0], colour[1], colour[2], 1.0)


def positive(value, where):
    value = api.number(value, where)
    if not 0.0 < value < float("inf"):
        raise api.PlanError("%s: a positive number is needed" % where)
    return value


def text_of(item, where):
    text = item.get("text")
    if not isinstance(text, str) or not text:
        raise api.PlanError("%s.text: a %s needs its text" % (where, item["kind"]))
    return text


def glow_of(item, where, colour, strength):
    return (api.colour(item.get("color", colour), where + ".color"),
            api.amount(item.get("strength", strength), where + ".strength"))


def offset_of(item, where, needed):
    """Where an annotation stands from its anchor; None when it stands on it."""
    if "offset" not in item:
        if needed:
            raise api.PlanError("%s.offset: a callout's text stands away from its anchor; it needs an offset" % where)
        return None
    offset = mathutils.Vector(api.vector(item["offset"], where + ".offset"))
    if offset.length <= 1e-9:
        if needed:
            raise api.PlanError("%s.offset: a callout's text stands away from its anchor; its offset is not zero" % where)
        return None
    return offset


# ---- the kinds ----

def label(made, item, where):
    colour, strength = glow_of(item, where, TEXT_COLOUR, 1.5)
    text = api.text_facing(made.camera, item["id"], text_of(item, where), positive(item.get("size"), where + ".size"),
                           made.glow(colour, strength))
    return [Part(text, "text", colour)]


def callout(made, item, where):
    colour, strength = glow_of(item, where, TEXT_COLOUR, 1.5)
    material = made.glow(colour, strength)
    size = positive(item.get("size"), where + ".size")
    offset = offset_of(item, where, needed=True)
    width = positive(item.get("lineWidth", size / 12.0), where + ".lineWidth")
    text = api.text_facing(made.camera, item["id"], text_of(item, where), size, material)
    # The text stands on the end of its line: above it when the line comes up to it.
    above = offset.z >= 0.0
    text.data.align_y = "BOTTOM" if above else "TOP"
    text.data.offset_y = (0.35 if above else -0.35) * size
    return [Part(text, "text", colour, offset), Part(line(item["id"], offset, width, material), "line", colour)]


SYMBOLS = ("ball", "pin", "cone", "cube", "ring", "image")


def marker(made, item, where):
    symbol = item.get("symbol", "ball")
    if symbol not in SYMBOLS:
        raise api.PlanError('%s.symbol: unknown symbol "%s"' % (where, symbol))
    if symbol != "image" and "image" in item:
        raise api.PlanError('%s.image: only a marker whose symbol is "image" has one' % where)
    radius = positive(item.get("radius"), where + ".radius")
    if symbol == "image":
        # A small picture turned to the camera, as a photo is; it shows its image as it is.
        card_item = {"id": item["id"], "image": item.get("image"), "width": 2.0 * radius,
                     "strength": item.get("strength", 1.0)}
        return photo(made, card_item, where)
    colour, strength = glow_of(item, where, MARKER_COLOUR, 2.0)
    material = made.glow(colour, strength)
    if symbol == "ball":
        return [Part(api.ball(item["id"], radius, material), "ball", colour)]
    mesh = bpy.data.meshes.new(item["id"])
    shape_of(symbol, mesh, radius)
    mesh.materials.append(material)
    obj = bpy.data.objects.new(item["id"], mesh)
    if symbol == "ring":
        api.face(obj, made.camera)
    return [Part(obj, symbol, colour)]


def shape_of(symbol, mesh, radius):
    """Fills a mesh with a symbol of a marker. A pin and a cone stand with their point on the
    anchor (the object's origin); a cube and a ring are centred on it."""
    if symbol == "ring":
        vertices, faces = [], []
        around, across, tube = 24, 8, radius * 0.18
        for i in range(around):
            a = 2.0 * math.pi * i / around
            for j in range(across):
                b = 2.0 * math.pi * j / across
                reach = radius + tube * math.cos(b)
                vertices.append((reach * math.cos(a), reach * math.sin(a), tube * math.sin(b)))
        for i in range(around):
            for j in range(across):
                first, next_i, next_j = i * across + j, ((i + 1) % around) * across + j, i * across + (j + 1) % across
                faces.append((first, next_i, ((i + 1) % around) * across + (j + 1) % across, next_j))
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        return
    import bmesh
    shape = bmesh.new()
    if symbol == "cube":
        bmesh.ops.create_cube(shape, size=2.0 * radius)
    else:
        sides = 16
        height = 2.0 * radius
        base = radius * (0.6 if symbol == "pin" else 1.0)
        bmesh.ops.create_cone(shape, cap_ends=True, segments=sides, radius1=0.0, radius2=base, depth=height)
        bmesh.ops.translate(shape, vec=(0.0, 0.0, height / 2.0), verts=shape.verts)  # the point on the origin
        if symbol == "pin":
            bmesh.ops.create_icosphere(shape, subdivisions=2, radius=radius,
                                       matrix=mathutils.Matrix.Translation((0.0, 0.0, height + radius * 0.8)))
    shape.to_mesh(mesh)
    shape.free()


def size_marker(made, item, obj, where):
    """A driver scales the marker with its distance from the camera, as labels.markers.byDistance
    does, and with the radius the marker was made at."""
    how = item["sizeBy"]
    if how not in ("distance", "screen"):
        raise api.PlanError('%s.sizeBy: "distance" or "screen"' % where)
    factor = positive(item.get("factor"), where + ".factor")
    radius = item["radius"]
    lowest = positive(item.get("minRadius", radius / 4.0), where + ".minRadius")
    highest = positive(item.get("maxRadius", radius * 4.0), where + ".maxRadius")
    if lowest > highest:
        raise api.PlanError("%s.minRadius: the smallest radius is not larger than the largest" % where)
    if how == "screen":
        # A share of the picture's width: the width the lens sees at a distance is 2 tan(a/2) of it.
        factor *= 2.0 * math.tan(made.camera.data.angle / 2.0)
    api.size_by_distance(obj, made.camera, radius, {"factor": factor, "minRadius": lowest, "maxRadius": highest}, None, 0)


def outline(made, item, where):
    colour, strength = glow_of(item, where, TEXT_COLOUR, 1.5)
    given = item.get("points")
    if not isinstance(given, list) or len(given) < 2:
        raise api.PlanError("%s.points: an outline needs at least two points" % where)
    at = mathutils.Vector(api.vector(item.get("at"), where + ".at"))
    points = [mathutils.Vector(api.vector(p, "%s.points[%d]" % (where, i))) - at for i, p in enumerate(given)]
    width = positive(item.get("width"), where + ".width")
    return [Part(tube_of(item["id"], points, width, made.glow(colour, strength), "outline"), "line", colour)]


def photo(made, item, where):
    name = item.get("image")
    if not isinstance(name, str) or not name.lower().endswith((".png", ".jpg", ".jpeg")):
        raise api.PlanError('%s.image: "%s" is neither a .png nor a .jpg file' % (where, name))
    image = made.image(name, where + ".image")
    width = positive(item.get("width"), where + ".width")
    # Without a height the picture has the shape of its image.
    height = positive(item["height"], where + ".height") if "height" in item else width * image.size[1] / image.size[0]
    strength = api.amount(item.get("strength", 1.0), where + ".strength")
    border, border_colour = 0.0, FRAME_COLOUR
    if item.get("frame") is not None:
        border = positive(item["frame"].get("width"), where + ".frame.width")
        border_colour = api.colour(item["frame"].get("color", FRAME_COLOUR), where + ".frame.color")

    mesh = card(item["id"], image, width, height, border, border_colour, made.draft)
    made.mosaics += 1 if made.draft else 0
    mesh.materials.append(made.picture(name, image, strength))
    if border > 0.0:
        mesh.materials.append(made.glow(border_colour, 1.0))
    obj = bpy.data.objects.new(item["id"], mesh)
    # A photo is seen, as a picture on a screen is: its colours do not light the cave.
    for switch in ("visible_diffuse", "visible_glossy", "visible_transmission", "visible_volume_scatter"):
        if hasattr(obj, switch):
            setattr(obj, switch, False)

    facing = item.get("facing", "camera")
    turn = None
    if facing == "camera":
        for key in ("normal", "up"):
            if key in item:
                raise api.PlanError('%s.%s: only a "fixed" photo has one; this one faces the camera' % (where, key))
        # Turned as the camera is: parallel to the picture, its top the picture's top. (A text
        # turns to where the camera stands instead, as the plan's labels do.)
        obj.constraints.new(type="COPY_ROTATION").target = made.camera
    elif facing == "fixed":
        turn = turned(item, where)
    else:
        raise api.PlanError('%s.facing: unknown facing "%s"' % (where, facing))

    offset = offset_of(item, where, needed=False)
    parts = [Part(obj, "picture", border_colour if border > 0.0 else [0.5, 0.5, 0.5], offset or NOWHERE, turn)]
    if "lineWidth" in item:
        if offset is None:
            raise api.PlanError(
                "%s.lineWidth: a photo has a line to its anchor only when it stands away from it; give it an offset" % where)
        thick = positive(item["lineWidth"], where + ".lineWidth")
        # The line ends where it meets the picture's edge, not in its middle.
        short = offset.length - to_the_edge(offset, width / 2.0 + border, height / 2.0 + border, turn)
        if short > 1e-6:
            end = offset * (short / offset.length)
            parts.append(Part(line(item["id"], end, thick, made.glow(border_colour, 1.0)), "line", border_colour))
    return parts


def to_the_edge(offset, half_width, half_height, turn):
    """How far before a picture's middle a line from its anchor meets its edge. A fixed picture
    has its own axes; one that faces the camera is taken as standing upright across the line,
    which is how a camera that looks level, with a level horizon, sees it."""
    way = offset.normalized()
    if turn is not None:
        across, up = abs(way.dot(turn.col[0])), abs(way.dot(turn.col[1]))
    else:
        across, up = math.hypot(way.x, way.y), abs(way.z)
    reaches = [half / part for half, part in ((half_width, across), (half_height, up)) if part > 1e-6]
    # A line that comes straight at the picture's face ends in its middle.
    return 0.98 * min(reaches) if reaches else 0.0


PARTS = {"label": label, "callout": callout, "marker": marker, "photo": photo, "outline": outline}


def turned(item, where):
    """The turn of a photo that faces along "normal": its own +Z along it, its own +Y as near
    "up" as that lets it be."""
    normal = mathutils.Vector(api.vector(item.get("normal"), where + ".normal"))
    if normal.length <= 1e-9:
        raise api.PlanError("%s.normal: a fixed photo needs the direction it faces" % where)
    normal.normalize()
    if "up" in item:
        up = mathutils.Vector(api.vector(item["up"], where + ".up"))
    else:
        up = mathutils.Vector((0.0, 1.0, 0.0) if abs(normal.z) > 0.999 else (0.0, 0.0, 1.0))
    top = up - normal * up.dot(normal)
    if top.length <= 1e-9 * max(1.0, up.length):
        raise api.PlanError("%s.up: the top of a photo points somewhere off the direction it faces" % where)
    top.normalize()
    return mathutils.Matrix((top.cross(normal), top, normal)).transposed()


# ---- shapes ----

def line(name, offset, width, material):
    """The line from an anchor to what stands offset away from it: a thin tube from the object's
    own origin, which is put on the anchor."""
    return tube_of(name, [mathutils.Vector(NOWHERE), offset], width, material, "line")


def tube_of(name, points, width, material, suffix):
    """A thin tube through points that are given from the object's own origin."""
    vertices, faces = [], []
    api.tube(points, None, width / 2.0, vertices, faces, [])
    mesh = bpy.data.meshes.new(name + "." + suffix)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    mesh.materials.append(material)
    return bpy.data.objects.new(name + "." + suffix, mesh)


def card(name, image, width, height, border, border_colour, draft):
    """The mesh of a photo, in its own XY plane, facing +Z with +Y up: the picture, with the image
    laid over it by its UV map, and a border about it when it has a frame. For the draft look
    the picture is cut into tiles, each in the colour the image has there."""
    columns = rows = 1
    tiles = None
    if draft:
        longer = max(width, height)
        columns = max(1, int(round(DRAFT_TILES * width / longer)))
        rows = max(1, int(round(DRAFT_TILES * height / longer)))
        tiles = colours_of(image, columns, rows)

    left, bottom = -width / 2.0, -height / 2.0
    vertices = [(left + width * x / columns, bottom + height * y / rows, 0.0)
                for y in range(rows + 1) for x in range(columns + 1)]
    faces, uvs, colours = [], [], []
    for y in range(rows):
        for x in range(columns):
            first = y * (columns + 1) + x
            faces.append((first, first + 1, first + columns + 2, first + columns + 1))
            for u, v in ((x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1)):
                uvs.extend((u / float(columns), v / float(rows)))
            if tiles is not None:
                colours.extend(tiles[y * columns + x] * 4)
    pictures = len(faces)
    if border > 0.0:
        # Four strips about the picture, in its plane: nothing lies over anything.
        right, top = -left, -bottom
        outer = len(vertices)
        vertices += [(left - border, bottom - border, 0.0), (right + border, bottom - border, 0.0),
                     (right + border, top + border, 0.0), (left - border, top + border, 0.0)]
        inner = len(vertices)
        vertices += [(left, bottom, 0.0), (right, bottom, 0.0), (right, top, 0.0), (left, top, 0.0)]
        for side in range(4):
            onward = (side + 1) % 4
            faces.append((outer + side, outer + onward, inner + onward, inner + side))
            uvs.extend((0.0, 0.0) * 4)
            colours.extend(rgba(border_colour) * 4)

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    mesh.uv_layers.new(name="UVMap").data.foreach_set("uv", uvs)
    mesh.polygons.foreach_set("material_index", [0] * pictures + [1] * (len(faces) - pictures))
    if tiles is not None:
        attribute = mesh.color_attributes.new(DRAFT_COLOURS, "FLOAT_COLOR", "CORNER")
        attribute.data.foreach_set("color", colours)
        mesh.color_attributes.active_color = attribute
    return mesh


def colours_of(image, columns, rows):
    """The colours of an image made as small as columns by rows, row by row from its bottom, as
    the plan's colours are: linear RGB."""
    small = image.copy()
    try:
        small.scale(columns, rows)
        pixels = [0.0] * (4 * columns * rows)
        small.pixels.foreach_get(pixels)
    finally:
        bpy.data.images.remove(small)
    # The pixels of an image file are as the file has them, which is as a screen shows them.
    as_light = image.colorspace_settings.name == "sRGB"
    return [tuple(from_screen(channel) if as_light else channel for channel in pixels[at:at + 3]) + (1.0,)
            for at in range(0, len(pixels), 4)]


def from_screen(channel):
    return channel / 12.92 if channel <= 0.04045 else math.pow((channel + 0.055) / 1.055, 2.4)


def showing(name, image, strength):
    """A material that shows an image by the UV map of what wears it, as a screen shows it: it
    emits the image's colours, so the picture reads in an unlit cave and no light changes it."""
    material = bpy.data.materials.new("Photo " + name)
    material.diffuse_color = (0.5, 0.5, 0.5, 1.0)  # in Blender's solid view
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    texture = tree.nodes.new("ShaderNodeTexImage")
    texture.image = image
    texture.extension = "EXTEND"
    emission = tree.nodes.new("ShaderNodeEmission")
    emission.inputs["Strength"].default_value = strength
    tree.links.new(texture.outputs["Color"], emission.inputs["Color"])
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    if image.depth in (32, 64, 128):
        # The image has parts that are clear (a PNG cut out of its background): there the photo
        # is not there, and what stands behind it is seen.
        nothing = tree.nodes.new("ShaderNodeBsdfTransparent")
        cut = tree.nodes.new("ShaderNodeMixShader")
        cut.name = api.CUT_OUT
        tree.links.new(texture.outputs["Alpha"], cut.inputs[0])
        tree.links.new(nothing.outputs[0], cut.inputs[1])
        tree.links.new(emission.outputs["Emission"], cut.inputs[2])
        tree.links.new(cut.outputs[0], output.inputs["Surface"])
    else:
        tree.links.new(emission.outputs["Emission"], output.inputs["Surface"])
    return material
