# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

"""Finds the feature modules a plan names.

A feature is one Python file named after it. The player's own are the package
thide_player.modules; extra module folders hold any others, so a feature is added by adding a file,
never by editing the core. A folder is named on the command line (--modules DIR, any folder) or in
the plan ("moduleFolders", only folders beside the plan). A name found in two places is an error:
nothing replaces a module silently.
"""

import importlib
import importlib.util
import os
import re
import traceback

from . import api

BUILT_IN = "thide_player.modules"
NAME = re.compile(r"[a-z][a-z0-9_]*\Z")


class Registry:
    def __init__(self, extra_folders=()):
        self.extra_folders = []
        for folder in extra_folders:
            if not os.path.isdir(folder):
                raise api.PlanError("module folder not found: %s" % folder)
            self.extra_folders.append(folder)

    def load(self, name):
        """The module that plays the feature, loaded and checked."""
        if not isinstance(name, str) or not NAME.match(name):
            raise api.PlanError(
                'features: "%s" is not a module name (lower-case letters, digits and "_")' % (name,))
        places = []
        if importlib.util.find_spec(BUILT_IN + "." + name) is not None:
            places.append(BUILT_IN)
        for folder in self.extra_folders:
            path = os.path.join(folder, name + ".py")
            if os.path.isfile(path):
                places.append(path)
        if not places:
            raise api.PlanError(
                'the plan needs the module "%s", which this player does not have%s'
                % (name, "".join("; looked in " + f for f in self.extra_folders)))
        if len(places) > 1:
            raise api.PlanError('the module "%s" is defined more than once: %s' % (name, ", ".join(places)))

        try:
            if places[0] == BUILT_IN:
                module = importlib.import_module(BUILT_IN + "." + name)
            else:
                spec = importlib.util.spec_from_file_location("thide_player_extra." + name, places[0])
                module = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(module)
        except Exception as error:
            traceback.print_exc()
            raise api.PlanError('the module "%s" (%s) cannot be loaded: %s: %s'
                                % (name, places[0], type(error).__name__, error))
        if not callable(getattr(module, "build", None)):
            raise api.PlanError('the module "%s" (%s) has no build(ctx, section) function' % (name, places[0]))
        return module
