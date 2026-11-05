// The window title names the active thconfig and, when the graph has a single root survey, that survey's
// title. It is recomputed from each new graph, also when the thconfig is changed on disk outside ThIDE.

using Avalonia.Threading;
using ThIDE.Services;

namespace ThIDE.Tests;

public sealed class WindowTitleAfterDiskEditTests
{
    private const string Demo =
        "encoding utf-8\nsurvey demo -title \"Invented demo cave\"\ncentreline\ndata normal from to length compass clino\n" +
        "0 1 5 0 0\n1 2 4 90 -10\nendcentreline\nendsurvey\n";

    private const string Side =
        "encoding utf-8\nsurvey side\ncentreline\ndata normal from to length compass clino\n0 1 3 0 0\nendcentreline\nendsurvey\n";

    // The session publishes off the UI thread and the view model posts its refresh: pump both.
    private static async Task WaitFor(Func<bool> done, string what)
    {
        for (int i = 0; i < 200; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (done()) return;
            await Task.Delay(50);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(done(), what);
    }

    [Fact]
    public Task The_survey_title_is_kept_after_the_thconfig_is_edited_on_disk() => UiThread.RunAsync(async () =>
    {
        using var dir = new TempDir();
        dir.Write("demo.th", Demo);
        var cfg = dir.Write("demo.thconfig", "encoding utf-8\nsource demo.th\n");
        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        int published = 0;
        session.Changed += (_, _) => Interlocked.Increment(ref published);
        var vm = MainWindowViewModelHost.Create(new ScriptedDocumentService(), session: session);

        await session.SetRootAsync(dir.Path);
        await WaitFor(() => vm.Title == "ThIDE — demo.thconfig > Invented demo cave", $"title after opening: {vm.Title}");

        int before = Volatile.Read(ref published);
        File.WriteAllText(cfg, "encoding utf-8\n# edited outside ThIDE\nsource demo.th\nexport model -o demo.lox\n");
        await WaitFor(() => Volatile.Read(ref published) > before, "the session republished the graph after the edit");
        await WaitFor(() => vm.Title == "ThIDE — demo.thconfig > Invented demo cave", $"title after the edit: {vm.Title}");
    });

    [Fact]
    public Task A_second_root_survey_drops_the_survey_title_and_removing_it_brings_it_back() => UiThread.RunAsync(async () =>
    {
        using var dir = new TempDir();
        dir.Write("demo.th", Demo);
        dir.Write("side.th", Side);
        var cfg = dir.Write("demo.thconfig", "encoding utf-8\nsource demo.th\n");
        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        var vm = MainWindowViewModelHost.Create(new ScriptedDocumentService(), session: session);

        await session.SetRootAsync(dir.Path);
        await WaitFor(() => vm.Title.EndsWith("> Invented demo cave", StringComparison.Ordinal), $"title after opening: {vm.Title}");

        // What the live check did: source a second top-level survey. Two roots name no single cave.
        File.WriteAllText(cfg, "encoding utf-8\nsource demo.th\nsource side.th\n");
        await WaitFor(() => vm.Title == "ThIDE — demo.thconfig", $"title with two root surveys: {vm.Title}");

        File.WriteAllText(cfg, "encoding utf-8\nsource demo.th\n");
        await WaitFor(() => vm.Title == "ThIDE — demo.thconfig > Invented demo cave", $"title after removing it: {vm.Title}");
    });
}
