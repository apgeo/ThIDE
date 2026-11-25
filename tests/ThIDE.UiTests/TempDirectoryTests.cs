using System.Text;

namespace ThIDE.UiTests;

// The temp folder each app is given (ThideApp.CreateTempDirectory). Plain facts: no app is started.
public class TempDirectoryTests
{
    [Fact]
    public void Each_app_gets_a_new_empty_folder()
    {
        var first = ThideApp.CreateTempDirectory();
        var second = ThideApp.CreateTempDirectory();
        try
        {
            Assert.NotEqual(first, second);
            Assert.Empty(Directory.GetFileSystemEntries(first));
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(first));
        }
        finally
        {
            Directory.Delete(first);
            Directory.Delete(second);
        }
    }

    [Fact]
    public void The_folder_has_no_link_in_its_path_and_leaves_room_for_a_socket_name()
    {
        var dir = ThideApp.CreateTempDirectory();
        try
        {
            Assert.Equal(dir, ThideApp.RealPath(dir));
            if (OperatingSystem.IsWindows()) return; // no Unix sockets in the temp folder there
            // The longest name the runtime uses, in at most 104 bytes with the end mark (macOS; Linux has 108).
            var socket = Path.Combine(dir, "dotnet-diagnostic-4194304-18446744073709551615-socket");
            Assert.InRange(Encoding.UTF8.GetByteCount(socket), 1, 103);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void RealPath_follows_a_link_in_the_middle_of_a_path()
    {
        if (OperatingSystem.IsWindows()) return; // making a link needs a privilege there
        var dir = ThideApp.CreateTempDirectory();
        try
        {
            var inner = Directory.CreateDirectory(Path.Combine(dir, "real", "inner")).FullName;
            Directory.CreateSymbolicLink(Path.Combine(dir, "link"), "real"); // relative, as `ln -s real link`
            Directory.CreateSymbolicLink(Path.Combine(dir, "again"), Path.Combine(dir, "link", "inner"));

            Assert.Equal(inner, ThideApp.RealPath(Path.Combine(dir, "link", "inner")));
            // A link whose target goes through another link.
            Assert.Equal(inner, ThideApp.RealPath(Path.Combine(dir, "again")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
