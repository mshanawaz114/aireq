namespace Aireq.Shared.Contracts;

/// <summary>
/// Snapshot of the DB schema state surfaced to the dashboard's schema tile.
/// Additive-only contract.
/// </summary>
public sealed record DbStatusResponse(
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    IReadOnlyDictionary<string, long> RowCounts,
    DateTimeOffset Timestamp);
