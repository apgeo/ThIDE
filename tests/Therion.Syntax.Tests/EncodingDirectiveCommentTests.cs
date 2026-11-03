using System.Linq;
using System.Text;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class EncodingDirectiveCommentTests
{
    [Fact]
    public void Encoding_line_with_trailing_comment_is_honoured()
    {
        // "Ob" + c-caron (0xE8 in ISO 8859-2) + "ina"
        var head = Encoding.ASCII.GetBytes("encoding iso8859-2 # x\nsurvey s -title \"Ob");
        var tail = Encoding.ASCII.GetBytes("ina\"\nendsurvey\n");
        var bytes = head.Concat(new byte[] { 0xE8 }).Concat(tail).ToArray();

        var text = EncodingResolver.Decode(bytes);

        Assert.True(text.Contains("Občina"), $"decoded text: {text.Replace("\n", "\\n")}");
    }
}
