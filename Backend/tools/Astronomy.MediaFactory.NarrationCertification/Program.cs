using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.NarrationCertification;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3 || args[0] != "--config" || args[2] is not ("--live" or "--dry-run"))
        {
            Console.Error.WriteLine("Usage: dotnet run --project Backend/tools/Astronomy.MediaFactory.NarrationCertification -- --config <json-path> --live|--dry-run");
            return 2;
        }
        var output = Directory.CreateTempSubdirectory("rc2-ollama-certification-").FullName;
        Console.WriteLine($"Disposable certification output: {output}");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(args[1]), optional: false)
                .AddEnvironmentVariables("RC2_CERT_").Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddNarrationAI(config);
            var logs = new CertificationLogger();
            var counters = new TransportCounters();
            services.AddSingleton<ILogger<OllamaNarrationPerformer>>(logs);
            services.AddHttpClient<OllamaNarrationPerformer>().AddHttpMessageHandler(sp =>
                new LocalOnlyTransportGuard(new Uri(sp.GetRequiredService<IOptions<NarrationAIOptions>>().Value.Ollama.BaseUrl), counters));
            services.AddTransient<AzureOpenAiContentGenerationService>(_ =>
            {
                Interlocked.Increment(ref counters.AzureResolutionAttempts);
                throw new InvalidOperationException("Azure resolution is forbidden in isolated local certification.");
            });
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<NarrationAIOptions>>().Value;
            if (!options.IsOllama || string.IsNullOrWhiteSpace(options.Ollama.ExpectedModelDigest))
                throw new InvalidOperationException("Certification requires explicit Ollama selection and a reviewed expected model digest.");
            using var scope = provider.CreateScope();
            var performer = scope.ServiceProvider.GetRequiredService<INarrationPerformer>();
            if (performer is not OllamaNarrationPerformer) throw new InvalidOperationException("Wrong selected provider.");
            await Write(output, "effective-configuration.json", new
            {
                options.Provider, options.Ollama, providerClass = performer.GetType().FullName,
                modelDigestStatus = "Configured pin; live observation recorded separately, never inferred from configuration",
                dryRun = args[2] == "--dry-run"
            });
            var results = new List<object>();
            var rejected = false;
            foreach (var language in new[] { "en", "hi" })
            {
                var fixture = GroundedNarrationCases.Create(language);
                var request = GroundedNarrationCases.BuildRequest(fixture);
                await Write(output, $"{language}-evidence.json", fixture);
                await Write(output, $"{language}-request.json", request);
                if (args[2] == "--dry-run")
                {
                    results.Add(new { language, fixture.PlanId, status = "NOT_RUN_DRY_RUN", validation = "NOT_RUN" });
                    continue;
                }
                var watch = Stopwatch.StartNew();
                try
                {
                    var result = await performer.InvokeAsync(request, cancellation.Token);
                    var validation = GroundedNarrationCases.Validate(fixture, result.Response);
                    await Write(output, $"{language}-response.json", result);
                    await Write(output, $"{language}-validation.json", validation);
                    rejected |= !validation.AutomatedGatesPassed;
                    results.Add(new { language, fixture.PlanId,
                        status = validation.AutomatedGatesPassed ? "AUTOMATED_GATES_PASSED_SCIENTIFIC_REVIEW_REQUIRED" : "REJECTED",
                        elapsedMilliseconds = watch.ElapsedMilliseconds, result.ProviderName, result.ModelOrDeployment, result.ResponseChecksum, validation });
                }
                catch (Exception ex) when (ex is NarrationProviderException or OperationCanceledException)
                {
                    rejected = true;
                    results.Add(new { language, fixture.PlanId, status = "INFERENCE_FAILED", validation = "NOT_RUN",
                        elapsedMilliseconds = watch.ElapsedMilliseconds, category = ex is NarrationProviderException failure ? failure.Category : "Cancellation",
                        reasonCode = ex is NarrationProviderException classified ? classified.ReasonCode : "CANCELLED" });
                }
                if (cancellation.IsCancellationRequested) break;
            }
            await Write(output, "provider-events.json", logs.Events.ToArray());
            await Write(output, "certification-summary.json", new
            {
                status = args[2] == "--dry-run" ? "NOT_RUN_DRY_RUN" : rejected ? "NOT_CERTIFIED" : "SCIENTIFIC_REVIEW_REQUIRED",
                certified = false, cases = results,
                azureOpenAiHttpCalls = 0, azureResolutionAttempts = counters.AzureResolutionAttempts,
                ollamaChatAttempts = counters.ChatAttempts, blockedNonlocalRequests = counters.BlockedRequests,
                peakDotNetClientWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                ollamaServerMemory = "UNKNOWN; client memory is not model-server memory",
                scope = "Isolated provider/composer/validator seam only; no phase authority publication, database, TTS, video or social publishing"
            });
            Console.WriteLine(args[2] == "--dry-run" ? "Dry run only: no inference or scientific certification." : rejected ? "Rejected/not certified; inspect the disposable evidence." : "Automated gates passed; scientific review is still required.");
            return rejected ? 1 : 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await Write(output, "configuration-error.json", new { status = "NOT_CERTIFIED", errorType = ex.GetType().Name, message = ex.Message });
            Console.Error.WriteLine("Certification could not start; inspect configuration-error.json in the disposable directory.");
            return 2;
        }
        finally { Console.CancelKeyPress -= onCancel; }
    }

    private static Task Write(string output, string name, object value)
        => File.WriteAllTextAsync(Path.Combine(output, name), JsonSerializer.Serialize(value, Json));
}

public sealed class TransportCounters
{
    public int AzureResolutionAttempts;
    public int ChatAttempts;
    public int BlockedRequests;
}

public sealed class LocalOnlyTransportGuard(Uri endpoint, TransportCounters counters) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var target = request.RequestUri!;
        var root = endpoint.AbsolutePath.TrimEnd('/') + "/api/";
        if (target.Scheme != endpoint.Scheme || target.Host != endpoint.Host || target.Port != endpoint.Port
            || target.AbsolutePath != root + "tags" && target.AbsolutePath != root + "chat")
        {
            Interlocked.Increment(ref counters.BlockedRequests);
            throw new InvalidOperationException("Nonlocal or unsupported transport route is forbidden in certification.");
        }
        if (target.AbsolutePath == root + "chat") Interlocked.Increment(ref counters.ChatAttempts);
        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed class CertificationLogger : ILogger<OllamaNarrationPerformer>
{
    public ConcurrentQueue<Dictionary<string, object?>> Events { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            Events.Enqueue(properties.ToDictionary(x => x.Key, x => x.Value));
    }
}
