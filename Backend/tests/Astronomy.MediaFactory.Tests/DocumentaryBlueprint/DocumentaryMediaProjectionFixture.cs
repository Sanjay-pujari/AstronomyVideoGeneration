using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class DocumentaryMediaProjectionFixture
{
    internal static DocumentaryMediaProjectionRequest Orion()=>Request("orion",DocumentaryAstronomyTopicFamily.Constellation,"Orion","ओरायन",["orion"],["betelgeuse","rigel"],["winter-sky"]);
    internal static DocumentaryMediaProjectionRequest Leo()=>Request("leo",DocumentaryAstronomyTopicFamily.Constellation,"Leo","सिंह",["leo"],["regulus","leo-triplet"],["spring-sky"]);
    internal static DocumentaryMediaProjectionRequest Conjunction()=>Request("mars-jupiter-conjunction",DocumentaryAstronomyTopicFamily.PlanetConjunction,"Mars Jupiter conjunction","मंगल बृहस्पति युति",["mars","jupiter"],[],["conjunction","planet-event"]);

    private static DocumentaryMediaProjectionRequest Request(string topicId,DocumentaryAstronomyTopicFamily family,string english,string hindi,string[] primary,string[] secondary,string[] tags)
    {
        var materialization=Materialization();
        var policy=new DocumentaryMediaProjectionPolicy(true,true,true,true,true,true,true,true,true,true,Enum.GetValues<DocumentaryMediaVariantType>(),Enum.GetValues<DocumentaryMediaLanguage>(),Enum.GetValues<DocumentaryVideoFormat>(),Enum.GetValues<DocumentaryAstronomyTopicFamily>(),4,12,3,4,30,300,15,120,500,34,2,"1.0");
        var metadata=new DocumentaryMediaProjectionMetadata(DocumentaryExportSpecificationFixture.Timestamp," projection fixture ","1.0",materialization.Metadata.CorrelationId);
        var profile=new DocumentaryAstronomyTopicProfile(topicId,family,english,english,hindi,primary,secondary,"northern-mid-latitudes","fixture-window",tags,metadata.CorrelationId);
        return new(materialization,policy,metadata,profile);
    }

    private static DocumentaryExportMaterializationRecord Materialization()
    {
        var specification=DocumentaryExportSpecificationFixture.Specification(2);
        var policy=new DocumentaryExportMaterializationPolicy(true,true,true,true,true,true,true,true,Enum.GetValues<DocumentaryExportPayloadType>(),Enum.GetValues<DocumentaryExportPayloadContentType>(),DocumentaryExportSerializerProfile.CanonicalWebJson,DocumentaryExportCharacterEncoding.Utf8,"1.0");
        var metadata=new DocumentaryExportMaterializationMetadata(DocumentaryExportSpecificationFixture.Timestamp," fixture materializer ","1.0",specification.Metadata.CorrelationId);
        var source=Assert.IsType<DocumentaryExportMaterializationRecord>(new DocumentaryExportMaterializer().Materialize(new(specification,policy,metadata,DocumentaryExportSerializerProfile.CanonicalWebJson)).MaterializationRecord);
        var facts=JsonSerializer.Serialize(new { semanticFacts=Facts() },new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var payloads=source.Payloads.Select((p,i)=>i==0?Copy(p,facts):p).ToArray();
        var manifest=new DocumentaryExportPayloadManifest(source.Manifest.ManifestId,source.MaterializationId,source.ExportSpecificationId,source.SerializerProfile,source.CharacterEncoding,payloads,payloads.Length,payloads.Sum(x=>x.Dependencies.Count),payloads.Sum(x=>x.CharacterCount),payloads.Sum(x=>x.ByteCount),"1.0",source.Metadata.CorrelationId);
        // O2.16 intentionally seals its canonical serializer output. The focused fixture clones
        // that already-certified record and substitutes O2.16-compatible semantic JSON without
        // weakening the production O2.16 validator.
        var record=(DocumentaryExportMaterializationRecord)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DocumentaryExportMaterializationRecord));
        foreach(var property in typeof(DocumentaryExportMaterializationRecord).GetProperties())
        {var field=typeof(DocumentaryExportMaterializationRecord).GetField($"<{property.Name}>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);field?.SetValue(record,property.Name switch{nameof(DocumentaryExportMaterializationRecord.Payloads)=>payloads,nameof(DocumentaryExportMaterializationRecord.Manifest)=>manifest,nameof(DocumentaryExportMaterializationRecord.TotalCharacterCount)=>payloads.Sum(x=>x.CharacterCount),nameof(DocumentaryExportMaterializationRecord.TotalByteCount)=>payloads.Sum(x=>x.ByteCount),_=>property.GetValue(source)});}
        return record;
    }

    private static DocumentaryExportPayload Copy(DocumentaryExportPayload p,string content)=>new(p.PayloadId,p.PayloadType,p.ContentType,p.SerializerProfile,p.CharacterEncoding,p.SourceItemId,p.ArtifactIdentity,p.ArtifactVersion,p.Sequence,p.Dependencies,content,Encoding.UTF8.GetBytes(content),content.Length,Encoding.UTF8.GetByteCount(content),p.CorrelationId);
    private static object[] Facts()=>
    [
        Fact("orion.identity",["orion"],["Constellation"],"Identity","Orion identity","Orion is a prominent constellation named for a hunter.","ओरायन एक प्रमुख तारामंडल है जिसका नाम एक शिकारी पर रखा गया है।",100,true,true,"Identity","ObjectPortrait",["orion"],["constellation"]),
        Fact("orion.location",["orion"],["Constellation"],"Location","Sky location","Orion straddles the celestial equator and is visible from both hemispheres.","ओरायन आकाशीय भूमध्य रेखा पर है और दोनों गोलार्धों से दिखाई देता है।",85,true,false,"Location","StarChart",["orion"],["winter-sky"]),
        Fact("orion.visibility",["orion"],["Constellation"],"Visibility","Winter visibility","Orion is best seen on clear winter evenings in the Northern Hemisphere.","उत्तरी गोलार्ध में साफ सर्दियों की शामों में ओरायन सबसे अच्छा दिखता है।",95,true,true,"Visibility","StarChart",["orion"],["winter-sky"]),
        Fact("orion.belt",["orion"],["Constellation"],"MajorFeature","Orion's Belt","Three aligned stars form Orion's Belt at the center of the constellation.","तीन सीध में स्थित तारे तारामंडल के केंद्र में ओरायन की बेल्ट बनाते हैं।",98,true,true,"MajorFeature","SkySimulation",["orion"],["constellation"]),
        Fact("orion.nebula",["orion"],["Constellation"],"Science","Orion Nebula","The Orion Nebula is a luminous stellar nursery below the Belt.","ओरायन नेबुला बेल्ट के नीचे एक चमकीली तारकीय जन्मस्थली है।",80,true,false,"Science","TelescopeView",["orion"],["constellation"]),
        Fact("orion.observe",["orion"],["Constellation"],"Observation","Finding Orion","Find the three Belt stars first, then trace bright Betelgeuse and Rigel.","पहले बेल्ट के तीन तारे खोजें, फिर चमकीले बेटेलजूस और राइजल को पहचानें।",90,true,true,"Observation","StarChart",["orion","betelgeuse","rigel"],["winter-sky"]),
        Fact("leo.identity",["leo"],["Constellation"],"Identity","Leo identity","Leo is the zodiac constellation of the lion.","सिंह राशि शेर का राशिचक्र तारामंडल है।",100,true,true,"Identity","ObjectPortrait",["leo"],["constellation"]),
        Fact("leo.visibility",["leo"],["Constellation"],"Visibility","Spring visibility","Leo is prominent during Northern Hemisphere spring evenings.","उत्तरी गोलार्ध की वसंत शामों में सिंह प्रमुख दिखाई देता है।",95,true,true,"Visibility","StarChart",["leo"],["spring-sky"]),
        Fact("leo.regulus",["leo"],["Constellation"],"MajorFeature","Regulus","Regulus is Leo's brightest star at the base of the Sickle.","रेगुलस सिंह का सबसे चमकीला तारा है और सिकल के आधार पर स्थित है।",98,true,true,"MajorFeature","ObjectPortrait",["leo","regulus"],["constellation"]),
        Fact("leo.sickle",["leo"],["Constellation"],"SupportingFeature","Sickle","The Sickle asterism outlines the lion's head and mane.","सिकल तारक समूह शेर के सिर और अयाल की रूपरेखा बनाता है।",82,true,false,"SupportingFeature","StarChart",["leo"],["constellation"]),
        Fact("leo.triplet",["leo"],["Constellation"],"Science","Leo Triplet","The Leo Triplet is a striking group of three galaxies.","लियो ट्रिपलेट तीन आकाशगंगाओं का आकर्षक समूह है।",80,true,false,"Science","TelescopeView",["leo","leo-triplet"],["constellation"]),
        Fact("leo.observe",["leo"],["Constellation"],"Observation","Finding Leo","Use the Big Dipper pointer stars to locate Leo and Regulus.","सिंह और रेगुलस खोजने के लिए सप्तर्षि के संकेतक तारों का उपयोग करें।",90,true,true,"Observation","StarChart",["leo","regulus"],["spring-sky"]),
        Fact("conjunction.identity",["mars-jupiter-conjunction"],["PlanetConjunction"],"Event identity","Conjunction identity","Mars and Jupiter form a close apparent conjunction.","मंगल और बृहस्पति एक निकट दृश्य युति बनाते हैं।",100,true,true,"Identity","OrbitalDiagram",["mars","jupiter"],["conjunction"]),
        Fact("conjunction.objects",["mars-jupiter-conjunction"],["PlanetConjunction"],"Objects involved","Objects involved","The two objects are reddish Mars and bright Jupiter.","दो पिंड लाल मंगल और चमकीला बृहस्पति हैं।",95,true,false,"Context","ObjectPortrait",["mars","jupiter"],["planet-event"]),
        Fact("conjunction.window",["mars-jupiter-conjunction"],["PlanetConjunction"],"Date or time window","Time window","The closest approach occurs before dawn on August fourteenth.","निकटतम मिलन चौदह अगस्त को भोर से पहले होगा।",95,true,false,"Visibility","Timeline",["mars","jupiter"],["planet-event"]),
        Fact("conjunction.separation",["mars-jupiter-conjunction"],["PlanetConjunction"],"Angular separation","Angular separation","The planets appear only zero point three degrees apart.","दोनों ग्रह केवल शून्य दशमलव तीन डिग्री दूर दिखाई देंगे।",99,true,true,"MajorFeature","OrbitalDiagram",["mars","jupiter"],["conjunction"]),
        Fact("conjunction.direction",["mars-jupiter-conjunction"],["PlanetConjunction"],"Direction","Visibility and direction","Look low in the eastern sky during morning twilight.","सुबह के धुंधलके में पूर्वी आकाश में नीचे देखें।",97,true,true,"Visibility","StarChart",["mars","jupiter"],["planet-event"]),
        Fact("conjunction.observe",["mars-jupiter-conjunction"],["PlanetConjunction"],"Observation guidance","Observation guidance","Use unaided eyes or binoculars from a clear eastern horizon.","साफ पूर्वी क्षितिज से नंगी आंखों या दूरबीन का उपयोग करें।",96,true,true,"Observation","StarChart",["mars","jupiter"],["planet-event"])
    ];
    private static object Fact(string factId,string[] topicIds,string[] topicFamilies,string category,string key,string valueEnglish,string valueHindi,int importance,bool supportsLong,bool supportsShort,string preferredSceneRole,string preferredVisualType,string[] subjectIds,string[] knowledgeTags)=>new {factId,topicIds,topicFamilies,category,key,valueEnglish,valueHindi,importance,supportsLong,supportsShort,preferredSceneRole,preferredVisualType,subjectIds,knowledgeTags};
}
