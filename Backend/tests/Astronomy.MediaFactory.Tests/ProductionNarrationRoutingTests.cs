using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionNarrationRoutingTests
{
    private static readonly string Digest = new('a', 64);

    [Theory]
    [InlineData("en", 12, 50)]
    [InlineData("hi", 12, 50)]
    [InlineData("en", 4, 20)]
    [InlineData("hi", 4, 20)]
    public void LocalBudget_UsesDurationAndLanguageAndRejectsInsufficientCapacity(string language, int scenes, int duration)
    {
        var beats = Enumerable.Range(1, scenes).Select(i => new NarrationContextBeat("Science", "Explain facts", "Documentary", [], [], null,
            "Continue", "calm", "natural", [], null, $"scene-{i}", i, "long", duration)).ToArray();
        var budget = NarrationRuntimeBudget.EstimateOutputTokens(beats, language, new() { MaxOutputTokens = 12288 });
        Assert.InRange(budget, 100, 12288);
        Assert.Throws<NarrationProviderException>(() => NarrationRuntimeBudget.EstimateOutputTokens(beats, language, new() { MaxOutputTokens = 100 }));
    }

    [Theory]
    [InlineData("Long", 12)]
    [InlineData("Short", 4)]
    public async Task ProductionResolvedPerformer_PreservesTwelveLongAndFourShortSceneContracts(string variant, int count)
    {
        var services = Services("Ollama");
        var handler = new OllamaHandler();
        services.AddHttpClient<OllamaNarrationPerformer>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider(services);
        using var scope = provider.CreateScope();
        var performer = scope.ServiceProvider.GetRequiredService<NarrationGeneratorV5>().RuntimeNarrationPerformer!;
        var result = await performer.InvokeAsync(new(Guid.NewGuid().ToString("N"), "test", variant, "Verified science", "Use only supplied facts", "test", count, 20, 2048), CancellationToken.None);
        using var json = JsonDocument.Parse(result.Response);
        Assert.Equal(count, json.RootElement.GetProperty("scenes").GetArrayLength());
        Assert.Equal("Ollama", result.ProviderName);
    }

    [Theory]
    [InlineData("Ollama", typeof(OllamaNarrationPerformer))]
    [InlineData("AzureOpenAI", typeof(AzureOpenAiContentGenerationService))]
    public void ActualProductionContainer_SelectsExactPhase7Performer(string selection, Type expected)
    {
        var services = Services(selection);
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider(services);
        using var scope = provider.CreateScope();
        var performer = scope.ServiceProvider.GetRequiredService<INarrationPerformer>();
        Assert.IsType(expected, performer);
        var generator = scope.ServiceProvider.GetRequiredService<NarrationGeneratorV5>();
        Assert.Same(performer, generator.RuntimeNarrationPerformer);
    }

    [Fact]
    public async Task ProductionPhase7Generator_UsesOllamaForBothVariants()
    {
        var services = Services("Ollama");
        // Keep this routing test independent of upstream semantic-engine migrations.
        // The container-resolution tests above retain the production resolver.
        services.AddScoped<IRequiredSemanticFactResolver, FixtureResolver>();
        var handler = new OllamaHandler();
        services.AddHttpClient<OllamaNarrationPerformer>().ConfigurePrimaryHttpMessageHandler(() => handler);
        var azureCalls = 0;
        services.AddHttpClient<AzureOpenAiContentGenerationService>().ConfigurePrimaryHttpMessageHandler(() =>
        {
            Interlocked.Increment(ref azureCalls);
            throw new InvalidOperationException("Azure client must not initialize");
        });
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider(services);
        using var scope = provider.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<NarrationGeneratorV5>();
        var root = Directory.CreateTempSubdirectory("m1-03-phase7-").FullName;
        try
        {
            var fixture = typeof(Phase7ProductionApiPathSemanticContextTests);
            fixture.GetMethod("WriteProductionArtifacts", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [root]);
            var production = (Astronomy.MediaFactory.Core.ContentPlanProductionPipelineRequest)fixture.GetMethod("BuildJupiterVenusRequest", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
            production = production with { PlanId = Guid.NewGuid(), EventType = "Constellation", Title = "Orion constellation", ShortTitle = "Orion", ContentStrategy = "Constellation", AngularSeparationDegrees = null, PrimaryObjects = ["Orion"], SecondaryObjects = [] };
            File.WriteAllText(Path.Combine(root, "plan-input", "production-event-intelligence.json"),
                """{"eventType":"Constellation","eventTitle":"Orion constellation","primaryObjects":["Orion"],"verificationStatus":"Verified","familyId":"Constellation","profileId":"Constellation"}""");
            Directory.CreateDirectory(Path.Combine(root, "06-story-frames"));
            File.WriteAllText(Path.Combine(root, "06-story-frames", "story-frames.json"), "{}");
            foreach (var variant in new[] { "long", "short" })
            {
                File.WriteAllText(Path.Combine(root, "creative", $"documentary-contract.{variant}.json"),
                    JsonSerializer.Serialize(new { eventType = "Constellation", beats = new[] { new { sceneId = $"{variant}-scene-1", beatOrder = 1, documentaryBeatId = $"{variant}-hook", narrativeRole = "Hook", allocatedFacts = new { Name = "Orion", ScientificIdentity = "Orion is a constellation, an official sky region and a recognizable pattern." } } } }));
                var scene = new DocumentaryNarrativeSceneInput(1, $"{variant}-scene-1", "Hook", "Hook", "What does Orion represent?",
                    "Explain the Orion constellation", "Introduce Orion as an official sky region and recognizable pattern.",
                    [new("classification", "Orion is a constellation, an official sky region and a recognizable pattern.", ["science"], ["catalog"], 1m, []),
                     new("separation", "Its stars appear near one another but are not physically adjacent at one distance.", ["science"], ["catalog"], 1m, []),
                     new("nebula", "M42, the Orion Nebula, is an emission nebula, not a galaxy.", ["science"], ["catalog"], 1m, [])],
                    [], [], [], ["Orion"], "Constellation pattern", 30, "", "Conclude naturally");
                var composition = new DocumentaryNarrativeCompositionRequest("isolated", production.PlanId, "test-event", "Constellation", "en", variant,
                    "Constellation", [scene], new(20, 30, 60), [], ["Orion"], ["science"]);
                var path = Path.Combine(root, "narration-v5", variant);
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "narrative-composition-request.json"), JsonSerializer.Serialize(composition, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }
            var request = new BatchGenerateFromPlansRequest(2026, production.RegionId, "en", DryRun: false,
                UseProductionPipeline: true, StartPhaseNo: 7, EndPhaseNo: 7, PlanId: production.PlanId);
            var response = new BatchGenerateFromPlansResponse(true, false, 1, 1, 1, [], [], [], [],
                UseProductionPipeline: true, PlanId: production.PlanId, OutputRoot: root, ProductionPipelineRequest: production);
            await generator.BuildAndWriteDiagnosticsAsync(request, response, CancellationToken.None);
            Assert.Equal(new[] { "long", "short" }, handler.Variants);
            Assert.Equal(0, azureCalls);
            foreach (var variant in new[] { "long", "short" })
            {
                using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "narration-v5", variant, "provider-response-metadata.attempt-1.json")));
                Assert.Equal("Ollama", metadata.RootElement.GetProperty("providerName").GetString());
                Assert.True(metadata.RootElement.GetProperty("schemaValidationPassed").GetBoolean());
                Assert.True(metadata.RootElement.GetProperty("sceneMappingPassed").GetBoolean());
                Assert.True(metadata.RootElement.GetProperty("narrationQualityValidationPassed").GetBoolean());
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static ServiceCollection Services(string selection) => ProductionSourcePolicyCatalogNonEmptyTests.BuildServices(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=tests;Username=test;Password=test",
            ["DatabaseSafety:AllowLocalhostPostgres"] = "true",
            ["NarrationAI:Provider"] = selection,
            ["NarrationAI:Ollama:Model"] = "qwen2.5:7b-instruct-q4_K_M",
            ["NarrationAI:Ollama:ExpectedModelDigest"] = Digest,
            ["NarrationAI:Ollama:MaxRetries"] = "0",
            ["AzureOpenAI:Endpoint"] = selection == "Ollama" ? "" : "https://example.openai.azure.com/",
            ["AzureOpenAI:ApiKey"] = selection == "Ollama" ? "" : "test-only",
            ["AzureOpenAI:ChatDeployment"] = selection == "Ollama" ? "" : "gpt-4o"
        }).Build());

    private sealed class OllamaHandler : HttpMessageHandler
    {
        public List<string> Variants { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("127.0.0.1", request.RequestUri!.Host);
            if (request.Method == HttpMethod.Get)
                return Json(new { models = new[] { new { name = "qwen2.5:7b-instruct-q4_K_M", digest = Digest } } });
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var schema = body.RootElement.GetProperty("format").GetProperty("properties");
            var variant = schema.GetProperty("variant").GetProperty("enum")[0].GetString()!;
            var count = schema.GetProperty("scenes").GetProperty("minItems").GetInt32();
            Variants.Add(variant.ToLowerInvariant());
            var prose = "Orion is a constellation, both an official sky region and a recognizable pattern. Its stars appear near one another in our view, but they are not physically adjacent at one distance. The Orion Nebula, known as M42, is an emission nebula, not a galaxy. These distinctions help us describe what the sky pattern actually represents without assuming that its stars form one nearby group.";
            var content = JsonSerializer.Serialize(new { variant, scenes = Enumerable.Range(1, count).Select(i => new { sceneNumber = i, narrationText = prose }) });
            return Json(new { model = "qwen2.5:7b-instruct-q4_K_M", done = true, done_reason = "stop", message = new { role = "assistant", content } });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }

    private sealed class FixtureResolver : IRequiredSemanticFactResolver
    {
        public RequiredSemanticFactResolutionResult Resolve(RequiredSemanticFactResolutionInput input)
        {
            ResolvedSemanticFact Fact(string type, string text) => new(type, type, text, null, type,
                "OrionConstellationKnowledgeProvider.GetOrion/test-fixture", type, null, "Verified", 1m,
                "Required", text, text, "en", true);
            var facts = new[]
            {
                Fact("Name", "Orion"),
                Fact("ScientificIdentity", "Orion is a constellation, an official sky region and recognizable pattern."),
                Fact("ObjectKnowledge", "Orion is a constellation, an official sky region and recognizable pattern."),
                Fact("DomainScientificKnowledge", "The stars form an apparent pattern and are not physically adjacent at one distance.")
            };
            return new(new[] { "long", "short" }.Select(variant => new ResolvedBeatFacts(variant,
                $"{variant}-scene-1", $"{variant}-hook", "Hook", facts, [], [], [], [], [], [], false)).ToArray(),
                new { source = "Controlled catalog-backed semantic fixture; not a production resolver certification" });
        }
    }
}
