// A failed read or write is told in the UI language with a short reason, not with the .NET message
// (English everywhere, and it quotes the full path).

using ThIDE.Resources;

namespace ThIDE.Tests;

public class IoErrorTextTests
{
    [Fact]
    public void Common_failures_get_their_own_reason()
    {
        Assert.Equal("IoErr_Denied", IoErrorText.ReasonKey(new UnauthorizedAccessException("Access to the path '/x/side.th' is denied.")));
        Assert.Equal("IoErr_NoFolder", IoErrorText.ReasonKey(new DirectoryNotFoundException()));
        Assert.Equal("IoErr_NoFile", IoErrorText.ReasonKey(new FileNotFoundException()));
        Assert.Equal("IoErr_PathTooLong", IoErrorText.ReasonKey(new PathTooLongException()));
        Assert.Equal("IoErr_Other", IoErrorText.ReasonKey(new IOException("something else")));
        Assert.Equal("IoErr_Other", IoErrorText.ReasonKey(new InvalidOperationException()));
    }

    [Fact]
    public void A_full_disk_is_recognised_on_this_platform()
    {
        // What .NET puts in HResult: the Win32 code in an HRESULT on Windows, the errno elsewhere.
        int diskFull = OperatingSystem.IsWindows() ? unchecked((int)0x80070070) : 28;
        Assert.Equal("IoErr_DiskFull", IoErrorText.ReasonKey(new IOException("No space left on device", diskFull)));
    }
}
