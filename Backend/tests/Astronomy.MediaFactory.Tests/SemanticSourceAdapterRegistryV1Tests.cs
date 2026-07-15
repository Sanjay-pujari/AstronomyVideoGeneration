using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Event;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public class SemanticSourceAdapterRegistryV1Tests
{
    private readonly ITestOutputHelper _output;
    private readonly SemanticSourceAdapterRegistryV1 _registry = new();

    public SemanticSourceAdapterRegistryV1Tests(ITestOutputHelper output) => _output = output;

    [Fact] public void Every_Adapter_Supports_One_Canonical_Capability_And_Unique_Id(){Assert.Equal(_registry.Adapters.Count,_registry.Adapters.Select(a=>a.AdapterId).Distinct(StringComparer.Ordinal).Count());foreach(var a in _registry.Adapters) Assert.Contains(a.SupportedCapabilityId.Value, SemanticCapabilityVocabularyV1.CanonicalIds);}
    [Fact] public void Adapter_Sources_And_Evidence_Are_Approved_By_Sprint3A_Policy(){var policies=new SemanticSourcePolicyCatalogV1();foreach(var a in _registry.Adapters){var p=policies.GetRequired(a.SupportedCapabilityId);var s=Assert.Single(p.ApprovedSources.Where(s=>s.SourceId==a.SourceId));Assert.Equal(s.EvidenceCategory,a.EvidenceCategory);Assert.True(a.MaximumEvidenceStrength>=s.MinimumStrength);}}
    [Fact] public void No_Active_Adapter_Uses_LegacyRawJsonScanner_Or_Recursive_Json()
    {
        Assert.DoesNotContain(_registry.Adapters, a => a.SourceId == SemanticSourcePolicyVocabularyV1.LegacyRawJsonScanner);

        var backendRoot = FindBackendRoot();
        var adapterDirectory = Path.Combine(
            backendRoot,
            "src",
            "Astronomy.MediaFactory.Infrastructure",
            "Production",
            "Narration",
            "Semantics",
            "Sources",
            "Adapters");

        Assert.True(Directory.Exists(adapterDirectory), $"Adapter source directory does not exist: {adapterDirectory}");

        var implementationDirectories = new[]
        {
            Path.Combine(adapterDirectory, "Event"),
            Path.Combine(adapterDirectory, "Knowledge")
        };

        foreach (var implementationDirectory in implementationDirectories)
        {
            Assert.True(
                Directory.Exists(implementationDirectory),
                $"Adapter implementation source directory does not exist: {implementationDirectory}");
        }

        var files = implementationDirectories
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .ToArray();
        Assert.NotEmpty(files);

        foreach (var f in files)
        {
            var relativePath = Path.GetRelativePath(backendRoot, f);
            var text = File.ReadAllText(f);

            Assert.False(text.Contains("JsonElement", StringComparison.Ordinal), $"Adapter source must not reference JsonElement: {relativePath}");
            Assert.False(text.Contains("dynamic", StringComparison.Ordinal), $"Adapter source must not reference dynamic: {relativePath}");
            Assert.False(text.Contains("GetProperties()", StringComparison.Ordinal), $"Adapter source must not call GetProperties(): {relativePath}");
            Assert.False(text.Contains("LegacyRawJsonScanner", StringComparison.Ordinal), $"Adapter source must not reference LegacyRawJsonScanner: {relativePath}");
        }
    }
    [Fact] public void Registry_Rejects_LegacyRawJsonScanner_Adapter()
    {
        Assert.DoesNotContain(_registry.Adapters, a => a.SourceId == SemanticSourcePolicyVocabularyV1.LegacyRawJsonScanner);

        var registry = new SemanticSourceAdapterRegistryV1([new LegacyRawJsonScannerSyntheticAdapter()]);
        var result = registry.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Raw JSON adapter is not allowed", StringComparison.Ordinal));
    }
    [Fact] public void Coverage_Is_Deterministic_And_Reports_Missing_Adapters(){var a=_registry.GetCoverageReport().ToArray();var b=_registry.GetCoverageReport().ToArray();Assert.Equal(a,b);Assert.Contains(a,i=>i.Status==SemanticSourceAdapterCertificationStatusV1.AdapterMissing);}
    [Fact] public void Contracts_Serialize_And_RoundTrip(){var r=new EventWindowSourceAdapterV1().TryExtract(new(ObservationMetadata:new(EventWindow:new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"),DateTimeOffset.Parse("2026-01-01T01:00:00Z"),DateTimeOffset.Parse("2026-01-01T02:00:00Z"),null,null,null,null,"UTC","window"))));var json=JsonSerializer.Serialize(r);var clone=JsonSerializer.Deserialize<SemanticSourceAdapterResultV1>(json);Assert.Equal(r.Status, clone!.Status);}

    [Fact]
    public void GetAdapters_Accepts_Every_Canonical_Production_Capability()
    {
        Assert.NotEmpty(_registry.Adapters);
        foreach (var canonical in SemanticCapabilityVocabularyV1.CanonicalIds)
        {
            var adapters = _registry.GetAdapters(new SemanticCapabilityId(canonical));
            Assert.NotNull(adapters);
        }
    }

    [Fact]
    public void GetAdapters_Accepts_Every_Approved_Legacy_Capability_Alias()
    {
        Assert.NotEmpty(_registry.Adapters);
        foreach (var alias in LegacySemanticCapabilityMapV1.Entries.Where(e => e.CanonicalCapabilityId is not null))
        {
            var adapters = _registry.GetAdapters(new SemanticCapabilityId(alias.LegacyTerm));
            Assert.NotNull(adapters);
        }
    }

    [Fact]
    public void GetAdapters_Null_Or_Default_Requested_Capability_Produces_Precise_ArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => _registry.GetAdapters(default));
        Assert.Equal("id", ex.ParamName);
        Assert.Contains("Semantic capability ID must contain a non-empty value.", ex.Message);
    }

    [Fact]
    public void GetAdapters_Blank_Value_Produces_Precise_ArgumentException()
    {
        var id = CreateCapabilityIdWithRawValue("   ");
        var ex = Assert.Throws<ArgumentException>(() => _registry.GetAdapters(id));
        Assert.Equal("id", ex.ParamName);
        Assert.Contains("Semantic capability ID must contain a non-empty value.", ex.Message);
    }

    [Fact] public void Constructor_Rejects_Null_Adapter_Collection()=>Assert.Throws<ArgumentNullException>(()=>new SemanticSourceAdapterRegistryV1(null!));
    [Fact] public void Constructor_Rejects_Null_Adapter_Entry(){var ex=Assert.Throws<InvalidOperationException>(()=>new SemanticSourceAdapterRegistryV1([null!]));Assert.Contains("null adapter entry",ex.Message);}
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("   ")] public void Constructor_Rejects_Null_Or_Blank_AdapterId(string? adapterId){var ex=Assert.Throws<InvalidOperationException>(()=>new SemanticSourceAdapterRegistryV1([new SyntheticAdapter(adapterId:adapterId)]));Assert.Contains("AdapterId",ex.Message);}
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("   ")] public void Constructor_Rejects_Null_Or_Blank_SourceId(string? sourceId){var ex=Assert.Throws<InvalidOperationException>(()=>new SemanticSourceAdapterRegistryV1([new SyntheticAdapter(sourceId:sourceId)]));Assert.Contains("SourceId",ex.Message);}
    [Fact] public void Constructor_Rejects_Adapter_Declaring_Null_Capability(){var ex=Assert.Throws<InvalidOperationException>(()=>new SemanticSourceAdapterRegistryV1([new SyntheticAdapter(capabilityFactory:()=>default)]));Assert.Contains("capability ID",ex.Message);}
    [Fact] public void Constructor_Rejects_Adapter_Declaring_Blank_Capability_Value(){var ex=Assert.Throws<InvalidOperationException>(()=>new SemanticSourceAdapterRegistryV1([new SyntheticAdapter(capabilityFactory:()=>CreateCapabilityIdWithRawValue("   "))]));Assert.Contains("capability ID",ex.Message);}
    [Fact] public void Constructor_Rejects_Duplicate_AdapterId(){var ex=Assert.Throws<InvalidOperationException>(()=>new SemanticSourceAdapterRegistryV1([new SyntheticAdapter(adapterId:"dup"),new SyntheticAdapter(adapterId:"dup",sourceId:"source.two")]));Assert.Contains("Duplicate",ex.Message);}

    [Fact]
    public void Production_Registry_Contains_No_Malformed_Adapters()
    {
        Assert.NotEmpty(_registry.Adapters);
        foreach (var adapter in _registry.Adapters)
        {
            var malformed = string.IsNullOrWhiteSpace(adapter.SupportedCapabilityId.Value);
            _output.WriteLine($"{adapter.GetType().FullName} | AdapterId={adapter.AdapterId} | SourceId={adapter.SourceId} | SupportedCapabilityIds=[{adapter.SupportedCapabilityId.Value ?? "<null>"}] | MalformedCapability={malformed}");
            Assert.False(malformed, $"Adapter {adapter.GetType().FullName} ({adapter.AdapterId}) has malformed capability metadata.");
        }
    }

    [Fact]
    public void Every_Registered_Adapter_Capability_Can_Be_Canonicalized()
    {
        Assert.NotEmpty(_registry.Adapters);
        foreach (var adapter in _registry.Adapters)
        {
            var adapters = _registry.GetAdapters(adapter.SupportedCapabilityId);
            Assert.Contains(adapters, a => a.AdapterId == adapter.AdapterId);
        }
    }

    [Fact]
    public void GetAdapters_For_All_PlanetPairing_Required_Capabilities_Does_Not_Throw()
    {
        foreach (var capability in PlanetPairingCapabilities())
            _ = _registry.GetAdapters(new SemanticCapabilityId(capability));
    }

    [Fact]
    public void JupiterVenus_Requirement_Inventory_Can_Call_GetAdapters()
    {
        foreach (var capability in new[]
        {
            SemanticCapabilityVocabularyV1.AstronomicalObjects,
            SemanticCapabilityVocabularyV1.EventIdentity,
            SemanticCapabilityVocabularyV1.EventWindow,
            SemanticCapabilityVocabularyV1.ObservationLocation,
            SemanticCapabilityVocabularyV1.ObservationDirection,
            SemanticCapabilityVocabularyV1.AngularSeparation,
            SemanticCapabilityVocabularyV1.DomainScientificKnowledge
        })
        {
            _ = _registry.GetAdapters(new SemanticCapabilityId(capability));
        }
    }

    private static IEnumerable<string> PlanetPairingCapabilities()
    {
        yield return SemanticCapabilityVocabularyV1.AstronomicalObjects;
        yield return SemanticCapabilityVocabularyV1.EventIdentity;
        yield return SemanticCapabilityVocabularyV1.EventWindow;
        yield return SemanticCapabilityVocabularyV1.ObservationLocation;
        yield return SemanticCapabilityVocabularyV1.ObservationDirection;
        yield return SemanticCapabilityVocabularyV1.AngularSeparation;
        yield return SemanticCapabilityVocabularyV1.DomainScientificKnowledge;
    }

    private static SemanticCapabilityId CreateCapabilityIdWithRawValue(string? rawValue)
    {
        var boxed = FormatterServices.GetUninitializedObject(typeof(SemanticCapabilityId));
        var field = typeof(SemanticCapabilityId).GetField("<Value>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(boxed, rawValue);
        return (SemanticCapabilityId)boxed;
    }

    private sealed class SyntheticAdapter : ISemanticSourceAdapterV1
    {
        private readonly Func<SemanticCapabilityId> _capabilityFactory;
        public SyntheticAdapter(string? adapterId = "test.synthetic", string? sourceId = "test.source", SemanticCapabilityId? capability = null, Func<SemanticCapabilityId>? capabilityFactory = null)
        {
            AdapterId = adapterId!;
            SourceId = sourceId!;
            _capabilityFactory = capabilityFactory ?? (() => capability ?? new SemanticCapabilityId(SemanticCapabilityVocabularyV1.EventIdentity));
        }
        public string AdapterId { get; }
        public SemanticCapabilityId SupportedCapabilityId => _capabilityFactory();
        public string SourceId { get; }
        public SemanticEvidenceCategoryV1 EvidenceCategory => SemanticEvidenceCategoryV1.VerifiedEventMetadata;
        public SemanticEvidenceStrengthV1 MaximumEvidenceStrength => SemanticEvidenceStrengthV1.Strong;
        public bool EventSpecific => true;
        public bool SupportsLocalization => false;
        public bool SupportsUnits => false;
        public bool SupportsProvenance => false;
        public SemanticSourceAdapterResultV1 TryExtract(SemanticSourceAdapterContextV1 context) => SemanticSourceAdapterResultV1.Reject(SupportedCapabilityId, AdapterId, SourceId, SemanticSourceAdapterStatusV1.ValueMissing, "Synthetic.");
    }

    private sealed class LegacyRawJsonScannerSyntheticAdapter : ISemanticSourceAdapterV1
    {
        public string AdapterId => "test.legacy-raw-json-scanner.synthetic";
        public SemanticCapabilityId SupportedCapabilityId => new(SemanticCapabilityVocabularyV1.EventIdentity);
        public string SourceId => SemanticSourcePolicyVocabularyV1.LegacyRawJsonScanner;
        public SemanticEvidenceCategoryV1 EvidenceCategory => SemanticEvidenceCategoryV1.LegacyCompatibilityData;
        public SemanticEvidenceStrengthV1 MaximumEvidenceStrength => SemanticEvidenceStrengthV1.Strong;
        public bool EventSpecific => true;
        public bool SupportsLocalization => false;
        public bool SupportsUnits => false;
        public bool SupportsProvenance => false;
        public SemanticSourceAdapterResultV1 TryExtract(SemanticSourceAdapterContextV1 context) =>
            SemanticSourceAdapterResultV1.Reject(
                SupportedCapabilityId,
                AdapterId,
                SourceId,
                SemanticSourceAdapterStatusV1.RejectedByPolicy,
                "Synthetic adapter exists only to verify registry validation.");
    }

    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Backend", "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Backend", "tests")))
            {
                return Path.Combine(directory.FullName, "Backend");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Backend/src and Backend/tests.");
    }
}
