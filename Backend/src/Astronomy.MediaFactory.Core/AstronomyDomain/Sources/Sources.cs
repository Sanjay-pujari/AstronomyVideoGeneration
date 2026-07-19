namespace Astronomy.MediaFactory.Core.AstronomyDomain.Sources;
public enum AstronomySourceType { ScientificAgency, PeerReviewedPaper, Observatory, AstronomicalCatalog, GovernmentPublication, AcademicBook, HistoricalPrimarySource, HistoricalSecondarySource, CulturalPrimarySource, CulturalSecondarySource, EducationalReference, MissionArchive, GeneralReference, InternalCuratedKnowledge }
public enum SourceReliability { Unknown, Low, Moderate, High, Authoritative }
public enum SourceAuthorityLevel { Reference, Supporting, Primary, Official }
public sealed record AstronomySourceReference(string SourceId,AstronomySourceType SourceType,string? Publisher=null,string? Title=null,string? Author=null,Uri? Url=null,string? Citation=null,DateOnly? PublishedDate=null,DateTimeOffset? RetrievedUtc=null,string? License=null,string? LanguageCode=null,SourceReliability Reliability=SourceReliability.Unknown,SourceAuthorityLevel AuthorityLevel=SourceAuthorityLevel.Reference,string? Notes=null);
