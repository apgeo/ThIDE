# ---- the player's start: its modules are loaded from SOURCES above instead of from files ----

import importlib.abc
import importlib.util
import os
import sys


class _FromSources(importlib.abc.MetaPathFinder, importlib.abc.Loader):
    def find_spec(self, name, path=None, target=None):
        if name not in SOURCES:
            return None
        is_package = any(other.startswith(name + ".") for other in SOURCES)
        return importlib.util.spec_from_loader(name, self, is_package=is_package)

    def create_module(self, spec):
        return None

    def exec_module(self, module):
        # Each source starts on the line after its opening quotes.
        source = SOURCES[module.__name__].split("\n", 1)[1]
        exec(compile(source, "<" + module.__name__ + ">", "exec"), module.__dict__)


sys.meta_path.insert(0, _FromSources())
sys.dont_write_bytecode = True  # leave an extra module folder as it was

from thide_player import core  # noqa: E402

core.main(os.path.dirname(os.path.abspath(__file__)), PLAN, FILES)
