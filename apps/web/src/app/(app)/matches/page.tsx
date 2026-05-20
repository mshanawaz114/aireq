"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { api, ApiError, type Match, type AtsAnalysis } from "@/lib/api";

// Job Matches — the real list (replaces the AIRMVP1-106 placeholder).
// Shows LLM-scored matches with rationale + missing-keyword chips, a min-score
// filter, and a Tailor & apply CTA.
//
// Refs: AIRMVP1-206
export default function MatchesPage() {
  const [matches, setMatches] = useState<Match[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [minScore, setMinScore] = useState(50);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setMatches(await api.matches.list({ minScore }));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't load matches.");
    } finally {
      setLoading(false);
    }
  }, [minScore]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="p-8">
      <header className="mb-6 flex items-end justify-between gap-4">
        <div>
          <p className="text-xs text-slate-500">Workspace</p>
          <h1 className="text-xl font-semibold">Job Matches</h1>
        </div>
        <div className="flex items-center gap-2">
          <label htmlFor="minScore" className="text-xs text-slate-400">
            Min score
          </label>
          <select
            id="minScore"
            value={minScore}
            onChange={(e) => setMinScore(Number(e.target.value))}
            className="rounded-md border border-ink-700 bg-ink-900 px-2 py-1 text-sm text-slate-200"
          >
            {[0, 50, 60, 70, 80, 90].map((s) => (
              <option key={s} value={s}>
                {s}+
              </option>
            ))}
          </select>
        </div>
      </header>

      {error && (
        <div
          role="alert"
          className="mb-6 rounded-md border border-bad-500/40 bg-bad-500/10 px-3 py-2 text-xs text-bad-500"
        >
          {error}
        </div>
      )}

      {loading ? (
        <p className="text-sm text-slate-400" role="status" aria-live="polite">
          Loading matches…
        </p>
      ) : matches && matches.length > 0 ? (
        <ul className="space-y-3">
          {matches.map((m) => (
            <MatchCard key={m.id} match={m} />
          ))}
        </ul>
      ) : (
        <EmptyState minScore={minScore} />
      )}
    </div>
  );
}

