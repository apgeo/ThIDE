"""Checks the menu paths written in the user guide against the menus the app really has.

  check-guide-menu-paths.py [--repo ROOT] [--guide DIR]

Every bold path in docs/user-guide, written '**A → B → C**' or '**A ▸ B**', is resolved against the
main menu: ThIDE/Views/MainWindow.axaml gives the tree, and the labels come from the English values
in ThIDE/Resources/Strings.resx (through the {l:Loc Key} headers, or through the
MainWindowViewModel label properties that the {Binding MenuXxx} headers use). A path that names an
item the menu does not have fails the run (exit code 1), with the line it is on.

How a path is read:
  * The first segment must be a top-level menu (File, View, Edit, Search, Tools, Compile, Settings,
    Help). A first segment that is a panel from the View menu ("Overview → Audit") is a path inside
    that panel and is not checked here. Anything else is reported.
  * "Settings → <page>" may also name a page of the Settings window (the guide's convention for
    opening Settings and picking a page); deeper segments there are controls, and only have to
    appear in some label of the app.
  * Items that are filled in at run time (recent files, macros, tasks, build targets) accept any
    further segment.
  * Access-key underscores, a trailing "…" and a closing parenthetical ("Overview (Dashboard / …)")
    may be left out; the case of the label may not.
"""
import argparse
import difflib
import os
import re
import sys
import xml.etree.ElementTree as ET

AV = "{https://github.com/avaloniaui}"
XNAME = "{http://schemas.microsoft.com/winfx/2006/xaml}Name"

# Menu items whose children are built in code or from data, so the .axaml lists none or one placeholder.
DYNAMIC_KEYS = {
    "Menu_File_Recent", "Menu_File_RecentDirectories",
    "Menu_Tools_Macros", "Menu_Tools_Tasks", "Menu_Build_Target",
}

SEPARATOR = re.compile(r"\s(?:→|▸)\s")
BOLD = re.compile(r"\*\*(?=\S)(.+?)(?<=\S)\*\*")
CODE_SPAN = re.compile(r"`[^`]*`")


def norm(label):
    """Label as the user reads it: no access-key underscore, no trailing ellipsis, trimmed."""
    s = re.sub(r"_(?=[^_])", "", label.replace("__", "\0")).replace("\0", "_")
    return s.rstrip("….").strip()


class Node:
    def __init__(self, labels, key=None):
        self.labels = labels        # normalized texts the item can show; empty when only known at run time
        self.key = key              # resx key when the header came from one
        self.children = []

    @property
    def label(self):
        return self.labels[0] if self.labels else None

    def find(self, label):
        for c in self.children:
            if any(l == label or re.sub(r"\s*\(.*\)$", "", l) == label for l in c.labels):
                return c
        return None


def read_resx(path):
    values = {}
    for d in ET.parse(path).getroot().iter("data"):
        v = d.find("value")
        if v is not None and v.text is not None:
            values[d.get("name")] = v.text
    return values


def read_vm_labels(*paths):
    """MainWindowViewModel (and its partials): public string MenuXxx => L("Key", "fallback").
    A property that chooses between several L(...) texts lists them all; templates ("Undo {0}") are left out."""
    labels = {}
    for path in paths:
        with open(path, encoding="utf-8") as f:
            text = f.read()
        for m in re.finditer(r"public string (\w+)\s*=>([^;]*);", text):
            found = re.findall(r'\bL\("([^"]+)",\s*"([^"]*)"\)', m.group(2))
            if found:
                labels[m.group(1)] = found
    return labels


def build_menu(axaml, resx, vm_labels):
    root = ET.parse(axaml).getroot()
    menu = next((e for e in root.iter(AV + "Menu") if e.get(XNAME) == "MainMenu"), None)
    if menu is None:
        raise SystemExit("MainWindow.axaml has no Menu named MainMenu")

    def header(el):
        h = el.get("Header") or ""
        m = re.fullmatch(r"\{l:Loc\s+(\w+)\}", h)
        if m:
            key = m.group(1)
            return ([norm(resx[key])] if key in resx else []), key
        m = re.fullmatch(r"\{Binding\s+(\w+)\}", h)
        if m and m.group(1) in vm_labels:
            found = vm_labels[m.group(1)]
            texts = [resx.get(k, fb) for k, fb in found]
            return [norm(t) for t in texts if "{" not in t], found[0][0]
        if h and not h.startswith("{"):
            return [norm(h)], None
        return [], None              # bound to something computed at run time

    def walk(el, parent):
        for child in el:
            if child.tag != AV + "MenuItem":
                continue
            labels, key = header(child)
            node = Node(labels, key)
            parent.children.append(node)
            walk(child, node)

    top = Node([""], None)
    walk(menu, top)
    return top


