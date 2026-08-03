using System.Globalization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Conservative, deterministic classifier. It never treats mere difference as authority precedence.</summary>
public sealed class Phase7KnowledgeMergeClassifier : IPhase7KnowledgeMergeClassifier
{
    private static readonly Regex Number = new(@"[-+]?\d+(?:\.\d+)?", RegexOptions.Compiled);
    private static readonly string[] ScopeWords = ["utc", "date", "time", "location", "latitude", "longitude", "altitude", "visibility", "from ", "on "];

    public Phase7KnowledgeMergeResult Classify(Phase7KnowledgeMergeRequest request)
    {
        var evergreen=Normalize(request.EvergreenCandidate.Text); var @event=Normalize(request.EventCandidate.Text);
        if (evergreen==@event) return Result(Phase7KnowledgeMergeClassification.Equivalent,"Normalized certified values are equal.");
        if (HasExplicitDifferentScope(request)) return Result(Phase7KnowledgeMergeClassification.Incomparable,"Candidates have distinct explicit scopes.");
        if ((@event.Contains(evergreen,StringComparison.Ordinal) || SharesCore(evergreen,@event)) && ScopeWords.Any(@event.Contains))
            return Result(Phase7KnowledgeMergeClassification.EventSpecificSpecialization,"Event authority adds execution-specific timing, location, geometry, or visibility scope.");
        var en=Numeric(evergreen); var ev=Numeric(@event);
        if (en is not null && ev is not null)
        {
            if (en.Value.Value != ev.Value.Value) return Block("Numeric values conflict under the same approved-field scope.");
            if (ev.Value.Decimals>en.Value.Decimals) return Result(Phase7KnowledgeMergeClassification.EventMorePrecise,"Event value expresses greater numeric precision.");
            if (en.Value.Decimals>ev.Value.Decimals) return Result(Phase7KnowledgeMergeClassification.EvergreenMorePrecise,"Evergreen value expresses greater numeric precision.");
        }
        if (IsGeneric(evergreen) && !IsGeneric(@event)) return Result(Phase7KnowledgeMergeClassification.EventMorePrecise,"Event value is more specific than general evergreen authority.");
        if (IsGeneric(@event) && !IsGeneric(evergreen)) return Result(Phase7KnowledgeMergeClassification.EvergreenMorePrecise,"Evergreen value is more specific than generic event data.");
        return Block("Candidates cannot both be accepted under the same semantic identity and scope.");
    }

    private static Phase7KnowledgeMergeResult Result(Phase7KnowledgeMergeClassification c,string reason)=>new(c,reason,[],[]);
    private static Phase7KnowledgeMergeResult Block(string reason)=>new(Phase7KnowledgeMergeClassification.Contradictory,reason,[],["P7KNOWLEDGE_CONTRADICTION"]);
    private static string Normalize(string value)=>Regex.Replace(value.Trim().ToLowerInvariant(),@"\s+"," ").TrimEnd('.');
    private static bool SharesCore(string a,string b)=>a.Split(' ',StringSplitOptions.RemoveEmptyEntries).Intersect(b.Split(' ',StringSplitOptions.RemoveEmptyEntries)).Count()>=Math.Min(4,a.Split(' ').Length);
    private static bool HasExplicitDifferentScope(Phase7KnowledgeMergeRequest r)=>r.DependencyMetadata.TryGetValue("evergreenScope",out var a)&&r.DependencyMetadata.TryGetValue("eventScope",out var b)&&!string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    private static bool IsGeneric(string value)=>new[]{"general","typically","usually","approximately","varies","may be"}.Any(value.Contains);
    private static (decimal Value,int Decimals)? Numeric(string value){var m=Number.Match(value);if(!m.Success||!decimal.TryParse(m.Value,NumberStyles.Number,CultureInfo.InvariantCulture,out var n))return null;var dot=m.Value.IndexOf('.');return(n,dot<0?0:m.Value.Length-dot-1);}
}
