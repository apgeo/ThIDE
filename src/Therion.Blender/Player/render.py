# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

# Renders the scene plan that lies beside this file (scene-plan.json) with the ThIDE player in
# thide_player/.
#
#   blender -b --factory-startup --python render.py
#   blender -b --factory-startup --python render.py -- --plan other-plan.json --modules my_modules
#   blender --python render.py -- --build-only        builds the scene and leaves it open
#   blender -b --factory-startup --python render.py -- --stage compose
#                                                     writes the output again from the frames that
#                                                     are there, after a text of the plan changed
#
# The options are described in thide_player/core.py.

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

# The player's deepest file, and the frames of a film's shot, lie about 45 characters below this
# folder. A Windows without long paths (the default) reads no path longer than 260 characters:
# Python would then only say that it has no thide_player.
DEEPEST = os.path.join(HERE, "thide_player", "modules", "textured_materials.py")
if len(DEEPEST) > 260 and not os.path.isfile(DEEPEST):
    print("THIDE:error=the path of this folder is too long for Windows (%d characters, at most %d): "
          "move it to a folder with a shorter path, such as C:\\render" % (len(HERE), 260 - len(DEEPEST) + len(HERE)),
          flush=True)
    sys.exit(64)

sys.dont_write_bytecode = True  # leave the folder as it was written


def load_player():
    """The player's package, loaded from its own folder beside this file. This folder is not put
    on Python's module path: what else lies here (the plans, the meshes, whatever a bundle brought)
    is never imported in place of a module, whatever its name."""
    import importlib.util

    package = os.path.join(HERE, "thide_player")
    spec = importlib.util.spec_from_file_location(
        "thide_player", os.path.join(package, "__init__.py"), submodule_search_locations=[package])
    module = importlib.util.module_from_spec(spec)
    sys.modules["thide_player"] = module
    spec.loader.exec_module(module)
    return module


load_player()

from thide_player import core  # noqa: E402

core.main(HERE, script=os.path.abspath(__file__))