def settings_pages(prefs_vm, resx):
    with open(prefs_vm, encoding="utf-8") as f:
        keys = re.findall(r'new\("\w+",\s*(?:Resources\.)?Tr\.Get\("(Pref_\w+)"\)', f.read())
    return {norm(resx[k]) for k in keys if k in resx}


class Checker:
    def __init__(self, repo):
        resx = read_resx(os.path.join(repo, "ThIDE", "Resources", "Strings.resx"))
        vm_dir = os.path.join(repo, "ThIDE", "ViewModels")
        vm = read_vm_labels(*(os.path.join(vm_dir, f) for f in sorted(os.listdir(vm_dir))
                              if f.startswith("MainWindowViewModel") and f.endswith(".cs")))
        self.menu = build_menu(os.path.join(repo, "ThIDE", "Views", "MainWindow.axaml"), resx, vm)
        self.pages = settings_pages(os.path.join(repo, "ThIDE", "ViewModels", "PreferencesViewModel.cs"), resx)
        self.all_labels = [norm(v).lower() for v in resx.values()]
        view = self.menu.find("View")
        self.panels = set()
        if view is not None:
            for c in view.children:
                if c.label:
                    self.panels.add(re.sub(r"\s*\(.*\)$", "", c.label))

    def check_path(self, segments):
        """Returns None when the path exists, else a sentence saying where it stops."""
        segs = [norm(s) for s in segments]
        top = self.menu.find(segs[0])
        if top is None:
            if segs[0] in self.panels:
                return None
            menus = ", ".join(c.label for c in self.menu.children if c.label)
            return f"'{segs[0]}' is not a top-level menu ({menus})"
        node = top
        for i, seg in enumerate(segs[1:], 1):
            if node.key in DYNAMIC_KEYS:
                return None
            nxt = node.find(seg)
            if nxt is None:
                if segs[0] == "Settings" and i == 1 and seg in self.pages:
                    return self._check_controls(segs[2:])
                options = [l for c in node.children for l in c.labels]
                if segs[0] == "Settings" and i == 1:
                    options += sorted(self.pages)
                near = difflib.get_close_matches(seg, options, n=2, cutoff=0.5)
                hint = f"; closest: {', '.join(near)}" if near else f"; it has: {', '.join(options)}"
                return f"'{seg}' is not in {segs[i - 1]}{hint}"
            node = nxt
        return None

    def _check_controls(self, rest):
        for seg in rest:
            if seg.lower() not in " \n".join(self.all_labels):
                return f"'{seg}' does not appear in any label of the app"
        return None


BLOCK_START = re.compile(r"\s*(?:[-*+]\s|\d+[.)]\s|\||#)")


def paragraphs(text):
    """[(first line number, text)]: a bold span that wraps onto the next line stays one span. Blank
    lines, list items, table rows and headings start a new paragraph; code blocks are left out."""
    result = []
    in_fence = False
    current = None
    for n, raw in enumerate(text.splitlines(), 1):
        if raw.lstrip().startswith("```"):
            in_fence = not in_fence
            current = None
            continue
        if in_fence:
            continue
        line = re.sub(r"^\s*(?:>\s*)+", "", raw).strip()
        if not line:
            current = None
        elif current is None or BLOCK_START.match(raw):
            current = [n, line]
            result.append(current)
        else:
            current[1] += " " + line
    return result


def paths_in(text):
    """(line number, bold text, segments) for each bold arrow path outside code."""
    for n, para in paragraphs(text):
        for m in BOLD.finditer(CODE_SPAN.sub("", para)):
            parts = SEPARATOR.split(m.group(1))
            if len(parts) > 1:
                yield n, m.group(1), parts


def check_files(checker, files):
    problems = []
    for path in files:
        with open(path, encoding="utf-8") as f:
            text = f.read()
        for n, shown, parts in paths_in(text):
            why = checker.check_path(parts)
            if why:
                problems.append((path, n, shown, why))
    return problems


def main(argv=None):
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--repo", default=os.path.normpath(os.path.join(here, "..", "..")))
    ap.add_argument("--guide", help="folder with the guide pages (default: <repo>/docs/user-guide)")
    args = ap.parse_args(argv)
    guide = args.guide or os.path.join(args.repo, "docs", "user-guide")
    files = sorted(os.path.join(guide, f) for f in os.listdir(guide) if f.endswith(".md"))
    problems = check_files(Checker(args.repo), files)
    for path, n, shown, why in problems:
        print(f"{os.path.relpath(path, args.repo)}:{n}: **{shown}** — {why}")
    print(f"{len(files)} pages checked, {len(problems)} menu path(s) not found")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
