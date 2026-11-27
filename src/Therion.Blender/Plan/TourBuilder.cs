// A tour lowered to a storyboard. The passage network says which way the main passage runs and
// which side passages are worth a visit (Therion.CaveGeometry's TourPlanner); here each becomes
// route shots with a time, and the whole is brought within its time budget.
//
// The film: the main passage is flown in stretches, from junction to junction. At a junction the
// chosen side passages are visited before the camera goes on: flown in to the end and left by a
// cut (a short dip back to the junction), or flown in and back out, or only looked into. A bypass
// is flown through to where it meets the main passage again, and the film cuts back.
//
// Time. A shot has the time its route takes at the passages' own speed (CameraRoutes.Measure).
// Over a budget, in this order: a side passage over its own budget is flown up to twice as fast,
// then flown only as far as the time allows ("continues 60 m"); a tour over its budget is flown
// up to twice as fast, then its fly-backs become cuts, then its lowest-ranked side passages are
// left out, and last the main passage itself is cut short. Whatever was left out or cut short is
// in the notices.

using System.Globalization;
using Therion.Blender.Emit;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Plan;

/// <summary>A row of a tour's table: the main passage, or one side passage, with what the tour
/// does about it.</summary>
public sealed record TourRow
{
    /// <summary><c>trunk</c>, or the branch's id ("B3", "B3.1").</summary>
    public required string Id { get; init; }

    /// <summary><c>trunk</c>, <c>branch</c> or <c>bypass</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>0 for the main passage, 1 for what hangs on it.</summary>
    public int Depth { get; init; }

    /// <summary>The station where it leaves the main passage; for the main passage, where the
    /// tour starts.</summary>
    public required string Junction { get; init; }

    /// <summary>Where its way ends: a branch's farthest station, the station where a bypass
    /// comes back, the end of the main passage.</summary>
    public required string End { get; init; }

    /// <summary>The length of all its legs (metres).</summary>
    public required double Length { get; init; }

    /// <summary>How far from the main passage it gets (metres); null for the main passage.</summary>
    public double? Reach { get; init; }

    public double DepthChange { get; init; }

    public double? MeanWidth { get; init; }

    public int Leads { get; init; }

    public IReadOnlyList<string> Surveys { get; init; } = [];

    public int? Rank { get; init; }

    /// <summary>Chosen by the rules or by hand, before the time budget had its say.</summary>
    public bool Chosen { get; init; }

    /// <summary>Why it is chosen or not: <c>trunk</c>, <c>byRank</c>, <c>included</c>,
    /// <c>lead</c>, <c>short</c>, <c>overCount</c>, <c>excluded</c>.</summary>
    public required string Why { get; init; }

    /// <summary>What the film does: <c>trunk</c>, <c>cut</c>, <c>return</c>, <c>through</c>,
    /// <c>peek</c> or <c>skip</c>.</summary>
    public required string Mode { get; init; }

    /// <summary>Why a chosen side passage is not in the film after all: <c>budget</c> (left out
    /// to keep the tour within its time) or <c>notReached</c> (it hangs on a way that was cut
    /// short before it); null otherwise.</summary>
    public string? Dropped { get; init; }

    /// <summary>Its share of the film (seconds).</summary>
    public double Seconds { get; init; }

    /// <summary>How much faster than the passages' own speed it is flown: 1 to 2.</summary>
    public double Speed { get; init; } = 1.0;

    /// <summary>How far along its way the camera gets (metres).</summary>
    public double FlownMeters { get; init; }

    /// <summary>How much of its way is left when the film cuts away; 0 when flown to the end.</summary>
    public double ContinuesMeters { get; init; }
}

/// <summary>A tour worked out for a cave: the storyboard to render, and the table and notices
/// that say what is in it.</summary>
public sealed record Tour
{
    /// <summary>The way and the side systems, as the passage network decides them.</summary>
    public required TourPlan Plan { get; init; }

    public required Storyboard Storyboard { get; init; }

    /// <summary>The main passage first, then every side passage in the order of the tour.</summary>
    public required IReadOnlyList<TourRow> Rows { get; init; }

    /// <summary>What was left out, cut short or changed, and what the options named in vain.</summary>
    public required IReadOnlyList<string> Notices { get; init; }

    /// <summary>The film's length (seconds).</summary>
    public required double Seconds { get; init; }

