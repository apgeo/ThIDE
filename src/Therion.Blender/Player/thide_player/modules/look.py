# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "look": render engine and quality, picture size, materials, lights and the world.

Runs after "models" and "camera_track": it gives the models the materials they ask for, and a
headlamp rides the camera.

A material that is "unlit" shows its colours as a screen shows them, whatever the light: a
survey line then reads in a cave no lamp reaches as it does under the sun. It emits to the camera
only, so it lights nothing round it.

The engine "workbench" is the draft look: Blender's solid view, many times faster than a real
render. It knows no lights and no node materials, so every model is given the colours of its
material as a colour attribute, which that engine does show.

The world is one colour, a sky, or an image all round (an HDRI): a Radiance .hdr or an OpenEXR
.exr picture by longitude and latitude, which is the background and lights the scene. Its
strength is the Strength of the world's Background node, whatever the kind, so a change of the
scene keys it the same way for all three ("changes": worldStrength). With a "backdrop" the world
is set apart: "strength" is then how strongly it lights the scene and "backdrop" how bright the
camera sees it, two numbers that a light path tells apart ("changes": worldBackdrop).
"""

import math

import bpy
import mathutils

from thide_player import api

SECTION = "look"
KEYS = {
    "engine": None, "samples": None, "denoise": None, "device": None, "seed": None,
    "transparent": None, "width": None, "height": None,
    "materials": [{
        "id": None, "kind": None, "color": None, "roughness": None,
        "stops": [{"at": None, "color": None}], "low": None, "high": None, "space": None, "scale": None,
        "bump": None, "unlit": None, "strength": None, "banded": None,
    }],
    "lights": [{"id": None, "kind": None, "energy": None, "direction": None, "size": None}],
    "world": {"kind": None, "color": None, "strength": None, "backdrop": None, "image": None, "rotation": None},
}

GPU_BACKENDS = {"optix": "OPTIX", "cuda": "CUDA", "hip": "HIP", "oneapi": "ONEAPI", "metal": "METAL"}


def build(ctx, look):
    api.report("phase", "engine")
    scene = ctx.scene
    scene.render.resolution_x = look["width"]
    scene.render.resolution_y = look["height"]
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = bool(look.get("transparent", False))

    engine = look.get("engine", "cycles")
    if engine == "cycles":
        use_cycles(scene, look)
    elif engine == "eevee":
        use_eevee(scene, look)
    elif engine == "workbench":
        use_workbench(scene, look)
    else:
        raise api.PlanError('look.engine: unknown engine "%s"' % engine)

    give_materials(scene, look.get("materials", []), draft=engine == "workbench")
    for index, light in enumerate(look.get("lights", [])):
        add_light(scene, light, "look.lights[%d]" % index)
    if look.get("world"):
        set_world(ctx, look["world"])
    else:
        scene.world = None  # black, and it lights nothing


# ---- engine ----

def use_cycles(scene, look):
    scene.render.engine = "CYCLES"
    cycles = scene.cycles
    cycles.samples = look.get("samples", 128)
    cycles.use_denoising = bool(look.get("denoise", True))
    cycles.seed = look.get("seed", 1)
    # A closed cave is noisy under global illumination: capped bounces give a much cleaner
    # picture for the same samples, at the cost of a little bounced light.
    cycles.max_bounces = 8
    cycles.diffuse_bounces = 3
    cycles.glossy_bounces = 3
    cycles.transmission_bounces = 4
    cycles.caustics_reflective = False
    cycles.caustics_refractive = False

    device = look.get("device", "auto")
    if device != "cpu" and device != "auto" and device not in GPU_BACKENDS:
        raise api.PlanError('look.device: unknown device "%s"' % device)
    wanted = [] if device == "cpu" else list(GPU_BACKENDS.values()) if device == "auto" else [GPU_BACKENDS[device]]
    for backend in wanted:
        try:
            if enable_gpu(backend):
                cycles.device = "GPU"
                # The denoiser runs on the CPU unless told otherwise (Blender 4.1 and newer can
                # run it on the GPU); on a card it would take most of a frame's time.
                if hasattr(cycles, "denoising_use_gpu"):
                    cycles.denoising_use_gpu = True
                api.report("device", backend)
                return
        except Exception:
            continue
    cycles.device = "CPU"
    api.report("device", "CPU")


def enable_gpu(backend):
    preferences = bpy.context.preferences.addons["cycles"].preferences
    preferences.compute_device_type = backend
    # The call that lists the devices has moved between versions.
    if hasattr(preferences, "refresh_devices"):
        preferences.refresh_devices()
    else:
        preferences.get_devices()
    found = False
    for device in preferences.devices:
        device.use = device.type == backend
        found = found or device.use
    return found


def use_eevee(scene, look):
    if api.set_enum(scene.render, "engine", ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE")) is None:
        raise api.PlanError("look.engine: this Blender has no EEVEE engine")
    scene.eevee.taa_render_samples = look.get("samples", 128)
    for switch in ("use_gtao", "use_shadows", "use_raytracing"):
        if hasattr(scene.eevee, switch):
            setattr(scene.eevee, switch, True)
    api.report("device", "EEVEE")


def use_workbench(scene, look):
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "VERTEX"  # the colour attribute of each mesh: see show_in_draft
    shading.show_cavity = True     # darkens creases, which is what shows the shape of a passage
    # The samples pick the anti-aliasing: none, a fast approximation, or one of five levels.
    samples = look.get("samples", 8)
    levels = [level for level in (5, 8, 11, 16, 32) if level <= samples]
    scene.display.render_aa = str(levels[-1]) if levels else "FXAA" if samples > 1 else "OFF"
    api.report("device", "WORKBENCH")


# ---- materials ----

DRAFT_COLOURS = "ThideDraft"


def give_materials(scene, materials, draft=False):
    specs = {spec["id"]: spec for spec in materials}
    made = {}
    if draft:
        bpy.context.view_layer.update()  # where the models stand, for a ramp over height
    for obj in [obj for obj in scene.objects if obj.get("thide_model")]:
        wanted = obj.get("thide_material")
        if not wanted:
            continue
        if wanted not in specs:
            raise api.PlanError(
                'the model "%s" asks for the material "%s", which look.materials does not have'
                % (obj.name, wanted))
        spec = specs[wanted]
        # A model without vertex colours gets the plain colour instead of a black surface.
        plain = spec.get("kind") == "vertexColor" and not obj.data.color_attributes
        key = (wanted, plain)
        if key not in made:
            made[key] = make_material(spec, plain)
        obj.data.materials.clear()
        obj.data.materials.append(made[key])
        if draft and not (spec.get("kind") == "vertexColor" and not plain):
            show_in_draft(obj, spec)


def show_in_draft(obj, spec):
    """Gives the mesh the colours of its material as a colour attribute, and makes it the one
    the solid view shows: one colour, or a ramp over the height of each vertex. A noise ramp
    shows as the material's plain colour."""
    mesh = obj.data
    if spec.get("kind") == "heightRamp":
        low, span = spec["low"], spec["high"] - spec["low"]
        own = ramp_space(spec) == "model"
        colours = []
        for vertex in mesh.vertices:
            height = vertex.co.z if own else (obj.matrix_world @ vertex.co).z
            colours.extend(rgba(ramp_colour(spec["stops"], (height - low) / span if span > 0.0 else 0.0, spec.get("banded"))))
    else:
        colours = rgba(spec["color"]) * len(mesh.vertices)
    attribute = mesh.color_attributes.new(DRAFT_COLOURS, "FLOAT_COLOR", "POINT")
    attribute.data.foreach_set("color", colours)
    mesh.color_attributes.active_color = attribute


