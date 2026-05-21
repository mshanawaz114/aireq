"use client";

import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type Submission } from "@/lib/api";

// Submission tracker — every application attempt with channel, status, and the
// raw provider/audit payload. Replaces the AIRMVP1-106 placeholder.
//
// Refs: AIRMVP1-306
export default function SubmissionsPage() {
  const [items, setItems] = useState<Submission[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setItems(await api.submissions.list());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't load submissions.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="p-8">
      <header className="mb-6">
        <p className="text-xs text-slate-500">Workspace</p>
        <h1 className="text-xl font-semibold">Submissions</h1>
        <p className="mt-1 text-sm text-slate-400">
          Every application attempt and its audit trail. Statuses are dry-run until you
          enable live submit.
        </p>
      </header>

      {error && (
        <div role="alert" className="mb-6 rounded-md border border-bad-500/40 bg-bad-500/10 px-3 py-2 text-xs text-bad-500">
          {error}
        </div>
      )}

      {loading ? (
        <p className="text-sm text-slate-400" role="status" aria-live="polite">Loading…</p>
      ) : items && items.length > 0 ? (
        <ul className="space-y-2">
          {items.map((s) => <SubmissionRow key={s.id} s={s} />)}
        </ul>
      ) : (
        <section className="rounded-xl border border-ink-700 bg-ink-900 p-6">
          <h2 className="text-lg font-medium">No submissions yet</h2>
          <p className="mt-2 max-w-prose text-sm text-slate-400">
            Tailor a match on the{" "}
            <a href="/matches" className="text-brand-400 hover:underline">Job Matches</a>{" "}
            page, then click Submit application — attempts show up here.
          </p>
        </section>
      )}
    </div>
  );
}

function SubmissionRow({ s }: { s: Submission }) {
  const [open, setOpen] = useState(false);
  return (
    <li className="rounded-xl border border-ink-700 bg-ink-900 p-4">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <h2 className="truncate text-sm font-medium text-slate-100">{s.jobTitle}</h2>
          <p className="text-xs text-slate-400">{s.company}</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <ChannelTag channel={s.channel} />
          <StatusTag status={s.responseStatus} />
        </div>
      </div>
      <div className="mt-2 flex items-center justify-between">
        <time className="text-[11px] text-slate-500" dateTime={s.submittedAt}>
          {new Date(s.submittedAt).toLocaleString()}
        </time>
        {s.responsePayloadJson && (
          <button
            type="button"
            onClick={() => setOpen((o) => !o)}
            aria-expanded={open}
            className="text-[11px] text-brand-400 hover:underline"
          >
            {open ? "Hide" : "Show"} audit detail
          </button>
        )}
      </div>
      {open && s.responsePayloadJson && (
        <pre className="mt-2 overflow-x-auto rounded-md bg-ink-950 p-3 text-[11px] text-slate-300">
          {prettyJson(s.responsePayloadJson)}
        </pre>
      )}
    </li>
  );
}

function ChannelTag({ channel }: { channel: string }) {
  return (
    <span className="rounded border border-ink-600 bg-ink-800 px-2 py-0.5 text-[10px] uppercase tracking-wide text-slate-300">
      {channel}
    </span>
  );
}

function StatusTag({ status }: { status: string | null }) {
  const s = status ?? "unknown";
  const tone =
    s === "received" ? "border-good-500/40 bg-good-500/10 text-good-500"
    : s === "dry_run" ? "border-brand-500/40 bg-brand-500/10 text-brand-400"
    : s === "throttled" || s === "pending_manual" ? "border-warn-500/40 bg-warn-500/10 text-warn-500"
    : s === "failed" ? "border-bad-500/40 bg-bad-500/10 text-bad-500"
    : "border-ink-600 bg-ink-800 text-slate-400";
  return <span className={`rounded border px-2 py-0.5 text-[10px] ${tone}`}>{s}</span>;
}

function prettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
