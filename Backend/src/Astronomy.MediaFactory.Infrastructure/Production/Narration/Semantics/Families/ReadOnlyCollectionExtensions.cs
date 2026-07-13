using System.Collections.Generic;
using System.Linq;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

internal static class ReadOnlyCollectionExtensions
{
    public static IReadOnlyList<T> AsReadOnlyList<T>(this IEnumerable<T> items)
    {
        return items as IReadOnlyList<T> ?? items.ToArray();
    }
}
