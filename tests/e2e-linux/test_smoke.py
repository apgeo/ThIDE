"""Black-box smoke tests: the real app, real windowing, real WebKit, driven through AT-SPI."""

from __future__ import annotations

import pytest


def test_starts_with_welcome_page(launch):
    ui = launch()
    frame = ui.find("role:frame")
    assert frame.name.startswith("ThIDE")
    ui.wait("role:page tab;name:Welcome", state="selected")
    for menu in ("File", "View", "Edit", "Search", "Tools", "Build", "Settings", "Help"):
        ui.find(f"#Menu.{menu}")


def test_opens_project_passed_on_command_line(launch, project):
    ui = launch(project)
    ui.wait("role:frame;name~:demo\\.thconfig")
    ui.wait("role:page tab;name:demo.thconfig", state="selected")
    # Workspace explorer lists the sourced file and the survey it declares.
    ui.wait("role:label;name:demo.th")
    ui.wait("role:label;name:demo")


def test_opens_project_passed_as_absolute_path(launch, project):
    ui = launch(raw_args=[str(project)], cwd=project.parent.parent)
    ui.wait("role:page tab;name:demo.thconfig", timeout=10)


def test_toolbar_buttons_have_accessible_names(launch):
    ui = launch()
    tree = ui.tree("#Toolbar.Panel")
    assert "Avalonia.Controls" not in tree
    assert ui.find("#Toolbar.OpenFile").name  # localized name, e.g. "Open File (Ctrl+O)"


def test_menu_opens_and_closes(launch):
    ui = launch()
    ui.click("#Menu.File")
    ui.wait("role:menu item;name~:^Open File")
    ui.key("Escape")
    ui.wait("role:menu item;name~:^Open File", gone=True, timeout=5)


@pytest.mark.parametrize("tab", ["Diagnostics", "Compiler Output", "Generated Files", "Log"])
def test_bottom_tool_tabs_switch(launch, project, tab):
    ui = launch(project)
    ui.click(f"role:page tab;name:{tab}")
    ui.wait(f"role:page tab;name:{tab}", state="selected", timeout=5)


def _status_value(ui, label: str) -> str:
    """The status bar shows `Label:` followed by a separate value label."""
    lines = ui.tree().splitlines()
    for i, line in enumerate(lines):
        if f'[label] "{label}"' in line:
            return lines[i + 1].split('"')[1]
    raise AssertionError(f"status bar label {label!r} not found")


def test_typing_in_editor_updates_buffer(launch, project):
    ui = launch(project)
    ui.click("role:page tab;name:Source")
    before = int(_status_value(ui, "Length:"))
    ui.click("#Editor", mouse=True)
    ui.key("ctrl+End")
    ui.type_text("# e2e")
    for _ in range(20):
        if int(_status_value(ui, "Length:")) == before + 5:
            break
    assert int(_status_value(ui, "Length:")) == before + 5
    ui.key("ctrl+z", "ctrl+z", "ctrl+z", "ctrl+z", "ctrl+z")


def test_3d_viewer_receives_model(launch, compiled_project):
    """CaveView's page reports back to C# (the JS->C# bridge works on WebKitGTK)."""
    ui = launch(compiled_project)
    ui.click("role:page tab;name:3D Viewer")
    ui.wait("role:page tab;name:3D Viewer", state="selected", timeout=5)
    ui.wait("role:label;name:demo.lox", timeout=30)
    ui.wait("role:label;name~:^Loading", gone=True, timeout=5)


def test_3d_viewer_renders_model(launch, compiled_project):
    """The embedded WebKit view is laid out over the panel and CaveView draws (real screen capture)."""
    from PIL import ImageGrab

    ui = launch(compiled_project)
    ui.click("role:page tab;name:3D Viewer")
    ui.wait("role:label;name:demo.lox", timeout=30)
    x, y, w, h = ui.find("#Viewer3D.WebHost").bounds()
    img = ImageGrab.grab(xdisplay=ui.state["display"]).crop((x, y, x + w, y + h)).convert("RGB")
    colors = sorted(img.getcolors(w * h) or [], reverse=True)
    dominant = colors[0][0] / (w * h) if colors else 1.0
    assert dominant < 0.98, f"3D panel is a flat colour ({colors[:1]})"
