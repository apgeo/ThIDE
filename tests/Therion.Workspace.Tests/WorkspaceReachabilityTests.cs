// Reachability over the include graph — the engine behind the Overview ▸ Audit orphan scan and the
// MCP `list_files orphansOnly` tool. It resolves includes the same way the workspace BFS does, so a
// file pulled in by an extensionless `input` is never mistaken for an orphan.

using System.IO;
using Therion.Workspace;

namespace Therion.Workspace.Tests;

public class WorkspaceReachabilityTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void An_extensionless_include_reaches_its_th2_target()
    {
        var dir = NewDir();
        try
        {
            var cfg = Path.Combine(dir, "thconfig");
            var th2 = Path.Combine(dir, "sketch.th2");
            File.WriteAllText(cfg, "source sketch\n");
            File.WriteAllText(th2, "scrap s1\nendscrap\n");

            var reachable = WorkspaceReachability.ReachableFrom(new[] { cfg });

            // Without the shared .th/.th2 resolution the walk would stop at a phantom `sketch.th`
            // and the audit would report this sketch as an orphan.
            Assert.Contains(Path.GetFullPath(th2), reachable);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_extensionless_include_prefers_the_default_th()
    {
        var dir = NewDir();
        try
        {
            var cfg = Path.Combine(dir, "thconfig");
            var th  = Path.Combine(dir, "cave.th");
            var th2 = Path.Combine(dir, "cave.th2");
            File.WriteAllText(cfg, "source cave\n");
            File.WriteAllText(th, "survey s\nendsurvey\n");
            File.WriteAllText(th2, "scrap s1\nendscrap\n");

            var reachable = WorkspaceReachability.ReachableFrom(new[] { cfg });

            Assert.Contains(Path.GetFullPath(th), reachable);
            Assert.DoesNotContain(Path.GetFullPath(th2), reachable);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Reachability_is_transitive_through_a_nested_include()
    {
        var dir = NewDir();
        try
        {
            var cfg = Path.Combine(dir, "thconfig");
            var th  = Path.Combine(dir, "cave.th");
            var th2 = Path.Combine(dir, "plan.th2");
            File.WriteAllText(cfg, "source cave\n");
            File.WriteAllText(th, "survey s\n  input plan\nendsurvey\n");
            File.WriteAllText(th2, "scrap s1\nendscrap\n");

            var reachable = WorkspaceReachability.ReachableFrom(new[] { cfg });

            Assert.Contains(Path.GetFullPath(th), reachable);
            Assert.Contains(Path.GetFullPath(th2), reachable);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_unreferenced_file_is_not_reachable()
    {
        var dir = NewDir();
        try
        {
            var cfg = Path.Combine(dir, "thconfig");
            var th  = Path.Combine(dir, "cave.th");
            var lone = Path.Combine(dir, "orphan.th");
            File.WriteAllText(cfg, "source cave\n");
            File.WriteAllText(th, "survey s\nendsurvey\n");
            File.WriteAllText(lone, "survey o\nendsurvey\n");

            var reachable = WorkspaceReachability.ReachableFrom(new[] { cfg });

            Assert.DoesNotContain(Path.GetFullPath(lone), reachable);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
