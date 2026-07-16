namespace Therion.Blender;

/// <summary>
/// A survey (sub-survey) entry from a <c>.lox</c> file's survey tree. <c>.3d</c> files
/// carry no explicit survey table — their hierarchy is implicit in dot-separated
/// station/leg labels (see <see cref="CaveModel.SeparatorChar"/>).
/// </summary>
/// <param name="Id">Survey id referenced by stations/shots/scraps.</param>
/// <param name="ParentId">Parent survey id; the root survey is its own parent.</param>
/// <param name="Name">Survey name (one hierarchy level).</param>
/// <param name="Title">Human-readable survey title, when present.</param>
public sealed record CaveSurvey(uint Id, uint ParentId, string Name, string? Title);
