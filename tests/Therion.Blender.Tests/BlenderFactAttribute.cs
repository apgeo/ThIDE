// Tests that run a real Blender are opt-in: they need Blender 4.2 or later on the machine and
// take seconds each. Marked [BlenderFact], they run when THIDE_BLENDER_SMOKE=1 and report as
// skipped otherwise. THIDE_BLENDER_PATH pins a specific Blender build.

namespace Therion.Blender.Tests;

public sealed class BlenderFactAttribute : FactAttribute
{
    public const string OptInVariable = "THIDE_BLENDER_SMOKE";

    public static bool Enabled => Environment.GetEnvironmentVariable(OptInVariable) == "1";

    public BlenderFactAttribute()
    {
        if (!Enabled)
            Skip = $"Needs a local Blender; set {OptInVariable}=1 to run.";
    }
}

/// <summary>As <see cref="BlenderFactAttribute"/>, for a test with cases.</summary>
public sealed class BlenderTheoryAttribute : TheoryAttribute
{
    public BlenderTheoryAttribute()
    {
        if (!BlenderFactAttribute.Enabled)
            Skip = $"Needs a local Blender; set {BlenderFactAttribute.OptInVariable}=1 to run.";
    }
}

/// <summary>A real-Blender test that states how the output should be and fails today because of
/// a known defect. It is skipped until the defect is fixed; set THIDE_BLENDER_DEFECTS=1 as well
/// to run it and see the current numbers. Whoever fixes the defect removes the attribute's
/// argument and turns it into a <see cref="BlenderFactAttribute"/>.</summary>
public sealed class BlenderKnownDefectFactAttribute : FactAttribute
{
    public const string RunVariable = "THIDE_BLENDER_DEFECTS";

    public BlenderKnownDefectFactAttribute(string defect)
    {
        if (!BlenderFactAttribute.Enabled)
            Skip = $"Needs a local Blender; set {BlenderFactAttribute.OptInVariable}=1 to run.";
        else if (Environment.GetEnvironmentVariable(RunVariable) != "1")
            Skip = $"Known defect, not fixed yet: {defect} Set {RunVariable}=1 to run it.";
    }
}
