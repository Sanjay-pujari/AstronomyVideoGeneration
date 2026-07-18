using System.Collections.Immutable;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public static class MeteorShowerExecutionContractFactory
{
    public static FamilyExecutionContract Create() => new(
        FamilyId: MeteorShowerExecutionKeys.FamilyId,
        ContractVersion: MeteorShowerExecutionKeys.ContractVersion,
        DisplayName: "Meteor Shower",
        Description: "Declarative shadow-mode execution requirements for the astronomy Meteor Shower family.",
        Aliases: ImmutableArray.Create("Meteor", "MeteorShowerPeak", "METEOR_SHOWER", "meteor shower"),
        InputRequirements: ImmutableArray.Create(
            Input("meteor.input.eventIdentity", MeteorShowerExecutionKeys.Inputs.EventIdentity, "Stable event identity, shower name, or external event identifier."),
            Input("meteor.input.eventStart", MeteorShowerExecutionKeys.Inputs.EventStart, "UTC start of the meteor-shower execution window."),
            Input("meteor.input.eventEnd", MeteorShowerExecutionKeys.Inputs.EventEnd, "UTC end of the meteor-shower execution window."),
            Input("meteor.input.observerLocation", MeteorShowerExecutionKeys.Inputs.ObserverLocation, "Observer region or location used for viewing guidance."),
            Input("meteor.input.language", MeteorShowerExecutionKeys.Inputs.Language, "Requested narration/output language."),
            Input("meteor.input.format", MeteorShowerExecutionKeys.Inputs.Format, "Requested production format or output format.", FamilyRequirementLevel.Optional),
            Input("meteor.input.contentStrategy", MeteorShowerExecutionKeys.Inputs.ContentStrategy, "Observed content strategy for family-strategy consistency checks; the contract does not require a specific strategy literal.", FamilyRequirementLevel.Optional)),
        SemanticRequirements: ImmutableArray.Create(
            Semantic("meteor.semantic.meteorActivity", MeteorShowerExecutionKeys.Semantic.MeteorActivity, "Canonical meteor activity containing observed shower activity, radiant, peak window, rate, parent-body, and viewing guidance when available."),
            Semantic("meteor.semantic.radiant", MeteorShowerExecutionKeys.Semantic.Radiant, "Radiant direction or radiant constellation retained as contract-owned semantic capability."),
            Semantic("meteor.semantic.peakWindow", MeteorShowerExecutionKeys.Semantic.PeakWindow, "Peak viewing window retained as contract-owned semantic capability.")),
        ProjectionRequirements: ImmutableArray.Create(
            Projection("meteor.projection.radiantFact", MeteorShowerExecutionKeys.Semantic.MeteorActivity, MeteorShowerExecutionKeys.Projection.RadiantFact, MeteorShowerExecutionKeys.Projection.RadiantRule, "MeteorActivity-to-radiant compatibility fact projection."),
            Projection("meteor.projection.peakWindowFact", MeteorShowerExecutionKeys.Semantic.MeteorActivity, MeteorShowerExecutionKeys.Projection.PeakWindowFact, MeteorShowerExecutionKeys.Projection.PeakWindowRule, "MeteorActivity-to-peak-window compatibility fact projection.")),
        ArtifactRequirements: ImmutableArray<FamilyPhaseArtifactRequirement>.Empty,
        ValidationRequirements: ImmutableArray.Create(
            Rule("meteor.validation.familyStrategyConsistency", MeteorShowerExecutionKeys.Rules.FamilyStrategyConsistency, FamilyValidationBoundary.PreExecution, FamilyValidationSeverity.Warning, "Observed family identity and content strategy remain consistent without requiring a brittle strategy literal."),
            Rule("meteor.validation.activityObserved", MeteorShowerExecutionKeys.Rules.ActivityObserved, FamilyValidationBoundary.SemanticResolution, FamilyValidationSeverity.Blocking, "MeteorActivity is observed by the semantic lifecycle before projection."),
            Rule("meteor.validation.semanticLifecycleComplete", MeteorShowerExecutionKeys.Rules.SemanticLifecycleComplete, FamilyValidationBoundary.PostExecution, FamilyValidationSeverity.Blocking, "MeteorActivity source, resolution, projection, and retention observations are complete."),
            Rule("meteor.validation.requiredFactsRetained", MeteorShowerExecutionKeys.Rules.RequiredFactsRetained, FamilyValidationBoundary.PostExecution, FamilyValidationSeverity.Blocking, "Required radiant and peak-window facts are retained when observable.")),
        Metadata: ImmutableDictionary<string, string>.Empty
            .Add("frameworkMilestone", "2C")
            .Add("validationMode", "shadow")
            .Add("contractAuthority", "production")
            .Add("contractRevision", "2C-A"),
        Status: FamilyRequirementStatus.Active);

    private static FamilyInputRequirement Input(string id, string key, string description, FamilyRequirementLevel level = FamilyRequirementLevel.Required) => new(id, key, description, level);
    private static FamilySemanticRequirement Semantic(string id, string capability, string description) => new(id, capability, description, MinimumEvidenceStrength: "Observed");
    private static FamilyProjectionRequirement Projection(string id, string source, string target, string rule, string description) => new(id, source, target, description, ProjectionRuleId: rule);
    private static FamilyValidationRequirement Rule(string id, string rule, FamilyValidationBoundary boundary, FamilyValidationSeverity severity, string description) => new(id, rule, boundary, severity, description);
}
