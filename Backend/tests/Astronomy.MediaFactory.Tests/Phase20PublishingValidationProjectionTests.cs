using System.Text.Json;
using Astronomy.MediaFactory.Api.Controllers;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Microsoft.AspNetCore.Mvc;

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
    public async Task Reader_rejects_validation_checksum_mismatch()
    {
        var plan = WriteEvidence(ChecksumB);

        var exception = await Assert.ThrowsAsync<Rc2PublishingControlException>(
            () => new Phase20PublishingAuthorityReader().ReadAsync(plan, CancellationToken.None));

        Assert.Equal("RC2_PUBLISH_PHASE20_INVALID", exception.Code);
    }

    [Fact]
    public async Task Status_endpoint_returns_200_for_valid_pending_package()
    {
        var response = new Rc2PublishingStatusResponse(Guid.NewGuid(), "Orion", "en", "global",
            new(true, true, "phase19", true),
            new(true, "Succeeded", PackageId, ChecksumA, true, true, false, false,
                Rc2PublishingApprovalStatus.Pending, 0),
            new Dictionary<string, int>(), [], new Dictionary<Rc2PublishingTarget, Rc2TargetStatus>());
        var controller = new Rc2PublishingController(new StubControlService(response));

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
