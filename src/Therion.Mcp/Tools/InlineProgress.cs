namespace Therion.Mcp.Tools;

/// <summary>
/// Runs the callback on the reporting thread, so a report has happened by the time it returns. Not
/// <see cref="Progress{T}"/>: that posts each callback to a captured context, so a report could land
/// after the work it describes has returned.
/// </summary>
internal sealed class InlineProgress<T>(Action<T> onReport) : IProgress<T>
{
    public void Report(T value) => onReport(value);
}
