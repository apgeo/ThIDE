"""Tests for check-guide-menu-paths.py: run with  python3 -m unittest discover -s tests/ci -p 'test_*.py'

The menu comes from the repository's real MainWindow.axaml and Strings.resx, so these also fail when a
label the tests rely on is renamed; the last test is the guide itself.
"""
import importlib.util
import os
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))

spec = importlib.util.spec_from_file_location("check_guide_menu_paths", os.path.join(HERE, "check-guide-menu-paths.py"))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)


class MenuPathTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.checker = check.Checker(REPO)

    def problems(self, markdown):
        with tempfile.TemporaryDirectory() as d:
            page = os.path.join(d, "page.md")
            with open(page, "w", encoding="utf-8") as f:
                f.write(markdown)
            return check.check_files(self.checker, [page])

    def test_real_paths_resolve(self):
        text = (
            "Use **File → Save As…**, **Compile → Compile This thconfig (Focused Tab)**, "
            "**Tools → Calculators → Unit Converter…** or **View → Layout → Restore Default Layout**.\n"
            "Also **Edit → Undo multi-file change** and **Help → About ThIDE**, and with the arrow ▸: **Search ▸ Bookmarks…**."
        )
        self.assertEqual([], self.problems(text))

    def test_a_path_with_a_wrong_item_is_reported_with_its_line(self):
        found = self.problems("fine\n\nOpen **Compile → Compile Everything** now.")
        self.assertEqual(1, len(found))
        _, line, shown, why = found[0]
        self.assertEqual(3, line)
        self.assertEqual("Compile → Compile Everything", shown)
        self.assertIn("'Compile Everything' is not in Compile", why)

    def test_a_menu_that_does_not_exist_is_reported(self):
        found = self.problems("**Build → Compile**")
        self.assertEqual(1, len(found))
        self.assertIn("'Build' is not a top-level menu", found[0][3])

    def test_the_old_menu_labels_the_guide_used_to_have_fail(self):
        for old in ("**View → Reset to Default Layout**", "**Help → Welcome**", "**Tools → Link survey file…**",
                    "**View → Strict parser mode**", "**Compile → Open last output folder**"):
            self.assertEqual(1, len(self.problems(old)), old)

    def test_the_case_of_a_label_matters_but_ellipsis_and_the_parenthetical_do_not(self):
        self.assertEqual(1, len(self.problems("**Tools → New trip…**")))
        self.assertEqual([], self.problems("**Tools → New Trip**"))
        self.assertEqual([], self.problems("**View → Overview**"))
        self.assertEqual([], self.problems("**Tools → Generate Survey Report**"))

    def test_settings_pages_and_controls(self):
        self.assertEqual([], self.problems("**Settings → External Tools**"))
        self.assertEqual([], self.problems("**Settings → Preferences…** and **Settings → Debug → Enable web developer tools**"))
        self.assertEqual(1, len(self.problems("**Settings → Build & Output**")))
        self.assertEqual(1, len(self.problems("**Settings → Visualization → A switch that does not exist**")))

    def test_items_filled_in_at_run_time_accept_anything_below_them(self):
        self.assertEqual([], self.problems("**File → Recent Files → demo.thconfig**"))
        self.assertEqual([], self.problems("**Compile → Build Target → plan-pdf**"))
        self.assertEqual([], self.problems("**Tools → Macros → Release check**"))

    def test_a_panel_path_is_not_a_menu_path(self):
        self.assertEqual([], self.problems("**Overview → Audit**, **Object Browser → Scraps**"))

    def test_a_bold_span_that_wraps_a_line_and_text_outside_bold_or_in_code(self):
        self.assertEqual([], self.problems("A **long bold\nstretch**, then **File → Open Folder…** here."))
        self.assertEqual(1, len(self.problems("A **long bold\nstretch**, then **File → Open Fold** here.")))
        self.assertEqual([], self.problems("Plain File → Nothing, `**Nowhere → Nothing**` and *Tools → Nothing*."))
        self.assertEqual([], self.problems("```\n**Nowhere → Nothing**\n```\n"))

    def test_arrows_inside_a_label_are_not_separators(self):
        self.assertEqual([], self.problems("**Tools → Import GPX Waypoints (→ fix)…**"))

    def test_the_user_guide_has_no_broken_menu_path(self):
        guide = os.path.join(REPO, "docs", "user-guide")
        pages = [os.path.join(guide, f) for f in sorted(os.listdir(guide)) if f.endswith(".md")]
        problems = check.check_files(self.checker, pages)
        self.assertEqual([], [f"{os.path.basename(p)}:{n}: {s} — {w}" for p, n, s, w in problems])


if __name__ == "__main__":
    unittest.main()
