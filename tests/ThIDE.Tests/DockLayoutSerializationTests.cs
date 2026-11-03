using System.Collections.ObjectModel;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Serializer.SystemTextJson;

namespace ThIDE.Tests;

// Pins a property of the third-party Dock serializer, not of ThIDE: the stock DockSerializer
// round-trips an IRootDock tree of plain Dock base types (Id + structure). A custom type-info
// resolver once dropped the polymorphic discriminators, which made Deserialize throw
// "Deserialization of interface or abstract types is not supported". No ThIDE code runs here:
// DockFactory's own dock-tree persistence (its skeleton/swap path) is switched off, and the
// layout is rebuilt from the saved profile instead, which other tests cover.
public class DockLayoutSerializationTests
{
    [Fact]
    public void Dock_serializer_round_trips_a_base_type_root_dock()
    {
        var serializer = new DockSerializer();
        var root = BuildSkeleton();

        var json = serializer.Serialize<IRootDock>(root);
        var restored = serializer.Deserialize<IRootDock>(json);

        Assert.NotNull(restored);
        Assert.Equal("Root", restored!.Id);
        Assert.True(ContainsId(restored, "Workspace"));   // tool placeholder survived
        Assert.True(ContainsId(restored, "Documents"));   // document well survived (empty)
        Assert.True(ContainsId(restored, "Diagnostics"));
    }

    private static IRootDock BuildSkeleton() => new RootDock
    {
        Id = "Root",
        Title = "Root",
        VisibleDockables = new ObservableCollection<IDockable>
        {
            new ProportionalDock
            {
                Id = "MainRow",
                Orientation = Orientation.Horizontal,
                VisibleDockables = new ObservableCollection<IDockable>
                {
                    new ToolDock
                    {
                        Id = "LeftTools",
                        Alignment = Alignment.Left,
                        VisibleDockables = new ObservableCollection<IDockable>
                        {
                            new Tool { Id = "Workspace", Title = "Workspace" },
                        },
                    },
                    new ProportionalDockSplitter(),
                    new DocumentDock
                    {
                        Id = "Documents",
                        VisibleDockables = new ObservableCollection<IDockable>(),
                    },
                    new ToolDock
                    {
                        Id = "BottomTools",
                        Alignment = Alignment.Bottom,
                        VisibleDockables = new ObservableCollection<IDockable>
                        {
                            new Tool { Id = "Diagnostics", Title = "Diagnostics" },
                        },
                    },
                },
            },
        },
    };

    private static bool ContainsId(IDockable node, string id)
    {
        if (string.Equals(node.Id, id, System.StringComparison.Ordinal)) return true;
        if (node is IDock dock && dock.VisibleDockables is { } children)
            foreach (var c in children)
                if (ContainsId(c, id)) return true;
        return false;
    }
}