def ramp_colour(stops, at, banded=False):
    """The colour of a ramp at a place between 0 and 1: straight from stop to stop, as the
    ramp of the material mixes them, or, of a banded ramp, that of the last stop the place has
    reached."""
    if at <= stops[0]["at"]:
        return stops[0]["color"]
    if banded:
        return [stop for stop in stops if stop["at"] <= at][-1]["color"]
    for low, high in zip(stops, stops[1:]):
        if at <= high["at"]:
            span = high["at"] - low["at"]
            share = (at - low["at"]) / span if span > 0.0 else 1.0
            return [a + (b - a) * share for a, b in zip(low["color"], high["color"])]
    return stops[-1]["color"]


def ramp_space(spec):
    """Where a ramp over height measures its heights: in the scene, or in the model itself."""
    space = spec.get("space", "scene")
    if space not in ("scene", "model"):
        raise api.PlanError('look.materials: unknown space "%s" for "%s"' % (space, spec["id"]))
    return space


def rgba(colour):
    return (colour[0], colour[1], colour[2], 1.0)


def make_material(spec, plain):
    kind = "flat" if plain else spec.get("kind", "flat")
    material = bpy.data.materials.new(spec["id"])
    if "color" in spec:
        material.diffuse_color = rgba(spec["color"])  # in Blender's solid view
    material.use_nodes = True
    tree = material.node_tree
    shader = next(node for node in tree.nodes if node.type == "BSDF_PRINCIPLED")
    shader.inputs["Roughness"].default_value = spec.get("roughness", 0.9)
    colour = shader.inputs["Base Color"]

    if kind == "flat":
        colour.default_value = rgba(spec["color"])
    elif kind == "vertexColor":
        # With no layer named, the node reads the mesh's default colour attribute.
        attribute = tree.nodes.new("ShaderNodeVertexColor")
        tree.links.new(attribute.outputs["Color"], colour)
    elif kind == "heightRamp":
        # Height between "low" and "high" picks the colour: the height in the scene (world Z),
        # or the model's own, which stays with the rock when the model is stretched or moved.
        separate = tree.nodes.new("ShaderNodeSeparateXYZ")
        if ramp_space(spec) == "model":
            coordinates = tree.nodes.new("ShaderNodeTexCoord")
            tree.links.new(coordinates.outputs["Object"], separate.inputs["Vector"])
        else:
            geometry = tree.nodes.new("ShaderNodeNewGeometry")
            tree.links.new(geometry.outputs["Position"], separate.inputs["Vector"])
        span = tree.nodes.new("ShaderNodeMapRange")
        span.inputs["From Min"].default_value = spec["low"]
        span.inputs["From Max"].default_value = spec["high"]
        tree.links.new(separate.outputs["Z"], span.inputs["Value"])
        ramp = make_ramp(tree, spec["stops"], spec.get("banded"))
        tree.links.new(span.outputs["Result"], ramp.inputs["Fac"])
        tree.links.new(ramp.outputs["Color"], colour)
    elif kind == "noiseRamp":
        # Noise picks the colour and raises the surface a little: rock without any data.
        coordinates = tree.nodes.new("ShaderNodeTexCoord")
        noise = tree.nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = spec.get("scale", 8.0)
        tree.links.new(coordinates.outputs["Object"], noise.inputs["Vector"])
        ramp = make_ramp(tree, spec["stops"], spec.get("banded"))
        tree.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
        tree.links.new(ramp.outputs["Color"], colour)
        bump = tree.nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = spec.get("bump", 0.15)
        tree.links.new(noise.outputs["Fac"], bump.inputs["Height"])
        tree.links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    else:
        raise api.PlanError('look.materials: unknown kind "%s" for "%s"' % (kind, spec["id"]))
    if spec.get("unlit"):
        make_unlit(tree, shader, api.amount(spec.get("strength", 1.0), 'look.materials: "strength" of "%s"' % spec["id"]))
    elif "strength" in spec:
        raise api.PlanError('look.materials: only an unlit material has a "strength" ("%s")' % spec["id"])
    return material


