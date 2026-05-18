namespace Aireq.Shared.Contracts;

/// <summary>
/// Stable, versioned health-check payload. Consumed by the web app to render
/// a status badge on the dashboard.
///
/// Keep this DTO additive-only — front-end will be backwards-compatible
/// for at least one minor version.
/// </summary>
public sealed record HealthResponse(
    string Status,
    string Service,
    string Version,
    IReadOnlyDictionary<string, bool>? DependenciesHealthy,
    DateTimeOffset Timestamp);
