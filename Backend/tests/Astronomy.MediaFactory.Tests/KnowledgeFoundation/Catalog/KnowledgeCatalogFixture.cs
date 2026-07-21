using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Microsoft.Extensions.DependencyInjection;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Catalog;
public static class KnowledgeCatalogFixture{ public static IAstronomyKnowledgeCatalog Catalog(){ var services=new ServiceCollection().AddAstronomyKnowledgeCatalog(); return services.BuildServiceProvider().GetRequiredService<IAstronomyKnowledgeCatalog>(); } public static AstronomyKnowledgeCatalogEntry Entry(AstronomyKnowledgeCatalogEntryKind k=AstronomyKnowledgeCatalogEntryKind.Domain,string v="a",string c="a",int o=0)=>new(new(k,v),c,"Name","Description",o); }
