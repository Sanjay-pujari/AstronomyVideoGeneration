using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Api.Controllers;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2RealPipelineCertificationTests
{
    [Fact]
    public async Task rc2_api_executes_real_certified_phase1_to_phase4()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "astronomy-rc2-real-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=production-di.invalid;Database=rc2;Username=test;Password=test;Pooling=false",
                ["Rendering:WorkingDirectory"] = workspace,
                ["ProductionPipeline:StaleRunningThresholdMinutes"] = "30",
                ["AzureOpenAI:Endpoint"] = "https://test.invalid", ["AzureOpenAI:ChatDeployment"] = "test", ["AzureOpenAI:UseManagedIdentity"] = "true",
                ["AzureSpeech:Region"] = "eastus", ["AzureSpeech:Key"] = "test-key",
                ["AzureBlob:AccountName"] = "test", ["AzureBlob:UseManagedIdentity"] = "true"
            }).Build();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddMediaFactory(configuration);
            builder.Services.RemoveAll<IHostedService>();
            builder.Services.RemoveAll<DbContextOptions<MediaFactoryDbContext>>();
            builder.Services.RemoveAll<MediaFactoryDbContext>();
            for (var index = builder.Services.Count - 1; index >= 0; index--)
                if (builder.Services[index].ImplementationType?.Assembly.GetName().Name?.StartsWith("Npgsql", StringComparison.Ordinal) == true)
                    builder.Services.RemoveAt(index);
            builder.Services.RemoveAll<Microsoft.EntityFrameworkCore.Storage.IDatabaseProvider>();
            var databaseServices = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();
            builder.Services.AddScoped(_ => new MediaFactoryDbContext(new DbContextOptionsBuilder<MediaFactoryDbContext>()
                .UseInMemoryDatabase("rc2-real").UseInternalServiceProvider(databaseServices).Options));
            builder.Services.AddControllers().AddApplicationPart(typeof(ContentPlanningRc2Controller).Assembly);

            await using var app = builder.Build();
            app.MapControllers();
            await SeedOrionAsync(app.Services);
            await app.StartAsync();

            var client = app.GetTestClient();
            var request = new BatchGenerateFromPlansRequest(2026, "GLOBAL", "en", DryRun: false,
                UseProductionPipeline: true, StartPhaseNo: 1, EndPhaseNo: 4,
                PlanId: OrionContentGenerationPlanSeeder.OrionPlanId, ExecutionMode: ContentPlanExecutionMode.Normal);
            var firstHttp = await client.PostAsJsonAsync("/api/content-planning/rc2/batch-generate-from-plans", request);
            var firstBody = await firstHttp.Content.ReadAsStringAsync();
            Assert.True(firstHttp.StatusCode == HttpStatusCode.OK, firstBody);
            var first = JsonSerializer.Deserialize<BatchGenerateFromPlansResponse>(firstBody, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.True(first.Success, string.Join("; ", first.Errors));
            Assert.Equal([1, 2, 3, 4], first.Steps.OfType<JsonElement>().Select(x => x.GetProperty("phaseNo").GetInt32()));
            Assert.Equal(4, first.LastCompletedPhaseNo);
            Assert.Null(first.LastFailedPhaseNo);

            var certified = Assert.IsType<Rc2CertifiedExecutionStatus>(first.Rc2CertifiedExecution);
            Assert.Equal([1, 2, 3, 4], certified.Phases.Select(x => x.PhaseNo));
            Assert.All(certified.Phases, phase => Assert.Equal("Succeeded", phase.Status));
            Assert.Equal("DocumentaryBlueprintPhase4IntegrationService", certified.Phase4Publication.IntegrationService);
            Assert.True(certified.Phase4Publication.PhysicalAuthorityExists);
            Assert.True(certified.Phase4Publication.CommittedStateValidationPassed);
            Assert.False(certified.Phase4Publication.LegacyAuthorityProduced);
            Assert.True(certified.PublicationCommitted);
            Assert.Equal("Valid", certified.ValidationStatus);
            Assert.True(certified.CommittedStateValidationPassed);
            Assert.False(certified.LegacyPhase4AuthorityUsed);
            Assert.Equal("PublishedDocumentaryBlueprintAggregate", certified.DownstreamAuthorityType);

            var outputRoot = Assert.IsType<string>(first.OutputRoot);
            var required = new[] { "04-blueprint/documentary-blueprint.json", "04-blueprint/documentary-blueprint.long.json", "04-blueprint/documentary-blueprint.short.json", "phase-manifest.json", "validation/phase-04-validation.json" };
            Assert.All(required, relative => Assert.True(File.Exists(Path.Combine(outputRoot, relative)), relative));
            var aggregate = JsonSerializer.Deserialize<DocumentaryBlueprintAggregate>(await File.ReadAllBytesAsync(Path.Combine(outputRoot, required[0])), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.False(string.IsNullOrWhiteSpace(aggregate.DeterministicChecksum));
            Assert.True(DocumentaryBlueprintProjectionChecksum.HasValidAggregateChecksum(aggregate));
            Assert.True(DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(aggregate.LongVariant));
            Assert.True(DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(aggregate.ShortVariant));
            Assert.Equal(certified.AggregateChecksum, aggregate.DeterministicChecksum);
            Assert.Equal(certified.LongSceneCount, aggregate.LongVariant.ActualSceneCount);
            Assert.Equal(certified.ShortSceneCount, aggregate.ShortVariant.ActualSceneCount);

            await using var scope = app.Services.CreateAsyncScope();
            var evaluation = await scope.ServiceProvider.GetRequiredService<IPhase4CommittedAuthorityEvaluator>()
                .EvaluateAsync(outputRoot, aggregate.ExecutionId, aggregate.PlanId, aggregate.EventId, aggregate.Language);
            Assert.True(evaluation.IsValid, string.Join("; ", evaluation.Errors.Select(x => x.Message)));
            Assert.Equal("P4REUSE_VALID", evaluation.ReasonCode);
            Assert.NotNull(evaluation.PublishedAuthority);
            Assert.Equal(certified.AggregateChecksum, evaluation.PublishedAuthority!.DeterministicChecksum);
            Assert.Contains(evaluation.ArtifactPaths, x => x.EndsWith("documentary-blueprint.json", StringComparison.Ordinal));

            var before = ReadPhaseChecksums(outputRoot);
            var rerunRequest = request with { StartPhaseNo = 4, EndPhaseNo = 4, ExecutionMode = ContentPlanExecutionMode.RerunPhase, OverwriteExisting = false };
            var rerunHttp = await client.PostAsJsonAsync("/api/content-planning/rc2/batch-generate-from-plans", rerunRequest);
            Assert.Equal(HttpStatusCode.OK, rerunHttp.StatusCode);
            var rerun = await rerunHttp.Content.ReadFromJsonAsync<BatchGenerateFromPlansResponse>();
            var rerunCertified = Assert.IsType<Rc2CertifiedExecutionStatus>(rerun!.Rc2CertifiedExecution);
            Assert.True(rerunCertified.AlreadyPublished);
            Assert.True(rerunCertified.PublicationCommitted);
            Assert.True(rerunCertified.CommittedStateValidationPassed);
            Assert.Equal(certified.AggregateChecksum, rerunCertified.AggregateChecksum);
            Assert.Equal(aggregate.LongProjectionChecksum, evaluation.PublishedAuthority.LongProjectionChecksum);
            Assert.Equal(aggregate.ShortProjectionChecksum, evaluation.PublishedAuthority.ShortProjectionChecksum);
            Assert.Equal(before, ReadPhaseChecksums(outputRoot));
            Assert.DoesNotContain(rerun.Steps.OfType<JsonElement>(), x => x.GetProperty("phaseNo").GetInt32() >= 5);
        }
        finally
        {
            if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        }
    }

    private static async Task SeedOrionAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaFactoryDbContext>();
        await db.Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<IOrionContentGenerationPlanSeeder>().SeedAsync(CancellationToken.None);
        var plan = await db.ContentGenerationPlans.SingleAsync(x => x.Id == OrionContentGenerationPlanSeeder.OrionPlanId);
        var intelligence = new AstronomyEventIntelligence
        {
            EventCode = "ORION-GOLD-2026", ExternalEventId = OrionContentGenerationPlanSeeder.OrionSourceExternalEventId,
            Year = 2026, Language = "en", VerificationStatus = "Verified", AutoGenerateAllowed = true,
            ContentStrategy = "AstronomyEducation", EventType = "CONSTELLATION", Title = plan.Title!, Summary = "Find Orion and understand its stars.",
            StartUtc = new DateTimeOffset(2026, 1, 1, 20, 0, 0, TimeSpan.Zero), RegionId = "GLOBAL",
            RarityScore = 5, VisibilityScore = 9, AudienceInterestScore = 9, ContentOpportunityScore = 9,
            Objects =
            [
                new AstronomyEventObject { ObjectName = "Orion", ObjectType = "Constellation", ObjectRole = "Primary" },
                new AstronomyEventObject { ObjectName = "Betelgeuse", ObjectType = "Star", ObjectRole = "Secondary" },
                new AstronomyEventObject { ObjectName = "Rigel", ObjectType = "Star", ObjectRole = "Secondary" },
                new AstronomyEventObject { ObjectName = "Orion Nebula", ObjectType = "DeepSkyObject", ObjectRole = "Secondary" }
            ]
        };
        db.AstronomyEventIntelligences.Add(intelligence);
        plan.AstronomyEventIntelligence = intelligence;
        plan.AstronomyEventIntelligenceId = intelligence.Id;
        await db.SaveChangesAsync();
    }

    private static string[] ReadPhaseChecksums(string root) => Enumerable.Range(1, 3)
        .Select(number => Path.Combine(root, "validation", $"phase-{number:00}-validation.json"))
        .Select(File.ReadAllBytes)
        .Select(bytes => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant())
        .ToArray();
}
