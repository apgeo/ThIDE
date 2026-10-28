using Therion.Assistant;
using ThIDE.Services;

namespace ThIDE.Tests;

public class AssistantDataPolicyTests
{
    private const string Result = """{"survey":"TH0001","stations":12}""";

    [Fact]
    public void A_policy_narrowed_during_a_turn_applies_to_the_rest_of_it()
    {
        Assert.Equal(Result, AssistantService.ForModel(AssistantDataPolicy.SendAll, AssistantDataPolicy.SendAll, Result));
        Assert.Equal(ToolResultRedactor.Redact(Result), AssistantService.ForModel(AssistantDataPolicy.SendAll, AssistantDataPolicy.Redact, Result));
        Assert.DoesNotContain("12", AssistantService.ForModel(AssistantDataPolicy.SendAll, AssistantDataPolicy.HideContent, Result));
    }

    [Fact]
    public void Widening_during_a_turn_does_not_unredact_it()
    {
        Assert.Equal(ToolResultRedactor.Redact(Result), AssistantService.ForModel(AssistantDataPolicy.Redact, AssistantDataPolicy.SendAll, Result));
    }
}
