using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
namespace Astronomy.MediaFactory.Core.AstronomyDomain.Classification;
public sealed record ScientificClassification(string ClassificationSystem,string PrimaryClass,string? SecondaryClass=null,string? Subclass=null,string? Authority=null,DateOnly? EffectiveDate=null,string? Notes=null);
public sealed record AstronomyClassification(AstronomyDomainCategory DomainCategory,AstronomyFamilyKind FamilyKind,AstronomyEntityKind EntityKind,AstronomySubjectTemporality Temporality,ScientificClassification? ScientificClassification=null,string? ParentClassificationId=null,IReadOnlyList<string>? Tags=null,bool IsObservableFromEarth=true,bool IsNaturalObject=true,bool IsHumanMade=false){ public IReadOnlyList<string> Tags{get;init;}=Tags??[]; }
