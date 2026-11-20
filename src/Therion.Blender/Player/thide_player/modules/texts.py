# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Feature "texts": what is written over the picture, in two dimensions: titles, captions, credits,
a scale bar, a north arrow, a card at the end.

It plays in the second stage of the render, in the video editor, over the frames the first stage
rendered: nothing in the scene can hide a text, and changing one costs the second stage only.

Where each item is, how large it is and when it shows comes from the plan, as shares of the
picture and seconds; nothing is laid out here. An item is a text, a box of one colour, or an arrow
that turns about its middle with a letter at its tip. Later items lie over earlier ones. The
letters are Blender's own font, white unless the plan says otherwise, with a dark outline and
shadow so that they read over any picture.
"""

import math
import os
import struct
import zlib

from thide_player import api

SECTION = "texts"
KEYS = {
    "hold": None, "fromBlack": None, "toBlack": None,
    "items": [{
        "id": None, "role": None, "kind": None, "at": None, "text": None, "size": None, "width": None,
        "height": None, "alignX": None, "alignY": None, "color": None, "opacity": None, "backing": None,
        "wrap": None, "start": None, "end": None, "fadeIn": None, "fadeOut": None, "slot": None,
        "turn": [{"t": None, "degrees": None}],
    }],
}
# A text changes no frame of the first stage: only the second stage runs again.
IN_FRAMES = False

KINDS = ("text", "box", "arrow")
ALIGN_X = {"left": "LEFT", "center": "CENTER", "right": "RIGHT"}
ALIGN_Y = {"top": "TOP", "center": "CENTER", "bottom": "BOTTOM"}

# The frames lie on channel 1 of the video editor; the items go above them.
FIRST_CHANNEL = 2
LAST_CHANNEL = 128

DARK = (0.0, 0.0, 0.0)
ARROW_PIXELS = 128    # the side of the arrow's picture; it is drawn larger or smaller as needed
LETTER_OF_ARROW = 0.4  # the letter at the tip of an arrow, as a share of the arrow's length
ARC_STEP = 5.0        # the letter goes round the arrow in steps of at most this many degrees


def two_stage(texts):
    return bool(texts) and bool(
        texts.get("items") or texts.get("hold") or texts.get("fromBlack") or texts.get("toBlack"))


def build(ctx, texts):
    """The first stage draws no text. It checks them, so that a text the second stage could not
    draw is refused before the long render and not after it."""
    check(texts or {})


def check(texts):
    ids = set()
    for index, item in enumerate(texts.get("items", [])):
        where = "texts.items[%d]" % index
        if item.get("id") in ids or not item.get("id"):
            raise api.PlanError("%s.id: every item needs an id of its own" % where)
        ids.add(item["id"])
        kind = item.get("kind")
        if kind not in KINDS:
            raise api.PlanError('%s.kind: unknown kind "%s"' % (where, kind))
        if item.get("alignX", "center") not in ALIGN_X or item.get("alignY", "center") not in ALIGN_Y:
            raise api.PlanError("%s: unknown alignment" % where)
        needed = {"text": ("text", "size"), "box": ("width", "height"), "arrow": ("size",)}[kind]
        for key in ("at",) + needed:
            if item.get(key) is None:
                raise api.PlanError("%s.%s: a %s needs it" % (where, key, kind))


def compose(ctx, texts):
    texts = texts or {}
    check(texts)
    scene = ctx.scene
    # The movie goes on after the scene's last frame for as long as the plan holds it.
    scene.frame_end = ctx.frame_count + max(0, ctx.frame_at(texts.get("hold", 0.0)) - 1)
    last = scene.frame_end

    taken = []  # (first frame, last frame, top channel) of every item placed so far
    top = FIRST_CHANNEL - 1
    for item, first, final in in_their_slots(ctx, texts.get("items", []), last):
        # Above every earlier item it shares a frame with; items that never meet share channels.
        channel = 1 + max([c for a, b, c in taken if a <= final and first <= b] + [FIRST_CHANNEL - 1])
        used = PLACE[item["kind"]](ctx, item, channel, first, final)
        if channel + used - 1 > LAST_CHANNEL:
            raise api.PlanError("texts: more items show at one time than the video editor has channels")
        taken.append((first, final, channel + used - 1))
        top = max(top, channel + used - 1)

    out_of_black = min(last, ctx.frame_at(texts.get("fromBlack", 0.0)) - 1)
    to_black = min(last, ctx.frame_at(texts.get("toBlack", 0.0)) - 1)
    if out_of_black > 0:
        strip = box(ctx, "FromBlack", top + 1, 1, out_of_black, (0.5, 0.5), 1.0, 1.0, DARK)
        key_opacity(strip, [(1, 1.0), (out_of_black + 1, 0.0)])
    if to_black > 0:
        strip = box(ctx, "ToBlack", top + 2, last - to_black + 1, last, (0.5, 0.5), 1.0, 1.0, DARK)
        key_opacity(strip, [(last - to_black, 0.0), (last, 1.0)])
    # Every change runs evenly from key to key.
    api.set_interpolation(scene, "LINEAR")
    api.report("texts", len(taken))


def in_their_slots(ctx, items, last):
    """The items that show at all, in the plan's order, each with its first and last frame. Of
    the items that share a slot only the one that started last shows: an earlier one goes at the
    frame the next one appears."""
    timed = []
    for index, item in enumerate(items):
        first = max(1, ctx.frame_at(item["start"])) if item.get("start") is not None else 1
        final = min(last, ctx.frame_at(item["end"]) - 1) if item.get("end") is not None else last
        timed.append([item, first, final, index])
    slots = {}
    for entry in timed:
        if entry[0].get("slot"):
            slots.setdefault(entry[0]["slot"], []).append(entry)
    for sharing in slots.values():
        sharing.sort(key=lambda entry: (entry[1], entry[3]))
        for entry, following in zip(sharing, sharing[1:]):
            entry[2] = min(entry[2], following[1] - 1)
    return [(item, first, final) for item, first, final, _ in timed if first <= final]


# ---- the kinds ----

def place_text(ctx, item, channel, first, last):
    strip = text(ctx, item["id"], channel, first, last, item["text"], item["at"], item["size"],
                 item.get("alignX", "center"), item.get("alignY", "center"), item.get("color"), item.get("wrap", 0.9))
    if item.get("backing"):
        strip.use_box = True
        strip.box_color = (0.0, 0.0, 0.0, 0.6)
        strip.box_margin = 0.012
    fade(ctx, strip, item, first, last)
    return 1


def place_box(ctx, item, channel, first, last):
    strip = box(ctx, item["id"], channel, first, last, item["at"], item["width"], item["height"],
                item.get("color", (1.0, 1.0, 1.0)))
    fade(ctx, strip, item, first, last)
    return 1


def place_arrow(ctx, item, channel, first, last):
    """An arrow is a small picture, drawn here, that the video editor turns; its letter is a text
    that goes round with the tip and stays upright."""
    scene = ctx.scene
    width, height = scene.render.resolution_x, scene.render.resolution_y
    at = item["at"]
    path = os.path.join(api.scratch_dir(), "arrow.png")
    write_arrow(path, ARROW_PIXELS, item.get("color", (1.0, 1.0, 1.0)))
    arrow = api.image_strip(scene, item["id"], path, channel, first)
    arrow.frame_final_duration = last - first + 1
    arrow.blend_type = "ALPHA_OVER"
    scale = item["size"] * height / ARROW_PIXELS
    arrow.transform.scale_x = arrow.transform.scale_y = scale
    arrow.transform.offset_x = (at[0] - 0.5) * width
    arrow.transform.offset_y = (at[1] - 0.5) * height

    turns = [(ctx.frame_at(turn["t"]), float(turn["degrees"])) for turn in item.get("turn") or []] or [(first, 0.0)]
    arrow.transform.rotation = math.radians(turns[0][1])
    if len(turns) > 1:
        for frame, degrees in turns:
            arrow.transform.rotation = math.radians(degrees)
            arrow.transform.keyframe_insert("rotation", frame=frame)
    fade(ctx, arrow, item, first, last)
    if not item.get("text"):
        return 1

    letter_size = item["size"] * LETTER_OF_ARROW
    reach = (item["size"] / 2.0 + letter_size * 0.75) * height  # from the arrow's middle, in pixels

    def tip(degrees):
        angle = math.radians(degrees)
        return (at[0] - math.sin(angle) * reach / width, at[1] + math.cos(angle) * reach / height)

    letter = text(ctx, item["id"] + ".letter", channel + 1, first, last, item["text"], tip(turns[0][1]),
                  letter_size, "center", "center", item.get("color"), 1.0)
    if len(turns) > 1:
        for frame, degrees in round_the_arc(turns):
            letter.location = tip(degrees)
            letter.keyframe_insert("location", frame=frame)
    fade(ctx, letter, item, first, last)
    return 2


PLACE = {"text": place_text, "box": place_box, "arrow": place_arrow}


def round_the_arc(turns):
    """The turns with more of them put between two that lie far apart, so that a letter which
    moves in straight lines from one to the next stays on its circle."""
    arc = [turns[0]]
    for (frame, degrees), (next_frame, next_degrees) in zip(turns, turns[1:]):
        steps = min(next_frame - frame, int(math.ceil(abs(next_degrees - degrees) / ARC_STEP)))
        for step in range(1, steps):
            between = frame + int(round(step * (next_frame - frame) / float(steps)))
            if between > arc[-1][0]:
                share = (between - frame) / float(next_frame - frame)
                arc.append((between, degrees + (next_degrees - degrees) * share))
        if next_frame > arc[-1][0]:
            arc.append((next_frame, next_degrees))
    return arc


# ---- strips ----

def text(ctx, name, channel, first, last, body, at, size, align_x, align_y, colour, wrap):
    scene = ctx.scene
    strip = api.effect_strip(scene, name, "TEXT", channel, first, last)
    strip.text = body
    strip.font_size = size * scene.render.resolution_y
    strip.location = at
    strip.wrap_width = wrap
    # Blender 4.4 split the one alignment into the side that lies on the location (anchor) and
    # the way the lines are set.
    if hasattr(strip, "anchor_x"):
        strip.anchor_x = strip.alignment_x = ALIGN_X[align_x]
        strip.anchor_y = ALIGN_Y[align_y]
    else:
        strip.align_x = ALIGN_X[align_x]
        strip.align_y = ALIGN_Y[align_y]
    strip.color = as_shown(colour or (1.0, 1.0, 1.0)) + (1.0,)
    strip.use_outline = True
    strip.outline_color = (0.0, 0.0, 0.0, 0.85)
    strip.outline_width = 0.08
    strip.use_shadow = True
    strip.shadow_color = (0.0, 0.0, 0.0, 0.7)
    strip.blend_type = "ALPHA_OVER"
    return strip


def box(ctx, name, channel, first, last, at, width, height, colour):
    """A colour strip fills the picture; it is scaled to the box and moved to its place."""
    scene = ctx.scene
    strip = api.effect_strip(scene, name, "COLOR", channel, first, last)
    strip.color = as_shown(colour)
    strip.transform.scale_x = width
    strip.transform.scale_y = height
    strip.transform.offset_x = (at[0] - 0.5) * scene.render.resolution_x
    strip.transform.offset_y = (at[1] - 0.5) * scene.render.resolution_y
    strip.blend_type = "ALPHA_OVER"
    return strip


def fade(ctx, strip, item, first, last):
    """The item's opacity, rising over its fade-in from the frame before it appears and falling
    over its fade-out to the frame after it has gone."""
    opacity = item.get("opacity", 1.0)
    strip.blend_alpha = opacity
    length = last - first + 1
    fade_in = min(ctx.frame_at(item.get("fadeIn") or 0.0) - 1, length // 2)
    fade_out = min(ctx.frame_at(item.get("fadeOut") or 0.0) - 1, length // 2)
    keys = []
    if fade_in > 0:
        keys += [(first - 1, 0.0), (first - 1 + fade_in, opacity)]
    if fade_out > 0:
        keys += [(last + 1 - fade_out, opacity), (last + 1, 0.0)]
    key_opacity(strip, keys)


def key_opacity(strip, keys):
    for frame, opacity in keys:
        strip.blend_alpha = opacity
        strip.keyframe_insert("blend_alpha", frame=frame)


def as_shown(colour):
    """A colour of the plan (linear RGB) as the video editor takes it (sRGB)."""
    return tuple(
        12.92 * c if c <= 0.0031308 else 1.055 * c ** (1.0 / 2.4) - 0.055
        for c in (min(1.0, max(0.0, float(channel))) for channel in colour[:3]))


# ---- the arrow's picture ----

# An arrowhead that points up, in a square from -0.5 to 0.5: the tip, the right barb, the notch
# in its tail, the left barb.
ARROW = ((0.0, 0.5), (0.3, -0.45), (0.0, -0.22), (-0.3, -0.45))
ARROW_CORE = 0.78  # the share of the arrowhead, about its middle, that has its colour; the rest is a dark rim


def write_arrow(path, side, colour):
    """Writes the arrow as a PNG with transparency: the colour inside a dark rim, each pixel from
    four samples."""
    red, green, blue = (int(round(c * 255)) for c in as_shown(colour))
    middle = (0.0, sum(y for _, y in ARROW) / len(ARROW))
    core = [(middle[0] + (x - middle[0]) * ARROW_CORE, middle[1] + (y - middle[1]) * ARROW_CORE) for x, y in ARROW]
    rows = []
    for row in range(side):
        line = bytearray(b"\x00")  # the filter of the row: none
        for column in range(side):
            rim = inner = 0
            for dx, dy in ((0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75)):
                x = (column + dx) / side - 0.5
                y = 0.5 - (row + dy) / side
                if inside(ARROW, x, y):
                    rim += 1
                    if inside(core, x, y):
                        inner += 1
            if rim == 0:
                line += b"\x00\x00\x00\x00"
            else:
                share = inner / float(rim)
                line += bytes((int(red * share), int(green * share), int(blue * share), int(255 * rim / 4.0)))
        rows.append(bytes(line))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    with open(path, "wb") as stream:
        stream.write(b"\x89PNG\r\n\x1a\n")
        stream.write(chunk(b"IHDR", struct.pack(">IIBBBBB", side, side, 8, 6, 0, 0, 0)))
        stream.write(chunk(b"IDAT", zlib.compress(b"".join(rows), 9)))
        stream.write(chunk(b"IEND", b""))


def inside(polygon, x, y):
    """Whether a point lies in a polygon: a ray to the right crosses its edge an odd number of times."""
    odd = False
    for (ax, ay), (bx, by) in zip(polygon, polygon[1:] + polygon[:1]):
        if (ay > y) != (by > y) and x < ax + (bx - ax) * (y - ay) / (by - ay):
            odd = not odd
    return odd