def make_unlit(tree, shader, strength):
    """Puts a shader that emits in the place of the one that is lit, with the colours that one
    had: the material then shows them as a screen does, whatever the light. It emits to the
    camera only, so a line that glows in a dark cave does not light the walls round it."""
    emission = tree.nodes.new("ShaderNodeEmission")
    colour = shader.inputs["Base Color"]
    if colour.is_linked:
        tree.links.new(colour.links[0].from_socket, emission.inputs["Color"])
    else:
        emission.inputs["Color"].default_value = colour.default_value
    seen = tree.nodes.new("ShaderNodeLightPath")
    by = tree.nodes.new("ShaderNodeMath")
    by.operation = "MULTIPLY"
    by.inputs[1].default_value = strength
    tree.links.new(seen.outputs["Is Camera Ray"], by.inputs[0])
    tree.links.new(by.outputs[0], emission.inputs["Strength"])
    surface = shader.outputs[0].links[0].to_socket
    tree.links.new(emission.outputs["Emission"], surface)
    tree.nodes.remove(shader)


def make_ramp(tree, stops, banded=False):
    """A colour ramp with the plan's stops. A new ramp has two; the rest are added. A banded
    ramp does not blend: each stop's colour holds from the stop up to the next one."""
    if len(stops) < 2:
        raise api.PlanError("look.materials: a ramp needs at least two stops")
    node = tree.nodes.new("ShaderNodeValToRGB")
    if banded:
        node.color_ramp.interpolation = "CONSTANT"
    elements = node.color_ramp.elements
    for index, stop in enumerate(stops):
        element = elements[index] if index < 2 else elements.new(stop["at"])
        element.position = stop["at"]
        element.color = rgba(stop["color"])
    return node