    /// <summary>The table as text, one row a line, for a terminal or a README.</summary>
    public string TableText()
    {
        static string M(double metres) => metres.ToString("0", CultureInfo.InvariantCulture) + " m";
        static string Signed(double metres) => (metres > 0 ? "+" : "") + M(metres);
        var lines = new List<string[]>
        {
            new[] { "Branch", "Junction", "Length", "Reach", "Depth", "Width", "Leads", "Rank", "Chosen", "Mode", "Seconds" },
        };
        foreach (var row in Rows)
        {
            string chosen = row.Chosen
                ? row.Dropped is null ? (row.Why is "included" or "lead" ? $"yes ({row.Why})" : "yes") : $"no ({(row.Dropped == "budget" ? "time" : "not reached")})"
                : $"no ({(row.Why == "overCount" ? "count" : row.Why)})";
            string seconds = row.Seconds.ToString("0.0", CultureInfo.InvariantCulture);
            var notes = new List<string>();
            if (row.Speed > 1.005) notes.Add("speed " + row.Speed.ToString("0.0#", CultureInfo.InvariantCulture) + "×");
            if (row.ContinuesMeters >= 0.5) notes.Add($"cut short at {M(row.FlownMeters)}: continues {M(row.ContinuesMeters)}");
            if (notes.Count > 0) seconds += " (" + string.Join("; ", notes) + ")";
            lines.Add(
            [
                row.Id, row.Kind == "bypass" || row.Kind == "trunk" ? $"{row.Junction} → {row.End}" : row.Junction, M(row.Length),
                row.Kind == "trunk" ? "—" : row.Kind == "bypass" ? "bypass" : M(row.Reach ?? 0), Signed(row.DepthChange),
                row.MeanWidth is { } width ? width.ToString("0.0", CultureInfo.InvariantCulture) + " m" : "—",
                row.Leads.ToString(CultureInfo.InvariantCulture), row.Rank?.ToString(CultureInfo.InvariantCulture) ?? "—",
                row.Kind == "trunk" ? "yes" : chosen, row.Kind == "trunk" ? "—" : row.Mode, seconds,
            ]);
        }
        var widths = Enumerable.Range(0, lines[0].Length).Select(c => lines.Max(l => l[c].Length)).ToArray();
        return string.Join("\n", lines.Select(l => string.Join("  ", l.Select((cell, c) => c == l.Length - 1 ? cell : cell.PadRight(widths[c]))).TrimEnd()));
    }

    /// <summary>The table, the film's length and the notices, for a bundle's README.</summary>
    public string ReadmeText()
    {
        var text = new System.Text.StringBuilder();
        text.Append(TableText()).Append('\n');
        text.Append('\n').Append("Film: ").Append(Seconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s in ")
            .Append(Storyboard.Shots.Count.ToString(CultureInfo.InvariantCulture)).Append(" shot(s).\n");
        foreach (string notice in Notices) text.Append("- ").Append(notice).Append('\n');
        return text.ToString();
    }
}

/// <summary>Lowers a <see cref="TourSpec"/> to a storyboard for one cave: see the file header.</summary>
public static class TourBuilder
{
    /// <summary>How long the camera holds at the end of a side passage (seconds).</summary>
    public const double EndHoldSeconds = 1.5;

    /// <summary>How long it holds where a bypass meets the main passage again.</summary>
    public const double ThroughHoldSeconds = 0.5;

    /// <summary>The dip that takes the film back to a junction (both halves together).</summary>
    public const double DipSeconds = 0.3;

    /// <summary>How long a look into a side passage from its junction takes.</summary>
    public const double PeekSeconds = 3.0;

    /// <summary>How many times faster the camera flies back out of a side passage than it flew in.</summary>
    public const double ReturnSpeed = 2.0;

    /// <summary>The most a flight is sped up to keep within a budget.</summary>
    public const double MaxSpeedUp = 2.0;

    /// <summary>No shot is shorter than this (seconds): there is room in it for a dip.</summary>
    public const double MinShotSeconds = 0.6;

    /// <summary>The languages of the texts a tour writes over the picture.</summary>
    public static readonly IReadOnlyList<string> Languages = ["en", "ro"];

    /// <summary>
    /// Works out the tour <paramref name="spec"/> asks for in the cave of
    /// <paramref name="geometry"/>.
    /// </summary>
    /// <param name="leads">The leads to count and to mark; null or empty takes the stations the
    /// model flags as continuations.</param>
    /// <param name="presets">Finds the preset the tour names; null knows the built-in ones.</param>
    /// <exception cref="ArgumentException">The tour cannot be made for this cave, or asks for
    /// what does not exist: see <see cref="TourPlanner.Plan"/>; the parameter name is the
    /// option's (<c>tour.start</c> …).</exception>
    public static Tour Build(
        GeometryResult geometry, TourSpec spec, IReadOnlyList<SourceLead>? leads = null, Func<string, RenderPreset?>? presets = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(spec);
        presets ??= BuiltInPresets.ByName;
        if (!(spec.TourSeconds > 0) || spec.TourSeconds > SceneSpecValidator.MaxDurationSeconds)
            throw new ArgumentException("The tour's time is a positive number of seconds.", "tour.tourSeconds");
        if (!(spec.BranchSeconds > 0) || spec.BranchSeconds > SceneSpecValidator.MaxDurationSeconds)
            throw new ArgumentException("A side passage's time is a positive number of seconds.", "tour.branchSeconds");
        if (spec.Fps is < SceneSpecValidator.MinFps or > SceneSpecValidator.MaxFps)
            throw new ArgumentException($"Fps must be within {SceneSpecValidator.MinFps}–{SceneSpecValidator.MaxFps}.", "tour.fps");
        if (!Languages.Contains(spec.Language, StringComparer.Ordinal))
            throw new ArgumentException($"The tour's texts are written in one of: {string.Join(", ", Languages)}.", "tour.language");
        if (!Enum.IsDefined(spec.Mode) || spec.Modes.Values.Any(m => !Enum.IsDefined(m)))
            throw new ArgumentException("A side passage is visited by cut, return or peek, or skipped.", "tour.mode");
        var preset = presets(spec.Preset)
            ?? throw new ArgumentException($"Unknown preset '{spec.Preset}'.", "tour.preset");
        return new Work(geometry, spec, leads, preset).Run();
    }

