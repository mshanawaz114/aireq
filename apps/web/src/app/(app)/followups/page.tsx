"use client";

// Follow-ups — the approval queue for auto-drafted nudges on quiet applications.
// Owner-approval is the default: review the draft, then Approve (sends next pass)
// or Cancel.
//
// Refs: AIRMVP1-404 (read/approve side)

import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type FollowUp } from "@/lib/api";

export default function FollowUpsPage() {
  const [items, setItems] = useState<FollowUp[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setItems(await api.followups.list(true));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't load follow-ups.");
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function act(id: string, kind: "approve" | "cancel") {
    setBusy(id);
    try {
      if (kind === "approve") await api.followups.approve(id);
      else await api.followups.cancel(id);
      setItems((cur) => cur?.filter((f) => f.id !== id) ?? null);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Action failed.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="mx-auto w-full max-w-3xl px-6 py-10">
      <header className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Follow-ups</h1>
          <p className="mt-1 text-sm text-slate-400">
            Drafted nudges for applications that have gone quiet. Approve to send, or cancel.
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
          No follow-ups waiting for approval. Quiet applications get a draft here automatically.
        </div>
      ) : (
        <ul className="space-y-3">
          {items.map((f) => (
            <li key={f.id} className="rounded-xl border border-ink-700 bg-ink-900/60 p-5">
              <div className="flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <div className="font-medium text-slate-100">
                    {f.jobTitle} <span className="text-slate-500">· {f.company}</span>
                  </div>
                  <div className="text-xs text-slate-500">
                    Nudge #{f.sequence} · to {f.recipient}
                  </div>
                </div>
                <div className="flex shrink-0 gap-2">
                  <button
                    onClick={() => act(f.id, "approve")}
                    disabled={busy === f.id}
                    className="rounded-md bg-brand-500 px-3 py-1.5 text-xs font-medium text-white hover:bg-brand-600 disabled:opacity-60"
                  >
                    {busy === f.id ? "…" : "Approve & send"}
                  </button>
                  <button
                    onClick={() => act(f.id, "cancel")}
                    disabled={busy === f.id}
                    className="rounded-md border border-ink-700 bg-ink-800 px-3 py-1.5 text-xs font-medium text-slate-300 hover:bg-ink-700 disabled:opacity-60"
                  >
                    Cancel
                  </button>
                </div>
              </div>
              <div className="mt-3 rounded-lg border border-ink-800 bg-ink-950/60 p-3">
                <div className="text-xs font-medium text-slate-300">{f.draftSubject}</div>
                <p className="mt-1 whitespace-pre-wrap text-sm text-slate-400">{f.draftBody}</p>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
