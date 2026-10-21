using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class OllamaNarrationPerformerTests
{
    private const string Model = "test-narrator:local";
    private static readonly string Digest = new('a', 64);
    private static NarrationProviderCall Call => new("call-1", "attempt-1", "short", "Narrate only supplied astronomy facts.", "Use the requested language.", "prompt-checksum", 1, 40, 100);
    private static string Narration(string text = "The Moon reflects sunlight.")
        => JsonSerializer.Serialize(new { variant = "Short", scenes = new[] { new { sceneNumber = 1, narrationText = text } } });

    [Theory]
    [InlineData("The Moon reflects sunlight.")]
    [InlineData("चन्द्रमा सूर्य के प्रकाश को परावर्तित करता है।")]
    public async Task Invoke_PreservesEnglishAndHindiAndRequestsExistingSceneSchema(string text)
    {
        using var fixture = new Fixture();
        var expected = Narration(text);
        fixture.Router.Chat = (_, _) => Task.FromResult(Chat(expected));
        var call = Call with { UserPrompt = text.Contains('च') ? "हिन्दी में केवल दिए गए तथ्य सुनाएँ।" : "Narrate the supplied facts in English." };
        var result = await fixture.Performer.InvokeAsync(call, CancellationToken.None);

        Assert.Equal(expected, result.Response);
        Assert.Equal("Ollama", result.ProviderName);
        Assert.Equal(Model, result.ModelOrDeployment);
        Assert.Equal(Call.ProviderCallId, result.ProviderCallId);
        Assert.Equal(Call.AttemptId, result.AttemptId);
        Assert.Equal(Call.Variant, result.Variant);
        using var body = JsonDocument.Parse(Assert.Single(fixture.Router.Bodies));
        Assert.Equal(Model, body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(Call.SystemPrompt, body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal(call.UserPrompt, body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
        var format = body.RootElement.GetProperty("format");
        Assert.False(format.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(1, format.GetProperty("properties").GetProperty("scenes").GetProperty("maxItems").GetInt32());
        Assert.Equal(8192, body.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal(100, body.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.Equal(1, fixture.Router.ChatCalls);
        Assert.Equal(2, fixture.Router.TagCalls);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("{\"variant\":\"Short\",\"scenes\":\"wrong-type\"}")]
    [InlineData("{\"variant\":\"Long\",\"scenes\":[{\"sceneNumber\":1,\"narrationText\":\"Moonlight\"}]}")]
    [InlineData("{\"variant\":\"Short\",\"scenes\":[{\"sceneNumber\":1,\"narrationText\":\"\"}]}")]
    [InlineData("{\"variant\":\"Short\",\"scenes\":[{\"sceneNumber\":1,\"narrationText\":\"Moonlight\",\"extra\":true}]}")]
    [InlineData("{\"variant\":\"Short\",\"variant\":\"Short\",\"scenes\":[]}")]
    public async Task InvalidStructuredOutput_IsRejectedWithoutTransportRetryOrPlaceholder(string output)
    {
        using var fixture = new Fixture();
        fixture.Router.Chat = (_, _) => Task.FromResult(Chat(output));
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call, CancellationToken.None));
        Assert.Equal("OutputValidation", ex.Category);
        Assert.False(ex.Retryable);
        Assert.True(ex.InvocationStarted);
        Assert.Equal(1, fixture.Router.ChatCalls);
        Assert.All(fixture.Router.Hosts, host => Assert.Equal("127.0.0.1", host));
    }

    [Fact]
    public async Task DuplicateSceneNumbers_AreRejected()
    {
        using var fixture = new Fixture();
        fixture.Router.Chat = (_, _) => Task.FromResult(Chat("{\"variant\":\"Short\",\"scenes\":[{\"sceneNumber\":1,\"narrationText\":\"Moonlight\"},{\"sceneNumber\":1,\"narrationText\":\"Sunlight\"}]}"));
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call with { SceneCount = 2 }, CancellationToken.None));
        Assert.Equal("OutputValidation", ex.Category);
    }

    [Fact]
    public async Task StructurallyValidOutput_StillRequiresExistingDomainValidation()
    {
        using var fixture = new Fixture();
        const string narration = "Advance LO-12 to reveal the scientific contract.";
        fixture.Router.Chat = (_, _) => Task.FromResult(Chat(Narration(narration)));
        var result = await fixture.Performer.InvokeAsync(Call, CancellationToken.None);
        Assert.Contains(narration, result.Response);
        Assert.NotEmpty(GeneratedNarrationValidator.Validate(narration, "short"));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task TransientHttpFailure_RetriesWithinBoundThenSucceeds(int status)
    {
        using var fixture = new Fixture();
        fixture.Router.Chat = (_, _) => Task.FromResult(fixture.Router.ChatCalls < 3 ? new HttpResponseMessage((HttpStatusCode)status) : Chat(Narration()));
        await fixture.Performer.InvokeAsync(Call, CancellationToken.None);
        Assert.Equal(3, fixture.Router.ChatCalls);
    }

    [Theory]
    [InlineData(429, "RateLimited")]
    [InlineData(502, "Transport")]
    [InlineData(400, "RequestRejected")]
    [InlineData(422, "RequestRejected")]
    [InlineData(401, "Authentication")]
    [InlineData(404, "MissingModel")]
    public async Task ExhaustedOrPermanentHttpFailure_HasNoPaidFallback(int status, string category)
    {
        using var fixture = new Fixture();
        fixture.Router.Chat = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call, CancellationToken.None));
        Assert.Equal(category, ex.Category);
        Assert.Equal(status, ex.HttpStatus);
        Assert.Equal(status == 429 || status >= 500 ? 3 : 1, fixture.Router.ChatCalls);
        Assert.All(fixture.Router.Hosts, host => Assert.Equal("127.0.0.1", host));
    }

    [Fact]
    public async Task ConnectionRefused_IsBoundedAndClassified()
    {
        using var fixture = new Fixture();
        fixture.Router.Chat = (_, _) => throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused));
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call, CancellationToken.None));
        Assert.Equal("Transport", ex.Category);
        Assert.True(ex.Retryable);
        Assert.Equal(3, fixture.Router.ChatCalls);
    }

    [Fact]
    public async Task Timeout_UsesTotalDeadlineAndReleasesConcurrencySlot()
    {
        using var fixture = new Fixture(o => o.TimeoutSeconds = 1);
        fixture.Router.Chat = async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Chat(Narration()); };
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call, CancellationToken.None));
        Assert.Equal("Timeout", ex.Category);
        Assert.True(ex.InvocationStarted);
        Assert.Equal(1, fixture.Router.ChatCalls);
        fixture.Router.Chat = (_, _) => Task.FromResult(Chat(Narration()));
        Assert.Equal("Ollama", (await fixture.Performer.InvokeAsync(Call, CancellationToken.None)).ProviderName);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesWithoutRetryAndReleasesSlot()
    {
        using var fixture = new Fixture();
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Router.Chat = async (_, ct) => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Chat(Narration()); };
        var pending = fixture.Performer.InvokeAsync(Call, cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, fixture.Router.ChatCalls);
        fixture.Router.Chat = (_, _) => Task.FromResult(Chat(Narration()));
        await fixture.Performer.InvokeAsync(Call, CancellationToken.None);
    }

    [Fact]
    public async Task MissingModel_DoesNotGeneratePullOrSwitchModels()
    {
        using var fixture = new Fixture();
        fixture.Router.Tags = () => Json(new { models = Array.Empty<object>() });
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call, CancellationToken.None));
        Assert.Equal("MissingModel", ex.Category);
        Assert.False(ex.InvocationStarted);
        Assert.Equal(0, fixture.Router.ChatCalls);
        Assert.Equal(1, fixture.Router.TagCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-digest")]
    public async Task MissingOrInvalidDigest_IsRejectedBeforeInference(string? digest)
    {
        using var fixture = new Fixture();
        fixture.Router.Tags = () => Json(new { models = new[] { new { name = Model, digest } } });
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call, CancellationToken.None));
        Assert.Equal("P7_PROVIDER_MODEL_IDENTITY_UNVERIFIED", ex.ReasonCode);
        Assert.Equal(0, fixture.Router.ChatCalls);
    }

    [Fact]
    public async Task DirectPerformAsync_UsesTheSameLocalStructuredBoundary()
    {
        using var fixture = new Fixture();
        var response = await fixture.Performer.PerformAsync("system", "user", CancellationToken.None);
        Assert.Equal(Narration(), response);
        Assert.Equal(1, fixture.Router.ChatCalls);
    }

    [Fact]
    public async Task CloudBackedModel_IsRejectedBeforeInference()
    {
        using var fixture = new Fixture();
        fixture.Router.Tags = () => Json(new { models = new[] { new { name = Model, digest = Digest, remote_host = "https://ollama.com", remote_model = "paid-model" } } });
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call, CancellationToken.None));
        Assert.Equal("P7_PROVIDER_REMOTE_MODEL_FORBIDDEN", ex.ReasonCode);
        Assert.Equal(0, fixture.Router.ChatCalls);
    }

    [Fact]
    public async Task ExpectedDigestMismatch_IsRejectedBeforeInference()
    {
        using var fixture = new Fixture(o => o.ExpectedModelDigest = new string('b', 64));
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call, CancellationToken.None));
        Assert.Equal("P7_PROVIDER_MODEL_DIGEST_MISMATCH", ex.ReasonCode);
        Assert.Equal(0, fixture.Router.ChatCalls);
    }

    [Fact]
    public async Task DigestChangeDuringRequest_RejectsResult()
    {
        using var fixture = new Fixture();
        fixture.Router.Tags = () => Tags(fixture.Router.TagCalls == 1 ? Digest : new string('b', 64));
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call, CancellationToken.None));
        Assert.Equal("P7_PROVIDER_MODEL_IDENTITY_CHANGED", ex.ReasonCode);
        Assert.Equal(1, fixture.Router.ChatCalls);
    }

    [Theory]
    [InlineData("wrong-model:tag", "stop", true)]
    [InlineData(Model, "length", true)]
    [InlineData(Model, "stop", false)]
    public async Task WrongModelOrIncompleteEnvelope_IsRejected(string model, string reason, bool done)
    {
        using var fixture = new Fixture();
        fixture.Router.Chat = (_, _) => Task.FromResult(Json(new { model, done, done_reason = reason, message = new { role = "assistant", content = Narration() } }));
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call, CancellationToken.None));
        Assert.Equal("OutputValidation", ex.Category);
        Assert.Equal(1, fixture.Router.ChatCalls);
    }

    [Fact]
    public async Task ContextBudgetFailure_DoesNotTruncateOrCallProvider()
    {
        using var fixture = new Fixture();
        var ex = await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call with { EstimatedInputTokens = 8192 }, CancellationToken.None));
        Assert.Equal("RequestRejected", ex.Category);
        Assert.Empty(fixture.Router.Hosts);
    }

    [Fact]
    public async Task FactoryCreatedInstances_ShareGlobalConcurrencyLimit()
    {
        using var fixture = new Fixture();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Router.Chat = async (_, ct) => { firstEntered.TrySetResult(); await release.Task.WaitAsync(ct); return Chat(Narration()); };
        var first = fixture.Performer.InvokeAsync(Call, CancellationToken.None);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = fixture.Performer.InvokeAsync(Call with { ProviderCallId = "call-2" }, CancellationToken.None);
        Assert.Equal(1, fixture.Router.ChatCalls);
        Assert.Equal(1, fixture.Router.TagCalls);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, fixture.Router.ChatCalls);
        Assert.Equal(4, fixture.Router.TagCalls);
    }

    [Fact]
    public async Task QueuedCancellation_DoesNotConsumeOrLeakSlot()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Router.Chat = async (_, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return Chat(Narration()); };
        var first = fixture.Performer.InvokeAsync(Call, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        var queued = fixture.Performer.InvokeAsync(Call, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(1, fixture.Router.TagCalls);
        release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Performer.InvokeAsync(Call, CancellationToken.None);
    }

    [Fact]
    public async Task Provenance_RecordsDigestPromptVersionValidationTimingAndFailureWithoutPromptBody()
    {
        using var fixture = new Fixture(o => o.ExpectedModelDigest = "sha256:" + Digest);
        var result = await fixture.Performer.InvokeAsync(Call, CancellationToken.None);
        var selected = Assert.Single(fixture.Logger.Events, e => e.ContainsKey("InvocationType"));
        Assert.Equal(7, selected["PhaseNumber"]);
        Assert.Equal("Ollama", selected["Provider"]);
        Assert.Equal(Model, selected["Model"]);
        Assert.Equal(Digest, selected["ModelDigest"]);
        Assert.Equal(Call.ProviderCallId, selected["CorrelationId"]);
        Assert.Equal(Call.Variant, selected["InvocationType"]);
        Assert.DoesNotContain(Call.SystemPrompt, string.Join(" ", selected.Values));
        var success = Assert.Single(fixture.Logger.Events, e => e.ContainsKey("OutputValidationResult"));
        Assert.Equal("Ollama", success["Provider"]);
        Assert.Equal(Model, success["Model"]);
        Assert.Equal(Digest, success["ModelDigest"]);
        Assert.Equal(OllamaNarrationPerformer.PromptTemplateVersion, success["PromptTemplateVersion"]);
        Assert.Equal(Call.PromptChecksum, success["PromptChecksum"]);
        Assert.Equal("StructuredContractValid", success["OutputValidationResult"]);
        Assert.Equal("None", success["FailureCategory"]);
        Assert.IsType<DateTime>(success["RequestStartedUtc"]);
        Assert.True(Assert.IsType<long>(success["ExecutionDurationMs"]) >= 0);
        Assert.True(result.RequestCompletedUtc >= result.RequestStartedUtc);
        Assert.Equal(NarrationProviderCallResult.Completed(Call, "Ollama", Model, result.RequestStartedUtc, result.RequestCompletedUtc, result.Response).ResponseChecksum, result.ResponseChecksum);
        Assert.DoesNotContain(Call.SystemPrompt, string.Join(" ", success.Values));
        fixture.Router.Chat = (_, _) => Task.FromResult(Chat("broken"));
        await Assert.ThrowsAsync<NarrationProviderException>(() => fixture.Performer.InvokeAsync(Call, CancellationToken.None));
        Assert.Equal("OutputValidation", fixture.Logger.Events.Last()["FailureCategory"]);
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private static HttpResponseMessage Chat(string content) => Json(new { model = Model, done = true, done_reason = "stop", message = new { role = "assistant", content } });
    private static HttpResponseMessage Tags(string digest) => Json(new { models = new[] { new { name = Model, digest } } });

    private sealed class Fixture : IDisposable
    {
        public Router Router { get; } = new();
        public RecordingLogger Logger { get; } = new();
        private readonly ServiceProvider _provider;
        // Typed HttpClient clients are transient; every access proves shared gate behavior.
        public INarrationPerformer Performer => _provider.GetRequiredService<OllamaNarrationPerformer>();

        public Fixture(Action<OllamaNarrationOptions>? configure = null)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NarrationAI:Provider"] = "Ollama", ["NarrationAI:Ollama:Model"] = Model,
                ["NarrationAI:Ollama:RetryDelayMilliseconds"] = "0"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddNarrationAI(configuration);
            if (configure is not null) services.Configure<NarrationAIOptions>(o => configure(o.Ollama));
            services.AddSingleton<ILogger<OllamaNarrationPerformer>>(Logger);
            services.AddHttpClient<OllamaNarrationPerformer>().ConfigurePrimaryHttpMessageHandler(() => Router);
            // Any accidental resolution/fallback to Azure makes a test fail immediately.
            services.AddTransient<AzureOpenAiContentGenerationService>(_ => throw new InvalidOperationException("Azure must not be initialized by local narration."));
            _provider = services.BuildServiceProvider();
        }
        public void Dispose() => _provider.Dispose();
    }

    private sealed class Router : HttpMessageHandler
    {
        private int _chatCalls, _tagCalls;
        public int ChatCalls => Volatile.Read(ref _chatCalls);
        public int TagCalls => Volatile.Read(ref _tagCalls);
        public ConcurrentQueue<string> Bodies { get; } = new();
        public ConcurrentQueue<string> Hosts { get; } = new();
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Chat { get; set; } = (_, _) => Task.FromResult(OllamaNarrationPerformerTests.Chat(Narration()));
        public Func<HttpResponseMessage> Tags { get; set; } = () => OllamaNarrationPerformerTests.Tags(Digest);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Hosts.Enqueue(request.RequestUri!.Host);
            Assert.False(request.Headers.Contains("api-key"));
            Assert.Null(request.Headers.Authorization);
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/api/tags")
            {
                Interlocked.Increment(ref _tagCalls);
                return Tags();
            }
            Assert.Equal("/api/chat", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Bodies.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));
            Interlocked.Increment(ref _chatCalls);
            return await Chat(request, cancellationToken);
        }
    }

    private sealed class RecordingLogger : ILogger<OllamaNarrationPerformer>
    {
        public ConcurrentQueue<Dictionary<string, object?>> Events { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
                Events.Enqueue(values.ToDictionary(x => x.Key, x => x.Value));
        }
    }
}
