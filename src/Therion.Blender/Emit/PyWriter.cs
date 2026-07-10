// PyWriter (BA-B5) — the indented Python text builder every emitter section writes
// through, and the ONLY way user-controlled text or numbers enter the generated
// script (R-08 culture bugs, R-12 script injection): Str() emits a fully-escaped
// Python string literal (station names and paths are hostile input — quotes,
// backslashes, newlines, diacritics), Num() emits invariant-culture round-trip
// floats (a ro-RO host writing "1,5" into Python is a scene-destroying bug).
// Output uses LF line endings only, so scripts are byte-identical across platforms
// (NFR-03 golden tests).

using System.Globalization;
using System.Text;

namespace Therion.Blender.Emit;

/// <summary>Indentation-aware Python source writer with safe literal formatting.</summary>
public sealed class PyWriter
{
    private const string IndentUnit = "    ";
    private readonly StringBuilder _text = new();
    private int _depth;

    /// <summary>Writes one line at the current indentation (empty text = blank line).</summary>
    public PyWriter Line(string text = "")
    {
        if (text.Length > 0)
        {
            for (int i = 0; i < _depth; i++) _text.Append(IndentUnit);
            _text.Append(text);
        }
        _text.Append('\n');
        return this;
    }

    public PyWriter Blank() => Line();

    /// <summary>Increases indentation until the returned scope is disposed.</summary>
    public IDisposable Indented() => new IndentScope(this);

    /// <summary>Writes <paramref name="header"/> (e.g. <c>"def main():"</c>) and indents
    /// the enclosed lines — the shape of every Python suite.</summary>
    public IDisposable Block(string header)
    {
        Line(header);
        return new IndentScope(this);
    }

    public override string ToString() => _text.ToString();

    private sealed class IndentScope : IDisposable
    {
        private PyWriter? _writer;
        public IndentScope(PyWriter writer)
        {
            _writer = writer;
            writer._depth++;
        }
        public void Dispose()
        {
            if (_writer is null) return;
            _writer._depth--;
            _writer = null;
        }
    }

    // ---- literal formatting (static: sections compose them into interpolated lines) ----

    /// <summary>
    /// A double-quoted Python string literal with full escaping: backslash, both quote
    /// characters, \n \r \t, and remaining control characters as \xNN. Non-ASCII text
    /// (diacritics, CJK) stays raw — the script is UTF-8, Python 3's default source
    /// encoding. Treats the input as hostile (R-12).
    /// </summary>
    public static string Str(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sb = new StringBuilder(value.Length + 8);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20 || c == 0x7F)
                        sb.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>An invariant-culture, round-trippable Python float literal. Refuses
    /// NaN/infinity — a non-finite number reaching the emitter is a caller bug.</summary>
    public static string Num(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Python literals must be finite.");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    public static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Python's capitalized booleans.</summary>
    public static string Bool(bool value) => value ? "True" : "False";
}
