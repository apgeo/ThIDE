using System.Collections.ObjectModel;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Serializer.SystemTextJson;

namespace TherionProc.Tests;

// Guards the dock-layout persistence fix: the stock DockSerializer must round-trip an
// IRootDock tree of plain Dock base types (Id + structure). A custom type-info resolver
// previously dropped the polymorphic discriminators, making Deserialize throw
// "Deserialization of interface or abstract types is not supported" and silently reset
// the layout on every launch. DockFactory now serializes exactly this kind of skeleton.
public class DockLayoutSerializationTests
{
    [Fact]
    public void Base_type_root_dock_round_trips_through_default_serializer()
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
