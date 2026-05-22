"use client";

// Tailor & Apply — the "ready to apply" queue: matches whose resume has already
// been tailored (status Tailored). One click submits through the best available
// channel (portal API → Playwright → email → manual). Tailoring itself starts
// from Job Matches ("Tailor & apply").
//
// Refs: AIRMVP1-302 / AIRMVP1-303 (apply)

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api, ApiError, type Match } from "@/lib/api";

export default function TailorPage() {
  const [items, setItems] = useState<Match[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setItems(await api.matches.list({ status: "Tailored" }));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't load the apply queue.");
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function submit(id: string) {
    setBusy(id);
    setNote(null);
    try {
      await api.submit(id);
      setNote("Submission queued. Check the Submissions tab for the channel + status.");
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Submit failed.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="mx-auto w-full max-w-3xl px-6 py-10">
      <header className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Tailor &amp; Apply</h1>
          <p className="mt-1 text-sm text-slate-400">
            Matches with a tailored resume, ready to submit. Start tailoring from{" "}
            <Link href="/matches" className="text-brand-400 hover:underline">
              Job Matches
            </Link>
            .
          </p>
        </div>
        <button onClick={load} className="text-xs text-slate-400 hover:text-slate-200">
          Refresh
        </button>
      </header>

      {note && (
        <div role="status" className="mb-4 rounded-md border border-good-500/40 bg-good-500/10 px-3 py-2 text-xs text-good-500">
          {note}
        </div>
      )}
      {error && (
        <div role="alert" className="mb-4 rounded-md border border-bad-500/40 bg-bad-500/10 px-3 py-2 text-xs text-bad-500">
          {error}
        </div>
      )}

      {items === null ? (
        <p className="text-sm text-slate-500">Loading…</p>
      ) : items.length === 0 ? (
        <div className="rounded-xl border border-ink-700 bg-ink-900/60 p-10 text-center text-sm text-slate-400">
          Nothing tailored yet. Go to{" "}
          <Link href="/matches" className="text-brand-400 hover:underline">
            Job Matches
          </Link>{" "}
          and hit “Tailor &amp; apply” on a match to queue it here.
        </div>
      ) : (
        <ul className="space-y-3">
          {items.map((m) => (
            <li key={m.id} className="flex items-center justify-between gap-4 rounded-xl border border-ink-700 bg-ink-900/60 p-5">
              <div className="min-w-0">
                <div className="font-medium text-slate-100">
                  {m.jobTitle} <span className="text-slate-500">· {m.company}</span>
                </div>
                <div className="mt-0.5 text-xs text-slate-500">
                  Fit score {m.score}
                  {m.summary ? ` · ${m.summary}` : ""}
                </div>
              </div>
              <button
                onClick={() => submit(m.id)}
                disabled={busy === m.id}
                className="shrink-0 rounded-md bg-brand-500 px-4 py-2 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-60"
              >
                {busy === m.id ? "Submitting…" : "Submit application"}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
