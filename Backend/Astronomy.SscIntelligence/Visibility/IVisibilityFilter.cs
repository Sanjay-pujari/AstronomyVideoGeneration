using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Visibility;

public interface IVisibilityFilter
{
    (IReadOnlyList<SkyObjectPosition> Visible, IReadOnlyList<string> RemovedReasons) Filter(
        IReadOnlyList<SkyObjectPosition> objects,
        VisibilityRules rules,
        double? sunAltitudeDeg = null);
}
