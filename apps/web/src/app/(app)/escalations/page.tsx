"use client";

// Escalations — the "needs you" queue. Recruiter replies the system flagged for
// human action (interview / info / scheduling / salary). Resolve once handled.
//
// Refs: AIRMVP1-402 (read side)

import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type Escalation } from "@/lib/api";

const REASON_LABEL: Record<string, string> = {
  interview_request: "Interview request",
  info_request: "Info request",
  salary_question: "Salary question",
  scheduling: "Scheduling",
  rejection: "Rejection",
  other: "Reply",
};

function sentimentClass(s: string | null): string {
  if (s === "positive") return "bg-good-500/15 text-good-500";
  if (s === "negative") return "bg-bad-500/15 text-bad-500";
  return "bg-ink-700 text-slate-300";
}

export default function EscalationsPage() {
  const [items, setItems] = useState<Escalation[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [resolving, setResolving] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setItems(await api.escalations.list(true));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't load escalations.");
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function resolve(id: string) {
    setResolving(id);
    try {
      await api.escalations.resolve(id);
      setItems((cur) => cur?.filter((e) => e.id !== id) ?? null);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't resolve.");
    } finally {
      setResolving(null);
    }
  }

  return (
    <div className="mx-auto w-full max-w-3xl px-6 py-10">
      <header className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Escalations</h1>
          <p className="mt-1 text-sm text-slate-400">
            The only things that need your attention right now.
          </p>
        </div>
        <button onClick={load} className="text-xs text-slate-400 hover:text-slate-200">
          Refresh
        </button>
      </header>

      {error && (
        <div role="alert" className="mb-4 rounded-md border border-bad-500/40 bg-bad-500/10 px-3 py-2 text-xs text-bad-500">
          {error}
        </div>
      )}

      {items === null ? (
        <p className="text-sm text-slate-500">Loading…</p>
      ) : items.length === 0 ? (
        <div className="rounded-xl border border-ink-700 bg-ink-900/60 p-10 text-center text-sm text-slate-400">
          Nothing needs you. Replies that require a human will show up here.
        </div>
      ) : (
        <ul className="space-y-3">
          {items.map((e) => (
            <li key={e.id} className="rounded-xl border border-ink-700 bg-ink-900/60 p-5">
              <div className="flex items-start justify-between gap-4">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="rounded bg-brand-500/15 px-2 py-0.5 text-xs font-medium text-brand-200">
                      {REASON_LABEL[e.reason] ?? e.reason}
                    </span>
                    {e.sentiment && (
                      <span className={`rounded px-2 py-0.5 text-xs ${sentimentClass(e.sentiment)}`}>
                        {e.sentiment}
                      </span>
                    )}
                  </div>
                  <div className="mt-2 font-medium text-slate-100">
                    {e.jobTitle} <span className="text-slate-500">· {e.company}</span>
                  </div>
                  {e.summary && <p className="mt-1 text-sm text-slate-400">{e.summary}</p>}
                  <p className="mt-2 text-xs text-slate-500">
                    {e.recruiterName ?? e.recruiterEmail ?? "Recruiter"} ·{" "}
                    {new Date(e.createdAt).toLocaleString()}
                  </p>
                </div>
                <button
                  onClick={() => resolve(e.id)}
                  disabled={resolving === e.id}
                  className="shrink-0 rounded-md border border-ink-700 bg-ink-800 px-3 py-1.5 text-xs font-medium text-slate-100 hover:bg-ink-700 disabled:opacity-60"
                >
                  {resolving === e.id ? "Resolving…" : "Mark resolved"}
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
