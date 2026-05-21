// WaitlistService — dedup + persist for marketing waitlist signups.
//
// Pulled out of the endpoint so the join logic is unit-testable on EF InMemory.
// Email normalization + validation stays in the endpoint; this owns the
// idempotent insert.
//
// Refs: AIRMVP1-405

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aireq.Api.Marketing;

public sealed class WaitlistService(AireqDbContext db)
{
    /// <summary>Join the waitlist. <paramref name="email"/> must already be
    /// normalized (trimmed + lower-cased). Idempotent on email.</summary>
    public async Task<WaitlistResponse> JoinAsync(
        string email, string? persona, string? source, CancellationToken ct)
    {
        var exists = await db.WaitlistEntries.AnyAsync(w => w.Email == email, ct);
        if (exists) return new WaitlistResponse(Joined: true, AlreadyJoined: true);

        db.WaitlistEntries.Add(new WaitlistEntry
        {
            Email = email,
            Persona = Trim(persona, 64),
            Source = Trim(source, 128),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Race: another request inserted the same email first.
            return new WaitlistResponse(Joined: true, AlreadyJoined: true);
        }

        return new WaitlistResponse(Joined: true, AlreadyJoined: false);
    }

    private static string? Trim(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim() is var t && t.Length <= max ? t : t[..max];
}
