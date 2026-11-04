using System.Net;
using System.Net.Http;
using System.Text;
using Therion.Assistant;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// The assistant service against a scripted model endpoint: whether a question is answered or refused
/// when the in-app tools server is off.
/// </summary>
public class AssistantServiceTests
{
    private sealed class FakeSettings(AppSettings current) : IAppSettingsService
    {
        public AppSettings Current { get; } = current;
        public event EventHandler? Changed { add { } remove { } }
        public void Save(AppSettings settings) { }
    }

    private sealed class Endpoint : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"role":"assistant","content":"General answer."}}],"usage":{"total_tokens":3}}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (AssistantService Service, Endpoint Model) NewService(AssistantDataPolicy policy, TempDir dir)
    {
        var settings = new FakeSettings(AppSettings.Default with
        {
            EnableMcpServer = false,
            AssistantDataPolicy = policy,
            AssistantDataPolicyChosen = true,
            AssistantStreaming = false,
        });
        var host = WorkspaceEditServiceTests.Stub<IMcpHostService>.Create(_ => null);
        var log = WorkspaceEditServiceTests.Stub<ILogService>.Create(_ => null);
        var model = new Endpoint();
        return (new AssistantService(settings, host, log, Path.Combine(dir.Path, "conversation.json"), modelHandler: model), model);
    }

    [Fact]
    public async Task HideContent_AnswersWithoutTheToolsServer()
    {
        using var dir = new TempDir();
        var (service, model) = NewService(AssistantDataPolicy.HideContent, dir);

        var result = await service.SendAsync("What is a loop closure?", new ChatCallbacks(), CancellationToken.None);

        Assert.Equal("General answer.", result.FinalText);
        Assert.DoesNotContain("\"tools\"", Assert.Single(model.Bodies));
    }

    [Fact]
    public async Task SendAll_WithTheToolsServerOff_IsStillRefused()
    {
        using var dir = new TempDir();
        var (service, model) = NewService(AssistantDataPolicy.SendAll, dir);

        var ex = await Assert.ThrowsAsync<AssistantUnavailableException>(() =>
            service.SendAsync("How many stations?", new ChatCallbacks(), CancellationToken.None));

        Assert.Equal(AssistantUnavailableReason.ServerDisabled, ex.Reason);
        Assert.Empty(model.Bodies);
    }
}
