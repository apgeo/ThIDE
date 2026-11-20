"""Feature "textured_materials": materials that drape an image over a model and let the scene
show through it: a terrain over a cave, with an aerial picture or a map on it.

Each material is given to the models it names. Its image lies on a model by the model's own UV
map; without an image, or on a model without a UV map, the material has its plain colour. How
solid it is may differ between its two sides: a terrain that is see-through from above and not
there at all from below never comes between the camera and a passage. A model can be kept from
casting shadows, so the sun still lights what lies under it.

Runs after "models" and "look". The draft look (Blender's solid view) cannot draw one model
see-through: there such a model is left out, and a solid one is drawn in its plain colour.
"""

import bpy

from thide_player import api

SECTION = "texturedMaterials"
KEYS = [{
    "id": None, "models": None, "color": None, "roughness": None, "image": None, "opacity": None,
    "backOpacity": None, "shadow": None,
}]

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
        for name in spec.get("models", []):
            obj = models.get(name)
            if obj is None:
                raise api.PlanError('%s.models: there is no model "%s"' % (where, name))
            draped = image is not None and bool(obj.data.uv_layers)
            if image is not None and not draped:
                api.warn('the model "%s" has no UV map; "%s" is drawn in its plain colour' % (name, spec["id"]))
            obj.data.materials.clear()
            obj.data.materials.append(make_material(spec, image if draped else None))
            if spec.get("shadow") is False and hasattr(obj, "visible_shadow"):
                obj.visible_shadow = False
            if draft:
                show_in_draft(obj, spec, name)


def make_material(spec, image):
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
    if front < 1.0 or back < 1.0:
        geometry = tree.nodes.new("ShaderNodeNewGeometry")
        alpha = tree.nodes.new("ShaderNodeMath")
        alpha.operation = "MULTIPLY_ADD"
        alpha.inputs[1].default_value = back - front
        alpha.inputs[2].default_value = front
        tree.links.new(geometry.outputs["Backfacing"], alpha.inputs[0])
        tree.links.new(alpha.outputs["Value"], shader.inputs["Alpha"])
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
