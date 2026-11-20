"""A feature module that is not part of the player: the tests load it from this folder.

Puts a cube of the plan's "marker_cube" section at each of its points.
"""

import bpy

from thide_player import api

SECTION = "marker_cube"


def build(ctx, section):
    for index, point in enumerate(section["points"]):
        bpy.ops.mesh.primitive_cube_add(size=section["size"], location=point)
        cube = bpy.context.active_object
        cube.name = "marker:%d" % index
        api.stamp(cube, "marker", cube.name)
    api.report("markers", len(section["points"]))
