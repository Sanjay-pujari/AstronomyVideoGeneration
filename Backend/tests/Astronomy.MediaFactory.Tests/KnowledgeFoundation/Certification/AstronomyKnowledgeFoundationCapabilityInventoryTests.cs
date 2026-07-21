using Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Certification;

public sealed class AstronomyKnowledgeFoundationCapabilityInventoryTests
{
    [Fact]
    public void Runtime_capabilities_match_documented_inventory()
    {
        using var provider = KnowledgeFoundationCertificationFixture.Provider();
        var caps = provider.GetRequiredService<IAstronomyKnowledgeFoundationCapabilities>().Snapshot.Capabilities;
        Assert.Equal(KnowledgeFoundationCertificationFixture.CapabilityIds, caps.Select(c => c.Id.Value));
        Assert.Equal(caps.Count, caps.Select(c => c.Id).Distinct().Count());
        foreach (var group in caps.GroupBy(c => c.Kind)) Assert.Equal(group.Count(), group.Select(c => c.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.All(caps, c => Assert.Equal(ServiceLifetime.Singleton, c.Lifetime));
        var doc = File.ReadAllText(Path.Combine(KnowledgeFoundationCertificationFixture.RepoRoot(), "docs", "architecture", "knowledge-foundation", "RegistrationAndCapabilities.md"));
        foreach (var cap in caps) { Assert.Contains(cap.Id.Value, doc, StringComparison.Ordinal); Assert.True(cap.ContractType.IsAssignableFrom(cap.ImplementationType), cap.Id.Value); }
    }
}
