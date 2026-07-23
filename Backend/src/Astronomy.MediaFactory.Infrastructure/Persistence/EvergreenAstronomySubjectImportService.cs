using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class EvergreenAstronomySubjectImportService(MediaFactoryDbContext db, IEvergreenAstronomyKnowledgeLoader loader) : IEvergreenAstronomySubjectImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public async Task<EvergreenAstronomySubjectImportResponse> ImportAsync(EvergreenAstronomySubjectImportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("regionId is required.");
        var language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim().ToLowerInvariant();
        var loaded = await loader.LoadByRelativePathAsync(request.RelativePath, cancellationToken);
        var p = loaded.Package;
        var externalEventId = $"{p.KnowledgeId}.v{p.KnowledgeVersion.Split('.')[0]}";
        var existing = await db.AstronomyEventIntelligences.Include(e => e.Objects)
            .SingleOrDefaultAsync(e => e.ExternalEventId == externalEventId && e.RegionId == request.RegionId && e.Language == language && e.EventType == "CONSTELLATION", cancellationToken);
        if (existing is not null && !request.OverwriteExisting && ReadChecksum(existing.MetadataJson) is { } old && old != loaded.Checksum)
            throw new InvalidOperationException($"metadataJson.sha256 checksum differs for knowledgeId {p.KnowledgeId} knowledgeVersion {p.KnowledgeVersion}; set overwriteExisting=true to replace.");
        var action = existing is null ? "Created" : "Unchanged";
        var now = DateTimeOffset.UtcNow; var start = request.EditorialStartUtc ?? now;
        var metadata = JsonSerializer.Serialize(new { knowledgeId = p.KnowledgeId, p.KnowledgeVersion, p.SchemaVersion, relativePath = loaded.RelativePath, sha256 = loaded.Checksum, p.ReviewStatus, evergreen = true, requiresSkyfieldForImport = false, importedUtc = now, versionPolicy = "Same knowledge ID/version is checksum-locked unless overwriteExisting=true; new package versions reuse the stable evergreen event identity version suffix policy." }, JsonOptions);
        if (request.DryRun) return new(true, existing is null ? "WouldCreate" : "WouldSkip", p.KnowledgeId, p.KnowledgeVersion, p.SchemaVersion, loaded.Checksum, existing?.Id, existing is null ? p.Objects.Count : 0, 0, existing?.Objects.Count ?? 0, true, [], []);
        if (existing is null) { existing = new AstronomyEventIntelligence(); db.AstronomyEventIntelligences.Add(existing); }
        else if (request.OverwriteExisting) action = "Updated";
        else return new(true, "Unchanged", p.KnowledgeId, p.KnowledgeVersion, p.SchemaVersion, loaded.Checksum, existing.Id, 0, 0, existing.Objects.Count, false, [], []);
        existing.ExternalEventId = externalEventId; existing.EventCode = "CONSTELLATION-ORION-EVERGREEN"; existing.EventType = "CONSTELLATION"; existing.Title = "Orion constellation guide"; existing.Summary = p.LocalizedContent[language].Summary; existing.Description = "Evergreen constellation education import; location-specific viewing is calculated later."; existing.Year = start.Year; existing.Language = language; existing.RegionId = request.RegionId; existing.StartUtc = start; existing.PeakUtc = null; existing.EndUtc = null; existing.RecommendedCategory = "AstronomyEducation"; existing.Status = "Verified"; existing.VerificationStatus = "Verified"; existing.ContentStrategy = "EvergreenConstellationEducation"; existing.AutoGenerateAllowed = true; existing.ContentOpportunityScore = 80; existing.VisibilityScore = 70; existing.RarityScore = 40; existing.AudienceInterestScore = 85; existing.RawDataJson = JsonSerializer.Serialize(p, JsonOptions); existing.MetadataJson = metadata; existing.Touch();
        if (existing.Objects.Count > 0) db.AstronomyEventObjects.RemoveRange(existing.Objects); existing.Objects.Clear();
        foreach (var o in p.Objects) existing.Objects.Add(new AstronomyEventObject { ObjectName = o.ObjectName, ObjectType = o.ObjectType, ObjectRole = o.ObjectRole, CatalogId = o.CatalogId, MetadataJson = JsonSerializer.Serialize(new { o.ObjectId, o.SourceIds, o.Metadata, source = "evergreenKnowledge" }, JsonOptions) });
        await db.SaveChangesAsync(cancellationToken);
        return new(true, action, p.KnowledgeId, p.KnowledgeVersion, p.SchemaVersion, loaded.Checksum, existing.Id, p.Objects.Count, 0, 0, false, [], []);
    }
    private static string? ReadChecksum(string? json) { if (string.IsNullOrWhiteSpace(json)) return null; using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("sha256", out var s) ? s.GetString() : null; }
}
