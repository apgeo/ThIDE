// LANG-11 — encoding directive + BOM handling. A leading BOM must be stripped so the first
// command parses correctly, and a declared `encoding` must round-trip non-ASCII content.

using System.Linq;
using System.Text;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class EncodingBomTests
{
    [Fact]
    public void Utf8_bom_is_stripped_before_decoding()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = Encoding.UTF8.GetBytes("encoding utf-8\nsurvey s\nendsurvey\n");
        var text = EncodingResolver.Decode(bom.Concat(body).ToArray());
        Assert.False(text.StartsWith('﻿'));
        Assert.StartsWith("encoding", text);
    }

    [Fact]
    public void Bom_does_not_break_first_command_parsing()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = Encoding.UTF8.GetBytes("survey cave\nendsurvey\n");
        var text = EncodingResolver.Decode(bom.Concat(body).ToArray());
        var file = new ThParser().Parse("/p/a.th", text).Value!;
        Assert.Single(file.Children.OfType<SurveyCommand>());
        Assert.Equal("cave", file.Children.OfType<SurveyCommand>().Single().Name);
    }

    [Fact]
    public void Declared_iso_8859_2_encoding_decodes_non_ascii_titles()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var latin2 = Encoding.GetEncoding("iso-8859-2");
        // "Občina" contains a c-with-caron (0xE8 in ISO-8859-2).
        var bytes = latin2.GetBytes("encoding iso-8859-2\nsurvey s -title \"Občina\"\nendsurvey\n");
        var text = EncodingResolver.Decode(bytes);
        Assert.Contains("Občina", text);
    }
}
