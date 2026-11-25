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
            pieces += api.tube(points, given, width / 2.0, vertices, faces, colours)

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