    private enum PieceKind { Stretch, Return, Peek }

    /// <summary>One shot to be: a stretch of a way, a flight in and back, or a look.</summary>
    private sealed record Piece(
        PieceKind Kind, Visit? Owner, IReadOnlyList<int> Stations, RouteParams? Route, double Travel, double Stops, double EndHold)
    {
        public int Start => Stations[0];

        public int End => Kind == PieceKind.Stretch ? Stations[^1] : Stations[0];
    }

    /// <summary>What the tour does about one side passage; changed while the budget is met.</summary>
    private sealed class Visit
    {
        public required TourBranch Branch { get; init; }
        public required TourBranchMode Mode { get; set; }
        public Visit? Parent { get; init; }
        public List<Visit> Children { get; } = [];

        /// <summary>How many stations of its way are flown.</summary>
        public int Count { get; set; }

        /// <summary>The speed its own budget asks for.</summary>
        public double Speed { get; set; } = 1.0;

        public string? Dropped { get; set; }
        public bool OverBudget { get; set; }
        public bool WasReturn { get; set; }

        /// <summary>Whose budget turned its flight back into a cut: the branch's or the tour's.</summary>
        public string ReturnLostTo { get; set; } = "tour";

        public bool Flies => Mode is TourBranchMode.Cut or TourBranchMode.Return;
        public bool Live => Dropped is null && (Parent is null || Parent.Live);
        public bool CutShort => Flies && Count < Branch.Stations.Count;
    }

    private sealed class Work
    {
        private readonly GeometryResult _geometry;
        private readonly TourSpec _spec;
        private readonly RenderPreset _preset;
        private readonly CameraFraming _framing;
        private readonly CenterlineGraph _graph;
        private readonly TourPlan _plan;
        private readonly Dictionary<int, string?> _leadNotes = new();
        private readonly Dictionary<int, string> _names = new();
        private readonly Dictionary<CaveVector3, TubeSection> _sections = new();
        private readonly Dictionary<(string, PieceKind, int, int, double), Piece> _pieces = new();
        private readonly List<Visit> _onTrunk = [];
        private readonly List<Visit> _visits = [];
        private readonly List<string> _notices = [];
        private readonly bool _ro;
        private int _trunkCount;

        public Work(GeometryResult geometry, TourSpec spec, IReadOnlyList<SourceLead>? leads, RenderPreset preset)
        {
            _geometry = geometry;
            _spec = spec;
            _preset = preset;
            _ro = spec.Language == "ro";
            _framing = CameraFraming.FromGeometry(geometry);
            _graph = geometry.Centerline;
            var model = geometry.RecenteredModel;

            // The leads: those given, each at the station it is found at; else the model's flags.
            var nodeAt = new Dictionary<CaveVector3, int>(model.Stations.Count);
            for (int i = 0; i < model.Stations.Count; i++) nodeAt.TryAdd(model.Stations[i].Position, i);
            if (leads is { Count: > 0 })
            {
                foreach (var lead in LeadsEnricher.Enrich(SceneMeta.Build(geometry), leads).Leads)
                    if (nodeAt.TryGetValue(new CaveVector3(lead.Position.X, lead.Position.Y, lead.Position.Z), out int node))
                        _leadNotes.TryAdd(node, lead.Note);
            }
            else
            {
                for (int i = 0; i < model.Stations.Count; i++)
                    if ((model.Stations[i].Flags & CaveStationFlags.Continuation) != 0)
                        _leadNotes.TryAdd(nodeAt[model.Stations[i].Position], string.IsNullOrWhiteSpace(model.Stations[i].Comment) ? null : model.Stations[i].Comment!.Trim());
            }

            _plan = TourPlanner.Plan(_graph, model, spec.ToOptions(), _leadNotes.Keys);
            _trunkCount = _plan.TrunkStations.Count;

            var flown = _plan.TrunkStations.Concat(_plan.Branches.SelectMany(b => b.Stations)).Distinct().ToList();
            var names = CameraRoutes.NamesOf(_framing, flown);
            for (int i = 0; i < flown.Count; i++) _names[flown[i]] = names[i];
            foreach (var tube in geometry.TubeLegs)
            {
                _sections.TryAdd(tube.From.Station, tube.From);
                _sections.TryAdd(tube.To.Station, tube.To);
            }
        }

        private string T(string en, string ro) => _ro ? ro : en;

        private static string Metres(double metres) => Math.Round(metres).ToString("0", CultureInfo.InvariantCulture);

        private static string Seconds(double seconds) => seconds.ToString("0.#", CultureInfo.InvariantCulture);

