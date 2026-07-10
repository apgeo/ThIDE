using System.Text;
using Therion.Workspace;

namespace Therion.Workspace.Tests;

/// <summary>
/// A Therion file may declare its own encoding, and Therion reads it back by that declaration. The
/// workspace loader has to honour it too, or every accented name in the file reaches the semantic
/// model as a replacement character — and anything that writes the file back out corrupts it.
/// </summary>
public class WorkspaceEncodingTests
{
    [Fact]
    public async Task Loads_a_latin1_file_that_declares_its_encoding()
    {
        var root = NewDirectory();
        try
        {
            var survey = Path.Combine(root, "grotte.th");
            File.WriteAllText(survey, "encoding iso-8859-1\nsurvey Bédeilhac\nendsurvey\n", Encoding.Latin1);

            await using var workspace = await WorkspaceLoader.OpenAsync(survey);
            var parsed = workspace.TryGetFile(survey);

            Assert.NotNull(parsed);
            var model = workspace.BuildSemanticModel();
            Assert.Contains("Bédeilhac", model.SurveysByFullName.Keys);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Loads_a_utf8_file_with_a_byte_order_mark()
    {
        var root = NewDirectory();
        try
        {
            var survey = Path.Combine(root, "bom.th");
            File.WriteAllText(survey, "survey Peștera\nendsurvey\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            await using var workspace = await WorkspaceLoader.OpenAsync(survey);
            var model = workspace.BuildSemanticModel();

            // A BOM left in the text would make the first token "﻿survey".
            Assert.Contains("Peștera", model.SurveysByFullName.Keys);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "thws_enc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
