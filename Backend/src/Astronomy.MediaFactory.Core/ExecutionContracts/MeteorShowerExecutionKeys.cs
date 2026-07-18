namespace Astronomy.MediaFactory.Core.ExecutionContracts;

/// <summary>Stable Meteor Shower execution-contract keys. These are declarative contract vocabulary, not runtime service identifiers.</summary>
public static class MeteorShowerExecutionKeys
{
    public const string FamilyId = "MeteorShower";
    public const string ContractVersion = "MeteorShowerExecutionContract-v1";

    public static class Inputs
    {
        public const string EventIdentity = "eventIdentity";
        public const string EventStart = "eventStart";
        public const string EventEnd = "eventEnd";
        public const string ObserverLocation = "observerLocation";
        public const string Language = "language";
        public const string Format = "format";
        public const string ContentStrategy = "contentStrategy";
    }

    public static class Semantic
    {
        public const string MeteorActivity = "MeteorActivity";
        public const string Radiant = "Radiant";
        public const string PeakWindow = "PeakWindow";
    }

    public static class Projection
    {
        public const string RadiantFact = "RadiantFact";
        public const string PeakWindowFact = "PeakWindowFact";
        public const string RadiantRule = "V1Projection.MeteorActivity.Radiant";
        public const string PeakWindowRule = "V1Projection.MeteorActivity.PeakWindow";
    }

    public static class Artifacts
    {
    }

    public static class Rules
    {
        public const string FamilyStrategyConsistency = "meteor.rule.familyStrategyConsistency";
        public const string ActivityObserved = "meteor.rule.activityObserved";
        public const string SemanticLifecycleComplete = "meteor.rule.semanticLifecycleComplete";
        public const string RequiredFactsRetained = "meteor.rule.requiredFactsRetained";
    }

    public static class Conditions
    {
        public const string LocalizedOutput = "meteor.condition.localizedOutput";
        public const string MultiFormatOutput = "meteor.condition.multiFormatOutput";
    }
}