        private double LengthOf(IReadOnlyList<int> stations, int count)
        {
            double length = 0;
            for (int i = 1; i < count; i++) length += (_graph.StationPosition(stations[i]) - _graph.StationPosition(stations[i - 1])).Length;
            return length;
        }

        // ---- the film ----

        public Tour Run()
        {
            SetVisits();
            foreach (var visit in _onTrunk) Fit(visit);
            double speed = MeetBudget();
            var pieces = Pieces();
            var seconds = pieces.Select(p => SecondsOf(p, speed)).ToList();
            double total = seconds.Sum();
            if (total > _spec.TourSeconds + 0.5 / _spec.Fps)
                _notices.Add($"The tour takes {Seconds(total)} s, over its {Seconds(_spec.TourSeconds)} s: its first leg alone takes that long at twice the passages' speed.");
            else if (speed > 1.005)
                _notices.Add($"The tour is flown {speed.ToString("0.0#", CultureInfo.InvariantCulture)}× faster than the passages' own speed, to keep within {Seconds(_spec.TourSeconds)} s.");
            AddNotices();

            var storyboard = new Storyboard
            {
                Name = _spec.Name ?? T("{cave}: tour", "{cave}: tur"),
                Fps = _spec.Fps,
                // The look of the preset, said once for every shot; and none of the cave's own
                // labels: a tour marks the leads on its way itself.
                Defaults = new StoryboardDefaults
                {
                    Engine = _preset.Spec.Engine, Materials = _preset.Spec.Materials, Lighting = _preset.Spec.Lighting, Labels = new LabelsSpec(),
                },
                Shots = Shots(pieces, seconds),
                Output = _spec.Output ?? new OutputSpec { BaseName = "cave-tour" },
            };
            return new Tour
            {
                Plan = _plan, Storyboard = storyboard, Rows = Rows(pieces, seconds, speed), Notices = _notices, Seconds = Math.Round(total, 3),
            };
        }

        /// <summary>A visit for every side passage the tour does something about, each on the
        /// way it hangs on.</summary>
        private void SetVisits()
        {
            var byId = new Dictionary<string, Visit>(StringComparer.Ordinal);
            foreach (var branch in _plan.Branches)
            {
                bool named = _spec.Modes.TryGetValue(branch.Id, out var mode);
                if (!named)
                    mode = branch.Chosen ? _spec.Mode
                        : _spec.PeekShort && branch.Choice == TourChoice.Short ? TourBranchMode.Peek : TourBranchMode.Skip;
                if (mode == TourBranchMode.Skip) continue;
                Visit? parent = null;
                // What hangs on a way that is not flown is not visited.
                if (branch.Parent is { } on && !(byId.TryGetValue(on, out parent) && parent.Flies)) continue;
                if (mode == TourBranchMode.Return && branch.Kind == TourSideKind.Bypass)
                {
                    if (named) _notices.Add($"{branch.Id} is a bypass: it is flown through to where it meets the main passage again, not in and back.");
                    mode = TourBranchMode.Cut;
                }
                var visit = new Visit { Branch = branch, Mode = mode, Parent = parent, Count = branch.Stations.Count };
                byId[branch.Id] = visit;
                _visits.Add(visit);
                (parent?.Children ?? _onTrunk).Add(visit);
            }
            // A flight in and back is one shot; with visits on its way it is flown in stretches.
            foreach (var visit in _visits.Where(v => v.Mode == TourBranchMode.Return && v.Children.Count > 0))
            {
                _notices.Add($"{visit.Branch.Id} has side passages of its own in the tour: it is left by a cut, not flown back.");
                visit.Mode = TourBranchMode.Cut;
            }
            foreach (string id in _spec.Modes.Keys.Where(id => _plan.Branches.All(b => b.Id != id)).Order(StringComparer.Ordinal))
                _notices.Add($"'{id}' (modes) names no side passage of this tour.");
        }

        private Piece Stretch(Visit? owner, IReadOnlyList<int> path, int from, int to, double hold)
        {
            var key = (owner?.Branch.Id ?? "", PieceKind.Stretch, from, to, hold);
            if (_pieces.TryGetValue(key, out var piece)) return piece;
            var stations = new int[to - from + 1];
            for (int i = 0; i < stations.Length; i++) stations[i] = path[from + i];
            var route = new RouteParams { Stations = stations.Select(s => _names[s]).ToList(), EndHoldSeconds = hold };
            var measure = CameraRoutes.Measure(_framing, route);
            return _pieces[key] = new Piece(PieceKind.Stretch, owner, stations, route, measure.TravelSeconds, measure.HoldSeconds + measure.TurnSeconds, hold);
        }

        private Piece Return(Visit visit)
        {
            var key = (visit.Branch.Id, PieceKind.Return, 0, visit.Count - 1, EndHoldSeconds);
            if (_pieces.TryGetValue(key, out var piece)) return piece;
            var stations = visit.Branch.Stations.Take(visit.Count).ToList();
            var route = new RouteParams
            {
                From = _names[stations[0]], Via = [_names[stations[^1]]], To = _names[stations[0]], HoldSeconds = EndHoldSeconds,
                Speeds = [1.0, ReturnSpeed],
            };
            var measure = CameraRoutes.Measure(_framing, route);
            return _pieces[key] = new Piece(PieceKind.Return, visit, stations, route, measure.TravelSeconds, measure.HoldSeconds + measure.TurnSeconds, EndHoldSeconds);
        }

        /// <summary>The shots of a way with what is visited from it: a stretch to each junction,
        /// the visits there, and on.</summary>
        private void Lay(Visit? owner, IReadOnlyList<int> path, int count, List<Visit> visits, double endHold, List<Piece> into)
        {
            int cursor = 0, last = count - 1;
            foreach (var group in visits.Where(v => v.Dropped is null && v.Branch.JunctionAt <= last).GroupBy(v => v.Branch.JunctionAt).OrderBy(g => g.Key))
            {
                if (group.Key > cursor) into.Add(Stretch(owner, path, cursor, group.Key, 0.0));
                foreach (var visit in group) LayVisit(visit, into);
                cursor = group.Key;
            }
            if (cursor < last) into.Add(Stretch(owner, path, cursor, last, endHold));
        }

        private void LayVisit(Visit visit, List<Piece> into)
        {
            if (visit.Dropped is not null) return;
            if (visit.Mode == TourBranchMode.Peek)
                into.Add(new Piece(PieceKind.Peek, visit, visit.Branch.Stations, null, 0.0, PeekSeconds, 0.0));
            else if (visit.Mode == TourBranchMode.Return)
                into.Add(Return(visit));
            else
                Lay(visit, visit.Branch.Stations, visit.Count, visit.Children,
                    visit.Branch.Kind == TourSideKind.Bypass && !visit.CutShort ? ThroughHoldSeconds : EndHoldSeconds, into);
        }

        private List<Piece> Pieces()
        {
            var pieces = new List<Piece>();
            Lay(null, _plan.TrunkStations, _trunkCount, _onTrunk, EndHoldSeconds, pieces);
            return pieces;
        }

        private double SecondsOf(Piece piece, double speed)
        {
            double factor = Math.Clamp(Math.Max(piece.Owner?.Speed ?? 1.0, speed), 1.0, MaxSpeedUp);
            double seconds = piece.Kind == PieceKind.Peek ? PeekSeconds : piece.Stops + piece.Travel / factor;
            // Whole frames, and room for a dip at either end.
            int frames = Math.Max((int)Math.Floor(seconds * _spec.Fps + 1e-6), Math.Max((int)Math.Ceiling(MinShotSeconds * _spec.Fps), 4));
            return frames / (double)_spec.Fps;
        }

        // ---- the budgets ----

        /// <summary>A side passage within its own budget: faster, up to twice; then only as far
        /// as the time allows. Then what hangs on the part of it that is flown.</summary>
        private void Fit(Visit visit)
        {
            if (visit.Flies)
            {
                (double Stops, double Travel) Own()
                {
                    var pieces = new List<Piece>();
                    LayVisit(visit, pieces);
                    var own = pieces.Where(p => p.Owner == visit).ToList();
                    return (own.Sum(p => p.Stops), own.Sum(p => p.Travel));
                }
                double budget = _spec.BranchSeconds;
                int all = visit.Branch.Stations.Count;
                visit.Count = all;
                visit.Speed = 1.0;
                visit.OverBudget = false;
                var (stops, travel) = Own();
                if (visit.Mode == TourBranchMode.Return && stops + travel / MaxSpeedUp > budget)
                {
                    // Rather the whole passage and a cut than half of it and a flight back.
                    visit.Mode = TourBranchMode.Cut;
                    visit.WasReturn = true;
                    visit.ReturnLostTo = "branch";
                    (stops, travel) = Own();
                }
                if (stops + travel > budget)
                {
                    if (budget > stops && travel / (budget - stops) <= MaxSpeedUp) visit.Speed = travel / (budget - stops);
                    else
                    {
                        visit.Speed = MaxSpeedUp;
                        int low = 2, high = all - 1, best = 0;
                        while (low <= high)
                        {
                            visit.Count = (low + high) / 2;
                            (stops, travel) = Own();
                            if (stops + travel / MaxSpeedUp <= budget) (best, low) = (visit.Count, visit.Count + 1);
                            else high = visit.Count - 1;
                        }
                        visit.Count = best > 0 ? best : Math.Min(2, all);
                        visit.OverBudget = best == 0;
                    }
                }
            }
            foreach (var child in visit.Children)
            {
                bool reached = visit.Flies && child.Branch.JunctionAt <= visit.Count - 1;
                if (!reached) child.Dropped = "notReached";
                else if (child.Dropped == "notReached") child.Dropped = null;
                if (child.Dropped is null) Fit(child);
            }
        }

        /// <summary>Brings the tour within its time, and returns the speed everything is flown at.</summary>
        private double MeetBudget()
        {
            double budget = _spec.TourSeconds + 1e-9;
            double Total(double speed) => Pieces().Sum(p => SecondsOf(p, speed));
            if (Total(1.0) <= budget) return 1.0;

            // The lowest first: a look before a visit, a visit the rules chose before one chosen
            // by hand, what hangs deeper before what it hangs on; then one not in the priority
            // list before one in it, the worse rank before the better, the lower in the list
            // before the higher.
            var priority = _spec.Priority ?? [];
            int Wanted(Visit v)
            {
                for (int i = 0; i < priority.Count; i++)
                    if (string.Equals(priority[i], v.Branch.Id, StringComparison.OrdinalIgnoreCase)) return priority.Count - i;
                return 0;
            }
            var lowestFirst = _visits
                .OrderBy(v => v.Mode == TourBranchMode.Peek ? 0 : v.Branch.Choice == TourChoice.Included ? 2 : 1)
                .ThenByDescending(v => v.Branch.Depth).ThenBy(Wanted).ThenByDescending(v => v.Branch.Rank).ToList();
            if (Total(MaxSpeedUp) > budget)
            {
                foreach (var visit in lowestFirst.Where(v => v.Live && v.Mode == TourBranchMode.Return))
                {
                    visit.Mode = TourBranchMode.Cut;
                    visit.WasReturn = true;
                    Fit(visit);
                    if (Total(MaxSpeedUp) <= budget) break;
                }
            }
            if (Total(MaxSpeedUp) > budget)
            {
                foreach (var visit in lowestFirst.Where(v => v.Live))
                {
                    visit.Dropped = "budget";
                    if (Total(MaxSpeedUp) <= budget) break;
                }
            }
            if (Total(MaxSpeedUp) > budget)
            {
                // Nothing is left but the main passage: it is flown as far as the time allows.
                int low = 2, high = _plan.TrunkStations.Count - 1, best = 0;
                while (low <= high)
                {
                    _trunkCount = (low + high) / 2;
                    if (Total(MaxSpeedUp) <= budget) (best, low) = (_trunkCount, _trunkCount + 1);
                    else high = _trunkCount - 1;
                }
                _trunkCount = best > 0 ? best : 2;
                if (best == 0) return MaxSpeedUp;
            }
            // The least speed that fits.
            double slow = 1.0, fast = MaxSpeedUp;
            for (int i = 0; i < 40; i++)
            {
                double middle = (slow + fast) * 0.5;
                if (Total(middle) <= budget) fast = middle;
                else slow = middle;
            }
            return fast;
        }

        private void AddNotices()
        {
            if (_plan.OtherComponents > 0)
                _notices.Add(
                    $"The tour stays in the part of the cave it starts in: {_plan.OtherComponents} other part(s), {Metres(_plan.OtherComponentsLength)} m of passage that no leg joins to it, get no visit.");
            foreach (string entry in _plan.Unmatched.Where(e => !_spec.Modes.ContainsKey(e)))
                _notices.Add($"'{entry}' (include or exclude) names no side passage of this tour.");
            foreach (string id in (_spec.Priority ?? []).Where(id => _plan.Branches.All(b => !string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase))
                _notices.Add($"'{id}' (priority) names no side passage of this tour.");
            if (_trunkCount < _plan.TrunkStations.Count)
            {
                double flown = LengthOf(_plan.TrunkStations, _trunkCount);
                _notices.Add(
                    $"The main passage is flown for {Metres(flown)} m of its {Metres(_plan.TrunkLength)} m, to keep the tour within {Seconds(_spec.TourSeconds)} s: it continues {Metres(_plan.TrunkLength - flown)} m.");
            }
            foreach (var visit in _visits)
            {
                var branch = visit.Branch;
                bool trunkReaches = visit.Parent is not null || branch.JunctionAt <= _trunkCount - 1;
                if (visit.Dropped == "budget")
                    _notices.Add($"{branch.Id} (rank {branch.Rank}, {Metres(branch.Length)} m) is left out, to keep the tour within {Seconds(_spec.TourSeconds)} s.");
                else if (visit.Dropped == "notReached" || !trunkReaches || !(visit.Parent?.Live ?? true))
                {
                    if (visit.Parent is null || visit.Parent.Live)
                        _notices.Add($"{branch.Id} is not visited: the way it hangs on is cut short before its junction.");
                }
                else
                {
                    if (visit.WasReturn)
                        _notices.Add(visit.ReturnLostTo == "branch"
                            ? $"{branch.Id} is left by a cut instead of a flight back, to keep within {Seconds(_spec.BranchSeconds)} s."
                            : $"{branch.Id} is left by a cut instead of a flight back, to keep the tour within {Seconds(_spec.TourSeconds)} s.");
                    if (visit.CutShort)
                    {
                        double flown = LengthOf(branch.Stations, visit.Count);
                        _notices.Add(
                            $"{branch.Id} is flown for {Metres(flown)} m of its way of {Metres(branch.RouteLength)} m, to keep within {Seconds(_spec.BranchSeconds)} s: it continues {Metres(branch.RouteLength - flown)} m.");
                    }
                    if (visit.OverBudget)
                        _notices.Add($"{branch.Id} takes more than {Seconds(_spec.BranchSeconds)} s: its first leg alone takes longer at twice the passage's speed.");
                }
            }
        }

        // ---- the storyboard ----

        private List<ShotSpec> Shots(List<Piece> pieces, List<double> seconds)
        {
            // A dip of at least two frames; the camera of the preset, flying a route.
            var dip = new TransitionSpec { Kind = TransitionKind.Dip, Seconds = Math.Max(DipSeconds, 2.0 / _spec.Fps) };
            var camera = new CameraSpec { FocalLength = _preset.Spec.Camera.FocalLength, Dof = _preset.Spec.Camera.Dof };
            var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
            var counts = pieces.GroupBy(p => p.Owner?.Branch.Id ?? "").ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            int lastOnTrunk = pieces.FindLastIndex(p => p.Owner is null);

            var shots = new List<ShotSpec>(pieces.Count);
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                var visit = piece.Owner;
                string id = visit?.Branch.Id ?? "";
                int number = numbers[id] = numbers.GetValueOrDefault(id) + 1;
                bool lastOfItsWay = number == counts[id];
                string name = visit is null ? $"{T("Main passage", "Galeria principală")} {number}"
                    : piece.Kind == PieceKind.Peek ? $"{id}: {T("a look", "privire")}"
                    : counts[id] > 1 ? $"{id} ({number})" : id;

                // "continues 60 m" while the camera holds where the way is cut short.
                double continues = !lastOfItsWay ? 0.0
                    : visit is null ? (i == lastOnTrunk ? _plan.TrunkLength - LengthOf(_plan.TrunkStations, _trunkCount) : 0.0)
                    : visit.CutShort ? visit.Branch.RouteLength - LengthOf(visit.Branch.Stations, visit.Count) : 0.0;
                IReadOnlyList<CaptionSpec> captions = continues >= 0.5
                    ? [new CaptionSpec
                    {
                        Text = string.Format(CultureInfo.InvariantCulture, T("continues {0} m", "continuă încă {0} m"), Metres(continues)),
                        Start = Math.Round(Math.Max(0.0, seconds[i] - Math.Max(piece.EndHold, EndHoldSeconds)), 3), End = Math.Round(seconds[i], 3),
                    }]
                    : [];
                string? corner = visit is null ? null : string.Format(
                    CultureInfo.InvariantCulture,
                    visit.Branch.Kind == TourSideKind.Bypass ? T("Bypass {0} · {1} m", "Ocolire {0} · {1} m") : T("Side passage {0} · {1} m", "Galerie laterală {0} · {1} m"),
                    id, Metres(visit.Branch.Length));

                var annotations = piece.Kind == PieceKind.Peek ? new List<AnnotationSpec>() : LeadAnnotations(piece.Stations);
                shots.Add(new ShotSpec
                {
                    Name = name,
                    Seconds = Math.Round(seconds[i], 4),
                    Camera = piece.Kind == PieceKind.Peek ? camera with { Template = CameraTemplate.Viewpoints, Viewpoints = Look(visit!.Branch) }
                        : camera with { Template = CameraTemplate.Route, Route = piece.Route },
                    Annotations = annotations.Count > 0 ? annotations : null,
                    Texts = corner is null && captions.Count == 0 ? null : new OverlaySpec { Corner = corner, CornerCaptions = captions },
                    In = i > 0 && pieces[i - 1].End != piece.Start ? dip : null,
                });
            }
            return shots;
        }

        private double RadiusAt(int station)
            => _sections.TryGetValue(_graph.StationPosition(station), out var section)
                ? Math.Max((section.HalfWidth + section.HalfHeight) * 0.5, 0.3)
                : CameraRoutes.FallbackRadius;

        /// <summary>A marker and a text at every lead on the way, there while the camera is near.</summary>
        private List<AnnotationSpec> LeadAnnotations(IReadOnlyList<int> stations)
        {
            var annotations = new List<AnnotationSpec>();
            foreach (int station in stations.Distinct())
            {
                if (!_leadNotes.TryGetValue(station, out string? note)) continue;
                double radius = RadiusAt(station);
                var local = _graph.StationPosition(station);
                var at = new CaveVector3(
                    Math.Round(local.X + _geometry.Offset.X, 3), Math.Round(local.Y + _geometry.Offset.Y, 3), Math.Round(local.Z + _geometry.Offset.Z, 3));
                double within = Math.Round(Math.Clamp(6.0 * radius, 10.0, 40.0), 1);
                string id = "lead-" + (annotations.Count / 2 + 1).ToString(CultureInfo.InvariantCulture);
                annotations.Add(new AnnotationSpec
                {
                    Id = id, Kind = AnnotationKind.Marker, At = at, Size = Math.Round(Math.Clamp(0.2 * radius, 0.1, 0.8), 3), Within = within, Role = "lead",
                });
                annotations.Add(new AnnotationSpec
                {
                    Id = id + "-text", Kind = AnnotationKind.Callout, At = at, Text = string.IsNullOrWhiteSpace(note) ? T("Lead", "Pistă") : note,
                    Size = Math.Round(Math.Clamp(0.12 * radius, 0.06, 0.5), 3), Offset = new CaveVector3(0, 0, Math.Round(0.5 * radius, 3)),
                    Within = within, Role = "lead-text",
                });
            }
            return annotations;
        }

        /// <summary>The camera of a look into a side passage: at the junction, turned to a point
        /// a few metres along the passage, and moving a little towards it.</summary>
        private ViewpointParams Look(TourBranch branch)
        {
            var station = _graph.StationPosition(branch.Junction);
            var eye = _sections.TryGetValue(station, out var section) ? section.Center : station;
            double reach = Math.Max(Math.Min(6.0, branch.RouteLength), 1.0);
            var target = _graph.StationPosition(branch.Stations[^1]);
            double along = 0;
            for (int i = 1; i < branch.Stations.Count; i++)
            {
                var a = _graph.StationPosition(branch.Stations[i - 1]);
                var b = _graph.StationPosition(branch.Stations[i]);
                double leg = (b - a).Length;
                if (along + leg >= reach && leg > 0)
                {
                    target = a + (b - a) * ((reach - along) / leg);
                    break;
                }
                along += leg;
            }
            var towards = target - eye;
            if (towards.Length < 0.5) (target, towards) = (eye + new CaveVector3(1, 0, 0), new CaveVector3(1, 0, 0));
            static CaveVector3 R(CaveVector3 p) => new(Math.Round(p.X, 3), Math.Round(p.Y, 3), Math.Round(p.Z, 3));
            return new ViewpointParams
            {
                Viewpoints =
                [
                    new CameraViewpoint { Position = R(eye), Target = R(target) },
                    new CameraViewpoint { Position = R(eye + towards.Normalized() * Math.Min(0.5, towards.Length * 0.2)), Target = R(target) },
                ],
            };
        }

        // ---- the table ----

        private List<TourRow> Rows(List<Piece> pieces, List<double> seconds, double speed)
        {
            double Time(Visit? owner) => Enumerable.Range(0, pieces.Count).Where(i => pieces[i].Owner == owner).Sum(i => seconds[i]);
            var trunk = _plan.TrunkStations;
            double trunkFlown = LengthOf(trunk, _trunkCount), fall = 0, startZ = _graph.StationPosition(trunk[0]).Z;
            foreach (int station in trunk)
            {
                double dz = _graph.StationPosition(station).Z - startZ;
                if (Math.Abs(dz) > Math.Abs(fall)) fall = dz;
            }
            var rows = new List<TourRow>
            {
                new()
                {
                    Id = "trunk", Kind = "trunk", Junction = _names[trunk[0]], End = _names[trunk[^1]], Length = Math.Round(_plan.TrunkLength, 1),
                    DepthChange = Math.Round(fall, 1), Leads = _plan.TrunkLeads.Count, Chosen = true, Why = "trunk", Mode = "trunk",
                    Seconds = Math.Round(Time(null), 2), Speed = Math.Round(speed, 2), FlownMeters = Math.Round(trunkFlown, 1),
                    ContinuesMeters = Math.Round(_plan.TrunkLength - trunkFlown, 1),
                },
            };
            var visits = _visits.ToDictionary(v => v.Branch.Id, StringComparer.Ordinal);
            foreach (var branch in _plan.Branches)
            {
                visits.TryGetValue(branch.Id, out var visit);
                bool inFilm = visit is { Live: true } && pieces.Any(p => p.Owner == visit);
                double flown = inFilm && visit!.Flies ? LengthOf(branch.Stations, visit.Count) : 0.0;
                string choice = branch.Choice.ToString();
                rows.Add(new TourRow
                {
                    Id = branch.Id, Kind = branch.Kind == TourSideKind.Bypass ? "bypass" : "branch", Depth = branch.Depth,
                    Junction = _names[branch.Junction], End = _names[branch.Stations[^1]],
                    Length = Math.Round(branch.Length, 1), Reach = Math.Round(branch.Reach, 1), DepthChange = Math.Round(branch.DepthChange, 1),
                    MeanWidth = branch.MeanWidth is { } width ? Math.Round(width, 2) : null,
                    Leads = branch.Leads.Count, Surveys = branch.Surveys, Rank = branch.Rank,
                    Chosen = branch.Chosen, Why = char.ToLowerInvariant(choice[0]) + choice[1..],
                    Mode = !inFilm ? "skip"
                        : visit!.Mode == TourBranchMode.Peek ? "peek"
                        : branch.Kind == TourSideKind.Bypass ? "through"
                        : visit.Mode == TourBranchMode.Return ? "return" : "cut",
                    Dropped = inFilm || visit is null ? null : visit.Dropped == "budget" || !(visit.Parent?.Live ?? true) && visit.Dropped is null ? "budget" : "notReached",
                    Seconds = inFilm ? Math.Round(Time(visit), 2) : 0.0,
                    Speed = inFilm && visit!.Flies ? Math.Round(Math.Clamp(Math.Max(visit.Speed, speed), 1.0, MaxSpeedUp), 2) : 1.0,
                    FlownMeters = Math.Round(flown, 1),
                    ContinuesMeters = inFilm && visit!.Flies ? Math.Round(branch.RouteLength - flown, 1) : 0.0,
                });
            }
            return rows;
        }
    }
}
