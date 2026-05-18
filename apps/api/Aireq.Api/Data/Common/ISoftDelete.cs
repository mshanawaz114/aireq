namespace Aireq.Api.Data.Common;

/// <summary>
/// Marker interface for entities that support soft-delete via DeletedAt. The
/// DbContext applies a global query filter that excludes soft-deleted rows
/// by default; use <c>.IgnoreQueryFilters()</c> for admin / audit reads.
/// </summary>
public interface ISoftDelete
{
    DateTimeOffset? DeletedAt { get; set; }
}
