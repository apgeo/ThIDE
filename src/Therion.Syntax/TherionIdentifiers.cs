// Therion identifier (keyword / ext-keyword) validation. thbook v6.4.0 §"Data types":
//   keyword      = A-Z a-z 0-9 _ - /            (not starting with '-')
//   ext keyword  = keyword + the chars + * . , ' (not in the first position)
//
// In practice Therion reads UTF-8 and accepts Unicode letters in names (the corpus has survey
// ids like "Veneția-Superioară"), so this validator treats any Unicode letter/digit as legal and
// only rejects characters that are unambiguously outside a Therion identifier (e.g. & ! ? $ ^ ~).
// Conservative on purpose: declaration-id checks must not fire on legitimate real-world data.

namespace Therion.Syntax;

/// <summary>Validates Therion identifier (keyword / ext-keyword) tokens.</summary>
public static class TherionIdentifiers
{
    /// <summary>
    /// Returns the first character of <paramref name="id"/> that cannot appear in a Therion
    /// keyword/ext-keyword, or <c>null</c> if every character is legal. Unicode letters and
    /// digits are always accepted; the punctuation allow-list is the ext-keyword set.
    /// </summary>
    public static char? FirstIllegalChar(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var c in id)
        {
            if (char.IsLetterOrDigit(c)) continue;            // includes Unicode letters/marks
            if (IsExtKeywordPunctuation(c)) continue;
            return c;
        }
        return null;
    }

    /// <summary>True if <paramref name="id"/> is a syntactically valid identifier token.</summary>
    public static bool IsValid(string? id) => FirstIllegalChar(id) is null;

    // ext-keyword punctuation (a superset of keyword punctuation). '@' and ':' are tolerated
    // because object references (station@survey, type:subtype) flow through the same paths.
    private static bool IsExtKeywordPunctuation(char c) =>
        c is '_' or '-' or '/' or '+' or '*' or '.' or ',' or '\'' or '@' or ':';
}
