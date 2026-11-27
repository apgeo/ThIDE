// The Storyboard tab beyond its shots, on its ViewModel: the film's title and end card, the
// language of the texts it writes itself, saving the film as a composite preset that the list then
// offers, and the branch table of a tour shot, worked out from an invented cave and written into
// the shot's route. A fake render service; no Blender, no view.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Blender;
using Therion.Blender.Execution;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public sealed class BlenderStoryboardFilmTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-storyboard-film-" + System.Guid.NewGuid().ToString("N")[..8]);
    private readonly string _lox;

    public BlenderStoryboardFilmTests()
    {
        Directory.CreateDirectory(_dir);
        _lox = Path.Combine(_dir, "invented.lox");
        LoxWriter.WriteFile(TourCave.Build(), _lox);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class FilmService : IBlenderRenderService
    {
        public Storyboard? LastStoryboard { get; private set; }

        public Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
            => throw new System.InvalidOperationException("the tab renders films");

        public Task<RenderResult> RenderStoryboardAsync(Storyboard storyboard, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        {
            LastStoryboard = storyboard;
            return Task.FromResult(new RenderResult { Succeeded = true, OutputPaths = ImmutableArray.Create("out/film.mp4"), FrameCount = 3 });
        }

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
            => Task.FromResult(Path.Combine(outputDir, "render.py"));
    }

    private sealed class Sources(string model) : IBlenderSourceProvider
    {
        public bool HasProject => false;
        public IReadOnlyList<ModelArtifact> DiscoverArtifacts() => [];
        public Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default)
            => Task.FromResult(new RenderSource(new ResolvedModelSource { Path = model, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile }, []));
    }

    private BlenderAnimationViewModel Vm(FilmService? service = null, PresetStore? store = null)
        => new(service ?? new FilmService(), new Sources(_lox), store)
        {
            UseExternalFile = true, ExternalFilePath = _lox, RenderDirectory = "", PanelTab = BlenderAnimationViewModel.StoryboardTab,
        };

    private static void AddComposite(BlenderAnimationViewModel vm, string name)
    {
        vm.SelectedComposite = vm.Composites.First(c => c.Name == name);
        vm.AddCompositeCommand.Execute(null);
    }

    // ---- the title and the end card ----

    [Fact]
    public void The_films_title_and_end_card_are_edited_in_the_tab()
    {
        var vm = Vm();
        AddComposite(vm, "Grand tour");
        Assert.Equal("{cave}", vm.FilmTitle);
        Assert.Equal(3, vm.FilmTitleSeconds);
        Assert.Equal("{cave}", vm.FilmEndCardText);
        Assert.True(vm.HasFilmEndCard);

        vm.FilmTitle = "{cave}, 2026";
        vm.FilmSubtitle = "Invented Caving Club";
        vm.FilmTitleSeconds = 0;
        vm.FilmEndCardText = "Thanks for watching";
        vm.FilmEndCardSubtitle = "An invented survey";
        vm.FilmEndCardSeconds = 6;
        var texts = vm.CurrentStoryboard.Texts!;
        Assert.Equal(("{cave}, 2026", "Invented Caving Club", (double?)null), (texts.Title, texts.Subtitle, texts.TitleSeconds));
        Assert.Equal(("Thanks for watching", "An invented survey", 6.0), (texts.EndCard!.Text, texts.EndCard.Subtitle, texts.EndCard.Seconds));
        Assert.Empty(vm.StoryboardBlockers);

        // A blank end card is none; blank texts are no texts.
        vm.FilmEndCardText = " ";
        Assert.Null(vm.CurrentStoryboard.Texts!.EndCard);
        Assert.False(vm.HasFilmEndCard);
        vm.FilmEndCardSeconds = 9;
        Assert.Null(vm.CurrentStoryboard.Texts!.EndCard);
        vm.FilmTitle = "";
        vm.FilmSubtitle = "";
        Assert.Null(vm.CurrentStoryboard.Texts);

        // A film of a single shot can have them too, and they go when the last shot does.
        vm.FilmTitle = "A title";
        while (vm.Shots.Count > 0) vm.RemoveShotCommand.Execute(vm.Shots[0]);
        Assert.Equal("", vm.FilmTitle);
        Assert.Null(vm.CurrentStoryboard.Texts);
    }

    // ---- the language of the film's texts ----

    [Fact]
    public async Task The_tours_texts_follow_the_films_language_else_the_interfaces()
    {
        var service = new FilmService();
        var vm = Vm(service);
        AddComposite(vm, "Grand tour");
        Assert.Null(vm.SelectedFilmLanguage.Code);
        Assert.Null(vm.CurrentStoryboard.Language);

        await vm.RenderFilmCommand.ExecuteAsync(null);
        Assert.Equal(BlenderAnimationViewModel.InterfaceFilmLanguage, service.LastStoryboard!.Language);

        vm.SelectedFilmLanguage = vm.FilmLanguages.Single(l => l.Code == "ro");
        Assert.Equal("ro", vm.CurrentStoryboard.Language);
        await vm.RenderFilmCommand.ExecuteAsync(null);
        Assert.Equal("ro", service.LastStoryboard!.Language);
        Assert.Equal(["en", "ro"], vm.FilmLanguages.Skip(1).Select(l => l.Code));
    }

    // ---- saving a composite ----

    [Fact]
    public void The_film_is_saved_as_a_composite_which_the_list_then_offers()
    {
        string presets = Path.Combine(_dir, "presets");
        var store = new PresetStore(presets);
        store.Save(BuiltInPresets.HelixDescent() with { Name = "My helix" });
        var vm = Vm(store: store);
        Assert.False(vm.SaveCompositeCommand.CanExecute(null));   // no shots, no name

        AddComposite(vm, "Grand tour");
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "My helix");
        vm.AddShotCommand.Execute(null);
        vm.FilmEndCardText = "Invented end";
        vm.SelectedFilmLanguage = vm.FilmLanguages.Single(l => l.Code == "ro");
        vm.RenderDirectory = Path.Combine(_dir, "renders");

        vm.NewCompositeName = "grand tour";
        Assert.Equal(Tr.Get("Blender_Composite_NameBuiltIn"), vm.CompositeNameProblem);
        Assert.False(vm.SaveCompositeCommand.CanExecute(null));

        vm.NewCompositeName = "  My grand tour ";
        vm.NewCompositeDescription = "Invented, for a test.";
        Assert.Null(vm.CompositeNameProblem);
        Assert.True(vm.SaveCompositeCommand.CanExecute(null));
        vm.SaveCompositeCommand.Execute(null);

        Assert.Equal("My grand tour", vm.SelectedComposite!.Name);
        Assert.Contains(vm.Composites, c => c.Name == "Grand tour");
        Assert.Equal("", vm.NewCompositeName);
        var file = Assert.Single(Directory.GetFiles(presets, "*" + PresetStore.CompositeExtension));
        var saved = CompositePresetSerializer.ReadFile(file);
        Assert.Equal(("My grand tour", "Invented, for a test."), (saved.Name, saved.Description));
        Assert.Equal("ro", saved.Storyboard.Language);
        Assert.Equal("Invented end", saved.Storyboard.Texts!.EndCard!.Text);
        Assert.Equal("", saved.Storyboard.Output.OutputDirectory);
        Assert.Equal(["Outside", "Tour", "My helix"], saved.Storyboard.Shots.Select(s => s.Name));
        // The user's preset is written into its shot: the composite renders where it is not known.
        Assert.Null(saved.Storyboard.Shots[2].Preset);
        Assert.Equal(CameraTemplate.Helix, saved.Storyboard.Shots[2].Camera!.Template);
        Assert.Equal("Orbit showcase", saved.Storyboard.Shots[0].Preset);
        Assert.Empty(StoryboardValidator.Validate(saved.Storyboard));

        // Chosen from the list, it brings the film back.
        var again = Vm(store: store);
        AddComposite(again, "My grand tour");
        Assert.Equal("Invented end", again.FilmEndCardText);
        Assert.Equal("ro", again.SelectedFilmLanguage.Code);
        Assert.Equal(3, again.Shots.Count);
    }

    // ---- the branch table ----

    [Fact]
    public async Task A_tour_shot_lists_its_branches_which_are_visited_left_out_and_ordered_into_its_route()
    {
        var vm = Vm();
        AddComposite(vm, "Grand tour");
        vm.SelectedShot = vm.Shots[0];
        Assert.False(vm.IsTourShot);
        Assert.False(vm.ListBranchesCommand.CanExecute(null));

        vm.SelectedShot = vm.Shots[1];
        Assert.True(vm.IsTourShot);
        await vm.ListBranchesCommand.ExecuteAsync(null);
        Assert.Null(vm.LastError);

        // The most wanted first: by rank, the rules' own order.
        var ids = vm.TourBranches.Select(b => b.Id).ToList();
        Assert.Equal(6, ids.Count);
        Assert.Equal(["B1", "B2", "B3", "B4", "B5", "B6"], ids.Order());
        Assert.All(vm.TourBranches, b => Assert.Equal(BranchVisit.Auto, b.Visit));
        Assert.Equal("Blender.Branch.1.Visit", vm.TourBranches[0].VisitId);
        var b2 = vm.TourBranches.Single(b => b.Id == "B2");
        Assert.Equal(Tr.Get("Blender_Branch_WhyShort"), b2.Outcome);
        Assert.False(b2.Chosen);
        Assert.NotNull(vm.TourSummary);

        // Left out and visited by hand: into the route, and the tour is worked out again.
        vm.TourBranches.Single(b => b.Id == "B3").Visit = BranchVisit.LeaveOut;
        b2.VisitChoice = BranchVisitItem.All.Single(i => i.Value == BranchVisit.Visit);
        var route = vm.Shots[1].Shot.Camera!.Route!;
        Assert.Equal(["B3"], route.Exclude);
        Assert.Equal(["B2"], route.Include);
        Assert.Null(route.Priority);
        await vm.TourPlanning!;
        Assert.Equal(Tr.Get("Blender_Branch_WhyExcluded"), vm.TourBranches.Single(b => b.Id == "B3").Outcome);
        Assert.True(vm.TourBranches.Single(b => b.Id == "B2").Chosen);

        // Moved, the whole order is said.
        var last = vm.TourBranches[^1];
        vm.MoveBranchUpCommand.Execute(last);
        Assert.Equal(5, last.Number);
        var order = vm.TourBranches.Select(b => b.Id).ToList();
        Assert.Equal(order, vm.Shots[1].Shot.Camera!.Route!.Priority);
        await vm.TourPlanning!;
        Assert.Empty(vm.StoryboardBlockers);

        // The settings edit the shot without losing its route's choices.
        vm.Samples += 1;
        route = vm.Shots[1].Shot.Camera!.Route!;
        Assert.Equal((RouteParams.AutoBranches, "B3"), (route.Branches, route.Exclude!.Single()));
        Assert.Equal(order, route.Priority);

        // Listed again, the table shows the order and the choices the route says.
        vm.SelectedShot = vm.Shots[0];
        Assert.Empty(vm.TourBranches);
        vm.SelectedShot = vm.Shots[1];
        await vm.ListBranchesCommand.ExecuteAsync(null);
        Assert.Equal(order, vm.TourBranches.Select(b => b.Id));
        Assert.Equal(BranchVisit.LeaveOut, vm.TourBranches.Single(b => b.Id == "B3").Visit);
        Assert.Equal(BranchVisit.Visit, vm.TourBranches.Single(b => b.Id == "B2").Visit);
    }

    [Fact]
    public async Task A_model_that_cannot_be_read_is_an_error_not_a_table()
    {
        File.WriteAllText(_lox, "not a model");
        var vm = Vm();
        AddComposite(vm, "Grand tour");
        vm.SelectedShot = vm.Shots[1];
        await vm.ListBranchesCommand.ExecuteAsync(null);
        Assert.NotNull(vm.LastError);
        Assert.Empty(vm.TourBranches);
        Assert.False(vm.IsPlanningTour);
    }
}
