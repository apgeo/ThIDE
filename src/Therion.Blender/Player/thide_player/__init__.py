# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""The ThIDE player: builds a Blender scene from a scene plan and renders it.

The plan (scene-plan.json) is data: placed models, timed camera poses, the look, the output. The
player holds no camera mathematics and no text of its own. It is a small core and one module per
feature:

    core.py        reads and checks the plan, then hands each section to the module that plays it
    registry.py    finds the modules a plan names, here and in extra module folders
    api.py         what a module may use
    modules/       the player's own feature modules, one file each

Only bpy, mathutils and the standard library are used: a Blender packaged by a Linux distribution
runs on the system Python and has no numpy.
"""

PLAYER_VERSION = "1.14"

# The newest scene-plan version this player reads.
PLAN_VERSION = 1
