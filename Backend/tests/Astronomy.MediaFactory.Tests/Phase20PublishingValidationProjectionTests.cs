using System.Text.Json;
using Astronomy.MediaFactory.Api.Controllers;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase20PublishingValidationProjectionTests : IDisposable
{
    private const string ChecksumA = "cadf7e84e338faf55536b7b17973cb10221a1a90163a67845517c6dc2065b5e4";
    private const string ChecksumB = "badf7e84e338faf55536b7b17973cb10221a1a90163a67845517c6dc2065b5e4";
    private const string PackageId = "ce2dab5771bde0f71cf18ad8c8a7ae0a06678764e4e7393e686481282cdeb101";
    private readonly string root = Path.Combine(Path.GetTempPath(), $"phase20-projection-{Guid.NewGuid():N}");

    [Fact]
    public async Task Reader_rejects_stale_validation_with_null_checksum()
    {
        var plan = WriteEvidence(validationChecksum: null);

        var exception = await Assert.ThrowsAsync<Rc2PublishingControlException>(
            () => new Phase20PublishingAuthorityReader().ReadAsync(plan, CancellationToken.None));

        Assert.Equal("RC2_PUBLISH_PHASE20_INVALID", exception.Code);
    }

    [Fact]
    public async Task Reader_accepts_pending_package_when_all_canonical_identity_agrees()
    {
        var plan = WriteEvidence(ChecksumA);

        var authority = await new Phase20PublishingAuthorityReader().ReadAsync(plan, CancellationToken.None);

        Assert.NotNull(authority);
        Assert.Equal(ChecksumA, authority.AuthorityChecksum);
        Assert.Equal(PackageId, authority.PublishingPackageId);
        Assert.Equal("Succeeded", authority.Status);
        Assert.True(authority.TechnicalQaApproved);
        Assert.True(authority.PublicationPackageReady);
    }

    [Fact]
    public async Task Package_requested_outputs_come_from_committed_production_request()
    {
        var planId = Guid.NewGuid();
        var phase1 = Path.Combine(root, "01-plan");
        Directory.CreateDirectory(phase1);
        var requestedOutputs = new[] { "ShortVideo", "LongVideo", "Thumbnail", "HeroAsset", "Gallery" };
        var productionRequest = new Phase1ProductionRequest("test", Guid.NewGuid(), planId, "en", "en", [],
            requestedOutputs, 1, 20, 1, 20, false, true, false, "Normal", "");
        productionRequest = productionRequest with
        {
            RequestChecksum = Phase1CanonicalJson.Checksum(productionRequest, nameof(Phase1ProductionRequest.RequestChecksum))
        };
        await File.WriteAllTextAsync(Path.Combine(phase1, "production-request.json"),
            Phase1CanonicalJson.Serialize(productionRequest));

        var outputs = await Rc2Phase20ExecutionService.ResolveGovernedRequestedOutputsAsync(root,
            Path.Combine(root, "20-publishing", "en"), planId,
            ["ShortVideo", "LongVideo", "Thumbnail"], CancellationToken.None);

        Assert.Equal(["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset", "Gallery"], outputs);
    }

    [Fact]
    public void Social_targets_require_actual_hero_and_gallery_roles()
    {
        var videoOnly = new Dictionary<string, int> { ["ShortVideo"] = 1, ["LongVideo"] = 1 };
        var videoTargets = Phase20PublishingAuthorityReader.PackageableTargets(videoOnly);
        Assert.DoesNotContain(Rc2PublishingTarget.InstagramPost, videoTargets);
        Assert.DoesNotContain(Rc2PublishingTarget.FacebookPost, videoTargets);
        Assert.DoesNotContain(Rc2PublishingTarget.InstagramCarousel, videoTargets);
        Assert.DoesNotContain(Rc2PublishingTarget.FacebookCarousel, videoTargets);

        var complete = new Dictionary<string, int>(videoOnly)
        {
            ["HeroPortrait"] = 1,
            ["HeroLandscape"] = 1,
            ["GalleryImage"] = 6
        };
        var completeTargets = Phase20PublishingAuthorityReader.PackageableTargets(complete);
        Assert.Equal(9, completeTargets.Count);
    }

    [Fact]
    public async Task Reader_rejects_validation_checksum_mismatch()
    {
        var plan = WriteEvidence(ChecksumB);

        var exception = await Assert.ThrowsAsync<Rc2PublishingControlException>(
            () => new Phase20PublishingAuthorityReader().ReadAsync(plan, CancellationToken.None));

        Assert.Equal("RC2_PUBLISH_PHASE20_INVALID", exception.Code);
    }

    [Fact]
    public async Task Reader_treats_partial_canonical_evidence_as_invalid_not_missing()
    {
        var plan = new Rc2PublishingPlan(Guid.NewGuid(), "Orion", "en", "global", root,
            Path.Combine(root, "19-video-qa", "en"), Path.Combine(root, "20-publishing", "en"),
            Path.Combine(root, "validation", "phase-20-validation.json"));
        Directory.CreateDirectory(plan.Phase20Root);
        File.WriteAllText(Path.Combine(plan.Phase20Root, "publishing-package.json"), "{}");

        var exception = await Assert.ThrowsAsync<Rc2PublishingControlException>(
            () => new Phase20PublishingAuthorityReader().ReadAsync(plan, CancellationToken.None));

        Assert.Equal("RC2_PUBLISH_PHASE20_INVALID", exception.Code);
    }

    [Fact]
    public async Task Reader_returns_missing_when_no_phase20_evidence_exists()
    {
        var plan = new Rc2PublishingPlan(Guid.NewGuid(), "Orion", "en", "global", root,
            Path.Combine(root, "19-video-qa", "en"), Path.Combine(root, "20-publishing", "en"),
            Path.Combine(root, "validation", "phase-20-validation.json"));

        Assert.Null(await new Phase20PublishingAuthorityReader().ReadAsync(plan, CancellationToken.None));
    }

    [Fact]
    public async Task Status_endpoint_returns_200_for_valid_pending_package()
    {
        var response = new Rc2PublishingStatusResponse(Guid.NewGuid(), "Orion", "en", "global",
            new(true, true, "phase19", true),
            new(true, "Succeeded", PackageId, ChecksumA, true, true, false, false,
                Rc2PublishingApprovalStatus.Pending, 0),
            new Dictionary<string, int>(), [], new Dictionary<Rc2PublishingTarget, Rc2TargetStatus>());
        var controller = new Rc2PublishingController(new StubControlService(response), NullLogger<Rc2PublishingController>.Instance);

        var action = await controller.Status(response.PlanId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal(200, ok.StatusCode);
        Assert.Same(response, ok.Value);
        Assert.False(response.Phase20.PublishApproved);
        Assert.False(response.Phase20.DownstreamReady);
        Assert.Equal(Rc2PublishingApprovalStatus.Pending, response.Phase20.ApprovalStatus);
    }

    [Fact]
    public void Generic_writer_preserves_phase20_committed_projection()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));

        Assert.Contains("phaseNo is 14 or 16 or 17 or 18 or 19 or 20 && File.Exists(validationPath)", source);
    }

    [Fact]
    public void Package_execution_boundary_excludes_phase20_from_phase4_recovery()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));

        Assert.Contains("phase.No is >= 5 and <= 19", source);
    }

    private Rc2PublishingPlan WriteEvidence(string? validationChecksum)
    {
        var phase20 = Path.Combine(root, "20-publishing", "en");
        var validation = Path.Combine(root, "validation", "phase-20-validation.json");
        Directory.CreateDirectory(phase20);
        Directory.CreateDirectory(Path.GetDirectoryName(validation)!);
        Write(Path.Combine(phase20, "publishing-manifest.json"), ChecksumA, manifest: true);
        Write(Path.Combine(phase20, "publishing-package.json"), ChecksumA, package: true);
        Write(Path.Combine(phase20, "phase20-authority-diagnostics.json"), ChecksumA, diagnostics: true);
        Write(Path.Combine(phase20, "phase20-publication-report.json"), ChecksumA, report: true);
        Write(validation, validationChecksum, validation: true);
        return new(Guid.NewGuid(), "Orion", "en", "global", root, Path.Combine(root, "19-video-qa", "en"), phase20, validation);
    }

    private static void Write(string path, string? checksum, bool manifest = false, bool package = false,
        bool diagnostics = false, bool report = false, bool validation = false)
    {
        var value = new Dictionary<string, object?>
        {
            ["publishingPackageId"] = PackageId,
            ["authorityChecksum"] = checksum
        };
        if (manifest) value["artifacts"] = Array.Empty<object>();
        if (package)
        {
            value["technicalQaApproved"] = true;
            value["publicationPackageReady"] = true;
            value["platformAssetMap"] = new Dictionary<string, object>();
        }
        if (diagnostics)
        {
            value["semanticValidationPassed"] = true;
            value["checksumValidationPassed"] = true;
            value["manifestValidationPassed"] = true;
        }
        if (report)
        {
            value["status"] = "Succeeded";
            value["reasonCode"] = Phase20ReasonCodes.GatePending;
            value["publicationCommitted"] = true;
            value["committedReadbackPassed"] = true;
            value["committedStateValidationPassed"] = true;
            value["validationStatus"] = "Valid";
            value["manifestValidationStatus"] = "Valid";
            value["publishApproved"] = false;
            value["downstreamReady"] = false;
        }
        if (validation)
        {
            value["generated"] = checksum is not null;
            value["publicationCommitted"] = checksum is not null;
            value["validationStatus"] = checksum is null ? null : "Valid";
            value["publishApproved"] = false;
            value["downstreamReady"] = false;
        }
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        GC.SuppressFinalize(this);
    }

    private sealed class StubControlService(Rc2PublishingStatusResponse response) : IRc2PublishingControlService
    {
        public Task<Rc2PublishingStatusResponse> GetStatusAsync(Guid planId, CancellationToken cancellationToken) => Task.FromResult(response);
        public Task<Rc2PublishingPackageResponse> CreateOrRefreshPackageAsync(Guid planId, bool overwriteExisting, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Rc2PublishingApprovalResponse> SetApprovalAsync(Guid planId, Rc2PublishingApprovalStatus decision, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
