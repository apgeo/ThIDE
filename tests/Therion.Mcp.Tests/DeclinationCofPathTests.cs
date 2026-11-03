using System.Diagnostics;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

/// <summary>
/// get_declination takes a raw filesystem path for its coefficient file. Whatever it accepts, it has to
/// be a regular file: a named pipe or a device node makes the read block or run unbounded, and the tool
/// is synchronous, so the caller hangs with it.
/// </summary>
public class DeclinationCofPathTests
{
    [Fact]
    public async Task Get_declination_returns_promptly_for_a_cof_path_that_is_not_a_regular_file()
    {
        if (OperatingSystem.IsWindows()) return;   // named pipes are not filesystem entries there

        var fifo = Path.Combine(Path.GetTempPath(), "thmcp_fifo_" + Guid.NewGuid().ToString("N"));
        using (var mkfifo = Process.Start(new ProcessStartInfo("mkfifo", fifo) { UseShellExecute = false }))
        {
            mkfifo!.WaitForExit();
            Assert.Equal(0, mkfifo.ExitCode);
        }

        try
        {
            var tools = new CalculatorTools();
            var call = Task.Run(() => tools.GetDeclination(46.77, 22.83, 2026.5, cofPath: fifo));

            var finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(3))) == call;

            if (!finished)
            {
                // Let the blocked reader see end-of-file so the test process can exit cleanly.
                try { using var writer = new FileStream(fifo, FileMode.Open, FileAccess.Write); } catch { /* reader gone */ }
            }

            Assert.True(finished,
                "get_declination did not return within 3 s for a cofPath that is a named pipe: the tool opens any existing path and blocks on a non-regular file");
            Assert.False(call.Result.Ok);
            Assert.Equal(ToolErrorCodes.InvalidArgument, call.Result.Error!.Code);
        }
        finally
        {
            try { File.Delete(fifo); } catch { /* best effort */ }
        }
    }
}
