using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Visibility;

public sealed class VisibilityFilter : IVisibilityFilter
{
    public (IReadOnlyList<SkyObjectPosition> Visible, IReadOnlyList<string> RemovedReasons) Filter(
        IReadOnlyList<SkyObjectPosition> objects,
        VisibilityRules rules,
        double? sunAltitudeDeg = null)
    {
        var visible = new List<SkyObjectPosition>();
        var removed = new List<string>();

        foreach (var obj in objects)
        {
            if (obj.AltitudeDeg < rules.MinimumObjectAltitudeDeg)
            {
                removed.Add($"{obj.Name}: altitude {obj.AltitudeDeg:F1} below minimum {rules.MinimumObjectAltitudeDeg:F1}");
                continue;
            }

            if (obj.Magnitude > rules.MaximumMagnitude)
            {
                removed.Add($"{obj.Name}: magnitude {obj.Magnitude:F1} above maximum {rules.MaximumMagnitude:F1}");
                continue;
            }

            visible.Add(obj);
        }

        return (visible, removed);
    }
}
