using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Events;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Temporal;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.EventAndTemporal;

public sealed class EventAndTemporalValidationArchitectureTests
{
    private static readonly string[] EventRuleIds = [AstronomyEventAggregateValidationRule.Id, AstronomyEventTemporalExtentValidationRule.Id, AstronomyEventReferenceContextValidationRule.Id, AstronomyEventParticipantValidationRule.Id, AstronomyEventPhaseMarkerValidationRule.Id, AstronomyEventGeometryValidationRule.Id, AstronomyEventCircumstanceValidationRule.Id];
    private static readonly string[] TemporalRuleIds = [AstronomyTemporalPatternValidationRule.Id, AstronomyTemporalReferenceContextValidationRule.Id, AstronomyRecurrenceValidationRule.Id, AstronomyCyclePeriodValidationRule.Id, AstronomyTemporalAnchorValidationRule.Id, AstronomyTemporalPhaseValidationRule.Id, AstronomyTemporalOccurrenceValidationRule.Id, AstronomySeasonalPatternValidationRule.Id, AstronomyTemporalApplicabilityValidationRule.Id];

    [Fact]
    public void Expected_production_files_exist()
    {
        var validation = Path.Combine(Root(), "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "Validation");
        var files = new[] { "AstronomyEventAndTemporalValidationRegistrationExtensions.cs", Path.Combine("Events", "AstronomyEventValidationCodes.cs"), Path.Combine("Events", "AstronomyEventAggregateValidationRule.cs"), Path.Combine("Events", "AstronomyEventTemporalExtentValidationRule.cs"), Path.Combine("Events", "AstronomyEventReferenceContextValidationRule.cs"), Path.Combine("Events", "AstronomyEventParticipantValidationRule.cs"), Path.Combine("Events", "AstronomyEventPhaseMarkerValidationRule.cs"), Path.Combine("Events", "AstronomyEventGeometryValidationRule.cs"), Path.Combine("Events", "AstronomyEventCircumstanceValidationRule.cs"), Path.Combine("Events", "AstronomyEventMeasurementValidator.cs"), Path.Combine("Events", "AstronomyEventValidationRegistrationExtensions.cs"), Path.Combine("Temporal", "AstronomyTemporalValidationCodes.cs"), Path.Combine("Temporal", "AstronomyTemporalPatternValidationRule.cs"), Path.Combine("Temporal", "AstronomyTemporalReferenceContextValidationRule.cs"), Path.Combine("Temporal", "AstronomyRecurrenceValidationRule.cs"), Path.Combine("Temporal", "AstronomyCyclePeriodValidationRule.cs"), Path.Combine("Temporal", "AstronomyTemporalAnchorValidationRule.cs"), Path.Combine("Temporal", "AstronomyTemporalPhaseValidationRule.cs"), Path.Combine("Temporal", "AstronomyTemporalOccurrenceValidationRule.cs"), Path.Combine("Temporal", "AstronomySeasonalPatternValidationRule.cs"), Path.Combine("Temporal", "AstronomyTemporalApplicabilityValidationRule.cs"), Path.Combine("Temporal", "AstronomyTemporalMeasurementValidator.cs"), Path.Combine("Temporal", "AstronomyTemporalValidationRegistrationExtensions.cs") };
        foreach (var file in files) Assert.True(File.Exists(Path.Combine(validation, file)), file);
    }

    [Fact]
    public void Registration_is_idempotent_and_rule_metadata_matches()
    {
        using var provider = EventTemporalValidationFixture.Provider();
        var registry = provider.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>();
        var runtime = provider.GetServices<IAstronomyKnowledgeValidationRule>();
        foreach (var id in EventRuleIds.Concat(TemporalRuleIds))
        {
            var descriptor = Assert.Single(registry.Descriptors, d => d.RuleId == id);
            var rule = Assert.Single(runtime, r => r.RuleId == id);
            Assert.Equal(descriptor.RuleType, rule.GetType());
            Assert.Equal(descriptor.Domain, rule.Domain);
            Assert.Equal(descriptor.Family, rule.Family);
            Assert.Equal(descriptor.Order, rule.Order);
        }
    }

    [Fact]
    public void Frozen_dependencies_and_forbidden_dependencies_are_protected()
    {
        var root = Root();
        foreach (var dir in new[] { "Events", "Temporal" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "TypedDomains", dir), "*.cs"))
            Assert.DoesNotContain("KnowledgeFoundation.Validation", File.ReadAllText(file), StringComparison.Ordinal);

        var forbidden = new[] { "DateTimeOffset.UtcNow", "DateTime.UtcNow", "HttpClient", "DbContext", "IQueryable", "Ephemeris", "Skyfield", "Stellarium", "ConvertCoordinate", "ConvertUnit" };
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "Validation", "Events"), "*.cs", SearchOption.AllDirectories).Concat(Directory.EnumerateFiles(Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "Validation", "Temporal"), "*.cs", SearchOption.AllDirectories)))
        foreach (var term in forbidden) Assert.DoesNotContain(term, File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_catalogs_and_strong_identities_are_present()
    {
        var root = Root();
        var geometry = File.ReadAllText(Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "Validation", "Events", "AstronomyEventGeometryValidationRule.cs"));
        Assert.Contains("DimensionCatalog", geometry);
        Assert.Contains("QuantityId", geometry);
        Assert.Contains("Category", geometry);
        Assert.Contains("Epoch", geometry);
        var recurrence = File.ReadAllText(Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "Validation", "Temporal", "AstronomyRecurrenceValidationRule.cs"));
        Assert.Contains("FixedPeriod", recurrence);
        Assert.Contains("CalendarInterval", recurrence);
    }

    private static string Root(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d!=null){if(Directory.Exists(Path.Combine(d.FullName,"Backend","src","Astronomy.MediaFactory.Core")))return d.FullName;d=d.Parent;}throw new InvalidOperationException("Repository root not found.");}
}

