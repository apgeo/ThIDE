using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

/// <summary>
/// The disk side of the edit service: files no editor tab holds. (Open-buffer routing needs a live
/// editor and is covered by the UI tests of the features that use it.)
/// </summary>
public class WorkspaceEditServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-edit-service-").FullName;
    private readonly List<string> _suppressed = [];
    private readonly List<string> _reloaded = [];

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string P(string name) => Path.Combine(_root, name);

    private WorkspaceEditService Service()
    {
        var docs = Stub<IDocumentService>.Create(m => m.Name == "get_Documents" ? new ObservableCollection<FileDocumentViewModel>() : null);
        var session = Stub<IWorkspaceSession>.Create(m => m.Name switch
        {
            "get_ActiveThconfig" => new ThconfigCandidate(P("thconfig"), "thconfig", false),
            "SuppressSelfWrite" => null,
            "SetActiveThconfigAsync" => Task.FromResult(true),
            _ => null,
        }, (m, args) =>
        {
            if (m.Name == "SuppressSelfWrite") _suppressed.Add((string)args[0]!);
            if (m.Name == "SetActiveThconfigAsync") _reloaded.Add((string)args[0]!);
        });
        return new WorkspaceEditService(docs, session);
    }

    [Fact]
    public async Task Edits_creates_and_copies_on_disk_in_the_files_own_encoding()
    {
        File.WriteAllText(P("main.th"), "encoding iso-8859-1\nsurvey Bédeilhac\nendsurvey\n", Encoding.Latin1);
        File.WriteAllText(P("src.th"), "survey new\nendsurvey\n");
        var batch = new EditBatch(
            [new FileTextEdits(P("main.th"), [TextEdit.Insert(37, "  input sub/new.th\n")])],
            [new FileCreate(P("notes.th"), "# é\n")],
            [new FileCopy(P("src.th"), P("sub/new.th"))],
            RequiresWorkspaceReload: true);

        var outcome = await Service().ApplyAsync(batch);

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal("encoding iso-8859-1\nsurvey Bédeilhac\n  input sub/new.th\nendsurvey\n",
            File.ReadAllText(P("main.th"), Encoding.Latin1));
        Assert.Equal("survey new\nendsurvey\n", File.ReadAllText(P("sub/new.th")));
        Assert.Equal("# é\n", File.ReadAllText(P("notes.th"), Encoding.UTF8));
        Assert.Equal([P("notes.th"), P("sub/new.th"), P("main.th")], outcome.Written);
        Assert.Equal(outcome.Written, _suppressed);
        Assert.Equal([P("thconfig")], _reloaded);
    }

    [Fact]
    public async Task A_stale_edit_writes_nothing()
    {
        File.WriteAllText(P("main.th"), "survey a\nendsurvey\n");
        File.WriteAllText(P("src.th"), "x");
        var batch = new EditBatch(
            [new FileTextEdits(P("main.th"), [new TextEdit(0, 6, "centre", "survey")])],
            [],
            [new FileCopy(P("src.th"), P("copy.th"))],
            RequiresWorkspaceReload: true);

        var outcome = await Service().ApplyAsync(batch);

        Assert.False(outcome.Success);
        Assert.Equal(EditProblemKind.Stale, Assert.Single(outcome.Problems).Kind);
        Assert.False(File.Exists(P("copy.th")));
        Assert.Empty(_reloaded);
    }

    /// <summary>An interface implementation answering every member from one function.</summary>
    public class Stub<T> : DispatchProxy where T : class
    {
        private Func<MethodInfo, object?> _answer = _ => null;
        private Action<MethodInfo, object?[]> _observe = (_, _) => { };

        public static T Create(Func<MethodInfo, object?> answer, Action<MethodInfo, object?[]>? observe = null)
        {
            var proxy = DispatchProxy.Create<T, Stub<T>>();
            var stub = (Stub<T>)(object)proxy;
            stub._answer = answer;
            if (observe is not null) stub._observe = observe;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            _observe(method!, args ?? []);
            var value = _answer(method!);
            if (value is null && method!.ReturnType.IsValueType && method.ReturnType != typeof(void))
                return Activator.CreateInstance(method.ReturnType);
            return value;
        }
    }
}
