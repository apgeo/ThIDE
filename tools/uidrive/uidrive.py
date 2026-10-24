#!/usr/bin/env python3
"""uidrive - drive the real ThIDE desktop app from outside, through AT-SPI (Linux).

A small "Playwright for the desktop": it starts ThIDE on a private virtual display (Xvfb) with its
own D-Bus session, window manager and throw-away profile, then finds controls in the app's
accessibility tree and clicks, types, reads and screenshots them. Nothing touches the developer's
own desktop or settings.

It is both a library (used by the end-to-end tests in tests/e2e-linux) and a CLI meant for people
and coding agents:

    uidrive.py start [--project demo.thconfig] [--automation]   # boots display + app, waits for the window
    uidrive.py tree [SELECTOR] [--depth N] [--raw]               # compact accessibility tree
    uidrive.py find SELECTOR                                     # list matches with state and bounds
    uidrive.py click SELECTOR [--mouse] [--double] [--right]     # AT-SPI action, or a real mouse click
    uidrive.py type TEXT [--into SELECTOR]  /  key ctrl+s ...    # real keyboard input
    uidrive.py set-text SELECTOR TEXT                            # set an editable's text directly
    uidrive.py get SELECTOR                                      # JSON: name, role, id, text, value, states
    uidrive.py wait SELECTOR [--gone] [--timeout 20]             # poll until (dis)appears
    uidrive.py shot [FILE] [--of SELECTOR]                       # PNG of the screen or one element
    uidrive.py call ui_tree '{"selector": "#Viewer3D.View"}'      # in-app automation server (start --automation)
    uidrive.py log [-n 50]  /  status  /  restart  /  stop

Selectors: `#Id` (AutomationId), `name:Text`, `name~:regex`, `role:page tab`, `text~:regex`, or a bare
string (= exact name). Combine with `;` (all must hold), chain descendants with ` >> `, and pick the
n-th match with a trailing `@n` (0-based). E.g. `role:page tab;name:3D Viewer`, `#MainMenu >> name:File`.

Requires: Xvfb, dbus-daemon, xdotool, python3-gi with Atspi 2.0, Pillow; xfwm4 is optional.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import signal
import subprocess
import sys
import time
import warnings
from dataclasses import dataclass
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DEFAULT_SIZE = "1600x1000"
DEFAULT_AUTOMATION_PORT = 47800
RUNTIME_ROOT = Path(os.environ.get("XDG_RUNTIME_DIR") or "/tmp") / "thide-uidrive"

# Roles that are pure layout in Avalonia's tree; compact output folds them away.
_LAYOUT_ROLES = {"panel", "filler", "unknown", "redundant object", "section"}
# Avalonia falls back to the control's type name when a control has no accessible name.
_TYPE_NAME = re.compile(r"^(Avalonia\.[\w.]+|TextBlock|AccessText|SelectableTextBlock)$")
_CLICK_ACTIONS = ("click", "press", "activate", "invoke", "toggle", "select", "expand or collapse")


class UiError(Exception):
    """A driver failure with a message fit to show an agent or a test report."""


# ---------------------------------------------------------------------------------------------
# selectors


@dataclass(frozen=True)
class _Term:
    key: str  # id | name | name~ | role | text~ | desc~
    value: str

    def matches(self, node: "Node") -> bool:
        if self.key == "id":
            return node.id == self.value
        if self.key == "name":
            return node.name == self.value
        if self.key == "role":
            return node.role.lower() == self.value.lower()
        if self.key == "name~":
            return re.search(self.value, node.name or "") is not None
        if self.key == "text~":
            return re.search(self.value, node.text() or "") is not None
        if self.key == "desc~":
            return re.search(self.value, node.description or "") is not None
        raise UiError(f"unknown selector key {self.key!r}")


@dataclass(frozen=True)
class Selector:
    steps: tuple[tuple[_Term, ...], ...]
    index: int | None
    source: str

    @staticmethod
    def parse(text: str) -> "Selector":
        src = text.strip()
        index = None
        m = re.search(r"@(\d+)$", src)
        if m:
            index, src = int(m.group(1)), src[: m.start()].rstrip()
        steps = []
        for part in src.split(" >> "):
            terms = []
            for raw in (t.strip() for t in part.split(";")):
                if not raw:
                    continue
                if raw.startswith("#"):
                    terms.append(_Term("id", raw[1:]))
                    continue
                mm = re.match(r"^(id|name~|name|role|text~|desc~):(.*)$", raw, re.S)
                terms.append(_Term(mm.group(1), mm.group(2)) if mm else _Term("name", raw))
            if not terms:
                raise UiError(f"empty selector step in {text!r}")
            steps.append(tuple(terms))
        return Selector(tuple(steps), index, text)


# ---------------------------------------------------------------------------------------------
# accessibility tree wrapper

_atspi = None


def _Atspi():
    """Imports Atspi lazily: it binds to the D-Bus session in the environment at first use."""
    global _atspi
    if _atspi is None:
        import gi

        gi.require_version("Atspi", "2.0")
        from gi.repository import Atspi

        _atspi = Atspi
    return _atspi


class Node:
    """A thin, exception-safe view of one Atspi.Accessible."""

    def __init__(self, acc):
        self.acc = acc

    def _safe(self, fn, default=None):
        try:
            return fn()
        except Exception:
            return default

    @property
    def name(self) -> str:
        return self._safe(self.acc.get_name, "") or ""

    @property
    def role(self) -> str:
        return self._safe(self.acc.get_role_name, "") or ""

    @property
    def id(self) -> str:
        return self._safe(self.acc.get_accessible_id, "") or ""

    @property
    def description(self) -> str:
        return self._safe(self.acc.get_description, "") or ""

    def children(self) -> list["Node"]:
        n = self._safe(self.acc.get_child_count, 0) or 0
        out = []
        for i in range(n):
            c = self._safe(lambda i=i: self.acc.get_child_at_index(i))
            if c is not None:
                out.append(Node(c))
        return out

    def states(self) -> list[str]:
        Atspi = _Atspi()
        ss = self._safe(self.acc.get_state_set)
        if ss is None:
            return []
        wanted = ("enabled", "showing", "visible", "focused", "selected", "checked", "expanded",
                  "editable", "pressed", "indeterminate", "busy", "modal")
        out = []
        for s in wanted:
            st = getattr(Atspi.StateType, s.upper(), None)
            if st is not None and ss.contains(st):
                out.append(s)
        return out

    def bounds(self) -> tuple[int, int, int, int] | None:
        Atspi = _Atspi()
        if self._safe(self.acc.get_component_iface) is None:
            return None
        r = self._safe(lambda: Atspi.Component.get_extents(self.acc, Atspi.CoordType.SCREEN))
        if r is None or r.width <= 0 or r.height <= 0:
            return None
        return (r.x, r.y, r.width, r.height)

    # Interface methods are called through their Atspi class: on the Accessible object itself
    # PyGObject resolves e.g. get_name/get_text to the (different) Accessible methods.
    def text(self) -> str:
        Atspi = _Atspi()
        if self._safe(self.acc.get_text_iface) is None:
            return ""
        n = self._safe(lambda: Atspi.Text.get_character_count(self.acc), 0) or 0
        return self._safe(lambda: Atspi.Text.get_text(self.acc, 0, n), "") or ""

    def value(self):
        Atspi = _Atspi()
        if self._safe(self.acc.get_value_iface) is None:
            return None
        return self._safe(lambda: Atspi.Value.get_current_value(self.acc))

    def actions(self) -> list[str]:
        Atspi = _Atspi()
        if self._safe(self.acc.get_action_iface) is None:
            return []
        n = self._safe(lambda: Atspi.Action.get_n_actions(self.acc), 0) or 0
        out = []
        for i in range(n):
            # Avalonia answers the older GetActionName (deprecated in libatspi), not GetName.
            with warnings.catch_warnings():
                warnings.simplefilter("ignore", DeprecationWarning)
                nm = self._safe(lambda: Atspi.Action.get_action_name(self.acc, i), "")
            out.append(nm or self._safe(lambda: Atspi.Action.get_name(self.acc, i), "") or "")
        return out

    def do_action(self, name: str | None = None) -> str | None:
        names = self.actions()
        if not names:
            return None
        pick = None
        if name is not None:
            pick = names.index(name) if name in names else None
            if pick is None:
                raise UiError(f"element has no action {name!r} (has {names})")
        else:
            for want in _CLICK_ACTIONS:
                if want in names:
                    pick = names.index(want)
                    break
            if pick is None:
                pick = 0
        _Atspi().Action.do_action(self.acc, pick)
        return names[pick]

    def describe(self) -> dict:
        d = {"role": self.role, "name": self.name, "id": self.id, "states": self.states()}
        b = self.bounds()
        if b:
            d["bounds"] = list(b)
        txt = self.text()
        if txt:
            d["text"] = txt
        val = self.value()
        if val is not None:
            d["value"] = val
        acts = self.actions()
        if acts:
            d["actions"] = acts
        if self.description:
            d["description"] = self.description
        d["children"] = len(self.children())
        return d

    def line(self) -> str:
        parts = [f"[{self.role}]"]
        if self.name:
            parts.append(json.dumps(self.name, ensure_ascii=False))
        if self.id:
            parts.append("#" + self.id)
        st = [s for s in self.states() if s not in ("enabled", "visible", "showing")]
        if "enabled" not in self.states():
            st.append("disabled")
        if "showing" not in self.states():
            st.append("hidden")
        if st:
            parts.append("{" + ",".join(st) + "}")
        b = self.bounds()
        if b:
            parts.append(f"@{b[0]},{b[1]} {b[2]}x{b[3]}")
        txt = self.text()
        if txt and txt != self.name:
            parts.append("text=" + json.dumps(txt[:80], ensure_ascii=False))
        return " ".join(parts)

    def is_noise(self) -> bool:
        """Layout-only nodes: no id worth targeting, no meaningful name, no action."""
        if self.role not in _LAYOUT_ROLES and self.role != "label":
            return False
        if self.id and not self.id.startswith("PART_"):
            return False
        if self.role == "label":
            # Unnamed text blocks report their type name ("TextBlock") as the name.
            return not self.name or _TYPE_NAME.match(self.name) is not None
        return True


# ---------------------------------------------------------------------------------------------
# session (Xvfb + D-Bus + WM + app)


def _alive(pid: int | None) -> bool:
    if not pid:
        return False
    try:
        os.kill(pid, 0)
    except OSError:
        return False
    try:  # a zombie child still answers kill(0)
        with open(f"/proc/{pid}/stat") as f:
            return f.read().split(") ")[-1][:1] != "Z"
    except OSError:
        return True


def _kill(pid: int | None, grace: float = 3.0) -> None:
    if not _alive(pid):
        return
    for sig in (signal.SIGTERM, signal.SIGKILL):
        try:
            os.killpg(pid, sig)
        except OSError:
            try:
                os.kill(pid, sig)
            except OSError:
                return
        end = time.time() + grace
        while time.time() < end and _alive(pid):
            time.sleep(0.1)
        if not _alive(pid):
            return


def _free_display() -> int:
    for n in range(90, 400):
        if not os.path.exists(f"/tmp/.X11-unix/X{n}") and not os.path.exists(f"/tmp/.X{n}-lock"):
            return n
    raise UiError("no free X display number")


def default_app_dll() -> Path:
    cands = [REPO / "ThIDE" / "bin" / c / "net8.0" / "ThIDE.dll" for c in ("Debug", "Release")]
    found = [c for c in cands if c.exists()]
    if not found:
        raise UiError("ThIDE is not built; run `dotnet build ThIDE/ThIDE.csproj -m:1` or pass --build")
    return max(found, key=lambda p: p.stat().st_mtime)


class Session:
    """One isolated desktop: a virtual display, a private session bus, a profile dir and the app.

    State lives in <runtime>/thide-uidrive/<name>/state.json so separate CLI invocations (and an
    agent reconnecting after a disconnect) find the same session.
    """

    def __init__(self, name: str = "default", root: Path | None = None):
        self.name = name
        self.dir = (root or RUNTIME_ROOT) / name
        self.state_file = self.dir / "state.json"
        self.state: dict = {}
        if self.state_file.exists():
            self.state = json.loads(self.state_file.read_text())

    # ---- lifecycle ----------------------------------------------------------------------

    def _save(self) -> None:
        self.dir.mkdir(parents=True, exist_ok=True)
        self.state_file.write_text(json.dumps(self.state, indent=2))

    @property
    def env(self) -> dict:
        e = dict(os.environ)
        e.update(DISPLAY=self.state["display"], DBUS_SESSION_BUS_ADDRESS=self.state["dbus"])
        prof = Path(self.state["profile"])
        e.update(XDG_CONFIG_HOME=str(prof / "config"), XDG_DATA_HOME=str(prof / "data"),
                 XDG_CACHE_HOME=str(prof / "cache"))
        e.pop("NO_AT_BRIDGE", None)
        return e

    def desktop_alive(self) -> bool:
        return bool(self.state) and _alive(self.state.get("xvfb_pid")) and _alive(self.state.get("dbus_pid"))

    def app_alive(self) -> bool:
        return bool(self.state) and _alive(self.state.get("app_pid"))

    def start_desktop(self, size: str = DEFAULT_SIZE, wm: bool = True, profile: Path | None = None) -> None:
        if self.desktop_alive():
            raise UiError(f"session {self.name!r} is already running (display {self.state['display']}); "
                          "use `stop` first or another --session name")
        for tool in ("Xvfb", "dbus-daemon", "xdotool"):
            if not shutil.which(tool):
                raise UiError(f"{tool} is not installed")
        if self.dir.exists():
            shutil.rmtree(self.dir / "shots", ignore_errors=True)
        self.dir.mkdir(parents=True, exist_ok=True)
        prof = profile or (self.dir / "profile")
        for sub in ("config", "data", "cache"):
            (prof / sub).mkdir(parents=True, exist_ok=True)

        n = _free_display()
        display = f":{n}"
        xvfb = subprocess.Popen(
            ["Xvfb", display, "-screen", "0", f"{size}x24", "-nolisten", "tcp"],
            stdout=open(self.dir / "xvfb.log", "wb"), stderr=subprocess.STDOUT, start_new_session=True)
        end = time.time() + 10
        while not os.path.exists(f"/tmp/.X11-unix/X{n}"):
            if time.time() > end or xvfb.poll() is not None:
                raise UiError(f"Xvfb did not start on {display}; see {self.dir / 'xvfb.log'}")
            time.sleep(0.1)

        out = subprocess.run(
            ["dbus-daemon", "--session", "--fork", "--print-address=1", "--print-pid=1"],
            capture_output=True, text=True, check=True, start_new_session=True,
            env={**os.environ, "DISPLAY": display}).stdout.split()
        self.state = {"name": self.name, "display": display, "size": size, "xvfb_pid": xvfb.pid,
                      "dbus": out[0], "dbus_pid": int(out[1]), "profile": str(prof), "wm_pid": None,
                      "app_pid": None}
        self._save()
        if wm and shutil.which("xfwm4"):
            w = subprocess.Popen(["xfwm4", "--compositor=off", "--sm-client-disable"], env=self.env,
                                 stdout=open(self.dir / "wm.log", "wb"), stderr=subprocess.STDOUT,
                                 start_new_session=True)
            self.state["wm_pid"] = w.pid
            self._save()
            time.sleep(1.0)

    def launch_app(self, args: list[str] | None = None, cwd: Path | None = None, dll: Path | None = None,
                   automation: bool = False, automation_port: int | None = None, extra_env: dict | None = None,
                   profile: Path | None = None, timeout: float = 60.0) -> None:
        if not self.desktop_alive():
            raise UiError("no desktop running; call start_desktop() / `uidrive start` first")
        if self.app_alive():
            raise UiError("the app is already running in this session; `restart` or `stop` it")
        if profile is not None:  # e.g. a fresh settings/layout dir per test
            for sub in ("config", "data", "cache"):
                (profile / sub).mkdir(parents=True, exist_ok=True)
            self.state["profile"] = str(profile)
        dll = dll or default_app_dll()
        env = self.env
        env.update(extra_env or {})
        if automation:
            env["THIDE_UI_AUTOMATION"] = "1"
            env["THIDE_UI_AUTOMATION_ENDPOINT"] = str(self.dir / "ui-automation-endpoint.json")
            if automation_port:
                env["THIDE_UI_AUTOMATION_PORT"] = str(automation_port)
            try:
                os.remove(self.dir / "ui-automation-endpoint.json")
            except OSError:
                pass
        log = open(self.dir / "app.log", "ab")
        log.write(f"\n==== launch {time.strftime('%H:%M:%S')} {dll} {args or []}\n".encode())
        log.flush()
        p = subprocess.Popen(["dotnet", str(dll), *(args or [])], cwd=str(cwd or Path.cwd()), env=env,
                             stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        self.state.update(app_pid=p.pid, app_args=args or [], app_cwd=str(cwd or Path.cwd()),
                          app_dll=str(dll), automation=automation, automation_port=automation_port)
        self._save()
        self._bind_env()
        end = time.time() + timeout
        while time.time() < end:
            if not self.app_alive():
                raise UiError(f"ThIDE exited during startup; see `uidrive log` ({self.dir / 'app.log'})")
            frame = self._main_frame()
            if frame is not None and frame.name:
                return
            time.sleep(0.5)
        raise UiError(f"ThIDE window did not appear within {timeout:.0f}s")

    def close_app(self) -> None:
        _kill(self.state.get("app_pid"))
        self.state["app_pid"] = None
        self._save()

    def stop(self) -> None:
        for key in ("app_pid", "wm_pid", "dbus_pid", "xvfb_pid"):
            _kill(self.state.get(key))
            self.state[key] = None
        if self.state:
            self._save()

    # ---- tree access --------------------------------------------------------------------

    def _bind_env(self) -> None:
        # Atspi reads these when it first connects; one process drives one session.
        os.environ["DISPLAY"] = self.state["display"]
        os.environ["DBUS_SESSION_BUS_ADDRESS"] = self.state["dbus"]
        bound = os.environ.get("_UIDRIVE_BOUND")
        if _atspi is not None and bound and bound != self.state["dbus"]:
            raise UiError("this process already drove another session; use a new process per session")
        os.environ["_UIDRIVE_BOUND"] = self.state["dbus"]

    def app(self) -> Node:
        if not self.app_alive():
            raise UiError("ThIDE is not running in this session (it may have crashed: see `uidrive log`)")
        self._bind_env()
        Atspi = _Atspi()
        pid = self.state["app_pid"]
        desk = Atspi.get_desktop(0)
        for i in range(desk.get_child_count()):
            a = desk.get_child_at_index(i)
            try:
                if a is not None and a.get_process_id() == pid:
                    try:
                        a.set_cache_mask(Atspi.Cache.NONE)
                    except Exception:
                        pass
                    return Node(a)
            except Exception:
                continue
        raise UiError("ThIDE is running but not (yet) on the accessibility bus")

    def _main_frame(self) -> Node | None:
        try:
            for c in self.app().children():
                if c.role == "frame":
                    return c
        except UiError:
            return None
        return None

    def walk(self, root: Node | None = None, limit: int = 30000):
        stack = [(root or self.app(), 0)]
        seen = 0
        while stack and seen < limit:
            node, depth = stack.pop()
            seen += 1
            yield node, depth
            kids = node.children()
            stack.extend((k, depth + 1) for k in reversed(kids))

    def find_all(self, selector: str | Selector, root: Node | None = None) -> list[Node]:
        sel = selector if isinstance(selector, Selector) else Selector.parse(selector)
        scopes = [root or self.app()]
        for step in sel.steps:
            nxt = []
            for scope in scopes:
                for node, depth in self.walk(scope):
                    if depth == 0 and scope is not None and len(sel.steps) > 1 and node.acc == scope.acc:
                        continue
                    if all(t.matches(node) for t in step):
                        nxt.append(node)
            scopes = nxt
        # Visible matches first, then document order.
        scopes.sort(key=lambda n: 0 if "showing" in n.states() else 1)
        if sel.index is not None:
            return scopes[sel.index: sel.index + 1]
        return scopes

    def find(self, selector: str, timeout: float = 5.0) -> Node:
        """First match, waiting up to `timeout` s for one to appear (like Playwright's auto-wait)."""
        end = time.time() + timeout
        while True:
            found = self.find_all(selector)
            if found:
                return found[0]
            if time.time() >= end:
                raise UiError(f"no element matches {selector!r}")
            time.sleep(0.3)

    def wait(self, selector: str, timeout: float = 20.0, gone: bool = False, state: str | None = None) -> Node | None:
        end = time.time() + timeout
        while True:
            found = self.find_all(selector)
            if state:
                found = [n for n in found if state in n.states()]
            if gone and not found:
                return None
            if not gone and found:
                return found[0]
            if time.time() >= end:
                what = "disappear" if gone else "appear" + (f" with state {state}" if state else "")
                raise UiError(f"{selector!r} did not {what} within {timeout:.0f}s")
            time.sleep(0.3)

    def tree(self, selector: str | None = None, depth: int = 999, raw: bool = False,
             hidden: bool = False) -> str:
        root = self.find(selector) if selector else self.app()
        lines = []
        stack = [(root, 0, 0)]  # node, real depth, printed indent
        while stack:
            node, d, indent = stack.pop()
            if d > depth:
                continue
            if not (raw or hidden) and node is not root and "showing" not in node.states():
                continue  # closed menus, inactive tabs, collapsed panes
            show = raw or not node.is_noise() or node is root
            if show:
                lines.append("  " * indent + node.line())
            child_indent = indent + 1 if show else indent
            stack.extend((k, d + 1, child_indent) for k in reversed(node.children()))
        return "\n".join(lines)

    # ---- input --------------------------------------------------------------------------

    def _xdotool(self, *args: str) -> None:
        r = subprocess.run(["xdotool", *args], env=self.env, capture_output=True, text=True)
        if r.returncode != 0:
            raise UiError(f"xdotool {' '.join(args)} failed: {r.stderr.strip()}")

    def mouse_click(self, x: int, y: int, button: int = 1, repeat: int = 1) -> None:
        self._xdotool("mousemove", "--sync", str(x), str(y), "click", "--repeat", str(repeat), str(button))

    def click(self, selector: str, mouse: bool = False, double: bool = False, right: bool = False,
              action: str | None = None, timeout: float = 5.0) -> str:
        node = self.find(selector, timeout=timeout)
        if not (mouse or double or right):
            done = node.do_action(action)
            if done:
                return f"{done}: {node.line()}"
        b = node.bounds()
        if not b:
            raise UiError(f"{selector!r} has no on-screen bounds to click")
        x, y = b[0] + b[2] // 2, b[1] + b[3] // 2
        self.mouse_click(x, y, button=3 if right else 1, repeat=2 if double else 1)
        return f"mouse@{x},{y}: {node.line()}"

    def focus(self, selector: str) -> None:
        node = self.find(selector, timeout=5)
        Atspi = _Atspi()
        if not node._safe(lambda: Atspi.Component.grab_focus(node.acc), False):
            b = node.bounds()
            if not b:
                raise UiError(f"cannot focus {selector!r}")
            self.mouse_click(b[0] + b[2] // 2, b[1] + b[3] // 2)

    def type_text(self, text: str, into: str | None = None) -> None:
        if into:
            self.focus(into)
            time.sleep(0.2)
        self._xdotool("type", "--delay", "15", "--", text)

    def key(self, *combos: str) -> None:
        self._xdotool("key", "--delay", "40", *combos)

    def set_text(self, selector: str, text: str) -> None:
        node = self.find(selector, timeout=5)
        Atspi = _Atspi()
        if node._safe(node.acc.get_editable_text_iface) is not None and \
                node._safe(lambda: Atspi.EditableText.set_text_contents(node.acc, text), False):
            return
        self.focus(selector)
        self.key("ctrl+a", "BackSpace")
        self.type_text(text)

    # ---- output -------------------------------------------------------------------------

    def screenshot(self, path: Path | None = None, of: str | None = None) -> Path:
        from PIL import ImageGrab

        img = ImageGrab.grab(xdisplay=self.state["display"])
        if of:
            b = self.find(of, timeout=5).bounds()
            if not b:
                raise UiError(f"{of!r} has no on-screen bounds")
            img = img.crop((b[0], b[1], b[0] + b[2], b[1] + b[3]))
        if path is None:
            (self.dir / "shots").mkdir(parents=True, exist_ok=True)
            path = self.dir / "shots" / (time.strftime("shot-%H%M%S-") + f"{int(time.time() * 1000) % 1000:03d}.png")
        path = Path(path)
        path.parent.mkdir(parents=True, exist_ok=True)
        img.save(path)
        return path

    def automation(self, timeout: float = 30.0) -> AutomationClient:
        if not self.state.get("automation"):
            raise UiError("the app was not started with the automation server (`start --automation`)")
        return AutomationClient(self.dir / "ui-automation-endpoint.json", timeout=timeout)

    def log_tail(self, n: int = 50) -> str:
        p = self.dir / "app.log"
        if not p.exists():
            return ""
        return "\n".join(p.read_text(errors="replace").splitlines()[-n:])


# ---------------------------------------------------------------------------------------------
# in-app automation server (MCP over streamable HTTP)


class AutomationClient:
    """Minimal MCP client for ThIDE's in-app UI-automation server (started with THIDE_UI_AUTOMATION=1).

    The server walks Avalonia's own automation tree, so it can read things AT-SPI can't (the code
    editor's text, property paths like DataContext.Status) and render controls without a display.
    """

    def __init__(self, endpoint_file: Path, timeout: float = 30.0):
        end = time.time() + timeout
        while not Path(endpoint_file).exists():
            if time.time() > end:
                raise UiError(f"no automation endpoint at {endpoint_file}; was the app started with --automation?")
            time.sleep(0.2)
        ep = json.loads(Path(endpoint_file).read_text())
        self.url, self.token = ep["url"], ep["token"]
        self.session_id: str | None = None
        self._id = 0
        self._rpc("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                 "clientInfo": {"name": "uidrive", "version": "1"}})
        self._rpc("notifications/initialized", None, notify=True)

    def _rpc(self, method: str, params, notify: bool = False):
        import urllib.request

        body = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            body["params"] = params
        if not notify:
            self._id += 1
            body["id"] = self._id
        req = urllib.request.Request(self.url, data=json.dumps(body).encode(), method="POST", headers={
            "Content-Type": "application/json", "Accept": "application/json, text/event-stream",
            "Authorization": f"Bearer {self.token}",
            **({"Mcp-Session-Id": self.session_id} if self.session_id else {})})
        with urllib.request.urlopen(req, timeout=120) as resp:
            self.session_id = resp.headers.get("Mcp-Session-Id") or self.session_id
            raw = resp.read().decode()
        if notify:
            return None
        msgs = [json.loads(l[5:]) for l in raw.splitlines() if l.startswith("data:")] if "data:" in raw \
            else [json.loads(raw)]
        for m in msgs:
            if m.get("id") == body["id"]:
                if "error" in m:
                    raise UiError(f"{method}: {m['error'].get('message')}")
                return m["result"]
        raise UiError(f"{method}: no response")

    def tools(self) -> list[dict]:
        return self._rpc("tools/list", {})["tools"]

    def call(self, tool: str, **args):
        """Calls a ui_* tool; returns its text, or raises UiError when the tool reports an error.

        Image results are returned as (text, [png bytes...])."""
        import base64

        res = self._rpc("tools/call", {"name": tool, "arguments": args})
        texts = [c["text"] for c in res.get("content", []) if c.get("type") == "text"]
        images = [base64.b64decode(c["data"]) for c in res.get("content", []) if c.get("type") == "image"]
        if res.get("isError"):
            raise UiError(f"{tool}: {' '.join(texts)}")
        text = "\n".join(texts)
        return (text, images) if images else text


# ---------------------------------------------------------------------------------------------
# CLI


def _cli(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(prog="uidrive", description=__doc__.split("\n\n")[0])
    ap.add_argument("--session", default=os.environ.get("UIDRIVE_SESSION", "default"),
                    help="session name (default: $UIDRIVE_SESSION or 'default')")
    sub = ap.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("start", help="start display + app and wait for the main window")
    s.add_argument("--project", action="append", default=[], help="file to open (repeatable); relative to --cwd")
    s.add_argument("--cwd", default=None, help="working directory for the app (default: the project file's dir)")
    s.add_argument("--size", default=DEFAULT_SIZE)
    s.add_argument("--no-wm", action="store_true", help="don't run a window manager")
    s.add_argument("--profile", default=None, help="reuse this profile dir (settings/layout) instead of a fresh one")
    s.add_argument("--dll", default=None, help="ThIDE.dll to run (default: newest Debug/Release build)")
    s.add_argument("--build", action="store_true", help="dotnet build the app first")
    s.add_argument("--automation", action="store_true", help="also enable the in-app UI-automation MCP server")
    s.add_argument("--automation-port", type=int, default=None,
                   help=f"fixed port for the automation server (default {DEFAULT_AUTOMATION_PORT} for the "
                        "'default' session, so an MCP client config can point at it; else a free port)")
    s.add_argument("--timeout", type=float, default=60)

    r = sub.add_parser("restart", help="restart only the app (same display/profile/args)")
    r.add_argument("--timeout", type=float, default=60)
    sub.add_parser("stop", help="stop app, display and bus")
    sub.add_parser("status", help="JSON session state")

    t = sub.add_parser("tree", help="print the (compact) accessibility tree")
    t.add_argument("selector", nargs="?")
    t.add_argument("--depth", type=int, default=999)
    t.add_argument("--raw", action="store_true", help="include layout-only and hidden nodes")
    t.add_argument("--hidden", action="store_true", help="include hidden subtrees (inactive tabs, closed menus)")

    f = sub.add_parser("find", help="list matching elements")
    f.add_argument("selector")
    f.add_argument("--limit", type=int, default=20)

    g = sub.add_parser("get", help="JSON details of the first match")
    g.add_argument("selector")

    c = sub.add_parser("click", help="activate an element (AT-SPI action, or real mouse)")
    c.add_argument("selector")
    c.add_argument("--mouse", action="store_true")
    c.add_argument("--double", action="store_true")
    c.add_argument("--right", action="store_true")
    c.add_argument("--action", default=None, help="a specific AT-SPI action name")
    c.add_argument("--timeout", type=float, default=5)

    ty = sub.add_parser("type", help="type text with the real keyboard")
    ty.add_argument("text")
    ty.add_argument("--into", default=None, help="focus this element first")

    k = sub.add_parser("key", help="press keys, xdotool syntax (ctrl+s, Escape, Return, F5 ...)")
    k.add_argument("combos", nargs="+")

    st = sub.add_parser("set-text", help="replace an editable element's text")
    st.add_argument("selector")
    st.add_argument("text")

    w = sub.add_parser("wait", help="wait for an element to appear (or --gone)")
    w.add_argument("selector")
    w.add_argument("--gone", action="store_true")
    w.add_argument("--state", default=None, help="also require this state (selected, focused, checked...)")
    w.add_argument("--timeout", type=float, default=20)

    sh = sub.add_parser("shot", help="screenshot (PNG) of the display or one element")
    sh.add_argument("file", nargs="?")
    sh.add_argument("--of", default=None)

    cl = sub.add_parser("call", help="call an in-app automation tool (needs `start --automation`)")
    cl.add_argument("tool", help="e.g. ui_tree, ui_get, ui_invoke, ui_menu, ui_key, ui_type, ui_screenshot")
    cl.add_argument("args", nargs="?", default="{}", help="JSON object of tool arguments")
    cl.add_argument("--png", default=None, help="where to save an image result")
    sub.add_parser("tools", help="list the in-app automation tools")
    sub.add_parser("mcp-headers", help="print the auth header JSON for an MCP client's headersHelper")

    lg = sub.add_parser("log", help="tail the app's stdout/stderr")
    lg.add_argument("-n", type=int, default=50)

    a = ap.parse_args(argv)
    sess = Session(a.session)

    if a.cmd == "start":
        if a.build:
            subprocess.run(["dotnet", "build", str(REPO / "ThIDE" / "ThIDE.csproj"), "-m:1",
                            "-p:TherionAutoIncrementBuild=false", "-v:q", "-nologo"], check=True)
        files = [str(Path(p).resolve()) for p in a.project]
        cwd = Path(a.cwd).resolve() if a.cwd else (Path(files[0]).parent if files else Path.cwd())
        args = files
        sess.start_desktop(size=a.size, wm=not a.no_wm, profile=Path(a.profile).resolve() if a.profile else None)
        try:
            port = a.automation_port or (DEFAULT_AUTOMATION_PORT if a.session == "default" else None)
            sess.launch_app(args, cwd=cwd, dll=Path(a.dll) if a.dll else None, automation=a.automation,
                            automation_port=port if a.automation else None, timeout=a.timeout)
        except Exception:
            print(sess.log_tail(30), file=sys.stderr)
            raise
        out = {k: sess.state[k] for k in ("name", "display", "app_pid", "profile")}
        out["dir"] = str(sess.dir)
        if a.automation:
            out["automation_endpoint"] = str(sess.dir / "ui-automation-endpoint.json")
        print(json.dumps(out, indent=2))
    elif a.cmd == "restart":
        sess.close_app()
        sess.launch_app(sess.state.get("app_args"), cwd=Path(sess.state.get("app_cwd", ".")),
                        dll=Path(sess.state["app_dll"]), automation=sess.state.get("automation", False),
                        automation_port=sess.state.get("automation_port"), timeout=a.timeout)
        print(f"restarted, pid {sess.state['app_pid']}")
    elif a.cmd == "stop":
        sess.stop()
        print("stopped")
    elif a.cmd == "status":
        st_ = dict(sess.state)
        st_.update(desktop_alive=sess.desktop_alive(), app_alive=sess.app_alive(), dir=str(sess.dir))
        print(json.dumps(st_, indent=2))
    elif a.cmd == "tree":
        print(sess.tree(a.selector, depth=a.depth, raw=a.raw, hidden=a.hidden))
    elif a.cmd == "find":
        found = sess.find_all(a.selector)
        for n in found[: a.limit]:
            print(n.line())
        if len(found) > a.limit:
            print(f"... {len(found) - a.limit} more")
        if not found:
            print(f"no match for {a.selector!r}", file=sys.stderr)
            return 1
    elif a.cmd == "get":
        print(json.dumps(sess.find(a.selector).describe(), indent=2, ensure_ascii=False))
    elif a.cmd == "click":
        print(sess.click(a.selector, mouse=a.mouse, double=a.double, right=a.right, action=a.action,
                         timeout=a.timeout))
    elif a.cmd == "type":
        sess.type_text(a.text, into=a.into)
    elif a.cmd == "key":
        sess.key(*a.combos)
    elif a.cmd == "set-text":
        sess.set_text(a.selector, a.text)
    elif a.cmd == "wait":
        n = sess.wait(a.selector, timeout=a.timeout, gone=a.gone, state=a.state)
        print("gone" if n is None else n.line())
    elif a.cmd == "shot":
        print(sess.screenshot(Path(a.file) if a.file else None, of=a.of))
    elif a.cmd == "mcp-headers":
        ep = json.loads((sess.dir / "ui-automation-endpoint.json").read_text())
        print(json.dumps({"Authorization": f"Bearer {ep['token']}"}))
    elif a.cmd == "tools":
        for t in sess.automation().tools():
            print(f"{t['name']}: {t.get('description', '')}")
    elif a.cmd == "call":
        res = sess.automation().call(a.tool, **json.loads(a.args))
        if isinstance(res, tuple):
            text, images = res
            for i, png in enumerate(images):
                out = Path(a.png) if a.png and i == 0 else sess.dir / "shots" / f"{a.tool}-{int(time.time() * 1000)}-{i}.png"
                out.parent.mkdir(parents=True, exist_ok=True)
                out.write_bytes(png)
                print(out)
            res = text
        if res:
            print(res)
    elif a.cmd == "log":
        print(sess.log_tail(a.n))
    return 0


def main() -> None:
    try:
        sys.exit(_cli(sys.argv[1:]))
    except UiError as e:
        print(f"uidrive: {e}", file=sys.stderr)
        sys.exit(2)


if __name__ == "__main__":
    main()