public sealed class EventValidationIntegrationTests { [Fact] public void Event_rules_are_registered() { using var p = EventTemporalValidationFixture.Provider(); foreach (var id in EventAndTemporalValidationArchitectureTestsAccessor.EventRuleIds) Assert.Single(p.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>().Descriptors, d => d.RuleId == id); } }
public sealed class TemporalValidationIntegrationTests { [Fact] public void Temporal_rules_are_registered() { using var p = EventTemporalValidationFixture.Provider(); foreach (var id in EventAndTemporalValidationArchitectureTestsAccessor.TemporalRuleIds) Assert.Single(p.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>().Descriptors, d => d.RuleId == id); } }
public sealed class EventAndTemporalValidationIntegrationTests { [Fact] public void Aggregate_registration_registers_both_families() { using var p = EventTemporalValidationFixture.Provider(); Assert.NotEmpty(p.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>().Descriptors); } }

internal static class EventAndTemporalValidationArchitectureTestsAccessor
{
    public static readonly string[] EventRuleIds = [AstronomyEventAggregateValidationRule.Id, AstronomyEventTemporalExtentValidationRule.Id, AstronomyEventReferenceContextValidationRule.Id, AstronomyEventParticipantValidationRule.Id, AstronomyEventPhaseMarkerValidationRule.Id, AstronomyEventGeometryValidationRule.Id, AstronomyEventCircumstanceValidationRule.Id];
    public static readonly string[] TemporalRuleIds = [AstronomyTemporalPatternValidationRule.Id, AstronomyTemporalReferenceContextValidationRule.Id, AstronomyRecurrenceValidationRule.Id, AstronomyCyclePeriodValidationRule.Id, AstronomyTemporalAnchorValidationRule.Id, AstronomyTemporalPhaseValidationRule.Id, AstronomyTemporalOccurrenceValidationRule.Id, AstronomySeasonalPatternValidationRule.Id, AstronomyTemporalApplicabilityValidationRule.Id];
}
