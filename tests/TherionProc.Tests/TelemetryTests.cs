using System.IO;
using System.Linq;
using TherionProc.Services;

namespace TherionProc.Tests;

// REL-05: telemetry/crash reporting is opt-in and writes only local files.
public class TelemetryTests
{
    [Fact]
    public void Disabled_by_default_writes_nothing()
    {
        using var dir = new TempDir();
        var svc = new LocalTelemetryService(dir.Path, new FakeSettings(), null);
        Assert.False(svc.IsEnabled);

        svc.TrackEvent("app.start");
        svc.ReportException(new System.InvalidOperationException("boom"), "test");

        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Enabled_records_events_and_crash_reports_locally()
    {
        using var dir = new TempDir();
        var settings = new FakeSettings(AppSettings.Default with { TelemetryEnabled = true });
        var svc = new LocalTelemetryService(dir.Path, settings, null);
        Assert.True(svc.IsEnabled);

        svc.TrackEvent("build.start", "cave.thconfig");
        svc.ReportException(new System.InvalidOperationException("boom"), "unit-test");

        var usage = Path.Combine(dir.Path, "usage.log");
        Assert.True(File.Exists(usage));
        Assert.Contains("build.start", File.ReadAllText(usage));
        Assert.Contains(Directory.GetFiles(dir.Path), f => Path.GetFileName(f).StartsWith("crash-"));
    }
}
