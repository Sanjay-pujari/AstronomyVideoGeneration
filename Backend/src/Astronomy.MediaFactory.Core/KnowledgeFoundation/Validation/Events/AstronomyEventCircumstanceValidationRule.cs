using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Events;
public sealed class AstronomyEventCircumstanceValidationRule:EventRuleBase{public const string Id="event.circumstances.identity"; public override string RuleId=>Id; public override int Order=>700; protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyEventPayload p,AstronomyKnowledgeValidationContext c){if(p.Event.Circumstances.GroupBy(x=>x.CircumstanceId).Any(g=>g.Count()>1)) yield return Issue(AstronomyEventValidationCodes.CircumstanceDuplicate,"Circumstances must be unique by circumstance ID.","$.event.circumstances",RuleId);}}
