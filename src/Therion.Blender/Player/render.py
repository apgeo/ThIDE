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


def long_paths_off():
    """True on a Windows that reads no path longer than 260 characters (the default)."""
    if os.name != "nt":
        return False
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SYSTEM\CurrentControlSet\Control\FileSystem") as key:
            return winreg.QueryValueEx(key, "LongPathsEnabled")[0] != 1
    except OSError:
        return True


# The player's deepest file, and the frames of a film's shot, lie about 45 characters below this
# folder: deeper than that, Windows finds neither, and Python would only say that it has no
# thide_player.
DEEPEST = os.path.join(HERE, "thide_player", "modules", "textured_materials.py")
if len(DEEPEST) > 260 and long_paths_off():
    print("THIDE:error=the path of this folder is too long for Windows (%d characters, at most %d): "
          "move it to a folder with a shorter path, such as C:\\render" % (len(HERE), 260 - len(DEEPEST) + len(HERE)),
          flush=True)
    sys.exit(64)

sys.path.insert(0, HERE)
sys.dont_write_bytecode = True  # leave the folder as it was written

from thide_player import core  # noqa: E402

core.main(HERE, script=os.path.abspath(__file__))
