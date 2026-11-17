# Renders the scene plan that lies beside this file (scene-plan.json) with the ThIDE player in
# thide_player/.
#
#   blender -b --factory-startup --python render.py
#   blender -b --factory-startup --python render.py -- --plan other-plan.json --modules my_modules
#   blender --python render.py -- --build-only        builds the scene and leaves it open
#
# The options are described in thide_player/core.py.

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True  # leave the folder as it was written

from thide_player import core  # noqa: E402

core.main(HERE)