# ---- lights and world ----

def add_light(scene, light, where):
    kind = light.get("kind")
    if kind == "sun":
        data = bpy.data.lights.new(light["id"], type="SUN")
        data.energy = light["energy"]
        obj = bpy.data.objects.new(light["id"], data)
        scene.collection.objects.link(obj)
        # A light shines along its own -Z axis.
        obj.rotation_euler = mathutils.Vector(light["direction"]).to_track_quat("-Z", "Y").to_euler()
    elif kind == "headlamp":
        if scene.camera is None:
            raise api.PlanError(
                '%s: a headlamp rides the camera; list "camera_track" before "look" in "features"' % where)
        data = bpy.data.lights.new(light["id"], type="AREA")
        data.energy = light["energy"]
        data.size = light.get("size", 0.3)
        obj = bpy.data.objects.new(light["id"], data)
        scene.collection.objects.link(obj)
        obj.parent = scene.camera
        obj.location = (0.0, 0.0, 0.0)
    else:
        raise api.PlanError('%s.kind: unknown light "%s"' % (where, kind))
    api.stamp(obj, "light", light["id"])


def set_world(ctx, spec):
    scene = ctx.scene
    world = bpy.data.worlds.new("CaveWorld")
    scene.world = world
    world.use_nodes = True
    tree = world.node_tree
    background = next(node for node in tree.nodes if node.type == "BACKGROUND")
    strength = spec.get("strength", 1.0)
    background.inputs["Strength"].default_value = strength
    kind = spec.get("kind", "color")
    if kind == "color":
        background.inputs["Color"].default_value = rgba(spec["color"])
        world.color = [min(1.0, channel * strength) for channel in spec["color"]]  # the draft's background
    elif kind == "sky":
        world.color = [min(1.0, channel * strength) for channel in (0.35, 0.55, 0.9)]  # a draft has no sky
        sky = tree.nodes.new("ShaderNodeTexSky")
        # The same sky in every Blender: Blender 4 calls its physical sky NISHITA, and Blender 5
        # has the same model as SINGLE_SCATTERING, beside a newer one (MULTIPLE_SCATTERING) that
        # is bluer and lights a scene about a third more strongly. Blender 5 also starts a sky
        # 100 m up where Blender 4 starts it on the ground: the height is set, not left.
        api.set_enum(sky, "sky_type", ("NISHITA", "SINGLE_SCATTERING", "MULTIPLE_SCATTERING"))
        if hasattr(sky, "altitude"):
            sky.altitude = 0.0
        tree.links.new(sky.outputs["Color"], background.inputs["Color"])
    elif kind == "image":
        world.color = [min(1.0, 0.5 * strength)] * 3  # a draft shows no image: a grey as strong
        image = load_environment(ctx, spec.get("image"))
        # The image lies round the scene by longitude and latitude, its upper edge overhead and
        # its middle in the east (+X). Turning the direction it is looked up by one way turns
        # the image the other way.
        coordinates = tree.nodes.new("ShaderNodeTexCoord")
        mapping = tree.nodes.new("ShaderNodeMapping")
        mapping.vector_type = "POINT"
        mapping.inputs["Rotation"].default_value = (0.0, 0.0, -math.radians(spec.get("rotation", 0.0)))
        environment = tree.nodes.new("ShaderNodeTexEnvironment")
        environment.image = image
        environment.projection = "EQUIRECTANGULAR"
        tree.links.new(coordinates.outputs["Generated"], mapping.inputs["Vector"])
        tree.links.new(mapping.outputs["Vector"], environment.inputs["Vector"])
        tree.links.new(environment.outputs["Color"], background.inputs["Color"])
    else:
        raise api.PlanError('look.world.kind: unknown world "%s"' % kind)
    if spec.get("backdrop") is not None:
        seen = api.amount(spec["backdrop"], "look.world.backdrop")
        set_apart(world)
        strength_of(world, WORLD_SEEN).default_value = seen
        if kind == "color":
            world.color = [min(1.0, channel * seen) for channel in spec["color"]]
        else:
            world.color = [min(1.0, channel * seen / strength) if strength > 0.0 else 0.0 for channel in world.color]


