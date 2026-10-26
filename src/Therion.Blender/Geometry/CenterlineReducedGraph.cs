namespace Therion.Blender.Geometry;

/// <summary>
/// One run of passage between two places where the network does something: a junction, a dead
/// end, or the point a loop closes on. Every station strictly inside a branch has exactly two
/// legs and so adds nothing to the shape of the network — it only says how the passage got
/// from one end to the other.
/// </summary>
public sealed record CenterlineBranch
{
    /// <summary>Stations along the branch in order, both ends included. For a branch that
    /// closes on itself the first and last entries are the same station.</summary>
    public required IReadOnlyList<int> StationIndices { get; init; }

    /// <summary>Distance walked along the branch: the leg lengths summed.</summary>
    public required double Length { get; init; }

    /// <summary>Straight distance between the two ends; zero for a branch that closes on
    /// itself.</summary>
    public required double ChordLength { get; init; }

    public int First => StationIndices[0];

    public int Last => StationIndices[^1];

    /// <summary>Whether the branch returns to the station it started from.</summary>
    public bool IsLoop => First == Last;

    /// <summary>How far the passage wanders relative to the straight line between its ends —
    /// 1 for a straight branch, larger for a winding one. Null when there is no straight line
    /// to compare against: a branch that closes on itself, or one whose ends coincide.</summary>
    public double? Tortuosity => ChordLength > 0 ? Length / ChordLength : null;
}

/// <summary>
/// The passage network with every run of degree-2 stations contracted into a single branch, so
/// that what remains is only the places passages meet or end and the runs between them. This is
/// the graph most measures of network shape are defined over: counting a survey's intermediate
/// stations would make a finely surveyed cave look more complex than a coarsely surveyed one of
/// the same shape.
/// </summary>
/// <remarks>
/// Branches are kept as a list rather than folded into a simple graph, because two junctions
/// can genuinely be joined by more than one passage and a junction can genuinely be joined to
/// itself. Collapsing those would destroy exactly the loops the reduction exists to expose.
/// </remarks>
public sealed class CenterlineReducedGraph
{
    private readonly Dictionary<int, int> _degree;

    private CenterlineReducedGraph(
        IReadOnlyList<CenterlineBranch> branches,
        IReadOnlyList<int> nodes,
        Dictionary<int, int> degree,
        int componentCount)
    {
        Branches = branches;
        Nodes = nodes;
        _degree = degree;
        ComponentCount = componentCount;
    }

    /// <summary>Every branch, ordered by the station each starts from.</summary>
    public IReadOnlyList<CenterlineBranch> Branches { get; }

    /// <summary>The stations that survived the contraction, in ascending index order.</summary>
    public IReadOnlyList<int> Nodes { get; }

    /// <summary>How many connected components the network has. Unchanged by the contraction:
    /// removing a degree-2 station never joins or separates anything.</summary>
    public int ComponentCount { get; }

    /// <summary>
    /// How many branch ends meet at this station. A branch that closes on itself counts twice,
    /// once for each end, and two branches joining the same pair of stations both count — the
    /// degree is over branch ends, not over neighbours.
    /// </summary>
    public int Degree(int station) => _degree.TryGetValue(station, out int d) ? d : 0;

    /// <summary>
    /// Number of independent loops in the network: branches minus nodes plus components. Zero
    /// for a network with no loop at all, and it is the count of passages that could be cut
    /// before the network starts falling into pieces.
    /// </summary>
    public int CyclomaticNumber => Branches.Count - Nodes.Count + ComponentCount;

    internal static CenterlineReducedGraph Build(CenterlineGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var branches = new List<CenterlineBranch>();
        var recorded = new HashSet<string>();

        foreach (var component in graph.Components)
        {
            var targets = component.StationIndices.Where(s => graph.Degree(s) != 2).ToList();

            // A component every station of which has two legs is one closed loop with nothing
            // to anchor it. Seeding at its lowest station gives the one branch it contains,
            // and the choice of station is arbitrary but has to be made or the loop is lost.
            if (targets.Count == 0) targets.Add(component.StationIndices[0]);

            foreach (int start in targets)
            {
                foreach (int neighbour in graph.Neighbours(start))
                {
                    var path = Walk(graph, start, neighbour);
                    if (recorded.Add(CanonicalKey(path))) branches.Add(Measure(graph, path));
                }
            }
        }

        branches.Sort(static (a, b) =>
            a.First != b.First ? a.First.CompareTo(b.First) : a.Last.CompareTo(b.Last));

        var degree = new Dictionary<int, int>();
        foreach (var branch in branches)
        {
            degree[branch.First] = degree.GetValueOrDefault(branch.First) + 1;
            degree[branch.Last] = degree.GetValueOrDefault(branch.Last) + 1;
        }

        var nodes = degree.Keys.Order().ToArray();
        return new CenterlineReducedGraph(branches, nodes, degree, graph.Components.Count);
    }

    /// <summary>
    /// Follows the passage from <paramref name="start"/> through <paramref name="neighbour"/>
    /// until it reaches a station that is not simply passed through — a junction, a dead end,
    /// or the station the walk began at.
    /// </summary>
    private static List<int> Walk(CenterlineGraph graph, int start, int neighbour)
    {
        var path = new List<int> { start, neighbour };
        while (path[^1] != start && graph.Degree(path[^1]) == 2)
        {
            int previous = path[^2];
            var onwards = graph.Neighbours(path[^1]);
            path.Add(onwards[0] == previous ? onwards[1] : onwards[0]);
        }
        return path;
    }

    private static CenterlineBranch Measure(CenterlineGraph graph, List<int> path)
    {
        double length = 0.0;
        for (int i = 1; i < path.Count; i++)
        {
            length += (graph.StationPosition(path[i]) - graph.StationPosition(path[i - 1])).Length;
        }
        double chord = (graph.StationPosition(path[^1]) - graph.StationPosition(path[0])).Length;
        return new CenterlineBranch { StationIndices = path, Length = length, ChordLength = chord };
    }

    /// <summary>
    /// One key for a branch and the same branch walked from the other end, so that the second
    /// walk — which always happens, because both ends are targets — is recognised rather than
    /// recorded as a second passage.
    /// </summary>
    private static string CanonicalKey(List<int> path)
    {
        var forward = string.Join(',', path);
        var reversed = string.Join(',', Enumerable.Reverse(path));
        return string.CompareOrdinal(forward, reversed) <= 0 ? forward : reversed;
    }
}
