using System.Collections.Immutable;
using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;

namespace Astronomy.MediaFactory.Tests;

public sealed class RequiredSemanticFactResolverV1MigrationTests
{
    [Fact]
    public void RequiredSemanticFactResolver_Calls_Engine_Once_Per_Unique_Resolution_Scope()
    {
        var engine = new CountingEngine();
        var resolver = CreateResolver(engine);
        var profile = Profile(required: ["EventIdentity"], optional: []);
        var contract = Contract("beat-1");

        var result = resolver.Resolve(new RequiredSemanticFactResolutionInput(profile, contract, contract, null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(1, engine.CallCount);
        Assert.Single(engine.Requests);
        Assert.Equal("EventIdentity", engine.Requests[0].CapabilityId.Value);
        Assert.Equal(2, result.Beats.Count);
        Assert.All(result.Beats, b => Assert.Equal("beat-1", Assert.Single(b.RequiredFacts).SourceBeatId));
        Assert.Single(engine.Requests.Select(ScopeSignature));
    }

    [Fact]
    public void Six_Identical_Beat_Requirements_Call_Engine_Once_And_Project_To_Six_Beats()
    {
        var engine = new CountingEngine();
        var resolver = CreateResolver(engine);
        var profile = Profile(required: ["EventIdentity"], optional: []);
        var longContract = Contract("long-1", "long-2", "long-3");
        var shortContract = Contract("short-1", "short-2", "short-3");

        var result = resolver.Resolve(new RequiredSemanticFactResolutionInput(profile, longContract, shortContract, null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(1, engine.CallCount);
        Assert.Equal(6, result.Beats.Count);
        Assert.Equal(["long-1", "long-2", "long-3", "short-1", "short-2", "short-3"], result.Beats.Select(b => Assert.Single(b.RequiredFacts).SourceBeatId).ToArray());
    }

    [Fact]
    public void Same_Capability_With_Required_And_Optional_Policies_Uses_Separate_Scopes()
    {
        var engine = new CountingEngine();
        var resolver = CreateResolver(engine);
        var profile = Profile(required: ["EventIdentity"], optional: ["EventIdentity"]);
        var contract = Contract("beat-1");

        resolver.Resolve(new RequiredSemanticFactResolutionInput(profile, contract, contract, null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(2, engine.CallCount);
        Assert.Contains(engine.Requests, r => r.Required && r.MissingValueBehavior == SemanticMissingValueBehaviorV1.BlockRequired);
        Assert.Contains(engine.Requests, r => !r.Required && r.MissingValueBehavior == SemanticMissingValueBehaviorV1.OmitOptional);
    }

    [Fact]
    public void Scope_Key_Separates_Different_Minimum_Evidence_Strengths()
    {
        var categories = Enum.GetValues<SemanticEvidenceCategoryV1>();
        var weak = new SemanticResolutionScopeKeyV1(new("EventIdentity"), true, SemanticEvidenceStrengthV1.Weak, categories, SemanticMissingValueBehaviorV1.BlockRequired, "ctx", "v1");
        var strong = new SemanticResolutionScopeKeyV1(new("EventIdentity"), true, SemanticEvidenceStrengthV1.Strong, categories, SemanticMissingValueBehaviorV1.BlockRequired, "ctx", "v1");

        Assert.NotEqual(weak, strong);
    }

    [Fact]
    public void Different_Capabilities_Call_Engine_Once_Per_Capability_Scope()
    {
        var engine = new CountingEngine();
        var resolver = CreateResolver(engine);
        var profile = Profile(required: ["EventIdentity", "ObservationTiming"], optional: []);
        var contract = Contract("beat-1");

        resolver.Resolve(new RequiredSemanticFactResolutionInput(profile, contract, contract, null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(2, engine.CallCount);
        Assert.Equal(["EventIdentity", "EventWindow"], engine.Requests.Select(r => r.CapabilityId.Value).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Reversed_Beat_Ordering_Produces_Identical_Engine_Call_Set()
    {
        var profile = Profile(required: ["EventIdentity"], optional: []);
        var firstEngine = new CountingEngine();
        var secondEngine = new CountingEngine();

        CreateResolver(firstEngine).Resolve(new RequiredSemanticFactResolutionInput(profile, Contract("a", "b", "c"), Contract("d", "e", "f"), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));
        CreateResolver(secondEngine).Resolve(new RequiredSemanticFactResolutionInput(profile, Contract("c", "b", "a"), Contract("f", "e", "d"), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(firstEngine.Requests.Select(ScopeSignature).Order(StringComparer.Ordinal), secondEngine.Requests.Select(ScopeSignature).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Migrated_Path_Does_Not_Invoke_Legacy_Json_Fact_Scanning()
    {
        var source = File.ReadAllText(TestPaths.Source("Orchestration", "RC2", "NarrationGeneratorV5.cs"));
        var resolveBody = source[source.IndexOf("public RequiredSemanticFactResolutionResult Resolve", StringComparison.Ordinal)..source.IndexOf("private static Dictionary<string, SemanticCapabilityCandidate[]>", StringComparison.Ordinal)];

        Assert.DoesNotContain("AddJsonFacts", resolveBody);
        Assert.DoesNotContain("AddDocumentary", resolveBody);
        Assert.DoesNotContain("SemanticSourceAdapter", resolveBody);
    }

    private static RequiredSemanticFactResolver CreateResolver(CountingEngine engine) => new(
        Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.SemanticDefaults.SemanticCapabilityResolver,
        Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.SemanticDefaults.DomainKnowledgeProvider,
        engine);

    private static AstronomyFamilyProfile Profile(string[] required, string[] optional) => new("TestFamily", "Event", "", "", required, optional, [], [], "", "", [], [], []);

    private static JsonElement Contract(params string[] beatIds)
    {
        var beats = string.Join(',', beatIds.Select((id, i) => $"{{\"documentaryBeatId\":\"{id}\",\"beatOrder\":{i + 1},\"narrativeRole\":\"Body\",\"allocatedFacts\":{{}}}}"));
        return JsonDocument.Parse($"{{\"beats\":[{beats}]}}").RootElement.Clone();
    }

    private static string ScopeSignature(SemanticResolutionRequestV1 r) => $"{r.CapabilityId.Value}|{r.Required}|{r.MinimumEvidenceStrength}|{r.MissingValueBehavior}|{string.Join(',', r.AllowedEvidenceCategories.Order())}";

    private sealed class CountingEngine : ISemanticResolutionEngineV1
    {
        public int CallCount { get; private set; }
        public List<SemanticResolutionRequestV1> Requests { get; } = [];
        public SemanticResolutionResultV1 Resolve(SemanticResolutionRequestV1 request)
        {
            CallCount++;
            Requests.Add(request);
            var fact = new ResolvedSemanticFactV1(request.CapabilityId, SemanticResolutionStatusV1.Resolved, request.Required, new($"value-{request.CapabilityId.Value}", "String"), $"value-{request.CapabilityId.Value}", $"value-{request.CapabilityId.Value}", "candidate-1", "adapter-1", "source-1", SemanticEvidenceCategoryV1.VerifiedEventData, SemanticEvidenceStrengthV1.Strong, .95m, [new("source-1", "model", "path", true)], [], [], [], "FirstApprovedByPriority", [], [], "Resolved", "Resolved");
            var diagnostics = new SemanticResolutionDiagnosticsV1(request.CapabilityId, request.Required, null, 1, 1, 0, [], [], [], [], "candidate-1", fact.Status, fact.ResolutionPolicy, [], [], [], []);
            return new(fact, diagnostics);
        }
    }
}
