using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase17MotionAuthorityTests
{
    [Fact]
    public void Phase17HousekeepingRemovesOnlyEmptyTransactionsAndParents()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var canonicalEnglish = Path.Combine(root, "17-motion", "en");
        var canonicalHindi = Path.Combine(root, "17-motion", "hi");
        var nonEmpty = Path.Combine(root, "17-motion", ".staging", "unknown-guid");
        Directory.CreateDirectory(canonicalEnglish);
        Directory.CreateDirectory(canonicalHindi);
        File.WriteAllText(Path.Combine(canonicalEnglish, "authority.json"), "unchanged");
        File.WriteAllText(Path.Combine(canonicalHindi, "authority.json"), "preserved");
        Directory.CreateDirectory(Path.Combine(root, "17-motion", ".staging", "old-guid"));
        Directory.CreateDirectory(Path.Combine(root, "17-motion", ".backup", "old-guid"));
        Directory.CreateDirectory(nonEmpty);
        File.WriteAllText(Path.Combine(nonEmpty, "recovery.json"), "retain");

        try
        {
            Phase17MotionAuthorityPublisher.SweepStaleTransactions(root);

            Assert.False(Directory.Exists(Path.Combine(root, "17-motion", ".staging", "old-guid")));
            Assert.True(Directory.Exists(nonEmpty));
            Assert.False(Directory.Exists(Path.Combine(root, "17-motion", ".backup")));
            Assert.Equal("unchanged", File.ReadAllText(Path.Combine(canonicalEnglish, "authority.json")));
            Assert.Equal("preserved", File.ReadAllText(Path.Combine(canonicalHindi, "authority.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Phase17HousekeepingDoesNotInvalidateValidReuse()
    {
        var source = PublisherSource();
        var sweep = source.IndexOf("SweepStaleTransactions(root)", StringComparison.Ordinal);
        var reuseDecision = source.IndexOf("if (File.Exists(existingPlanPath)", StringComparison.Ordinal);
        Assert.True(sweep >= 0 && sweep < reuseDecision);
        Assert.Contains("false, true, false", source);
    }

    [Fact]
    public void Phase17ExplicitOverwriteSuppressesReuse()
    {
        Assert.False(Phase17MotionAuthorityPublisher.ShouldReuseExistingAuthority(
            overwriteExisting: true, existingAuthorityValidAndMatching: true));
    }

    [Fact]
    public void Phase17OverwriteFalseAllowsReuse()
    {
        Assert.True(Phase17MotionAuthorityPublisher.ShouldReuseExistingAuthority(
            overwriteExisting: false, existingAuthorityValidAndMatching: true));
    }

    [Fact]
    public void Phase17ReceivesOverwriteExistingFromProductionRequest()
    {
        var source = PipelineSource();
        Assert.Contains("request.OverwriteExisting, startPhaseNo", source);
        Assert.Contains("context.OverwriteExisting, cancellationToken", source);
    }

    [Fact]
    public void Phase17ForcedRebuildRecordsTransactionalPublicationEvidence()
    {
        var source = PublisherSource();
        Assert.Contains("reuseSuppressedByOverwrite = overwrite && reuseEligibleBeforeOverwrite", source);
        Assert.Contains("previousAuthorityExisted, candidateGenerated = true", source);
        Assert.Contains("replacedExistingAuthority = replacingExistingAuthority", source);
        Assert.Contains("transactionId", source);
    }

    [Fact]
    public void Phase17ReuseDecisionPrecedesCandidateBuilder()
    {
        var source = PublisherSource();
        Assert.True(source.IndexOf("ShouldReuseExistingAuthority(overwrite", StringComparison.Ordinal) <
            source.IndexOf("BuildEntry(root", StringComparison.Ordinal));
    }

    [Fact]
    public void Phase17ReuseSweepRemovesEmptyTransactionParents()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "17-motion", ".staging", "old-guid"));
        Directory.CreateDirectory(Path.Combine(root, "17-motion", ".backup", "old-guid"));

        try
        {
            Phase17MotionAuthorityPublisher.SweepStaleTransactions(root);

            Assert.False(Directory.Exists(Path.Combine(root, "17-motion", ".staging")));
            Assert.False(Directory.Exists(Path.Combine(root, "17-motion", ".backup")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string PublisherSource() => File.ReadAllText(Path.Combine(RepositoryRoot(),
        "src", "Astronomy.MediaFactory.Infrastructure", "Persistence", "Phase17MotionAuthorityPublisher.cs"));

    private static string PipelineSource() => File.ReadAllText(Path.Combine(RepositoryRoot(),
        "src", "Astronomy.MediaFactory.Infrastructure", "Persistence", "ProductionPipelineExecutionService.cs"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Astronomy.MediaFactory.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Backend repository root was not found.");
    }

    [Fact]
    public void SemanticSelector_ReusesDeterministicMaturePolicy()
    {
        var selector = new MotionProfileSelector();

        var first = selector.SelectSemantic("planetary conjunction hook", 0, 4);
        var repeated = selector.SelectSemantic("planetary conjunction hook", 0, 4);

        Assert.Equal(first, repeated);
        Assert.Equal(MotionProfileType.SlowZoomIn, first.MotionType);
        Assert.Equal(MotionEasingKind.EaseInOutSine, first.Easing);
    }

    [Fact]
    public void StaticFallback_IsAClosedValidProductionMotion()
    {
        Assert.Contains(Phase17MotionType.Static, Enum.GetValues<Phase17MotionType>());
        Assert.DoesNotContain(Enum.GetNames<Phase17MotionType>(), value =>
            value is "Parallax" or "Orbit" or "Tilt");
    }

    [Fact]
    public void Phase17ComparesPhysicalBytesToCertifiedPhysicalAssetHash()
    {
        var source = PublisherSource();
        Assert.Contains("visual.PhysicalSha256", source);
        Assert.Contains("actualHash != expectedHash", source);
    }

    [Fact]
    public void Phase17DoesNotComparePhysicalBytesToAuthorityChecksum() =>
        Assert.DoesNotContain("BuildEntry(root, scene, visual.PhysicalPath, p9.DeterministicChecksum", PublisherSource());

    [Fact]
    public void Phase17DoesNotComparePhysicalBytesToManifestChecksum() =>
        Assert.DoesNotContain("BuildEntry(root, scene, visual.PhysicalPath, p10.DeterministicChecksum", PublisherSource());

    [Fact]
    public void Phase17LongSceneUsesExactPhase9CertifiedPhysicalAsset()
    {
        var source = PublisherSource();
        Assert.Contains("Path.Combine(root, \"09-long-scenes\")", source);
        Assert.Contains("longById[scene.SceneId]", source);
    }

    [Fact]
    public void Phase17LongSceneDoesNotFallbackToPhase8OrLegacyV3ByPosition()
    {
        var source = PublisherSource();
        Assert.DoesNotContain("scene-assets-v3", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longVisuals[", source);
    }

    [Fact]
    public void Phase17LongSceneMappingIsStableWhenManifestOrderChanges() =>
        Assert.Contains("ToDictionary(x => x.SceneId, StringComparer.Ordinal)", PublisherSource());

    [Fact]
    public void Phase17PhysicalEvidenceFailureProjectsReasonCode()
    {
        var source = PipelineSource();
        Assert.Contains("reasonCodeOverride: ex.ReasonCode", source);
    }

    [Fact]
    public void Phase17PhysicalEvidenceFailureProjectsInputFiles() =>
        Assert.Contains("ex.LoadedAuthorityArtifacts", PipelineSource());

    [Fact]
    public void Phase17PhysicalEvidenceFailureIsNotDownstreamReady() =>
        Assert.Contains("phase17Certification?.DownstreamReady", PipelineSource());

    [Theory]
    [InlineData("Phase17SuccessfulAuthorityProjectsAcceptedReasonCode", "phase17Certification?.ReasonCode")]
    [InlineData("Phase17SuccessfulAuthorityProjectsGeneratedTrue", "phase17Certification?.Generated")]
    [InlineData("Phase17SuccessfulAuthorityProjectsPublicationCommitted", "phase17Certification?.PublicationCommitted")]
    [InlineData("Phase17SuccessfulAuthorityProjectsCommittedReadback", "phase17Certification?.CommittedReadbackPassed")]
    [InlineData("Phase17SuccessfulAuthorityProjectsValidationValid", "phase17Certification?.ValidationStatus")]
    [InlineData("Phase17SuccessfulAuthorityProjectsAuthorityChecksum", "phase17Certification?.AuthorityChecksum")]
    [InlineData("Phase17SuccessfulAuthorityProjectsDownstreamReady", "phase17Certification?.DownstreamReady")]
    public void Phase17SuccessfulResultProjectionIsTyped(string _, string projection) =>
        Assert.Contains(projection, PipelineSource());

    [Fact]
    public void Phase17CannotReturnSucceededWithInvalidAuthorityFlags() =>
        Assert.Contains("P17_FINAL_AUTHORITY_INVARIANT_FAILED", PipelineSource());

    [Fact]
    public void Phase17ReportsLoadedPhase16AuthorityInputs() =>
        Assert.Contains("phase16-publication-report.json", PublisherSource());

    [Fact]
    public void Phase17ReportsLoadedVisualAuthorityInputs() =>
        Assert.Contains("phase10-authority-diagnostics.json", PublisherSource());

    [Fact]
    public void Phase17InputFilesNonEmptyOnSuccessfulStandaloneRun() =>
        Assert.Contains("phase17Authority?.LoadedAuthorityArtifacts", PipelineSource());

    [Fact]
    public void Phase17DoesNotReportLegacyTimingAsCanonicalInput() =>
        Assert.DoesNotContain("timing/scene-duration-plan.json", PublisherSource());

    [Fact]
    public void Phase17DoesNotReportAudioOrSrtAsInput()
    {
        Assert.DoesNotContain("tts/", PublisherSource(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".srt", PublisherSource(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Phase17SuccessRemovesTransactionStagingDirectory", "CleanupTransactionDirectory(Path.GetDirectoryName(stage)!")]
    [InlineData("Phase17FailureRemovesTransactionStagingDirectory", "finally")]
    [InlineData("Phase17SuccessRemovesTransactionBackupDirectory", "CleanupTransactionDirectory(Path.GetDirectoryName(backup)!")]
    [InlineData("Phase17CleanupRemovesEmptyTransactionParents", "Directory.EnumerateFileSystemEntries(parent).Any()")]
    public void Phase17TransactionCleanupIsGuaranteed(string _, string evidence) =>
        Assert.Contains(evidence, PublisherSource());

    [Fact]
    public void Phase17RollbackRestoresPreviousAuthority() =>
        Assert.Contains("ReplaceCommittedDirectoryAsync(stage, finalRoot, backup", PublisherSource());

    [Theory]
    [InlineData("Phase17EnglishOverwriteDoesNotDeleteHindiAuthority")]
    [InlineData("Phase17HindiOverwriteDoesNotDeleteEnglishAuthority")]
    [InlineData("Phase17CompatibilityCleanupIsLanguageScoped")]
    public void Phase17CanonicalReplacementIsLanguageScoped(string _) =>
        Assert.Contains("Path.Combine(root, \"17-motion\", language)", PublisherSource());

    [Fact]
    public void Phase17OnlyExecutionDoesNotSetShortVideoGenerated() =>
        Assert.Contains("phase18Succeeded && !string.IsNullOrWhiteSpace(finalShortVideoPath)", PipelineSourceForContentPlan());

    [Fact]
    public void Phase17OnlyExecutionDoesNotSetLongVideoGenerated() =>
        Assert.Contains("phase18Succeeded && !string.IsNullOrWhiteSpace(finalLongVideoPath)", PipelineSourceForContentPlan());

    [Fact]
    public void Phase17OnlyExecutionDoesNotReportFinalVideoCompleted() =>
        Assert.Contains("!requestedRequiredPhases.Contains(18)", PipelineSource());

    private static string PipelineSourceForContentPlan() => File.ReadAllText(Path.Combine(RepositoryRoot(),
        "src", "Astronomy.MediaFactory.Infrastructure", "Persistence", "ContentPlanProductionExecutionService.cs"));
}
