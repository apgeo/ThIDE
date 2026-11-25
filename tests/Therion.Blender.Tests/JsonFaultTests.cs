// What the readers of a spec, a plan and a preset say of a file that is not what it was taken
// for: where it stops being JSON, and nothing of what is in it. A message, a log and an agent's
// context are no place for the contents of a file that was named by mistake.

using Therion.Blender.Plan;
using Therion.Blender.Presets;

namespace Therion.Blender.Tests;

public class JsonFaultTests
{
    private static readonly SceneSpec Plain = new()
    {
        Source = new SourceSpec { PlyPath = "job/model.ply" },
        Output = new OutputSpec { OutputDirectory = "out" },
    };

    private const string Secret = "INVENTED-SECRET-7731";

    /// <summary>Texts that are not JSON, each with something in it that must not be passed on,
    /// and where each stops being JSON.</summary>
    public static TheoryData<string, string> NotJson => new()
    {
        { $"name;date\n{Secret};1987-07-12\n", "line 1, byte 2" },  // an n could have been the start of null
        { $"station;date\n{Secret};1987-07-12\n", "line 1, byte 1" },
        { $"{{ \"version\": 1,\n  \"name\": {Secret} }}", "line 2, byte 11" },
        { $"{{ \"version\": 1, \"name\": \"{Secret}\" ", "line 1, byte 48" },
        { $"\"{Secret}\" trailing", "line 1, byte 24" },
    };

    [Theory]
    [MemberData(nameof(NotJson))]
    public void AFileThatIsNotJson_IsSaidToBeSoAndWhere_AndNothingOfItIsPassedOn(string text, string where)
    {
        string spec = Assert.Throws<SceneSpecFormatException>(() => SceneSpecSerializer.Read(text)).Message;
        string plan = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.Read(text)).Message;
        string preset = Assert.Throws<PresetFormatException>(() => PresetSerializer.Read(text)).Message;

        Assert.Equal($"Malformed render spec JSON: the text is not JSON at {where}.", spec);
        Assert.Equal($"Malformed scene plan JSON: the text is not JSON at {where}.", plan);
        Assert.Equal($"Malformed preset JSON: the text is not JSON at {where}.", preset);
        Assert.All([spec, plan, preset], message => Assert.DoesNotContain(Secret, message));
    }

    [Fact]
    public void JsonOfTheWrongShape_IsNamedByItsKey_InOneLine_CutShort()
    {
        // The reader names the key and the kind of value, not the value.
        string wrong = SceneSpecSerializer.Write(Plain).Replace("\"seed\": 1", $"\"seed\": \"{Secret}\"");
        string spec = Assert.Throws<SceneSpecFormatException>(() => SceneSpecSerializer.Read(wrong)).Message;
        Assert.StartsWith("Malformed render spec JSON: The JSON value could not be converted to System.Int32. Path: $.seed", spec);
        Assert.DoesNotContain(Secret, spec);
        Assert.DoesNotContain('\n', spec);

        string plan = Assert.Throws<ScenePlanFormatException>(
            () => ScenePlanSerializer.Read("{ \"version\": 1, \"timeline\": { \"fps\": \"" + Secret + "\" } }")).Message;
        Assert.StartsWith("Malformed scene plan JSON: The JSON value could not be converted to System.Int32. Path: $.timeline.fps", plan);
        Assert.DoesNotContain(Secret, plan);

        string preset = Assert.Throws<PresetFormatException>(
            () => PresetSerializer.Read("{ \"version\": 1, \"name\": \"x\", \"spec\": { \"version\": 1, \"seed\": \"" + Secret + "\" } }")).Message;
        Assert.StartsWith("Malformed preset JSON: The JSON value could not be converted to System.Int32. Path: $.spec.seed", preset);
        Assert.DoesNotContain(Secret, preset);

        // However long the reader's own line is, three hundred characters of it are passed on.
        string longKey = new('k', 2000);
        string cut = Assert.Throws<ScenePlanFormatException>(
            () => ScenePlanSerializer.Read("{ \"version\": 1, \"frame\": { \"origin\": { \"" + longKey + "\": 1 } } }")).Message;
        Assert.True(cut.Length <= "Malformed scene plan JSON: ".Length + 301, $"{cut.Length} characters were passed on");
    }
}
