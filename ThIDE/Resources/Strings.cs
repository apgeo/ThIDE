// Implementation Plan §7.6 — i18n marker / strongly-typed scope for IStringLocalizer<Strings>.

namespace ThIDE.Resources;

/// <summary>
/// Marker class used as the type argument for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/>.
/// Strings live in <c>Strings.resx</c> / <c>Strings.ro.resx</c>.
/// </summary>
public sealed class Strings { }
