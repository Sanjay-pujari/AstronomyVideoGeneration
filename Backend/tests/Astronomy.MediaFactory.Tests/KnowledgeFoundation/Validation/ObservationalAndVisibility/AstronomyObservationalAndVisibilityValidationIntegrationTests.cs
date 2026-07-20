using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Orbital;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Positional;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Visibility;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ObservationalAndVisibility;

public sealed class AstronomyObservationalAndVisibilityValidationIntegrationTests
{
    private static readonly string[] Task24DIds = [AstronomyObservationContextValidationRule.Id, AstronomyObservationConditionsValidationRule.Id, AstronomyObservationalQuantityValidationRule.Id, AstronomyHorizontalCoordinatesValidationRule.Id, AstronomyVisibilityContextValidationRule.Id, AstronomyVisibilityWindowValidationRule.Id, AstronomyVisibilityAssessmentValidationRule.Id, AstronomyVisibilityPeakValidationRule.Id];
    private static ServiceProvider P(){var s=new ServiceCollection();s.AddAstronomyTypedKnowledgePayloadDescriptors();s.AddAstronomyObservationalAndVisibilityValidation().AddAstronomyObservationalAndVisibilityValidation();return s.BuildServiceProvider();}
    [Fact] public void Aggregate_registration_is_idempotent(){using var p=P();foreach(var id in Task24DIds)Assert.Single(p.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>().Descriptors,d=>d.RuleId==id);}
    [Fact] public void Aggregate_registration_contains_all_eight_rules_exactly_once(){using var p=P();Assert.Equal(8,p.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>().Descriptors.Count(d=>Task24DIds.Contains(d.RuleId)));}
    [Fact] public void Observation_payload_runs_only_observational_rules(){using var p=P();var app=p.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>().GetApplicable(typeof(AstronomyObservationConditionsPayload),AstronomyKnowledgeDomain.Observational,AstronomyKnowledgePayloadFamily.ObservationCondition).Select(d=>d.RuleId);Assert.Equal(Task24DIds.Take(4),app);}
    [Fact] public void Visibility_payload_runs_only_visibility_rules(){using var p=P();var app=p.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>().GetApplicable(typeof(AstronomyVisibilityWindowsPayload),AstronomyKnowledgeDomain.Observational,AstronomyKnowledgePayloadFamily.VisibilityWindow).Select(d=>d.RuleId);Assert.Equal(Task24DIds.Skip(4),app);}
    [Fact] public void Unrelated_payload_does_not_run_task_2_4d_rules(){using var p=P();var app=p.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>().GetApplicable(typeof(AstronomyObservationConditionsPayload),AstronomyKnowledgeDomain.Observational,AstronomyKnowledgePayloadFamily.AstronomicalEvent).Select(d=>d.RuleId);Assert.DoesNotContain(app,Task24DIds.Contains);}
    [Fact] public void Final_issue_ordering_is_owned_by_validation_result(){var result=new AstronomyKnowledgeValidationResult([new("z.rule",AstronomyKnowledgeValidationSeverity.Warning,"z","$.z","observational.context.integrity",AstronomyKnowledgeDomain.Observational,AstronomyKnowledgePayloadFamily.ObservationCondition),new("a.rule",AstronomyKnowledgeValidationSeverity.Error,"a","$.a","observational.context.integrity",AstronomyKnowledgeDomain.Observational,AstronomyKnowledgePayloadFamily.ObservationCondition)]);Assert.Equal(["a.rule","z.rule"],result.Issues.Select(i=>i.Code));}
    [Fact] public void Existing_task_2_4a_to_2_4c_registration_remains_valid(){var s=new ServiceCollection();s.AddAstronomyTypedKnowledgePayloadDescriptors();s.AddAstronomyClassificationValidation();s.AddAstronomyPhysicalValidation();s.AddAstronomyOrbitalAndPositionalValidation();using var p=s.BuildServiceProvider();Assert.NotEmpty(p.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>().Descriptors);}
}
