# For the player tests: after the player has run its second stage (--stage compose), reports the
# text strips its video editor holds, and for each frame asked for the texts that show at it with
# their opacity there.
#
#   blender -b --factory-startup --python render.py --python reveal_strips.py -- --stage compose --strips out.json --strip-frames 1,2,3

import json
import sys

import bpy

from thide_player import api

scene = bpy.context.scene
texts = [strip for strip in api.strips(scene) if strip.type == "TEXT"]
curves = api.fcurves(scene)


def opacity_at(strip, frame):
    for curve in curves:
        if curve.data_path.endswith('["%s"].blend_alpha' % strip.name):
            return curve.evaluate(frame)
    return strip.blend_alpha


def last_frame(strip):
    return strip.frame_final_start + strip.frame_final_duration - 1


frames = [int(f) for f in sys.argv[sys.argv.index("--strip-frames") + 1].split(",") if f.strip()]
report = {
    "frameEnd": scene.frame_end,
    "strips": [{"name": strip.name, "text": strip.text, "first": strip.frame_final_start, "last": last_frame(strip),
                "channel": strip.channel} for strip in sorted(texts, key=lambda strip: (strip.frame_final_start, strip.name))],
    "frames": [{"frame": frame,
                "texts": [{"name": strip.name, "text": strip.text, "opacity": round(opacity_at(strip, frame), 5)}
                          for strip in sorted(texts, key=lambda strip: strip.name)
                          if strip.frame_final_start <= frame <= last_frame(strip)]}
               for frame in frames],
}
with open(sys.argv[sys.argv.index("--strips") + 1], "w", encoding="utf-8") as stream:
    json.dump(report, stream, indent=1)
