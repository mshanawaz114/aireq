// MatchListService — read model for the Matches UI.
//
// Tenant-scoped automatically (Match carries the global query filter), joined
// with its Job, ordered best-score-first, with optional minScore/status
// filters. The ReasoningJson (if the LLM scorer has run) is deserialized
// in-memory into MatchResponse's summary/rationale/missingKeywords.
//
// Refs: AIRMVP1-206

using System.Text.Json;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Matches;

public sealed class MatchListService(AireqDbContext db)
{
    private const int MaxRows = 200;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<MatchResponse>> ListAsync(
        int? minScore, MatchStatus? status, CancellationToken ct)
    {
        var q = db.Matches.AsQueryable(); // tenant-scoped by global filter
        if (minScore is int ms) q = q.Where(m => m.Score >= ms);
        if (status is MatchStatus s) q = q.Where(m => m.Status == s);

        var rows = await q
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.Job.PostedAt)
            .Take(MaxRows)
            .Select(m => new Row(
                m.Id, m.JobId, m.Job.Title, m.Job.Company, m.Job.Location,
                m.Job.PostedAt, m.Score, m.Status, m.ReasoningJson))
            .ToListAsync(ct);

        return rows.Select(Map).ToList();
    }

    private static MatchResponse Map(Row r)
    {
        MatchReasoning? reasoning = null;
        if (!string.IsNullOrWhiteSpace(r.ReasoningJson))
        {
            try { reasoning = JsonSerializer.Deserialize<MatchReasoning>(r.ReasoningJson, JsonOpts); }
            catch (JsonException) { /* stored value somehow unparseable — treat as unreasoned */ }
        }

        return new MatchResponse(
            r.Id,
            r.JobId,
            r.Title,
            r.Company,
            r.Location,
            r.PostedAt,
            r.Score,
            r.Status.ToString(),
            Reasoned: reasoning is not null,
            Summary: reasoning?.Summary,
            Rationale: reasoning?.Rationale ?? Array.Empty<string>(),
            MissingKeywords: reasoning?.MissingKeywords ?? Array.Empty<string>());
    }

    // Flat projection shape pulled from the DB before in-memory JSON parsing.
    private sealed record Row(
        Guid Id, Guid JobId, string Title, string Company, string? Location,
        DateTimeOffset PostedAt, int Score, MatchStatus Status, string? ReasoningJson);
}
