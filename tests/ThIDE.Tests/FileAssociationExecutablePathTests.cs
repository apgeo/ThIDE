using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// The path written into the Linux desktop entry's Exec line must survive the app exiting. Inside
/// an AppImage the process path is the runtime's temporary squashfs mount; the runtime exports the
/// durable file as APPIMAGE, and that is what an association has to point at.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class FileAssociationExecutablePathTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-appimage-").FullName;
    private readonly string? _originalAppImage = Environment.GetEnvironmentVariable("APPIMAGE");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPIMAGE", _originalAppImage);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ExecutablePath_prefers_the_AppImage_file_over_the_mounted_process_path()
    {
        var appImage = Path.Combine(_root, "ThIDE-x86_64.AppImage");
        File.WriteAllText(appImage, "stub");
        Environment.SetEnvironmentVariable("APPIMAGE", appImage);

        var exec = FileAssociationCatalog.ExecutablePath();

        Assert.True(exec == appImage, $"expected the AppImage path '{appImage}' but the association would use the transient process path '{exec}'");
    }
}
