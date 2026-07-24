using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;

public static class SemanticCapabilityVocabularyV1
{
    public const string EventIdentity = nameof(EventIdentity);
    public const string EventWindow = nameof(EventWindow);
    public const string AstronomicalObjects = nameof(AstronomicalObjects);
    public const string SecondaryAstronomicalObjects = nameof(SecondaryAstronomicalObjects);
    public const string AngularSeparation = nameof(AngularSeparation);
    public const string ObservationDirection = nameof(ObservationDirection);
    public const string ObservationLocation = nameof(ObservationLocation);
    public const string ObservationConditions = nameof(ObservationConditions);
    public const string ObservationEquipment = nameof(ObservationEquipment);
    public const string MeteorActivity = nameof(MeteorActivity);
    public const string FullMoonObservation = nameof(FullMoonObservation);
    public const string EclipseCircumstances = nameof(EclipseCircumstances);
    public const string OccultationContacts = nameof(OccultationContacts);
    public const string ObjectKnowledge = nameof(ObjectKnowledge);
    public const string DomainScientificKnowledge = nameof(DomainScientificKnowledge);
    public const string CulturalContext = nameof(CulturalContext);
    public const string CulturalNameContext = nameof(CulturalNameContext);
    public const string EditorialContext = nameof(EditorialContext);
    public const string SafetyGuidance = nameof(SafetyGuidance);

    public static readonly IReadOnlyList<string> CanonicalIds =
    [
        EventIdentity, EventWindow, AstronomicalObjects, SecondaryAstronomicalObjects, AngularSeparation,
        ObservationDirection, ObservationLocation, ObservationConditions, ObservationEquipment, MeteorActivity,
        FullMoonObservation, EclipseCircumstances, OccultationContacts, ObjectKnowledge, DomainScientificKnowledge,
        CulturalContext, CulturalNameContext, EditorialContext, SafetyGuidance
    ];

    public static SemanticCapabilityId Id(string value) => new(value);
}
