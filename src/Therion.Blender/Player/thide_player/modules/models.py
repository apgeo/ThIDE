"""Feature "models": imports the plan's meshes, one object per model, and places them.

A model is a PLY or a GLB file. A GLB may hold several meshes under a tree of nodes; they are
joined into one object with the placements of the nodes applied.
"""

import os

import bmesh
import bpy
import mathutils

from thide_player import api

SECTION = "models"


def build(ctx, models):
    api.report("phase", "import")
    triangles = 0
    for index, model in enumerate(models or []):
        where = "models[%d]" % index
        path = ctx.input_file(model["path"], where + ".path")
        extension = os.path.splitext(model["path"])[1].lower()
        if extension == ".ply":
            obj = import_ply(path, where)
        elif extension == ".glb":
            obj = import_glb(path, where)
        else:
            raise api.PlanError('%s.path: "%s" is neither a .ply nor a .glb file' % (where, model["path"]))

        obj.name = model["id"]
        obj.location = model.get("translation", (0.0, 0.0, 0.0))
        api.stamp(obj, "model", model["id"])
        if model.get("material"):
            obj["thide_material"] = model["material"]  # the "look" module assigns it
        triangles += sum(len(polygon.vertices) - 2 for polygon in obj.data.polygons)
    api.report("triangles", triangles)


def imported_by(importer, path):
    """Runs an import operator and returns the objects it added."""
    before = set(bpy.data.objects)
    importer(filepath=path)
    return [obj for obj in bpy.data.objects if obj not in before]


def import_ply(path, where):
    added = imported_by(bpy.ops.wm.ply_import, path)
    if len(added) != 1 or added[0].type != "MESH":
        raise api.PlanError("%s: the PLY file gave no mesh: %s" % (where, path))
    return added[0]


def import_glb(path, where):
    try:
        added = imported_by(bpy.ops.import_scene.gltf, path)
    except (AttributeError, RuntimeError) as error:
        # Blender's glTF importer needs numpy, which a Blender packaged by a Linux distribution
        # may lack. The last line of what the operator reports is the reason.
        reason = [line.strip() for line in str(error).splitlines() if line.strip() and not line.startswith("Location:")]
        raise api.PlanError("%s: this Blender cannot import the GLB file (%s): %s"
                            % (where, reason[-1] if reason else type(error).__name__, path))
    parts = [obj for obj in added if obj.type == "MESH"]
    if not parts:
        raise api.PlanError("%s: the GLB file holds no mesh: %s" % (where, path))
    bpy.context.view_layer.update()  # the nodes' placements, before they are read

    model = parts[0]
    materials = [slot.material for part in parts for slot in part.material_slots if slot.material]
    if len(parts) == 1:
        placed = model.data
        placed.transform(model.matrix_world)
    else:
        joined = bmesh.new()
        for part in parts:
            mesh = part.data.copy()
            mesh.transform(part.matrix_world)
            joined.from_mesh(mesh)
            bpy.data.meshes.remove(mesh)
        placed = bpy.data.meshes.new(model.data.name)
        joined.to_mesh(placed)
        joined.free()
        model.data = placed
    model.parent = None
    model.matrix_world = mathutils.Matrix.Identity(4)
    # One material for the whole model: the file's first, until the plan names another.
    placed.materials.clear()
    if materials:
        placed.materials.append(materials[0])
    bpy.data.batch_remove([obj for obj in added if obj != model])
    return model
