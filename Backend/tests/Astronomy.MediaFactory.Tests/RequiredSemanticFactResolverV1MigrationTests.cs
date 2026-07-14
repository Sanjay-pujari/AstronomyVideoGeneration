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
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Orchestration", "RC2", "NarrationGeneratorV5.cs"));
        var resolveBody = source[source.IndexOf("public RequiredSemanticFactResolutionResult Resolve", StringComparison.Ordinal)..source.IndexOf("private static Dictionary<string, SemanticCapabilityCandidate[]>", StringComparison.Ordinal)];

        Assert.DoesNotContain("AddJsonFacts", resolveBody);
        Assert.DoesNotContain("AddDocumentary", resolveBody);
        Assert.DoesNotContain("SemanticSourceAdapter", resolveBody);
    }


    [Fact]
    public void Legacy_Aliases_With_Identical_Policy_Share_One_Engine_Call_And_Project_Both_Fact_Types()
    {
        var engine = new CountingEngine();
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["ObservationTiming", "PeakWindow"], optional: []), Contract("beat-1"), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(1, engine.CallCount);
        Assert.Equal(["EventWindow"], engine.Requests.Select(r => r.CapabilityId.Value).Distinct().ToArray());
        Assert.Equal(["ObservationTiming", "PeakWindow"], result.Beats.Single().RequiredFacts.Select(f => f.FactType).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Legacy_Aliases_With_Different_Policies_Use_One_Engine_Call_Per_Policy_Scope()
    {
        var engine = new CountingEngine();
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["ObservationTiming"], optional: ["PeakWindow"]), Contract("beat-1"), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(2, engine.CallCount);
        Assert.Contains(engine.Requests, r => r.CapabilityId.Value == "EventWindow" && r.Required && r.MissingValueBehavior == SemanticMissingValueBehaviorV1.BlockRequired);
        Assert.Contains(engine.Requests, r => r.CapabilityId.Value == "EventWindow" && !r.Required && r.MissingValueBehavior == SemanticMissingValueBehaviorV1.OmitOptional);
        Assert.Equal(["ObservationTiming"], result.Beats.Single().RequiredFacts.Select(f => f.FactType).ToArray());
        Assert.Equal(["PeakWindow"], result.Beats.Single().OptionalFacts.Select(f => f.FactType).ToArray());
    }

    [Fact]
    public void Same_Requirement_In_Long_And_Short_With_Distinct_Beat_Ids_Uses_One_Call_And_Preserves_Both_Projections()
    {
        var engine = new CountingEngine();
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["EventIdentity"], optional: []), Contract("long-1"), Contract("short-1"), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(1, engine.CallCount);
        Assert.Equal(["long-1", "short-1"], result.Beats.Select(b => Assert.Single(b.RequiredFacts).SourceBeatId).ToArray());
    }

    [Fact]
    public void Same_Beat_Id_In_Long_And_Short_Does_Not_Add_Engine_Call_And_Preserves_Format_Occurrences()
    {
        var engine = new CountingEngine();
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["EventIdentity"], optional: []), Contract("beat-1"), Contract("beat-1"), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(1, engine.CallCount);
        Assert.Equal(["long", "short"], result.Beats.Select(b => b.Format).ToArray());
        Assert.All(result.Beats, b => Assert.Equal("beat-1", Assert.Single(b.RequiredFacts).SourceBeatId));
    }

    [Fact]
    public void Empty_Beat_List_Does_Not_Invoke_Engine_And_Returns_Empty_Result()
    {
        var engine = new CountingEngine();
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["EventIdentity"], optional: []), Contract(), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(0, engine.CallCount);
        Assert.Empty(result.Beats);
        Assert.False(result.Blocking);
    }

    [Fact]
    public void Repeated_Requirement_Inside_Profile_List_Uses_One_Call_Per_Unique_Scope()
    {
        var engine = new CountingEngine();
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["EventIdentity", "EventIdentity"], optional: []), Contract("beat-1"), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(1, engine.CallCount);
        Assert.Single(result.Beats.Single().RequiredFacts);
    }

    [Fact]
    public void MissingRequiredValue_Does_Not_Create_Legacy_Filler_Fact()
    {
        var engine = new CountingEngine(SemanticResolutionStatusV1.MissingRequiredValue);
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["EventIdentity"], optional: []), Contract("beat-1"), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(1, engine.CallCount);
        Assert.Empty(result.Beats.Single().RequiredFacts);
        Assert.Equal(["EventIdentity"], result.Beats.Single().MissingRequiredFacts);
    }

    [Fact]
    public void Resolved_Projection_Preserves_Legacy_Metadata()
    {
        var engine = new CountingEngine();
        var fact = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["EventIdentity"], optional: []), Contract("beat-1"), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en"))).Beats.Single().RequiredFacts.Single();

        Assert.Equal("EventIdentity", fact.FactType);
        Assert.Equal("EventIdentity", fact.FactKey);
        Assert.Equal("beat-1", fact.SourceBeatId);
        Assert.Equal("source-1", fact.SourceArtifact);
        Assert.Equal("candidate-1", fact.SourceField);
        Assert.Equal("Required", fact.Requiredness);
        Assert.Equal("en", fact.Language);
    }

    [Fact]
    public void Projection_Order_Is_Deterministic_By_Beat_Order_Not_Input_Order()
    {
        var profile = Profile(required: ["EventIdentity"], optional: []);
        var result = CreateResolver(new CountingEngine()).Resolve(new RequiredSemanticFactResolutionInput(profile, Contract("c", "b", "a"), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(["c", "b", "a"], result.Beats.Select(b => Assert.Single(b.RequiredFacts).SourceBeatId).ToArray());
    }

    [Fact]
    public void Counting_Engine_Confirms_No_Engine_Call_Occurs_During_Projection()
    {
        var engine = new CountingEngine();
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["EventIdentity"], optional: []), Contract("beat-1"), Contract("beat-2"), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(1, engine.CallCount);
        var projectedFacts = result.Beats.Select(b => Assert.Single(b.RequiredFacts)).ToArray();
        Assert.All(projectedFacts, fact => Assert.Equal("value-EventIdentity", fact.CanonicalValue));
        Assert.Equal(1, engine.CallCount);
    }

    [Fact]
    public void Architecture_Guards_For_Migrated_Resolver_Orchestration()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Orchestration", "RC2", "NarrationGeneratorV5.cs"));
        var resolverBody = source[source.IndexOf("public sealed class RequiredSemanticFactResolver", StringComparison.Ordinal)..source.IndexOf("public static class NarrationRealizedContextMapper", StringComparison.Ordinal)];
        var resolveBody = ExtractMethodBody(resolverBody, "public RequiredSemanticFactResolutionResult Resolve");
        var projectSignature = resolverBody[resolverBody.IndexOf("private static ResolvedSemanticFact? Project", StringComparison.Ordinal)..resolverBody.IndexOf("private IEnumerable<RequirementOccurrence>", StringComparison.Ordinal)];
        var scopeRecord = source[source.IndexOf("public sealed record SemanticResolutionScopeKeyV1", StringComparison.Ordinal)..source.IndexOf("public sealed class RequiredSemanticFactResolver", StringComparison.Ordinal)];

        Assert.Contains("_semanticResolutionEngine.Resolve", resolveBody);
        Assert.DoesNotContain("SemanticSourceAdapter", resolveBody);
        Assert.DoesNotContain("AddJsonFacts", resolveBody);
        Assert.DoesNotContain("AddDocumentary", resolveBody);
        Assert.True(resolveBody.IndexOf("_semanticResolutionEngine.Resolve", StringComparison.Ordinal) < resolveBody.IndexOf("Project(", StringComparison.Ordinal));
        Assert.DoesNotContain("ISemanticResolutionEngineV1", projectSignature);
        Assert.DoesNotContain("BeatId", scopeRecord);
        Assert.DoesNotContain("BeatRole", scopeRecord);
    }

    [Fact]
    public void Unsupported_Legacy_Capability_Is_Classified_Without_Creating_Request()
    {
        var engine = new CountingEngine();
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["UnsupportedFutureThing"], optional: []), Contract("beat-1"), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(0, engine.CallCount);
        Assert.Equal(["UnsupportedFutureThing"], result.Beats.Single().MissingRequiredFacts);
        Assert.Contains(result.Beats.Single().ResolutionWarnings, w => w.Contains("Unsupported legacy capability 'UnsupportedFutureThing': Disposition=UnsupportedLegacyTerm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Optional_Unsupported_Legacy_Capability_Is_Omitted_Without_Blocking()
    {
        var engine = new CountingEngine();
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: [], optional: ["UnsupportedFutureThing"]), Contract("beat-1"), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(0, engine.CallCount);
        Assert.Empty(result.Beats.Single().MissingRequiredFacts);
        Assert.Contains("UnsupportedFutureThing", result.Beats.Single().OmittedOptionalFacts);
        Assert.False(result.Beats.Single().Blocking);
        Assert.Contains(result.Beats.Single().ResolutionWarnings, w => w.Contains("Unsupported legacy capability 'UnsupportedFutureThing': Disposition=UnsupportedLegacyTerm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Future_Legacy_Capability_Is_Classified_Without_Creating_Request()
    {
        var engine = new CountingEngine();
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["BestSeason"], optional: []), Contract("beat-1"), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(0, engine.CallCount);
        Assert.Equal(["BestSeason"], result.Beats.Single().MissingRequiredFacts);
        Assert.Contains(result.Beats.Single().ResolutionWarnings, w => w.Contains("Unsupported legacy capability 'BestSeason': Disposition=Future", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Structured_Field_Mapping_Creates_Canonical_ObjectKnowledge_Request()
    {
        var engine = new CountingEngine();
        CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["Distance"], optional: []), Contract("beat-1"), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(1, engine.CallCount);
        Assert.Equal("ObjectKnowledge", engine.Requests.Single().CapabilityId.Value);
    }

    [Fact]
    public void Null_Event_Identity_Context_Does_Not_Throw_During_Scope_Creation()
    {
        var engine = new CountingEngine();
        var result = CreateResolver(engine).Resolve(new RequiredSemanticFactResolutionInput(Profile(required: ["EventIdentity"], optional: []), Contract("beat-1"), Contract(), null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(1, engine.CallCount);
        Assert.Single(result.Beats.Single().RequiredFacts);
    }


    private static string ExtractMethodBody(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Could not find method signature: {signature}");

        var bodyStart = source.IndexOf('{', signatureIndex);
        Assert.True(bodyStart >= 0, $"Could not find method body for: {signature}");

        var depth = 0;
        for (var i = bodyStart; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            if (source[i] != '}') continue;

            depth--;
            if (depth == 0) return source[bodyStart..(i + 1)];
        }

        throw new InvalidOperationException($"Could not find method body end for: {signature}");
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
        private readonly SemanticResolutionStatusV1 _status;
        public CountingEngine(SemanticResolutionStatusV1 status = SemanticResolutionStatusV1.Resolved) => _status = status;
        public int CallCount { get; private set; }
        public List<SemanticResolutionRequestV1> Requests { get; } = [];
        public SemanticResolutionResultV1 Resolve(SemanticResolutionRequestV1 request)
        {
            CallCount++;
            Requests.Add(request);
            var value = _status is SemanticResolutionStatusV1.Resolved or SemanticResolutionStatusV1.ResolvedByCombination ? new SemanticSourceValueV1($"value-{request.CapabilityId.Value}", "String") : null;
            var fact = new ResolvedSemanticFactV1(request.CapabilityId, _status, request.Required, value, $"value-{request.CapabilityId.Value}", $"value-{request.CapabilityId.Value}", "candidate-1", "adapter-1", "source-1", SemanticEvidenceCategoryV1.VerifiedEventData, SemanticEvidenceStrengthV1.Strong, .95m, [new("source-1", "model", "path", true)], [], [], [], "FirstApprovedByPriority", [], [], _status.ToString(), _status.ToString());
            var diagnostics = new SemanticResolutionDiagnosticsV1(request.CapabilityId, request.Required, null, 1, 1, 0, [], [], [], [], "candidate-1", fact.Status, fact.ResolutionPolicy, [], [], [], []);
            return new(fact, diagnostics);
        }
    }
}
