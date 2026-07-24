using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
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
    public void MissingRootPathDefaultsToWorkingDirectoryKnowledge()
    {
        var workingDirectory = CreateTempDirectory();
        var resolved = EvergreenAstronomyKnowledgeLoader.ResolveRootPath(" ", workingDirectory);
        Assert.Equal(Path.GetFullPath(Path.Combine(workingDirectory, "Knowledge")), resolved);
    }

    [Fact]
    public void RelativeRootPathResolvesUnderWorkingDirectory()
    {
        var workingDirectory = CreateTempDirectory();
        var resolved = EvergreenAstronomyKnowledgeLoader.ResolveRootPath("Knowledge", workingDirectory);
        Assert.Equal(Path.GetFullPath(Path.Combine(workingDirectory, "Knowledge")), resolved);
    }

    [Fact]
    public void AbsoluteRootPathRemainsUnchanged()
    {
        var rootPath = CreateTempDirectory();
        var workingDirectory = CreateTempDirectory();
        var resolved = EvergreenAstronomyKnowledgeLoader.ResolveRootPath(rootPath, workingDirectory);
        Assert.Equal(Path.GetFullPath(rootPath), resolved);
    }

    [Fact]
    public async Task OrionRelativePathResolvesUnderWorkingDirectoryKnowledge()
    {
        var workingDirectory = CreateTempDirectory();
        CopyOrionKnowledgeFile(workingDirectory);
        var loader = new EvergreenAstronomyKnowledgeLoader(
            Options.Create(new AstronomyKnowledgeOptions { RootPath = "Knowledge" }),
            Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }));

        var result = await loader.LoadByRelativePathAsync("Constellations/Orion/Orion.v1.json", CancellationToken.None);

        Assert.Equal(Path.GetFullPath(Path.Combine(workingDirectory, "Knowledge", "Constellations", "Orion", "Orion.v1.json")), result.FullPath);
    }

    [Fact]
    public async Task AbsoluteRootPathSupportsAppContextBaseDirectoryPackaging()
    {
        var absoluteRootPath = Path.GetFullPath("Knowledge");
        var loader = new EvergreenAstronomyKnowledgeLoader(
            Options.Create(new AstronomyKnowledgeOptions { RootPath = absoluteRootPath }),
            Options.Create(new RenderingOptions { WorkingDirectory = CreateTempDirectory() }));

        var result = await loader.LoadByRelativePathAsync(OrionPath, CancellationToken.None);

        Assert.Equal("constellation.orion", result.Package.KnowledgeId);
    }


    [Fact]
    public async Task RelativeRootPathFallsBackToRepositoryKnowledgeWhenWorkingDirectoryDoesNotContainKnowledge()
    {
        var loader = new EvergreenAstronomyKnowledgeLoader(
            Options.Create(new AstronomyKnowledgeOptions { RootPath = "Knowledge" }),
            Options.Create(new RenderingOptions { WorkingDirectory = CreateTempDirectory() }));

        var result = await loader.LoadByRelativePathAsync(OrionPath, CancellationToken.None);

        Assert.Equal(Path.GetFullPath(OrionPath), result.FullPath);
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
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "astronomy-knowledge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CopyOrionKnowledgeFile(string workingDirectory)
    {
        var destination = Path.Combine(workingDirectory, "Knowledge", "Constellations", "Orion", "Orion.v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(Path.GetFullPath(OrionPath), destination);
    }
    private static MediaFactoryDbContext CreateDb() => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
