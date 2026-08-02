using System.Reflection;
using System.Runtime.Serialization;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase6InputAuthorityEvaluatorTests
{
    private static readonly Phase6InputAuthorityRequest LongRequest =
        new("root", "execution", "plan", "event", "en", ["Long"]);

    [Fact]
    public async Task EvaluateAsync_CancellationBeforePhase4_Propagates()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var evaluator = new Phase6InputAuthorityEvaluator(
            new ThrowingPhase4(new IOException("unused")),
            new ThrowingPhase5(new InvalidOperationException("unused")));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => evaluator.EvaluateAsync(LongRequest, source.Token));
    }

    [Theory]
    [MemberData(nameof(ExpectedPhase4Exceptions))]
    public async Task EvaluateAsync_ExpectedPhase4Exception_ReturnsPhase4Invalid(Exception exception)
    {
        var phase5 = new RecordingPhase5(InvalidPhase5());
        var evaluator = new Phase6InputAuthorityEvaluator(new ThrowingPhase4(exception), phase5);

        var result = await evaluator.EvaluateAsync(LongRequest);

        Assert.False(result.IsValid);
        Assert.Null(result.Authority);
        Assert.Equal("P6INPUT_PHASE4_INVALID", result.ReasonCode);
        Assert.Single(result.Errors);
        Assert.Equal(0, phase5.Calls);
    }

    [Fact]
    public async Task EvaluateAsync_OperationCanceledExceptionFromPhase4_Propagates()
    {
        var evaluator = new Phase6InputAuthorityEvaluator(
            new ThrowingPhase4(new OperationCanceledException("cancelled")),
            new ThrowingPhase5(new InvalidOperationException("unused")));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => evaluator.EvaluateAsync(LongRequest));
    }

    [Fact]
    public async Task EvaluateAsync_InvalidPhase4Result_ReturnsPhase4Invalid()
    {
        var phase4 = new StaticPhase4(new Phase4CommittedAuthorityEvaluation(
            false, null, "P4REUSE_AUTHORITY_MISSING", [], []));
        var phase5 = new RecordingPhase5(InvalidPhase5());
        var evaluator = new Phase6InputAuthorityEvaluator(phase4, phase5);

        var result = await evaluator.EvaluateAsync(LongRequest);

        Assert.False(result.IsValid);
        Assert.Equal("P6INPUT_PHASE4_INVALID", result.ReasonCode);
        Assert.Contains("P4REUSE_AUTHORITY_MISSING", result.Errors);
        Assert.Equal(0, phase5.Calls);
    }

    [Fact]
    public async Task EvaluateAsync_MissingPhase4ValidationEvidence_ReturnsPhase4Invalid()
    {
        var aggregate = MinimalAggregate();
        var phase4 = ValidPhase4(aggregate) with { CommittedValidationEvidence = [] };
        var evaluator = new Phase6InputAuthorityEvaluator(
            new StaticPhase4(phase4),
            new RecordingPhase5(InvalidPhase5()));

        var result = await evaluator.EvaluateAsync(LongRequest);

        Assert.False(result.IsValid);
        Assert.Equal("P6INPUT_PHASE4_INVALID", result.ReasonCode);
        Assert.Contains(result.Errors, value =>
            value.Contains("validation evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvaluateAsync_UnsafePhase4ValidationEvidence_ReturnsPhase4Invalid()
    {
        var aggregate = MinimalAggregate();
        var phase4 = ValidPhase4(aggregate) with
        {
            CommittedValidationEvidence = ["../validation/phase-04-validation.json"],
            ArtifactPaths = ["../validation/phase-04-validation.json", "phase-manifest.json"]
        };
        var evaluator = new Phase6InputAuthorityEvaluator(
            new StaticPhase4(phase4),
            new RecordingPhase5(InvalidPhase5()));

        var result = await evaluator.EvaluateAsync(LongRequest);

        Assert.False(result.IsValid);
        Assert.Equal("P6INPUT_PHASE4_INVALID", result.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_MissingPhase4ManifestEvidence_ReturnsPhase4Invalid()
    {
        var aggregate = MinimalAggregate();
        var phase4 = ValidPhase4(aggregate) with { ManifestEvidence = [] };
        var evaluator = new Phase6InputAuthorityEvaluator(
            new StaticPhase4(phase4),
            new RecordingPhase5(InvalidPhase5()));

        var result = await evaluator.EvaluateAsync(LongRequest);

        Assert.False(result.IsValid);
        Assert.Equal("P6INPUT_PHASE4_INVALID", result.ReasonCode);
        Assert.Contains(result.Errors, value =>
            value.Contains("manifest evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(ExpectedPhase5Exceptions))]
    public async Task EvaluateAsync_ExpectedPhase5Exception_ReturnsPhase5Invalid(Exception exception)
    {
        var evaluator = new Phase6InputAuthorityEvaluator(
            new StaticPhase4(ValidPhase4(MinimalAggregate())),
            new ThrowingPhase5(exception));

        var result = await evaluator.EvaluateAsync(LongRequest);

        Assert.False(result.IsValid);
        Assert.Null(result.Authority);
        Assert.Equal("P6INPUT_PHASE5_INVALID", result.ReasonCode);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task EvaluateAsync_OperationCanceledExceptionFromPhase5_Propagates()
    {
        var evaluator = new Phase6InputAuthorityEvaluator(
            new StaticPhase4(ValidPhase4(MinimalAggregate())),
            new ThrowingPhase5(new OperationCanceledException("cancelled")));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => evaluator.EvaluateAsync(LongRequest));
    }

    [Theory]
    [InlineData("P5REUSE_SOURCE_PHASE4_MISMATCH", "aggregate mismatch", "P6INPUT_PHASE4_LINEAGE_MISMATCH")]
    [InlineData("P5REUSE_SOURCE_PHASE4_MISMATCH", "Long projection lineage mismatch", "P6INPUT_LONG_LINEAGE_MISMATCH")]
    [InlineData("P5REUSE_SOURCE_PHASE4_MISMATCH", "Short projection lineage mismatch", "P6INPUT_SHORT_LINEAGE_MISMATCH")]
    [InlineData("P5REUSE_CHECKSUM_INVALID", "checksum mismatch", "P6INPUT_PHASE5_INVALID")]
    public async Task EvaluateAsync_InvalidPhase5Result_MapsDeterministicReasonCode(
        string phase5Code, string phase5Error, string expectedPhase6Code)
    {
        var phase5 = new Phase5CommittedStateEvaluation(
            false, phase5Code, [phase5Error], [], null);
        var evaluator = new Phase6InputAuthorityEvaluator(
            new StaticPhase4(ValidPhase4(MinimalAggregate())),
            new StaticPhase5(phase5));

        var result = await evaluator.EvaluateAsync(LongRequest);

        Assert.False(result.IsValid);
        Assert.Equal(expectedPhase6Code, result.ReasonCode);
        Assert.Contains(phase5Code, result.Errors[0], StringComparison.Ordinal);
        Assert.Contains(phase5Error, result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Phase6InputAuthorityException_PreservesReasonCodeAndFiltersBlankErrors()
    public async Task EvaluateAsync_ValidCommittedAuthorities_CallsPhase4ThenPhase5AndReturnsTypedAuthority()
    {
        var calls = new List<string>();
        var fixture = Phase5CertificationFixture.Create();
        var phase4 = new RecordingPhase4(fixture.PublishedPhase4, calls);
        var phase5 = new RecordingPhase5(Published(fixture), calls);
        var evaluator = new Phase6InputAuthorityEvaluator(phase4, phase5);

        var result = await evaluator.EvaluateAsync(Request with
        {
            ExecutionId = fixture.Request.ExecutionId,
            PlanId = fixture.Request.PlanId,
            EventId = fixture.Request.EventId,
            Language = fixture.Request.Language
        });

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.NotNull(result.Authority);
        Assert.Same(fixture.PublishedPhase4, result.Authority.Phase4Aggregate);
        Assert.Equal(["phase4", "phase5"], calls);
        Assert.Equal(["Long"], result.Authority.RequestedVariants);
    }

    [Fact]
    public async Task EvaluateAsync_Phase4Failure_DoesNotCallPhase5()
    {
        var calls = new List<string>();
        var fixture = Phase5CertificationFixture.Create();
        var phase4 = new RecordingPhase4(fixture.PublishedPhase4, calls, valid: false);
        var phase5 = new RecordingPhase5(Published(fixture), calls);

        var result = await new Phase6InputAuthorityEvaluator(phase4, phase5).EvaluateAsync(Request);

        Assert.False(result.IsValid);
        Assert.Equal("P6INPUT_PHASE4_INVALID", result.ReasonCode);
        Assert.Equal(["phase4"], calls);
    }

    [Fact]
    public void Phase6InputAuthorityException_PreservesReasonCodeAndDeterministicErrors()
    {
        var exception = new Phase6InputAuthorityException(
            "P6INPUT_PHASE5_INVALID", ["first", "", " ", "second"]);

        Assert.Equal("P6INPUT_PHASE5_INVALID", exception.ReasonCode);
        Assert.Equal(["first", "second"], exception.Errors);
        Assert.Equal("P6INPUT_PHASE5_INVALID: first; second", exception.Message);
    }

    [Fact]
    public void Phase6InputAuthorityException_RequiresReasonCode() =>
        Assert.Throws<ArgumentException>(
            () => new Phase6InputAuthorityException("", ["error"]));

    [Theory]
    [InlineData("validation/phase-04-validation.json", true)]
    [InlineData("phase-manifest.json", true)]
    [InlineData("05-editorial/blueprint-certification.json", true)]
    [InlineData("../phase-manifest.json", false)]
    [InlineData("/phase-manifest.json", false)]
    [InlineData("C:\\phase-manifest.json", false)]
    [InlineData("validation\\phase-04-validation.json", false)]
    [InlineData(".phase-06-staging-x/file.json", false)]
    [InlineData(".phase-06-backup-x/file.json", false)]
    public void SafeRelative_EnforcesCommittedRelativePathPolicy(string path, bool expected)
    {
        var method = typeof(Phase6InputAuthorityEvaluator)
            .GetMethod("SafeRelative", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, (bool)method!.Invoke(null, [path])!);
    }

    [Theory]
    [InlineData("long", "Long")]
    [InlineData("LONG", "Long")]
    [InlineData("Long", "Long")]
    [InlineData("short", "Short")]
    [InlineData("SHORT", "Short")]
    [InlineData("Short", "Short")]
    public void Canonical_NormalizesSupportedVariantCasing(string input, string expected)
    {
        var method = typeof(Phase6InputAuthorityEvaluator)
            .GetMethod("Canonical", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, [input]));
    }

    public static IEnumerable<object[]> ExpectedPhase4Exceptions()
    {
        yield return [new IOException("io")];
        yield return [new System.Text.Json.JsonException("json")];
        yield return [new InvalidDataException("invalid")];
        yield return [new InvalidOperationException("operation")];
        yield return [new NotSupportedException("unsupported")];
        yield return [new ArgumentException("argument")];
    }

    public static IEnumerable<object[]> ExpectedPhase5Exceptions() =>
        ExpectedPhase4Exceptions();

    private static Phase5CommittedStateEvaluation InvalidPhase5() =>
        new(false, "P5REUSE_AUTHORITY_MISSING", ["missing"], [], null);

    private static Phase4CommittedAuthorityEvaluation ValidPhase4(DocumentaryBlueprintAggregate aggregate) =>
        new(true, aggregate, "P4REUSE_VALID", [],
            ["phase-manifest.json", "validation/phase-04-validation.json"])
        {
            CommittedValidationEvidence = ["validation/phase-04-validation.json"],
            ManifestEvidence = ["phase-manifest.json"]
        };

    private static DocumentaryBlueprintAggregate MinimalAggregate()
    {
#pragma warning disable SYSLIB0050
        var aggregate = (DocumentaryBlueprintAggregate)
            FormatterServices.GetUninitializedObject(typeof(DocumentaryBlueprintAggregate));
#pragma warning restore SYSLIB0050
        SetAutoProperty(aggregate, "AggregateId", "aggregate-id");
        SetAutoProperty(aggregate, "DeterministicChecksum", Sha('a'));
        SetAutoProperty(aggregate, "LongProjectionChecksum", Sha('b'));
        SetAutoProperty(aggregate, "ShortProjectionChecksum", Sha('c'));
        return aggregate;
    }

    private static string Sha(char value) => new(value, 64);

    private static void SetAutoProperty(object target, string propertyName, object? value)
    {
        var field = target.GetType().GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private sealed class StaticPhase4(Phase4CommittedAuthorityEvaluation result)
        : IPhase4CommittedAuthorityEvaluator
    {
        public Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(
            string executionRoot, string expectedExecutionId, string expectedPlanId,
            string expectedEventId, string expectedLanguage,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class ThrowingPhase4(Exception exception)
        : IPhase4CommittedAuthorityEvaluator
    {
        public Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(
            string executionRoot, string expectedExecutionId, string expectedPlanId,
            string expectedEventId, string expectedLanguage,
            CancellationToken cancellationToken = default) =>
            Task.FromException<Phase4CommittedAuthorityEvaluation>(exception);
    }

    private sealed class StaticPhase5(Phase5CommittedStateEvaluation result)
        : IPhase5CommittedAuthorityEvaluator
    {
        public Task<Phase5CommittedStateEvaluation> EvaluateAsync(
            string executionRoot, string expectedExecutionId, string expectedPlanId,
            string expectedEventId, string expectedLanguage,
            Phase5ExpectedPhase4Authority expectedPhase4,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class RecordingPhase5(Phase5CommittedStateEvaluation result)
        : IPhase5CommittedAuthorityEvaluator
    {
        public int Calls { get; private set; }
        public Task<Phase5CommittedStateEvaluation> EvaluateAsync(
            string executionRoot, string expectedExecutionId, string expectedPlanId,
            string expectedEventId, string expectedLanguage,
            Phase5ExpectedPhase4Authority expectedPhase4,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingPhase5(Exception exception)
        : IPhase5CommittedAuthorityEvaluator
    {
        public Task<Phase5CommittedStateEvaluation> EvaluateAsync(
            string executionRoot, string expectedExecutionId, string expectedPlanId,
            string expectedEventId, string expectedLanguage,
            Phase5ExpectedPhase4Authority expectedPhase4,
            CancellationToken cancellationToken = default) =>
            Task.FromException<Phase5CommittedStateEvaluation>(exception);
    }

    private sealed class RecordingPhase4(DocumentaryBlueprintAggregate aggregate, List<string> calls, bool valid = true)
        : IPhase4CommittedAuthorityEvaluator
    {
        public Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(string executionRoot, string expectedExecutionId,
            string expectedPlanId, string expectedEventId, string expectedLanguage, CancellationToken cancellationToken = default)
        {
            calls.Add("phase4");
            return Task.FromResult(new Phase4CommittedAuthorityEvaluation(valid, valid ? aggregate : null,
                valid ? "P4REUSE_VALID" : "P4REUSE_INVALID", [],
                ["04-blueprint/documentary-blueprint-aggregate.json", "validation/phase-04-validation.json", "phase-manifest.json"])
            {
                CommittedValidationEvidence = ["validation/phase-04-validation.json"],
                ManifestEvidence = ["phase-manifest.json"]
            });
        }
    }

    private sealed class RecordingPhase5(PublishedBlueprintCertification authority, List<string> calls)
        : IPhase5CommittedAuthorityEvaluator
    {
        public Task<Phase5CommittedStateEvaluation> EvaluateAsync(string executionRoot, string expectedExecutionId,
            string expectedPlanId, string expectedEventId, string expectedLanguage, Phase5ExpectedPhase4Authority expectedPhase4,
            CancellationToken cancellationToken = default)
        {
            calls.Add("phase5");
            var artifact = new Phase5ArtifactInventoryEntry("05-editorial/blueprint-certification.json", "certification",
                authority.Certification.SemanticChecksum, "physical", 1, expectedPhase4.AggregateChecksum);
            return Task.FromResult(new Phase5CommittedStateEvaluation(true, "P5REUSE_VALID", [], [artifact], authority)
            {
                PublicationTransactionId = "publication",
                PublicationCommitted = true,
                CommittedStateValidationPassed = true,
                CommittedValidationEvidence = ["validation/phase-05-validation.json"],
                ManifestEvidence = ["phase-manifest.json"]
            });
        }
    }

    private static PublishedBlueprintCertification Published(Phase5CertificationFixtureResult fixture) => new(
        fixture.Result.Certification, fixture.Result.EditorialContract, fixture.Result.Validation,
        fixture.Result.SceneIntents, fixture.Result.Coverage, fixture.Result.Transitions, fixture.Result.PauseTest,
        fixture.PublishedPhase4.AggregateId, fixture.PublishedPhase4.DeterministicChecksum, "1.0", "published");
}
