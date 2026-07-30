using System.Reflection;
using System.Security.Cryptography;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2EarlyValidationOwnershipTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Authoritative_validation_is_preserved_and_not_overwritten_by_generic_validator(int phaseNo)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rc2-validation-owner-" + Guid.NewGuid())).FullName;
        var validationPath = Path.Combine(root, "validation", $"phase-{phaseNo:00}-validation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(validationPath)!);
        await File.WriteAllTextAsync(validationPath, $$"""{"phaseNo":{{phaseNo}},"status":"Succeeded","errors":[],"authorityMarker":"keep-me"}""");
        var beforeHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(validationPath)));
        var beforeWrite = File.GetLastWriteTimeUtc(validationPath);
        var phase = new ProductionPhaseResult(phaseNo, "authoritative", ProductionPhaseStatus.Succeeded, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, [], ["authority-output.json"], validationPath, [], [], false);
        var response = Response(root, phase);

        var reconciled = await InvokeReconciliation(response, [phaseNo]);

        Assert.True(reconciled.Success);
        Assert.Equal(beforeHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(validationPath))));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(validationPath));
        Assert.Contains("keep-me", await File.ReadAllTextAsync(validationPath));
    }

    [Fact]
    public async Task Failed_authoritative_physical_validation_drives_API_failure()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rc2-validation-failure-" + Guid.NewGuid())).FullName;
        var path = Path.Combine(root, "validation", "phase-02-validation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"phaseNo":2,"status":"Failed","errors":["physical failure"]}""");
        var phase = new ProductionPhaseResult(2, "authoritative", ProductionPhaseStatus.Succeeded, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, [], [], path, [], [], false);

        var reconciled = await InvokeReconciliation(Response(root, phase), [2]);

        Assert.False(reconciled.Success);
        Assert.Equal(2, reconciled.LastFailedPhaseNo);
        Assert.Equal(ProductionPhaseStatus.Failed, Assert.Single(reconciled.Steps.OfType<ProductionPhaseResult>()).Status);
    }

    [Fact]
    public void Generic_validator_retains_ownership_for_phase_three_only()
    {
        var source = File.ReadAllText(FindRepositoryFile("Backend", "src", "Astronomy.MediaFactory.Infrastructure", "Orchestration", "RC2", "Rc2ContentPlanningBatchOrchestrator.cs"));
        var mapStart = source.IndexOf("var map = new Dictionary", StringComparison.Ordinal);
        var mapEnd = source.IndexOf("foreach (var phaseNo in requestedPhases.Where(map.ContainsKey))", mapStart, StringComparison.Ordinal);
        var map = source[mapStart..mapEnd];
        Assert.Contains("[3]", map);
        Assert.DoesNotContain("[1]", map);
        Assert.DoesNotContain("[2]", map);
        Assert.DoesNotContain("production-pipeline-request.json", source);
        Assert.DoesNotContain("production-event-intelligence-diagnostics.json", source);
    }

    private static async Task<BatchGenerateFromPlansResponse> InvokeReconciliation(BatchGenerateFromPlansResponse response, IReadOnlyList<int> phases)
    {
        var method = typeof(Rc2ContentPlanningBatchOrchestrator).GetMethod("ReconcileEarlyPhaseValidationsAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<BatchGenerateFromPlansResponse>)method.Invoke(null, [response, phases, CancellationToken.None])!;
    }

    private static BatchGenerateFromPlansResponse Response(string root, ProductionPhaseResult phase) => new(
        true, false, 1, 1, 1, [], [phase], [], [], OutputRoot: root, LastCompletedPhaseNo: phase.PhaseNo);

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }
}
