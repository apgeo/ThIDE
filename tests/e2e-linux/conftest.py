"""Fixtures for the black-box end-to-end UI tests (Linux, AT-SPI).

One virtual desktop (Xvfb + private D-Bus + xfwm4) is shared by the whole run; every test gets a
fresh app process with a fresh profile, so settings and dock layout never leak between tests or
into the developer's own ~/.config. On failure, a screenshot, the accessibility tree and the app
log tail are written to the artifacts directory ($E2E_ARTIFACTS, default tests/e2e-linux/artifacts).
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1] / "tools" / "uidrive"))

import uidrive  # noqa: E402

ARTIFACTS = Path(os.environ.get("E2E_ARTIFACTS", HERE / "artifacts"))
FIXTURES = HERE.parent / "ui-fixtures"


def _missing_prereqs() -> str | None:
    if not sys.platform.startswith("linux"):
        return "AT-SPI end-to-end tests run on Linux only"
    for tool in ("Xvfb", "dbus-daemon", "xdotool", "dotnet"):
        if not shutil.which(tool):
            return f"{tool} is not installed"
    try:
        uidrive.default_app_dll()
    except uidrive.UiError as e:
        return str(e)
    return None


@pytest.fixture(scope="session")
def desktop():
    reason = _missing_prereqs()
    if reason:
        pytest.skip(reason)
    s = uidrive.Session(f"e2e-{os.getpid()}")
    s.start_desktop(size="1600x1000")
    yield s
    s.stop()
    shutil.rmtree(s.dir, ignore_errors=True)


@pytest.fixture
def project(tmp_path) -> Path:
    """A writable copy of the invented demo project; returns its .thconfig."""
    dst = tmp_path / "demo-cave"
    shutil.copytree(FIXTURES / "demo-cave", dst)
    return dst / "demo.thconfig"


@pytest.fixture
def compiled_project(project) -> Path:
    """The demo project after a real Therion compile (.lox/.3d next to the .thconfig)."""
    if not shutil.which("therion"):
        pytest.skip("therion is not installed")
    subprocess.run(["therion", project.name], cwd=project.parent, check=True, capture_output=True, timeout=120)
    assert (project.parent / "demo.lox").exists()
    return project


@pytest.fixture
def launch(desktop, tmp_path, request):
    """launch(*files, cwd=None, automation=False) -> Session with ThIDE running.

    Files are passed relative to cwd (default: the first file's folder)."""

    def _launch(*files: Path, cwd: Path | None = None, raw_args: list[str] | None = None, **kw):
        cwd = cwd or (files[0].parent if files else tmp_path)
        args = raw_args if raw_args is not None else [os.path.relpath(f, cwd) for f in files]
        desktop.launch_app(args, cwd=cwd, profile=tmp_path / "profile", **kw)
        request.node._uidrive = desktop
        return desktop

    yield _launch
    desktop.close_app()


@pytest.hookimpl(hookwrapper=True)
def pytest_runtest_makereport(item, call):
    outcome = yield
    rep = outcome.get_result()
    s = getattr(item, "_uidrive", None)
    if rep.when != "call" or not rep.failed or s is None:
        return
    out = ARTIFACTS / item.name
    out.mkdir(parents=True, exist_ok=True)
    try:
        s.screenshot(out / "screen.png")
    except Exception as e:  # the app may have died
        (out / "screen.error.txt").write_text(str(e))
    try:
        (out / "tree.txt").write_text(s.tree(hidden=True))
    except Exception as e:
        (out / "tree.error.txt").write_text(str(e))
    (out / "app.log").write_text(s.log_tail(400))
    rep.sections.append(("uidrive artifacts", str(out)))
