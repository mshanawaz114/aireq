namespace Aireq.Api.Data.Common;

/// <summary>
/// Marker interface for entities whose CreatedAt / UpdatedAt should be set
/// automatically by AireqDbContext.SaveChangesAsync. Avoids litterring every
/// service with "now = DateTimeOffset.UtcNow; entity.UpdatedAt = now" calls.
/// </summary>
public interface ITimestamped
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}
