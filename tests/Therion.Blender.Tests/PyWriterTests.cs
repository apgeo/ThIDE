// PyWriter tests (BA-B5 batch 2): indentation/blocks, the py_str escaping torture set
// (quotes, backslashes, newlines, control chars, diacritics, CJK — R-12), invariant
// float formatting under ro-RO (R-08), non-finite rejection, LF-only output.

using System.Globalization;
using Therion.Blender.Emit;

namespace Therion.Blender.Tests;

public class PyWriterTests
{
    [Fact]
    public void Lines_IndentAndNest()
    {
        var w = new PyWriter();
        w.Line("import bpy");
        using (w.Block("def main():"))
        {
            w.Line("x = 1");
            using (w.Block("if x:"))
                w.Line("pass");
        }
        w.Line("main()");

        Assert.Equal(
            "import bpy\n" +
            "def main():\n" +
            "    x = 1\n" +
            "    if x:\n" +
            "        pass\n" +
            "main()\n",
            w.ToString());
    }

    [Fact]
    public void BlankLines_CarryNoIndentation()
    {
        var w = new PyWriter();
        using (w.Block("def f():"))
        {
            w.Line("a = 1");
            w.Blank();
            w.Line("b = 2");
        }
        Assert.Equal("def f():\n    a = 1\n\n    b = 2\n", w.ToString());
    }

    [Fact]
    public void Output_UsesLfOnly()
    {
        var w = new PyWriter();
        w.Line("a").Line("b");
        Assert.DoesNotContain('\r', w.ToString());
    }

    // ---- py_str torture (R-12: treat label text as hostile) ----

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("it's", "\"it's\"")]                                  // single quotes need no escape
    [InlineData(@"C:\caves\model.ply", "\"C:\\\\caves\\\\model.ply\"")]
    [InlineData("line1\nline2", "\"line1\\nline2\"")]
    [InlineData("cr\rtab\t", "\"cr\\rtab\\t\"")]
    [InlineData("Peștera Însorită", "\"Peștera Însorită\"")]           // diacritics raw
    [InlineData("洞穴測量", "\"洞穴測量\"")]                             // CJK raw
    [InlineData("", "\"\\x01\\x1f\\x7f\"")]          // control chars hex-escaped
    [InlineData("\"; import os; os.system(\"rm\")", "\"\\\"; import os; os.system(\\\"rm\\\")\"")] // injection attempt stays inert text
    public void Str_EscapesHostileInput(string input, string expected)
    {
        Assert.Equal(expected, PyWriter.Str(input));
    }

    [Fact]
    public void Str_NeverEmitsRawQuoteFromInput()
    {
        // Property: for arbitrary nasty input, every '"' inside the literal is escaped —
        // strip the delimiters and check no unescaped quote remains.
        var nasty = "a\"b\\\"c\\\\\"d\n\"e";
        var literal = PyWriter.Str(nasty);
        var body = literal[1..^1];
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] != '"') continue;
            int backslashes = 0;
            for (int j = i - 1; j >= 0 && body[j] == '\\'; j--) backslashes++;
            Assert.True(backslashes % 2 == 1, $"unescaped quote at {i} in {literal}");
        }
    }

    // ---- numbers (R-08) ----

    [Fact]
    public void Num_IsInvariant_UnderRoRoCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ro-RO"); // decimal-comma locale
            Assert.Equal("1.5", PyWriter.Num(1.5));
            Assert.Equal("-0.125", PyWriter.Num(-0.125));
            Assert.Equal("407654.25", PyWriter.Num(407_654.25));
            Assert.Equal("1234567", PyWriter.Num(1_234_567));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Num_RoundTripsThroughDoubleParse()
    {
        double[] values = [1.0 / 3.0, Math.PI, 1e-17, 4.9e300, -0.0];
        foreach (var value in values)
            Assert.Equal(value, double.Parse(PyWriter.Num(value), CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Num_RejectsNonFinite(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PyWriter.Num(value));
    }

    [Fact]
    public void Bool_IsPythonCapitalized()
    {
        Assert.Equal("True", PyWriter.Bool(true));
        Assert.Equal("False", PyWriter.Bool(false));
    }
}
