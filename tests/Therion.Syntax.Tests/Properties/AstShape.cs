// A structural dump of a parsed tree, for comparing two parses: every public property of every node,
// recursively, with numbers in invariant "R" form. Spans can be left out, which is how a tree
// re-parsed from written text is compared with the original (same meaning, different offsets).

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using Therion.Core;

namespace Therion.Syntax.Tests.Properties;

internal static class AstShape
{
    public static string Dump(object? value, bool spans)
    {
        var sb = new StringBuilder();
        Write(sb, value, spans, 0);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object? v, bool spans, int depth)
    {
        if (depth > 60) throw new InvalidOperationException("AST dump nests too deep");
        switch (v)
        {
            case null: sb.Append("null"); return;
            case string s: sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"'); return;
            case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
            case IFormattable f when v.GetType().IsPrimitive || v.GetType().IsEnum:
                sb.Append(f.ToString(null, CultureInfo.InvariantCulture)); return;
            case bool b: sb.Append(b ? "true" : "false"); return;
            case SourceSpan span:
                sb.Append(spans ? $"@{span.StartOffset}+{span.Length}" : "@"); return;
            case SourceLocation loc:
                sb.Append(spans ? $"{loc.Line}:{loc.Column}" : "~"); return;
        }

        if (v is System.Runtime.CompilerServices.ITuple tuple)
        {
            sb.Append('(');
            for (int i = 0; i < tuple.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                Write(sb, tuple[i], spans, depth + 1);
            }
            sb.Append(')');
            return;
        }

        var type = v.GetType();
        // A default ImmutableArray throws on enumeration; it reads as empty here.
        if (type.GetProperty("IsDefault")?.GetValue(v) is true) { sb.Append("[]"); return; }
        if (v is IEnumerable items)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in items)
            {
                if (!first) sb.Append(", ");
                first = false;
                Write(sb, item, spans, depth + 1);
            }
            sb.Append(']');
            return;
        }

        sb.Append(type.Name).Append('{');
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract")
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            sb.Append(p.Name).Append('=');
            Write(sb, p.GetValue(v), spans, depth + 1);
            sb.Append(';');
        }
        sb.Append('}');
    }
}
