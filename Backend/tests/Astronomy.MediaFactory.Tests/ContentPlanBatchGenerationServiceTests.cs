using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public sealed class ContentPlanBatchGenerationServiceTests
{
    private static readonly Guid GeminidsPlanId = Guid.Parse("2af19a66-3777-47c7-8672-6e9d6245ac1c");

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionDryRun_UsesProductionPipelineOnly()
    {
        await using var db = CreateDb();
        var intelligence = new AstronomyEventIntelligence
        {
            EventCode = "GEMINIDS-2026",
            ExternalEventId = "geminids-2026",
            Year = 2026,
            Language = "en",
            VerificationStatus = "Verified",
            AutoGenerateAllowed = true,
            ContentStrategy = "AutoGenerate",
            EventType = "MeteorShower",
            Title = "Geminids Meteor Shower Peak",
            Summary = "Geminids",
            StartUtc = new DateTimeOffset(2026, 12, 13, 0, 0, 0, TimeSpan.Zero),
            PeakUtc = new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
            RegionId = "IN-RJ-UDAIPUR",
            RarityScore = 9,
            VisibilityScore = 9,
            AudienceInterestScore = 9,
            ContentOpportunityScore = 9
        };
        db.AstronomyEventIntelligences.Add(intelligence);

        var plan = new ContentGenerationPlan
        {
            Title = "Geminids Meteor Shower Peak",
            ContentCategoryCode = "RareEventAlert",
            RegionId = "IN-RJ-UDAIPUR",
            Language = "en",
            ScheduledUtc = new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
            Status = "Planned",
            PlanStatus = "Planned",
            Priority = 1,
            PriorityScore = 9,
            AstronomyEventIntelligenceId = intelligence.Id,
            AstronomyEventIntelligence = intelligence,
            SourceExternalEventId = "geminids-2026",
            RequestedOutputTypesJson = "[\"Short\",\"Long\"]"
        };
        plan.AssignId(GeminidsPlanId);
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();

        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: true,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.UseProductionPipeline);
        Assert.False(response.UsedPlaceholderVisuals);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(GeminidsPlanId, response.PlanId);
        Assert.NotNull(response.ProductionPipelineRequest);
        Assert.Equal(CapturingProductionExecutionService.ExpectedSteps, response.PlannedSteps);
        Assert.Equal(CapturingProductionExecutionService.ExpectedSteps.Cast<object>(), response.Steps);
        Assert.DoesNotContain(response.Steps, step => step.ToString() is "GenerateAssetPlans" or "CreateAssetProductionJobs" or "GenerateVisualAssets" or "RenderSceneVideos");
        Assert.Equal(GeminidsPlanId, production.CapturedPlanId);
        Assert.True(production.CapturedDryRun);
        Assert.False(production.CapturedOverwriteExisting);
        Assert.False(legacy.WasCalled);
    }


    [Fact]
    public async Task GenerateFromPlansAsync_ForwardsOverwriteExistingToProductionPipeline()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db);
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            OverwriteExisting: true), CancellationToken.None);

        Assert.True(production.CapturedOverwriteExisting);
    }


    [Fact]
    public async Task GenerateFromPlansAsync_ProductionFailedWithoutRetryMode_IsExcluded()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionFailed", planStatus: "ProductionFailed");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(0, response.SelectedPlanCount);
        Assert.Contains(response.Warnings, warning => warning.RequestedTitle == "Geminids Meteor Shower Peak"
            && warning.Reason.Contains("ProductionFailed", StringComparison.OrdinalIgnoreCase)
            && !warning.Reason.Contains("allowed status or planStatus values are Draft, Planned, Approved, ProductionFailed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionFailedWithRetryMode_IsSelectedAndForwardsPhaseOptions()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionFailed", planStatus: "ProductionFailed");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            OverwriteExisting: true,
            StartPhaseNo: 1,
            EndPhaseNo: 19,
            RetryFailedOnly: true,
            AllowFailedPlanRetry: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.UseProductionPipeline);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(GeminidsPlanId, response.PlanId);
        Assert.Equal(GeminidsPlanId, production.CapturedPlanId);
        Assert.True(production.CapturedOverwriteExisting);
        Assert.Equal(1, production.CapturedStartPhaseNo);
        Assert.Equal(19, production.CapturedEndPhaseNo);
        Assert.True(production.CapturedRetryFailedOnly);
        Assert.False(legacy.WasCalled);
    }


    [Fact]
    public async Task GenerateFromPlansAsync_ProductionRunningWithoutRecoveryMode_IsExcluded()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionRunning", planStatus: "ProductionRunning");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            RetryFailedOnly: true,
            AllowFailedPlanRetry: true,
            StartPhaseNo: 17,
            EndPhaseNo: 19), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(0, response.SelectedPlanCount);
        Assert.Contains(response.Warnings, warning => warning.RequestedTitle == "Geminids Meteor Shower Peak"
            && warning.Reason.Contains("ProductionRunning", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionRunningWithRecoveryMode_IsSelectedAndForwardsPhaseOptions()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionRunning", planStatus: "ProductionRunning");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            OverwriteExisting: false,
            StartPhaseNo: 17,
            EndPhaseNo: 19,
            RetryFailedOnly: true,
            AllowFailedPlanRetry: true,
            AllowRunningPlanRecovery: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.UseProductionPipeline);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(GeminidsPlanId, response.PlanId);
        Assert.Equal(GeminidsPlanId, production.CapturedPlanId);
        Assert.Equal(17, production.CapturedStartPhaseNo);
        Assert.Equal(19, production.CapturedEndPhaseNo);
        Assert.True(production.CapturedRetryFailedOnly);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionRunningRecoveryRequiresAllowRunningPlanRecoveryFlag()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionRunning", planStatus: "ProductionRunning");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            ExecutionMode: ContentPlanExecutionMode.RecoverRunning), CancellationToken.None));

        Assert.Contains("allowRunningPlanRecovery=true", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
    }



    [Fact]
    public async Task GenerateFromPlansAsync_ProductionCompletedWithRebuildOutputs_IsSelectedAndForwardsRerunOptions()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionCompleted", planStatus: "ProductionCompleted");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            OverwriteExisting: true,
            ExecutionMode: ContentPlanExecutionMode.RebuildOutputs,
            AllowCompletedPlanRerun: true,
            ArchivePreviousRun: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(ContentPlanExecutionMode.RebuildOutputs, production.CapturedExecutionMode);
        Assert.True(production.CapturedAllowCompletedPlanRerun);
        Assert.True(production.CapturedArchivePreviousRun);
        Assert.Equal(3, production.CapturedStartPhaseNo);
        Assert.Equal(19, production.CapturedEndPhaseNo);
        Assert.Equal(ContentPlanExecutionMode.RebuildOutputs, response.ExecutionMode);
        Assert.True(response.CompletedPlanRerun);
        Assert.True(response.PreviousOutputArchived);
        Assert.Equal("/archive/geminids", response.ArchivePath);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionCompletedWithoutExplicitFlag_IsRejected()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionCompleted", planStatus: "ProductionCompleted");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            OverwriteExisting: true,
            ExecutionMode: ContentPlanExecutionMode.RebuildOutputs), CancellationToken.None));

        Assert.Contains("allowCompletedPlanRerun=true", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
    }

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static void SeedGeminidsPlan(MediaFactoryDbContext db, string status = "Planned", string planStatus = "Planned")
    {
        var intelligence = new AstronomyEventIntelligence
        {
            EventCode = "GEMINIDS-2026",
            ExternalEventId = "geminids-2026",
            Year = 2026,
            Language = "en",
            VerificationStatus = "Verified",
            AutoGenerateAllowed = true,
            ContentStrategy = "AutoGenerate",
            EventType = "MeteorShower",
            Title = "Geminids Meteor Shower Peak",
            Summary = "Geminids",
            StartUtc = new DateTimeOffset(2026, 12, 13, 0, 0, 0, TimeSpan.Zero),
            PeakUtc = new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
            RegionId = "IN-RJ-UDAIPUR",
            RarityScore = 9,
            VisibilityScore = 9,
            AudienceInterestScore = 9,
            ContentOpportunityScore = 9
        };
        db.AstronomyEventIntelligences.Add(intelligence);

        var plan = new ContentGenerationPlan
        {
            Title = "Geminids Meteor Shower Peak",
            ContentCategoryCode = "RareEventAlert",
            RegionId = "IN-RJ-UDAIPUR",
            Language = "en",
            ScheduledUtc = new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
            Status = status,
            PlanStatus = planStatus,
            Priority = 1,
            PriorityScore = 9,
            AstronomyEventIntelligenceId = intelligence.Id,
            AstronomyEventIntelligence = intelligence,
            SourceExternalEventId = "geminids-2026",
            RequestedOutputTypesJson = "[\"Short\",\"Long\"]"
        };
        plan.AssignId(GeminidsPlanId);
        db.ContentGenerationPlans.Add(plan);
        db.SaveChanges();
    }

    private sealed class CapturingProductionExecutionService : IContentPlanProductionExecutionService
    {
        public static readonly string[] ExpectedSteps =
        [
            "Question Engine",
            "Scene Engine Short",
            "Scene Engine Long",
            "Hero Engine",
            "Thumbnail Engine",
            "Narration Short",
            "Narration Long",
            "TTS Short",
            "TTS Long",
            "Video Assembly Short",
            "Video Assembly Long"
        ];

        public Guid CapturedPlanId { get; private set; }
        public bool CapturedDryRun { get; private set; }
        public bool CapturedOverwriteExisting { get; private set; }
        public int? CapturedStartPhaseNo { get; private set; }
        public int? CapturedEndPhaseNo { get; private set; }
        public bool CapturedRetryFailedOnly { get; private set; }
        public ContentPlanExecutionMode CapturedExecutionMode { get; private set; }
        public bool CapturedAllowCompletedPlanRerun { get; private set; }
        public bool CapturedArchivePreviousRun { get; private set; }

        public Task<ContentPlanProductionExecutionResult> ExecuteContentPlanAsync(Guid contentGenerationPlanId, bool dryRun, bool overwriteExisting, CancellationToken cancellationToken)
        {
            CapturedPlanId = contentGenerationPlanId;
            CapturedDryRun = dryRun;
            CapturedOverwriteExisting = overwriteExisting;

            var request = new ContentPlanProductionPipelineRequest(
                contentGenerationPlanId,
                "RareEventAlert",
                "Geminids Meteor Shower Peak",
                "Geminids",
                "MeteorShower",
                "IN-RJ-UDAIPUR",
                "en",
                ["Geminids"],
                [],
                null,
                new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
                null,
                new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
                "geminids-2026",
                null,
                ["Short", "Long"],
                9,
                9,
                9,
                9,
                "Verified",
                null,
                "AutoGenerate",
                null,
                null,
                "IN-RJ-UDAIPUR",
                null,
                null,
                null,
                null,
                null,
                [],
                [],
                []);

            return Task.FromResult(new ContentPlanProductionExecutionResult(
                true,
                dryRun,
                true,
                false,
                1,
                contentGenerationPlanId,
                "Geminids Meteor Shower Peak",
                @"D:\AstronomyWorkspace\Astronomy\media-output\plans\IN-RJ-UDAIPUR\2026\2af19a66-3777-47c7-8672-6e9d6245ac1c",
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                string.Empty,
                string.Empty,
                request,
                ExpectedSteps,
                [],
                [],
                [],
                ExecutionMode: CapturedExecutionMode,
                CompletedPlanRerun: CapturedAllowCompletedPlanRerun,
                PreviousOutputArchived: CapturedArchivePreviousRun,
                ArchivePath: CapturedArchivePreviousRun ? "/archive/geminids" : null,
                DeletedOutputFolders: [],
                StartPhaseNo: CapturedStartPhaseNo,
                EndPhaseNo: CapturedEndPhaseNo));
        }

        public Task<ContentPlanProductionExecutionResult> ExecuteContentPlanWithProductionPipelineAsync(ContentPlanProductionExecutionRequest request, CancellationToken cancellationToken)
        {
            CapturedStartPhaseNo = request.StartPhaseNo;
            CapturedEndPhaseNo = request.EndPhaseNo;
            CapturedRetryFailedOnly = request.RetryFailedOnly;
            CapturedExecutionMode = request.ExecutionMode;
            CapturedAllowCompletedPlanRerun = request.AllowCompletedPlanRerun;
            CapturedArchivePreviousRun = request.ArchivePreviousRun;
            return ExecuteContentPlanAsync(request.ContentGenerationPlanId, request.DryRun, request.OverwriteExisting, cancellationToken);
        }
    }

    private sealed class ThrowingLegacyPipeline : IAstronomyAssetPlanningService, IAstronomyAssetProductionJobService, IVisualAssetGenerationService, ISceneRenderer
    {
        public bool WasCalled { get; private set; }

        public Task<AstronomyAssetPlanningResult> GenerateAssetPlansAsync(AstronomyAssetPlanningRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Placeholder planning pipeline must not be called in production mode.");
        }

        public Task<AstronomyAssetProductionJobResult> CreateAssetProductionJobsAsync(AstronomyAssetProductionJobRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Placeholder planning pipeline must not be called in production mode.");
        }

        public Task<VisualAssetGenerationResponse> GenerateVisualAssetsAsync(VisualAssetGenerationRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Placeholder planning pipeline must not be called in production mode.");
        }

        public Task<SceneRenderingResponse> RenderScenesAsync(SceneRenderingRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Placeholder planning pipeline must not be called in production mode.");
        }
    }
}
