using System.Collections.Immutable;
using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;

namespace Astronomy.MediaFactory.Tests;

public sealed class RequiredSemanticFactResolverV1MigrationTests
{
    [Fact]
    public void RequiredSemanticFactResolver_Calls_SemanticResolutionEngineV1_Once_Per_Requirement()
    {
        var engine = new CountingEngine();
        var resolver = new RequiredSemanticFactResolver(
            Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.SemanticDefaults.SemanticCapabilityResolver,
            Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.SemanticDefaults.DomainKnowledgeProvider,
            engine);

        var profile = new AstronomyFamilyProfile("TestFamily", "Event", "", "", ["EventIdentity"], [], [], [], "", "", [], [], []);
        var contract = JsonDocument.Parse("{\"beats\":[{\"documentaryBeatId\":\"beat-1\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}}]}").RootElement.Clone();

        resolver.Resolve(new RequiredSemanticFactResolutionInput(profile, contract, contract, null, null, null, null, null, LanguageProfileResolver.Resolve("en")));

        Assert.Equal(2, engine.CallCount);
        Assert.All(engine.Requests, r => Assert.Equal("EventIdentity", r.CapabilityId.Value));
    }

    [Fact]
    public void Migrated_Path_Does_Not_Invoke_Legacy_Json_Fact_Scanning()
    {
        var source = File.ReadAllText(TestPaths.Source("Orchestration", "RC2", "NarrationGeneratorV5.cs"));
        var resolveBody = source[source.IndexOf("public RequiredSemanticFactResolutionResult Resolve", StringComparison.Ordinal)..source.IndexOf("private static Dictionary<string, SemanticCapabilityCandidate[]>", StringComparison.Ordinal)];

        Assert.DoesNotContain("AddJsonFacts", resolveBody);
        Assert.DoesNotContain("AddDocumentary", resolveBody);
    }

    private sealed class CountingEngine : ISemanticResolutionEngineV1
    {
        public int CallCount { get; private set; }
        public List<SemanticResolutionRequestV1> Requests { get; } = [];
        public SemanticResolutionResultV1 Resolve(SemanticResolutionRequestV1 request)
        {
            CallCount++;
            Requests.Add(request);
            var fact = new ResolvedSemanticFactV1(request.CapabilityId, SemanticResolutionStatusV1.MissingRequiredValue, request.Required, null, null, null, null, null, null, null, default, 0, [], [], [], [], "None", [], [], "Missing", "Missing");
            var diagnostics = new SemanticResolutionDiagnosticsV1(request.CapabilityId, request.Required, null, 0, 0, 0, [], [], [], [], null, fact.Status, fact.ResolutionPolicy, [], [fact.DiagnosticCode], [], []);
            return new(fact, diagnostics);
        }
    }
}
