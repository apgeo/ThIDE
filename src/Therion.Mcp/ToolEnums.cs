namespace Therion.Mcp;

/// <summary>
/// Parsing the string enums that arrive on the wire.
/// </summary>
/// <remarks>
/// <c>Enum.TryParse</c> is the wrong tool here and every hand-rolled guard around it has missed
/// something. It accepts a bare number (<c>"3"</c>), a signed one (<c>"+1"</c>), and — for any enum,
/// not just <c>[Flags]</c> ones — a comma-separated list of names which it bitwise-ORs together. So
/// <c>"Station, Survey"</c> parses to <c>1|2 = 3</c>, which <c>Enum.IsDefined</c> then happily
/// confirms is <c>Map</c>. The tool answers a question nobody asked.
/// <para>Matching against <c>Enum.GetNames</c> accepts exactly the names the tool documents.</para>
/// </remarks>
public static class ToolEnums
{
    /// <summary>True when <paramref name="value"/> is one of the enum's declared names, case-insensitively.</summary>
    public static bool TryParse<TEnum>(string? value, out TEnum parsed) where TEnum : struct, Enum
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        foreach (var name in Enum.GetNames<TEnum>())
            if (name.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                parsed = Enum.Parse<TEnum>(name);
                return true;
            }

        return false;
    }

    /// <summary>The enum's names, lowercased, for an error message that tells the caller what to send.</summary>
    public static string Names<TEnum>() where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetNames<TEnum>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]));
}
