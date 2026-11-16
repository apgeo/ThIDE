// The Flathub Mapiah build (io.github.rsevero.mapiah) is frozen at 0.3.0 and has no filesystem
// permission, so it cannot open the sketch it is launched with. Auto-detection must prefer the
// official flatpak and any native install over it.

using System.Linq;
using Therion.Build;

namespace Therion.Build.Tests;

public class MapiahLocatorOrderTests
{
    private const string Official = "org.mapiah.mapiah";
    private const string Flathub = "io.github.rsevero.mapiah";

    [Fact]
    public void Official_flatpak_is_tried_before_the_flathub_build()
    {
        var paths = ExternalToolLocator.LinuxMapiahCandidatePaths("/home/u").ToList();

        int lastOfficial = paths.FindLastIndex(p => p.EndsWith(Official));
        int firstFlathub = paths.FindIndex(p => p.EndsWith(Flathub));
        Assert.True(lastOfficial >= 0 && firstFlathub >= 0);
        Assert.True(lastOfficial < firstFlathub);
    }

    [Fact]
    public void Native_installs_are_tried_before_the_flathub_build()
    {
        var paths = ExternalToolLocator.LinuxMapiahCandidatePaths("/home/u").ToList();

        int firstFlathub = paths.FindIndex(p => p.EndsWith(Flathub));
        Assert.True(paths.IndexOf("/usr/bin/mapiah") < firstFlathub);
        Assert.True(paths.IndexOf("/usr/local/bin/mapiah") < firstFlathub);
    }

    [Fact]
    public void Both_flatpak_ids_are_looked_for_system_and_user_wide()
    {
        var paths = ExternalToolLocator.LinuxMapiahCandidatePaths("/home/u").ToList();

        foreach (var id in new[] { Official, Flathub })
        {
            Assert.Contains("/var/lib/flatpak/exports/bin/" + id, paths);
            Assert.Contains(System.IO.Path.Combine("/home/u", ".local/share/flatpak/exports/bin/" + id), paths);
        }
    }

    [Fact]
    public void Without_a_home_folder_only_system_locations_are_listed()
    {
        var paths = ExternalToolLocator.LinuxMapiahCandidatePaths(null).ToList();

        Assert.DoesNotContain(paths, p => p.Contains(".local"));
        Assert.Equal(Official, System.IO.Path.GetFileName(paths[0]));
    }
}
