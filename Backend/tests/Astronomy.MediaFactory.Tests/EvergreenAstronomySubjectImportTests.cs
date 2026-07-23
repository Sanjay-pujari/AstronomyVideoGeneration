using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class EvergreenAstronomySubjectImportTests
{
    private const string OrionPath = "Knowledge/Constellations/Orion/Orion.v1.json";

    [Fact]
    public async Task Loader_LoadsOrionAndComputesDeterministicChecksum()
    {
        var loader = CreateLoader();
        var a = await loader.LoadByRelativePathAsync(OrionPath, CancellationToken.None);
        var b = await loader.LoadByRelativePathAsync(OrionPath, CancellationToken.None);
        Assert.Equal("constellation.orion", a.Package.KnowledgeId);
        Assert.Equal("CONSTELLATION", a.Package.FamilyCode);
        Assert.True(a.Package.LocalizedContent.ContainsKey("en"));
        Assert.True(a.Package.LocalizedContent.ContainsKey("hi"));
        Assert.Equal(a.Checksum, b.Checksum);
        Assert.Single(a.Package.Objects.Where(o => o.ObjectName == "Orion" && o.ObjectType == "Constellation" && o.ObjectRole == "Primary"));
    }

    [Fact]
    public async Task Loader_RejectsPathTraversal()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => CreateLoader().LoadByRelativePathAsync("../Orion.v1.json", CancellationToken.None));
        Assert.Contains("relativePath", ex.Message);
    }

    [Fact]
    public async Task Import_FirstImportCreatesIntelligenceObjectsAndNoOpportunityOrPlan()
    {
        await using var db = CreateDb();
        var service = new EvergreenAstronomySubjectImportService(db, CreateLoader());
        var result = await service.ImportAsync(Request(), CancellationToken.None);
        Assert.Equal("Created", result.Action);
        var evt = await db.AstronomyEventIntelligences.Include(e => e.Objects).SingleAsync();
        Assert.Equal("CONSTELLATION", evt.EventType);
        Assert.Equal("AstronomyEducation", evt.RecommendedCategory);
        Assert.Equal("Verified", evt.VerificationStatus);
        Assert.Null(evt.PeakUtc);
        Assert.Contains("knowledgeId", evt.MetadataJson);
        Assert.Equal(9, evt.Objects.Count);
        Assert.Contains(evt.Objects, o => o.ObjectName == "Orion" && o.ObjectRole == "Primary" && o.CatalogId == "IAU:ORI");
        Assert.Equal(0, await db.AstronomyContentOpportunities.CountAsync());
        Assert.Equal(0, await db.ContentGenerationPlans.CountAsync());
    }

    [Fact]
    public async Task Import_SecondImportIsIdempotent()
    {
        await using var db = CreateDb();
        var service = new EvergreenAstronomySubjectImportService(db, CreateLoader());
        await service.ImportAsync(Request(), CancellationToken.None);
        var second = await service.ImportAsync(Request(), CancellationToken.None);
        Assert.Equal("Unchanged", second.Action);
        Assert.Equal(1, await db.AstronomyEventIntelligences.CountAsync());
        Assert.Equal(9, await db.AstronomyEventObjects.CountAsync());
    }

    [Fact]
    public async Task Import_DryRunDoesNotMutateDatabase()
    {
        await using var db = CreateDb();
        var service = new EvergreenAstronomySubjectImportService(db, CreateLoader());
        var result = await service.ImportAsync(Request(dryRun: true), CancellationToken.None);
        Assert.Equal("WouldCreate", result.Action);
        Assert.Equal(0, await db.AstronomyEventIntelligences.CountAsync());
    }

    [Fact]
    public void Validator_RejectsMissingKnowledgeId()
    {
        var package = new EvergreenAstronomyKnowledgePackage { SchemaVersion = "1.0", FamilyCode = "CONSTELLATION", CanonicalName = "Orion", KnowledgeVersion = "1.0.0", ReviewStatus = "Reviewed", LocalizedContent = new Dictionary<string, EvergreenLocalizedContent> { ["en"] = new(), ["hi"] = new() }, Sources = [new() { SourceId = "s", Reference = "r" }], Objects = [new() { ObjectId = "o", ObjectName = "Orion", ObjectType = "Constellation", ObjectRole = "Primary" }] };
        var ex = Assert.Throws<ArgumentException>(() => EvergreenAstronomyKnowledgeLoader.Validate(package));
        Assert.Contains("knowledgeId", ex.Message);
    }

    private static EvergreenAstronomySubjectImportRequest Request(bool dryRun = false) => new(OrionPath, "GLOBAL", "en", DateTimeOffset.Parse("2026-08-01T12:00:00Z"), false, dryRun);
    private static EvergreenAstronomyKnowledgeLoader CreateLoader() => new(Options.Create(new AstronomyKnowledgeOptions { RootPath = Path.GetFullPath("Knowledge") }));
    private static MediaFactoryDbContext CreateDb() => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
