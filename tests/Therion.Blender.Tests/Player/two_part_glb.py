# Writes a GLB with two meshes under a node of their own, for the test that a GLB of several
# parts becomes one model: a 2 m cube and a 4 m cube, children of an empty at (1, 2, 3), so that in
# the world they are centred on (11, 2, 3) and (1, 22, 8).
#
#   blender -b --factory-startup --python two_part_glb.py -- OUT.glb

import sys

import bpy

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.object.empty_add(location=(1.0, 2.0, 3.0))
parent = bpy.context.active_object
for size, location in ((2.0, (10.0, 0.0, 0.0)), (4.0, (0.0, 20.0, 5.0))):
    bpy.ops.mesh.primitive_cube_add(size=size, location=location)
    bpy.context.active_object.parent = parent
bpy.ops.export_scene.gltf(filepath=sys.argv[sys.argv.index("--") + 1], export_format="GLB")
