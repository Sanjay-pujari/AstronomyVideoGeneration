using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Certification;

public sealed class AstronomyKnowledgeFoundationRuleInventoryTests
{
    [Fact]
    public void Runtime_rules_are_unique_ordered_and_documented()
    {
        using var provider = KnowledgeFoundationCertificationFixture.Provider();
        var domain = provider.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>().Descriptors;
        Assert.Equal(domain.Select(d => d.RuleId).Distinct(StringComparer.Ordinal).Count(), domain.Count);
        Assert.Equal(domain.OrderBy(d => d.Order).ThenBy(d => d.RuleId, StringComparer.Ordinal).Select(d => d.RuleId), domain.Select(d => d.RuleId));
        var cross = provider.GetRequiredService<IAstronomyCrossDomainValidationRuleRegistry>().Descriptors;
        Assert.Equal(KnowledgeFoundationCertificationFixture.CrossRuleIds, cross.Select(d => d.RuleId));
        var graph = provider.GetRequiredService<IAstronomyKnowledgeGraphValidationRuleRegistry>().Descriptors;
        Assert.Equal(KnowledgeFoundationCertificationFixture.GraphRuleIds, graph.Select(d => d.RuleId));
        var doc = File.ReadAllText(Path.Combine(KnowledgeFoundationCertificationFixture.RepoRoot(), "docs", "architecture", "knowledge-foundation", "ValidationArchitecture.md"));
        foreach (var id in domain.Select(d => d.RuleId).Concat(cross.Select(d => d.RuleId)).Concat(graph.Select(d => d.RuleId))) Assert.Contains(id, doc, StringComparison.Ordinal);
        Assert.Equal(11, graph.Count);
    }
}
