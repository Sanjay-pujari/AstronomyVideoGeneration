using System.Text.Json.Serialization;
namespace Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
[JsonConverter(typeof(JsonStringEnumConverter))] public enum AstronomyDomainCategory { TransientEvent, EvergreenSky, SolarSystem, DeepSky, Observation, CulturalAstronomy, AstronomyHistory, SpaceExploration, AstronomyScience }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum AstronomyFamilyKind { Event, CelestialObject, PlanetarySystem, SkyPattern, ObservationTopic, CulturalTopic, ScientificTopic, HistoricalTopic, MissionTopic }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum AstronomyEntityKind { Event, Constellation, Asterism, Star, BinaryStar, VariableStar, Planet, Moon, DwarfPlanet, Comet, Asteroid, Meteoroid, Galaxy, Nebula, OpenCluster, GlobularCluster, SupernovaRemnant, BlackHole, Exoplanet, SolarSystemBody, ObservationGuide, EquipmentTopic, CulturalTradition, HistoricalSubject, SpaceMission, ScientificConcept }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum AstronomySubjectTemporality { Transient, Periodic, Seasonal, Persistent, Historical, Predictive }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum AstronomyKnowledgeDomain { Identity, Classification, Observation, Science, Culture, History, Education, Visualization, Safety, SourceAttribution }
