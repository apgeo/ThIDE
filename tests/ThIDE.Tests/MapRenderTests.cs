// format dispatch for the in-app renderer (the actual rasterization needs the Avalonia
// platform + native libs and is verified at runtime).

using ThIDE.Services;
using Xunit;

namespace ThIDE.Tests;

public class MapRenderTests
{
    private readonly IMapRenderService _svc = new MapRenderService();

    [Theory]
    [InlineData("map.pdf", true)]
    [InlineData("map.svg", true)]
    [InlineData("map.png", true)]
    [InlineData("map.JPG", true)]
    [InlineData("model.lox", false)]
    [InlineData("model.3d", false)]
    [InlineData("notes.txt", false)]
    public void CanRender_matches_supported_formats(string path, bool expected) =>
        Assert.Equal(expected, _svc.CanRender(path));

    [Fact]
    public void Missing_file_reports_an_error_not_a_crash()
    {
        var result = _svc.Render("does-not-exist.png", 0, 1.0);
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }
}
