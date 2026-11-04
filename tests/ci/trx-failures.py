"""Lists the failed tests of one or more TRX files.

  trx-failures.py [--filter-out FILE] [--annotate] [--summary TITLE] <file.trx>...

--filter-out FILE  writes a `dotnet test --filter` expression that selects exactly the failed tests
                   (empty file when nothing failed), for a re-run.
--annotate         prints a GitHub Actions ::error:: line per failed test, so failures show on the
                   run page without downloading logs.
--summary TITLE    appends a Markdown table of the failures to $GITHUB_STEP_SUMMARY when it is set.
Exit code is always 0; the caller decides what a failure means.
"""
import os
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def failures(path):
    root = ET.parse(path).getroot()
    methods = {}
    for unit in root.iterfind(".//t:TestDefinitions/t:UnitTest", NS):
        m = unit.find("t:TestMethod", NS)
        if m is not None:
            methods[unit.get("id")] = f"{m.get('className')}.{m.get('name')}"
    for result in root.iterfind(".//t:Results/t:UnitTestResult", NS):
        if result.get("outcome") != "Failed":
            continue
        message = result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS).strip()
        yield methods.get(result.get("testId"), result.get("testName")), result.get("testName"), message


def main(argv):
    filter_out = annotate = summary = None
    files = []
    it = iter(argv)
    for arg in it:
        if arg == "--filter-out":
            filter_out = next(it)
        elif arg == "--annotate":
            annotate = True
        elif arg == "--summary":
            summary = next(it)
        else:
            files.append(arg)
    found = [f for path in files if os.path.exists(path) for f in failures(path)]

    if filter_out is not None:
        methods = sorted({method for method, _, _ in found})
        with open(filter_out, "w", encoding="utf-8") as out:
            out.write("|".join(f"FullyQualifiedName={m}" for m in methods))
    for _, name, message in found:
        if annotate:
            first = message.splitlines()[0] if message else "failed"
            print(f"::error title=Test failed: {name}::{first[:500]}")
        else:
            print(name)
    step_summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary and step_summary and found:
        with open(step_summary, "a", encoding="utf-8") as out:
            out.write(f"### {summary}\n\n| Test | Message |\n|---|---|\n")
            for _, name, message in found:
                first = (message.splitlines()[0] if message else "").replace("|", "\\|")
                out.write(f"| `{name}` | {first[:300]} |\n")
            out.write("\n")


if __name__ == "__main__":
    main(sys.argv[1:])
