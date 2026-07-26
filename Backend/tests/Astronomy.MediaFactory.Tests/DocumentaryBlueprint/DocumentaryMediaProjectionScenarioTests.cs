using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionScenarioTests
{
 [Theory] [InlineData("orion","Orion","Belt","Nebula","winter","Betelgeuse")] [InlineData("leo","Leo","Regulus","Sickle","Triplet","spring")] [InlineData("conjunction","Mars","Jupiter","August","degrees","eastern")]
 public void Certified_scenarios_project_fixture_facts_into_all_variants(string scenario,params string[] facts)
 {var r=scenario=="orion"?DocumentaryMediaProjectionFixture.Orion():scenario=="leo"?DocumentaryMediaProjectionFixture.Leo():DocumentaryMediaProjectionFixture.Conjunction();var p=DocumentaryMediaProjectionFixture.Complete(r);Assert.Equal(4,p.Variants.Count);var text=string.Join(' ',p.Variants.Where(x=>x.Language==DocumentaryMediaLanguage.English).SelectMany(x=>x.Scenes).SelectMany(x=>x.Narration).Select(x=>x.Text));Assert.All(facts,x=>Assert.Contains(x,text,StringComparison.OrdinalIgnoreCase));}
}
