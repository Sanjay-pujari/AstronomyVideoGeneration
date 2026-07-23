using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyKnowledgeOptions
{
    public const string SectionName = "AstronomyKnowledge";
    public string RootPath { get; init; } = "Knowledge";
}

public sealed record EvergreenAstronomySubjectImportRequest(
    string RelativePath,
    string RegionId = "GLOBAL",
    string Language = "en",
    DateTimeOffset? EditorialStartUtc = null,
    bool OverwriteExisting = false,
    bool DryRun = false);

public sealed record EvergreenAstronomySubjectImportResponse(
    bool Success,
    string Action,
    string KnowledgeId,
    string KnowledgeVersion,
    string SchemaVersion,
    string Checksum,
    Guid? AstronomyEventIntelligenceId,
    int CreatedObjectCount,
    int UpdatedObjectCount,
    int ExistingObjectCount,
    bool DryRun,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ValidationErrors);

public interface IEvergreenAstronomyKnowledgeLoader
{
    Task<EvergreenAstronomyKnowledgeLoadResult> LoadByRelativePathAsync(string relativePath, CancellationToken cancellationToken);
}

public interface IEvergreenAstronomySubjectImportService
{
    Task<EvergreenAstronomySubjectImportResponse> ImportAsync(EvergreenAstronomySubjectImportRequest request, CancellationToken cancellationToken);
}

public sealed record EvergreenAstronomyKnowledgeLoadResult(EvergreenAstronomyKnowledgePackage Package, string RelativePath, string FullPath, string Checksum);

public sealed record EvergreenAstronomyKnowledgePackage
{
    public string SchemaVersion { get; init; } = "";
    public string KnowledgeId { get; init; } = "";
    public string FamilyCode { get; init; } = "";
    public string CanonicalName { get; init; } = "";
    public string KnowledgeVersion { get; init; } = "";
    public string ReviewStatus { get; init; } = "";
    public JsonElement Identity { get; init; }
    public JsonElement Scientific { get; init; }
    public JsonElement Observation { get; init; }
    public JsonElement CultureAndMythology { get; init; }
    public JsonElement AstrologyRelationships { get; init; }
    public JsonElement History { get; init; }
    public IReadOnlyList<JsonElement> InterestingFacts { get; init; } = [];
    public JsonElement Astrophotography { get; init; }
    public IReadOnlyDictionary<string, EvergreenLocalizedContent> LocalizedContent { get; init; } = new ReadOnlyDictionary<string, EvergreenLocalizedContent>(new Dictionary<string, EvergreenLocalizedContent>());
    public JsonElement EditorialSafety { get; init; }
    public IReadOnlyList<EvergreenAstronomyObject> Objects { get; init; } = [];
    public IReadOnlyList<EvergreenKnowledgeSource> Sources { get; init; } = [];
}

public sealed record EvergreenLocalizedContent
{
    public string DisplayName { get; init; } = "";
    public string Pronunciation { get; init; } = "";
    public string Summary { get; init; } = "";
    public IReadOnlyList<string> NarrationVocabulary { get; init; } = [];
    public IReadOnlyList<string> HookIdeas { get; init; } = [];
    public IReadOnlyList<string> KeyMessages { get; init; } = [];
    public IReadOnlyList<string> NaturalTerminology { get; init; } = [];
    public IReadOnlyList<string> DoNotBlindlyTranslate { get; init; } = [];
}

public sealed record EvergreenAstronomyObject
{
    public string ObjectId { get; init; } = "";
    public string ObjectName { get; init; } = "";
    public string ObjectType { get; init; } = "";
    public string ObjectRole { get; init; } = "";
    public string CatalogId { get; init; } = "";
    public IReadOnlyList<string> SourceIds { get; init; } = [];
    [JsonExtensionData] public IDictionary<string, JsonElement>? Metadata { get; init; }
}

public sealed record EvergreenKnowledgeSource
{
    public string SourceId { get; init; } = "";
    public string Authority { get; init; } = "";
    public string SourceType { get; init; } = "";
    public string Title { get; init; } = "";
    public string Reference { get; init; } = "";
    public DateOnly ReviewDate { get; init; }
    public IReadOnlyList<string> SupportedSections { get; init; } = [];
    public string Confidence { get; init; } = "";
    public string ReviewStatus { get; init; } = "";
    public string? Notes { get; init; }
}
