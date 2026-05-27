using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Camera;

public sealed class DynamicFovCalculator : IDynamicFovCalculator
{
    public CameraSolution Calculate(IReadOnlyList<SkyObjectPosition> visibleObjects, IReadOnlyList<SkyObjectPosition> primaryTargets, IReadOnlyList<SkyObjectPosition> secondaryTargets, IReadOnlyList<SkyObjectPosition> contextTargets, double centerAltitudeDeg, double centerAzimuthDeg, VisibilityRules rules, SceneIntentType intent)
    {
        ArgumentNullException.ThrowIfNull(visibleObjects);
        if (visibleObjects.Count == 0) throw new ArgumentException("At least one visible object is required.", nameof(visibleObjects));
        var scoped = intent switch
        {
            SceneIntentType.HeroShot or SceneIntentType.CloseUp when primaryTargets.Count > 0 => primaryTargets,
            SceneIntentType.WideNight => visibleObjects,
            SceneIntentType.Grouping => primaryTargets.Concat(secondaryTargets).ToList(),
            SceneIntentType.Educational => primaryTargets.Concat(secondaryTargets).Concat(contextTargets).ToList(),
            _ => visibleObjects
        };
        if (scoped.Count == 0) scoped = visibleObjects;
        var spread = CalculateAngularSpread(scoped);
        var fov = visibleObjects.Count == 1 ? Single(intent) : Clamp(intent, spread * Pad(intent));
        return new CameraSolution(centerAltitudeDeg, centerAzimuthDeg, fov, spread > rules.MaximumGroupSpreadDeg, spread);
    }
    static double Single(SceneIntentType i)=>i switch{SceneIntentType.HeroShot=>25,SceneIntentType.CloseUp=>18,SceneIntentType.WideNight=>55,SceneIntentType.Educational=>45,_=>35};
    static double Pad(SceneIntentType i)=>i switch{SceneIntentType.HeroShot=>1.35,SceneIntentType.WideNight=>1.85,SceneIntentType.Educational=>1.7,SceneIntentType.CloseUp=>1.25,_=>1.55};
    static double Clamp(SceneIntentType i,double v)=>i switch{SceneIntentType.CloseUp=>Math.Clamp(v,12,35),SceneIntentType.HeroShot=>Math.Clamp(v,18,55),SceneIntentType.WideNight=>Math.Clamp(v,45,95),SceneIntentType.Educational=>Math.Clamp(v,35,90),_=>Math.Clamp(v,25,75)};
    private static double CalculateAngularSpread(IReadOnlyList<SkyObjectPosition> objects){double m=0;for(var i=0;i<objects.Count;i++)for(var j=i+1;j<objects.Count;j++){var s=AngularSeparationDeg(objects[i],objects[j]);if(s>m)m=s;}return m;}
    private static double AngularSeparationDeg(SkyObjectPosition a, SkyObjectPosition b){var alt1=DegToRad(a.AltitudeDeg);var az1=DegToRad(a.AzimuthDeg);var alt2=DegToRad(b.AltitudeDeg);var az2=DegToRad(b.AzimuthDeg);var c=Math.Sin(alt1)*Math.Sin(alt2)+Math.Cos(alt1)*Math.Cos(alt2)*Math.Cos(az1-az2);c=Math.Clamp(c,-1,1);return RadToDeg(Math.Acos(c));}
    static double DegToRad(double d)=>d*Math.PI/180.0; static double RadToDeg(double r)=>r*180.0/Math.PI;
}
