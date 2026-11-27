// Progress of a long stage, for a progress bar: which stage, and how far through it.

namespace Therion.MeshSurvey;

/// <param name="Stage">"void", "skeleton", "chambers", "plan", "splays" …</param>
/// <param name="Fraction">0 … 1 within the stage.</param>
public readonly record struct MeshSurveyProgress(string Stage, double Fraction);
