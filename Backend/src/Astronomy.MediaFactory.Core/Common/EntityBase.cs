namespace Astronomy.MediaFactory.Core.Common;
public abstract class EntityBase
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedUtc { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedUtc { get; protected set; }
    public void Touch() => UpdatedUtc = DateTimeOffset.UtcNow;
}
