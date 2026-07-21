using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph;
using Microsoft.Extensions.DependencyInjection;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration;
internal static class AstronomyKnowledgeFoundationCapabilityCatalog
{ public static AstronomyKnowledgeFoundationCapabilitySnapshot CreateSnapshot()=>new(Descriptors());
 static IEnumerable<AstronomyKnowledgeFoundationCapabilityDescriptor> Descriptors(){
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.TypedKnowledge,"typed-knowledge.registry","typed-knowledge.registry","Typed payload registry","Built-in typed astronomy knowledge payload descriptors.",100,typeof(IAstronomyTypedPayloadRegistry),typeof(AstronomyTypedPayloadRegistry));
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.Validation,"validation.foundation","validation.registry","Knowledge validation registry","Foundation/domain validation rule registry.",100,typeof(IAstronomyKnowledgeValidationRuleRegistry),typeof(AstronomyKnowledgeValidationRuleRegistry));
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.Validation,"validation.typed-validator","validation.typed-validator","Typed knowledge validator","Frozen typed knowledge validation orchestrator.",200,typeof(IAstronomyTypedKnowledgeValidator),typeof(AstronomyTypedKnowledgeValidator));
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.CrossDomainValidation,"validation.cross-domain","validation.cross-domain.registry","Cross-domain validation registry","Cross-domain validation rule registry.",100,typeof(IAstronomyCrossDomainValidationRuleRegistry),typeof(AstronomyCrossDomainValidationRuleRegistry));
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.CrossDomainValidation,"validation.cross-domain.validator","validation.cross-domain.validator","Cross-domain validator","Frozen cross-domain validator.",200,typeof(IAstronomyCrossDomainValidator),typeof(AstronomyCrossDomainValidator));
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.GraphValidation,"validation.graph","validation.graph.registry","Graph validation registry","Graph validation rule registry.",100,typeof(IAstronomyKnowledgeGraphValidationRuleRegistry),typeof(AstronomyKnowledgeGraphValidationRuleRegistry));
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.GraphValidation,"validation.graph.validator","validation.graph.validator","Graph validator","Frozen graph validator.",200,typeof(IAstronomyKnowledgeGraphValidator),typeof(AstronomyKnowledgeGraphValidator));
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.Catalog,"catalog.metadata","catalog.metadata","Knowledge catalog","Immutable knowledge catalog.",100,typeof(IAstronomyKnowledgeCatalog),typeof(AstronomyKnowledgeCatalog));
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.Catalog,"catalog.builder","catalog.builder","Knowledge catalog builder","Catalog builder used at registration time.",200,typeof(IAstronomyKnowledgeCatalogBuilder),typeof(AstronomyKnowledgeCatalogBuilder));
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.QueryModel,"query.model","query.model.validator","Knowledge query validator","Frozen query model validator.",100,typeof(IAstronomyKnowledgeQueryValidator),typeof(AstronomyKnowledgeQueryValidator));
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.QueryExecution,"query.execution.catalog","query.execution.catalog","Catalog query engine","In-memory catalog query execution.",100,typeof(IAstronomyKnowledgeCatalogQueryEngine),typeof(AstronomyKnowledgeCatalogQueryEngine));
  yield return D(AstronomyKnowledgeFoundationCapabilityKind.QueryExecution,"query.execution.statement","query.execution.statement","Statement query engine","In-memory statement query execution.",200,typeof(IAstronomyKnowledgeStatementQueryEngine),typeof(AstronomyKnowledgeStatementQueryEngine));}
 static AstronomyKnowledgeFoundationCapabilityDescriptor D(AstronomyKnowledgeFoundationCapabilityKind k,string id,string code,string name,string desc,int order,Type c,Type i)=>new(new(k,id),code,name,desc,order,c,i,ServiceLifetime.Singleton); }