# The two numbers of a world whose light and whose look are set apart, by the names of their nodes.
WORLD_LIGHT = "ThideWorldLight"
WORLD_SEEN = "ThideWorldSeen"


def is_set_apart(world):
    return world.node_tree is not None and WORLD_SEEN in world.node_tree.nodes


def strength_of(world, name):
    """One of the two numbers of a world that is set apart, as the socket that holds it."""
    return world.node_tree.nodes[name].outputs[0]


def set_apart(world):
    """Sets how strongly the world lights the scene apart from how bright the camera sees it:
    two numbers in the place of the one strength, the second for the rays that come from the
    camera. Both start as the strength the world has. A world is set apart once."""
    tree = world.node_tree
    if is_set_apart(world):
        return
    background = next(node for node in tree.nodes if node.type == "BACKGROUND")
    strength = background.inputs["Strength"].default_value
    light = tree.nodes.new("ShaderNodeValue")
    light.name = WORLD_LIGHT
    seen = tree.nodes.new("ShaderNodeValue")
    seen.name = WORLD_SEEN
    light.outputs[0].default_value = seen.outputs[0].default_value = strength
    path = tree.nodes.new("ShaderNodeLightPath")
    pick = tree.nodes.new("ShaderNodeMix")
    pick.data_type = "FLOAT"
    pick.clamp_factor = True
    sockets = {socket.identifier: socket for socket in pick.inputs}
    tree.links.new(path.outputs["Is Camera Ray"], sockets["Factor_Float"])
    tree.links.new(light.outputs[0], sockets["A_Float"])
    tree.links.new(seen.outputs[0], sockets["B_Float"])
    tree.links.new(next(socket for socket in pick.outputs if socket.identifier == "Result_Float"), background.inputs["Strength"])


ENVIRONMENT_HEADS = (b"#?", b"\x76\x2f\x31\x01")  # Radiance HDR, OpenEXR


def load_environment(ctx, name):
    """The world's image, loaded. A file that is not a Radiance HDR or an OpenEXR picture by
    what it holds is refused before Blender is asked to read it."""
    where = "look.world.image"
    if not name:
        raise api.PlanError("%s: a world of the kind \"image\" needs its image file" % where)
    path = ctx.input_file(name, where)
    with open(path, "rb") as stream:
        head = stream.read(4)
    if not head.startswith(ENVIRONMENT_HEADS):
        raise api.PlanError('%s: "%s" is neither a Radiance HDR nor an OpenEXR image' % (where, name))
    image = bpy.data.images.load(path)
    # Reading the size loads the file: one that cannot be read has none.
    if image.size[0] < 1 or image.size[1] < 1:
        bpy.data.images.remove(image)
        raise api.PlanError('%s: "%s" cannot be read as an image' % (where, name))
    return image
