// The names Therion itself accepts on an `encoding` line (`therion --print-encodings`, 6.3.x):
// ASCII, CP1250, CP1251, CP1252, CP1253, ISO8859-1, ISO8859-2, ISO8859-5, ISO8859-7, UTF-8,
// matched case-insensitively. A file that declares one of them must decode the way Therion does.

using System.Linq;
using System.Text;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class TherionEncodingNameTests
{
    [Theory]
    [InlineData("ascii")]
    [InlineData("cp1250")]
    [InlineData("CP1250")]
    [InlineData("cp1251")]
    [InlineData("cp1252")]
    [InlineData("cp1253")]
    [InlineData("iso8859-1")]
    [InlineData("iso8859-2")]
    [InlineData("iso8859-5")]
    [InlineData("iso8859-7")]
    [InlineData("utf-8")]
    public void Every_name_therion_accepts_is_recognised(string name)
    {
        var bytes = Encoding.ASCII.GetBytes($"encoding {name}\nsurvey s\nendsurvey\n");

        var encoding = EncodingResolver.DetectEncoding(bytes);

        Assert.True(encoding is not null, $"'encoding {name}' is valid for Therion but resolved to nothing (the file would be decoded as UTF-8)");
    }

    [Fact]
    public void Declared_cp1250_file_decodes_its_czech_letters()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1250 = Encoding.GetEncoding(1250);
        var bytes = cp1250.GetBytes("encoding cp1250\nsurvey s -title \"ěčý\"\nendsurvey\n");

        var text = EncodingResolver.Decode(bytes);

        Assert.DoesNotContain('�', text);
        Assert.Contains("ěčý", text);
    }
}
