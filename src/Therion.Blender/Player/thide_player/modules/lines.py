# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "lines": lines drawn through the points the plan gives, as thin tubes: the legs of a
survey, a route, an outline.

A line is one object made of strips. A strip runs through its points in order, "width" metres
thick; a strip of two points is one straight piece. A strip may give every point a colour, which
the tube takes on at that point and blends along the way to the next. The tubes are a mesh the
player builds, so nobody has to write one to a file.

In everything else a line is a model: it is placed by "translation", "rotation" and "scale" as a
model is, it wears the material of the look that it names (a colour of its own, a ramp over
height, or "vertexColor" for the colours of its points), and what changes of a model changes of
it. Runs before "look", which gives the materials.
"""

import math

import bpy
import mathutils

from thide_player import api

SECTION = "lines"
KEYS = [{
    "id": None, "role": None, "width": None, "material": None, "translation": None,
    "rotation": {"axis": None, "degrees": None}, "scale": None,
    "strips": [{"points": None, "colors": None}],
}]

SIDES = 6            # of a tube
COLOURS = "Col"      # the colour attribute the points' colours are in


def build(ctx, lines):
    features = ctx.plan.get("features", [])
    if lines and "look" in features and SECTION in features and features.index("look") < features.index(SECTION):
        raise api.PlanError('lines: list "lines" before "look" in "features": the look gives the lines their materials')
    pieces = 0
    for index, line in enumerate(lines or []):
        where = "lines[%d]" % index
        width = line.get("width")
        if isinstance(width, bool) or not isinstance(width, (int, float)) or not width > 0.0:
            raise api.PlanError("%s.width: a line is a positive number of metres thick" % where)
        if not line.get("strips"):
            raise api.PlanError("%s.strips: a line needs at least one strip" % where)
        coloured = [strip.get("colors") is not None for strip in line["strips"]]
        if any(coloured) and not all(coloured):
            raise api.PlanError("%s.strips: either every strip gives its points colours or none does" % where)

        vertices, faces, colours = [], [], []
        for number, strip in enumerate(line["strips"]):
            at = "%s.strips[%d]" % (where, number)
            points = [point_of(value, "%s.points[%d]" % (at, i)) for i, value in enumerate(strip.get("points") or [])]
            if len(points) < 2:
                raise api.PlanError("%s.points: a strip runs through at least two points" % at)
            given = strip.get("colors")
            if given is not None and len(given) != len(points):
                raise api.PlanError("%s.colors: a strip has a colour for every point or none" % at)
            if given is not None:
                given = [colour_of(value, "%s.colors[%d]" % (at, i)) for i, value in enumerate(given)]
            pieces += add_tube(points, given, width / 2.0, vertices, faces, colours)

        mesh = bpy.data.meshes.new(line["id"])
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        if all(coloured):
            attribute = mesh.color_attributes.new(COLOURS, "FLOAT_COLOR", "POINT")
            attribute.data.foreach_set("color", [channel for colour in colours for channel in colour])
            mesh.color_attributes.active_color = attribute
        obj = bpy.data.objects.new(line["id"], mesh)
        ctx.scene.collection.objects.link(obj)
        api.place(obj, line, where)
        # A line is a model to every module after this one: "look" gives it its material, and
        # what changes of a model changes of it.
        api.stamp(obj, line.get("role", "line"), line["id"])
        obj["thide_model"] = True
        if line.get("material"):
            obj["thide_material"] = line["material"]
    api.report("lines", pieces)


def point_of(value, where):
    if not isinstance(value, list) or len(value) != 3 or not all(
            isinstance(part, (int, float)) and not isinstance(part, bool) and math.isfinite(part) for part in value):
        raise api.PlanError("%s: a point is three numbers" % where)
    return mathutils.Vector(value)


def colour_of(value, where):
    if not isinstance(value, list) or len(value) != 3 or not all(
            isinstance(part, (int, float)) and not isinstance(part, bool) and 0.0 <= part <= 1.0 for part in value):
        raise api.PlanError("%s: a colour is three numbers from 0 to 1" % where)
    return (value[0], value[1], value[2], 1.0)


def add_tube(points, colours, radius, vertices, faces, vertex_colours):
    """Adds the tube of one strip to the mesh being made, and returns how many straight pieces it
    has. A ring of vertices stands about every point, across the way the strip runs there (at a
    bend: half-way between the way in and the way out), and is carried from point to point
    without a twist. Points that lie on the point before them are left out."""
    kept, kept_colours = [points[0]], [colours[0]] if colours else None
    for index in range(1, len(points)):
        if (points[index] - kept[-1]).length > 1e-9:
            kept.append(points[index])
            if colours:
                kept_colours.append(colours[index])
    if len(kept) < 2:
        return 0
    ways = [(b - a).normalized() for a, b in zip(kept, kept[1:])]

    first = len(vertices)
    along = ways[0]
    # Any direction across the first piece: the frame's axis the piece runs least along.
    across = along.cross(mathutils.Vector(min(((1, 0, 0), (0, 1, 0), (0, 0, 1)), key=lambda axis: abs(along.dot(axis)))))
    across.normalize()
    for index, point in enumerate(kept):
        if 0 < index < len(kept) - 1:
            way = ways[index - 1] + ways[index]
            way = way.normalized() if way.length > 1e-6 else ways[index].copy()  # it doubles back
        else:
            way = ways[min(index, len(ways) - 1)]
        across = along.rotation_difference(way) @ across
        along = way
        side = along.cross(across)
        for step in range(SIDES):
            angle = 2.0 * math.pi * step / SIDES
            vertices.append(point + radius * (math.cos(angle) * across + math.sin(angle) * side))
            if colours:
                vertex_colours.append(kept_colours[index])

    for index in range(len(kept) - 1):
        ring, following = first + index * SIDES, first + (index + 1) * SIDES
        for step in range(SIDES):
            onward = (step + 1) % SIDES
            faces.append((ring + step, ring + onward, following + onward, following + step))
    # Closed at both ends, each end facing away from the tube.
    last = first + (len(kept) - 1) * SIDES
    faces.append(tuple(first + step for step in reversed(range(SIDES))))
    faces.append(tuple(last + step for step in range(SIDES)))
    return len(kept) - 1
