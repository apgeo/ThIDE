"""The driver's sessions must not see the developer's real home.

GTK file choosers (Scaffold project, Generate report, ...) list the Recent files and folder
bookmarks they find through GLib's idea of the user's data/config directories and of $HOME. These
tests ask GLib/GTK, in a child process started with exactly the environment a session gives the app,
where those files would be, and assert every path lies inside the session's throw-away profile. They
only compute paths: no file of the real home is opened or read.
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1] / "tools" / "uidrive"))

import uidrive  # noqa: E402

_PROBE = r"""
import json, os
import gi
from gi.repository import GLib
out = {
    "home": GLib.get_home_dir(),
    "data": GLib.get_user_data_dir(),
    "config": GLib.get_user_config_dir(),
    "cache": GLib.get_user_cache_dir(),
    "state": GLib.get_user_state_dir(),
}
out["recent"] = os.path.join(out["data"], "recently-used.xbel")             # GTK 3 recent store
out["bookmarks"] = os.path.join(out["config"], "gtk-3.0", "bookmarks")      # GTK 3 folder bookmarks
out["legacy_bookmarks"] = os.path.join(out["home"], ".gtk-bookmarks")       # fallbacks GTK reads under $HOME
out["legacy_recent"] = os.path.join(out["home"], ".recently-used.xbel")
try:
    gi.require_version("Gtk", "3.0")
    from gi.repository import Gtk
    out["gtk_recent"] = Gtk.RecentManager.get_default().props.filename
except Exception:
    pass
print(json.dumps(out))
"""


def _session_env(tmp_path: Path) -> tuple[dict, Path]:
    sess = uidrive.Session("iso", root=tmp_path)
    prof = tmp_path / "iso" / "profile"
    uidrive.make_profile(prof)
    sess.state = {"display": ":99", "dbus": "unix:path=/nonexistent", "profile": str(prof)}
    return sess.env, prof


def _inside(path: str, root: Path) -> bool:
    return Path(os.path.realpath(path)).is_relative_to(os.path.realpath(root))


def test_session_env_points_every_user_directory_into_the_profile(tmp_path):
    env, prof = _session_env(tmp_path)
    for var, sub in (("HOME", "home"), ("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data"),
                     ("XDG_CACHE_HOME", "cache"), ("XDG_STATE_HOME", "state")):
        assert env[var] == str(prof / sub)
        assert (prof / sub).is_dir()  # a missing folder makes .NET fall back to ~/.config
    assert env["HOME"] != os.path.expanduser("~") or "HOME" not in os.environ


def test_glib_and_gtk_resolve_the_chooser_stores_inside_the_profile(tmp_path):
    pytest.importorskip("gi")
    env, prof = _session_env(tmp_path)
    r = subprocess.run([sys.executable, "-I", "-c", _PROBE], env=env, capture_output=True, text=True, timeout=60)
    if r.returncode != 0:
        pytest.skip("python3-gi not usable in the isolated environment: " + r.stderr.strip()[-200:])
    paths = json.loads(r.stdout)
    for name, path in paths.items():
        assert _inside(path, prof), f"{name} resolves outside the session profile: {path}"
    real_home = os.path.realpath(os.path.expanduser("~"))
    assert not any(os.path.realpath(p).startswith(real_home + os.sep) and not _inside(p, prof)
                   for p in paths.values())


def test_a_session_keeps_its_profile_unless_started_fresh(tmp_path):
    for tool in ("Xvfb", "dbus-daemon", "xdotool"):
        if not shutil.which(tool):
            pytest.skip(f"{tool} is not installed")
    sess = uidrive.Session("fresh-check", root=tmp_path)
    marker = tmp_path / "fresh-check" / "profile" / "data" / "left-over"
    try:
        sess.start_desktop(wm=False)
        marker.write_text("x")
        sess.stop()

        sess = uidrive.Session("fresh-check", root=tmp_path)
        sess.start_desktop(wm=False)
        assert marker.exists(), "a plain restart keeps the profile"
        sess.stop()

        sess = uidrive.Session("fresh-check", root=tmp_path)
        sess.start_desktop(wm=False, fresh=True)
        assert not marker.exists(), "--fresh starts from an empty profile"
        for sub in uidrive.PROFILE_DIRS:
            assert (tmp_path / "fresh-check" / "profile" / sub).is_dir()
    finally:
        sess.stop()


def test_fresh_refuses_to_wipe_a_profile_the_caller_named(tmp_path):
    sess = uidrive.Session("fresh-named", root=tmp_path)
    with pytest.raises(uidrive.UiError):
        sess.start_desktop(wm=False, profile=tmp_path / "mine", fresh=True)
    assert not (tmp_path / "mine").exists()


def test_services_the_bus_starts_on_demand_inherit_the_profile(tmp_path):
    """The portal behind the GTK file chooser is activated by the session bus and takes the bus
    daemon's environment, so the daemon (not just the app) must run inside the profile."""
    for tool in ("Xvfb", "dbus-daemon", "xdotool"):
        if not shutil.which(tool):
            pytest.skip(f"{tool} is not installed")
    sess = uidrive.Session("bus-env", root=tmp_path)
    try:
        sess.start_desktop(wm=False)
        prof = tmp_path / "bus-env" / "profile"
        environ = Path(f"/proc/{sess.state['dbus_pid']}/environ").read_bytes().split(b"\0")
        have = dict(e.decode().split("=", 1) for e in environ if b"=" in e)
        for var, sub in (("HOME", "home"), ("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data"),
                         ("XDG_CACHE_HOME", "cache"), ("XDG_STATE_HOME", "state")):
            assert have.get(var) == str(prof / sub), var
    finally:
        sess.stop()