function MatchCard({ match }: { match: Match }) {
  const router = useRouter();
  return (
    <li className="rounded-xl border border-ink-700 bg-ink-900 p-5">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <h2 className="truncate text-base font-medium text-slate-100">{match.jobTitle}</h2>
          <p className="mt-0.5 text-sm text-slate-400">
            {match.company}
            {match.location ? ` · ${match.location}` : ""}
          </p>
        </div>
        <ScoreBadge score={match.score} reasoned={match.reasoned} />
      </div>

      {match.summary && <p className="mt-3 text-sm text-slate-300">{match.summary}</p>}

      {match.rationale.length > 0 && (
        <ul className="mt-3 list-disc space-y-1 pl-5 text-sm text-slate-400">
          {match.rationale.map((r, i) => (
            <li key={i}>{r}</li>
          ))}
        </ul>
      )}

      {match.missingKeywords.length > 0 && (
        <div className="mt-3">
          <p className="text-[11px] uppercase tracking-wide text-slate-500">Missing keywords</p>
          <ul className="mt-1.5 flex flex-wrap gap-1.5" aria-label="Missing ATS keywords">
            {match.missingKeywords.map((k) => (
              <li
                key={k}
                className="rounded bg-warn-500/15 px-2 py-0.5 text-xs text-warn-500"
              >
                {k}
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="mt-4 flex items-center gap-3">
        <button
          type="button"
          onClick={() => router.push("/tailor")}
          className="rounded-md bg-brand-500 px-3.5 py-2 text-sm font-medium text-white hover:bg-brand-600"
        >
          Tailor &amp; apply
        </button>
        {!match.reasoned && (
          <span className="text-[11px] text-slate-500" aria-live="polite">
            Vector score — AI review pending
          </span>
        )}
      </div>

      <AtsPanel matchId={match.id} />
    </li>
  );
}

function AtsPanel({ matchId }: { matchId: string }) {
  const [open, setOpen] = useState(false);
  const [ats, setAts] = useState<AtsAnalysis | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function toggle() {
    const next = !open;
    setOpen(next);
    if (next && !ats && !loading) {
      setLoading(true);
      setError(null);
      try {
        setAts(await api.ats(matchId));
      } catch (err) {
        setError(err instanceof ApiError ? err.message : "Couldn't load ATS analysis.");
      } finally {
        setLoading(false);
      }
    }
  }

  return (
    <div className="mt-4 border-t border-ink-800 pt-3">
      <button
        type="button"
        onClick={toggle}
        aria-expanded={open}
        className="text-xs font-medium text-brand-400 hover:underline"
      >
        {open ? "Hide" : "Show"} ATS keyword analysis
      </button>

      {open && (
        <div className="mt-3">
          {loading && (
            <p className="text-xs text-slate-400" role="status" aria-live="polite">
              Analyzing…
            </p>
          )}
          {error && (
            <p className="text-xs text-bad-500" role="alert">
              {error}
            </p>
          )}
          {ats && (
            <>
              <div className="flex items-center gap-2">
                <div
                  className="h-2 flex-1 overflow-hidden rounded-full bg-ink-800"
                  role="progressbar"
                  aria-valuenow={ats.coveragePercent}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-label="ATS keyword coverage"
                >
                  <div
                    className={
                      ats.coveragePercent >= 70 ? "h-full bg-good-500"
                      : ats.coveragePercent >= 40 ? "h-full bg-warn-500"
                      : "h-full bg-bad-500"
                    }
                    style={{ width: `${ats.coveragePercent}%` }}
                  />
                </div>
                <span className="text-xs tabular-nums text-slate-300">
                  {ats.coveragePercent}% coverage
                </span>
              </div>

              {ats.missingKeywords.length > 0 ? (
                <div className="mt-3">
                  <p className="text-[11px] uppercase tracking-wide text-slate-500">
                    Missing ({ats.missingKeywords.length}/{ats.jobKeywordCount})
                  </p>
                  <ul className="mt-1.5 flex flex-wrap gap-1.5">
                    {ats.missingKeywords.map((k) => (
                      <li key={k} className="rounded bg-bad-500/15 px-2 py-0.5 text-xs text-bad-500">
                        {k}
                      </li>
                    ))}
                  </ul>
                </div>
              ) : (
                <p className="mt-3 text-xs text-good-500">
                  Resume covers all detected ATS keywords for this role.
                </p>
              )}

              {ats.presentKeywords.length > 0 && (
                <div className="mt-3">
                  <p className="text-[11px] uppercase tracking-wide text-slate-500">
                    Present ({ats.presentKeywords.length})
                  </p>
                  <ul className="mt-1.5 flex flex-wrap gap-1.5">
                    {ats.presentKeywords.map((k) => (
                      <li key={k} className="rounded bg-good-500/15 px-2 py-0.5 text-xs text-good-500">
                        {k}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
}

function ScoreBadge({ score, reasoned }: { score: number; reasoned: boolean }) {
  const tone =
    score >= 80 ? "border-good-500/40 bg-good-500/10 text-good-500"
    : score >= 60 ? "border-warn-500/40 bg-warn-500/10 text-warn-500"
    : "border-ink-600 bg-ink-800 text-slate-400";
  return (
    <div
      className={`flex shrink-0 flex-col items-center rounded-lg border px-3 py-1.5 ${tone}`}
      aria-label={`Match score ${score} out of 100${reasoned ? "" : ", AI review pending"}`}
    >
      <span className="text-lg font-semibold tabular-nums">{score}</span>
      <span className="text-[9px] uppercase tracking-wide">score</span>
    </div>
  );
}

function EmptyState({ minScore }: { minScore: number }) {
  return (
    <section className="rounded-xl border border-ink-700 bg-ink-900 p-6">
      <h2 className="text-lg font-medium">No matches yet</h2>
      <p className="mt-2 max-w-prose text-sm text-slate-400">
        {minScore > 0
          ? `No matches at score ${minScore}+ — try lowering the filter.`
          : "Matches appear once your resume is parsed and the discovery pipeline has run. "}
      </p>
      <p className="mt-2 max-w-prose text-sm text-slate-400">
        Upload a resume on your{" "}
        <a href="/profile" className="text-brand-400 hover:underline">
          Consultant Profile
        </a>
        , then the system discovers, embeds, and scores real openings against it.
      </p>
    </section>
  );
}
