// STRUCT-01 Phase 2 — resolves a single magnetic declination δ from the user's preference + supplied
// inputs. Pure: the caller pre-computes the survey-declared value (from the AST) and the WMM-auto value
// (from GeoMagneticModel + the fix point + survey date); this just picks per the chosen source and
// degrades gracefully (→ δ = 0 with a note) when the requested value isn't available.

namespace Therion.Structural;

public static class DeclinationResolver
{
    public static DeclinationResolution Resolve(DeclinationOptions options, DeclinationInputs inputs)
    {
        switch (options.Source)
        {
            case DeclinationSource.Manual:
                return new DeclinationResolution(options.ManualDegrees, DeclinationSource.Manual, null);

            case DeclinationSource.SurveyDeclared:
                return inputs.SurveyDeclaredDegrees is { } d
                    ? new DeclinationResolution(d, DeclinationSource.SurveyDeclared, null)
                    : new DeclinationResolution(0, DeclinationSource.None, "no declination declared in the survey");

            case DeclinationSource.WmmAuto:
                return inputs.WmmAutoDegrees is { } w
                    ? new DeclinationResolution(w, DeclinationSource.WmmAuto, inputs.WmmNote)
                    : new DeclinationResolution(0, DeclinationSource.None,
                        "WMM declination unavailable (need a magnetic model, a fix point and a survey date)");

            case DeclinationSource.None:
            default:
                return new DeclinationResolution(0, DeclinationSource.None, null);
        }
    }
}
