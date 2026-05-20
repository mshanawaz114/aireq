// Consultant DTOs shared between API + web.
// Refs: AIRMVP1-106

namespace Aireq.Shared.Contracts;

public sealed record ConsultantResponse(
    Guid Id,
    string FullName,
    string? Headline,
    string? Location,
    string? WorkAuth,
    decimal? RateTargetUsdHourly,
    int ResumeCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Body for create (POST) and update (PUT) — same shape.</summary>
public sealed record UpsertConsultantRequest(
    string FullName,
    string? Headline,
    string? Location,
    string? WorkAuth,
    decimal? RateTargetUsdHourly);
