"use client";

// Recruiter Inbox — recruiter threads (newest activity first) on the left, the
// selected conversation on the right. Replies are pulled from Gmail and threaded
// against the originating match by the worker; this is the read view.
//
// Refs: AIRMVP1-401 (read side)

import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type Thread } from "@/lib/api";

function sentimentDot(s: string | null): string {
  if (s === "positive") return "bg-good-500";
  if (s === "negative") return "bg-bad-500";
  if (s === "neutral") return "bg-warn-500";
  return "bg-slate-600";
}

export default function InboxPage() {
  const [threads, setThreads] = useState<Thread[] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const t = await api.threads.list();
      setThreads(t);
      setSelected((cur) => cur ?? t[0]?.id ?? null);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't load the inbox.");
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const active = threads?.find((t) => t.id === selected) ?? null;

  return (
    <div className="mx-auto flex h-[calc(100vh-3.5rem)] w-full max-w-6xl gap-4 px-6 py-6">
      {/* Thread list */}
      <aside className="flex w-72 shrink-0 flex-col rounded-xl border border-ink-700 bg-ink-900/60">
        <div className="flex items-center justify-between border-b border-ink-800 px-4 py-3">
          <h1 className="text-sm font-semibold">Recruiter Inbox</h1>
          <button onClick={load} className="text-xs text-slate-400 hover:text-slate-200">
            Refresh
          </button>
        </div>
        <div className="flex-1 overflow-y-auto">
          {threads === null ? (
            <p className="p-4 text-sm text-slate-500">Loading…</p>
          ) : threads.length === 0 ? (
            <p className="p-4 text-sm text-slate-500">
              No recruiter replies yet. They&rsquo;ll appear here once Gmail is connected and a
              recruiter responds.
            </p>
          ) : (
            <ul>
              {threads.map((t) => (
                <li key={t.id}>
                  <button
                    onClick={() => setSelected(t.id)}
                    className={`flex w-full flex-col gap-1 border-b border-ink-800 px-4 py-3 text-left hover:bg-ink-800/60 ${
                      t.id === selected ? "bg-ink-800" : ""
                    }`}
                  >
                    <div className="flex items-center gap-2">
                      <span className={`h-2 w-2 rounded-full ${sentimentDot(t.sentiment)}`} aria-hidden />
                      <span className="truncate text-sm font-medium text-slate-100">
                        {t.recruiterName ?? t.recruiterEmail}
                      </span>
                      {t.requiresHuman && (
                        <span className="ml-auto rounded bg-brand-500/20 px-1.5 py-0.5 text-[10px] text-brand-200">
                          action
                        </span>
                      )}
                    </div>
                    <span className="truncate text-xs text-slate-400">
                      {t.jobTitle} · {t.company}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </aside>

      {/* Conversation */}
      <section className="flex flex-1 flex-col rounded-xl border border-ink-700 bg-ink-900/60">
        {error && (
          <div role="alert" className="m-4 rounded-md border border-bad-500/40 bg-bad-500/10 px-3 py-2 text-xs text-bad-500">
            {error}
          </div>
        )}
        {!active ? (
          <div className="grid flex-1 place-items-center text-sm text-slate-500">
            Select a conversation.
          </div>
        ) : (
          <>
            <header className="border-b border-ink-800 px-5 py-4">
              <div className="font-medium text-slate-100">
                {active.recruiterName ?? active.recruiterEmail}
              </div>
              <div className="text-xs text-slate-400">
                {active.jobTitle} · {active.company} · {active.recruiterEmail}
              </div>
            </header>
            <div className="flex-1 space-y-4 overflow-y-auto px-5 py-4">
              {active.messages.map((m) => (
                <div
                  key={m.id}
                  className={`max-w-[80%] rounded-xl px-4 py-3 text-sm ${
                    m.direction === "Inbound"
                      ? "bg-ink-800 text-slate-100"
                      : "ml-auto bg-brand-500/15 text-slate-100"
                  }`}
                >
                  <div className="mb-1 flex items-center gap-2 text-[11px] text-slate-400">
                    <span>{m.direction === "Inbound" ? "Recruiter" : "You / Aireq"}</span>
                    {m.generatedByAi && (
                      <span className="rounded bg-ink-700 px-1 py-0.5 text-[10px]">AI</span>
                    )}
                    <span className="ml-auto">{new Date(m.sentAt).toLocaleString()}</span>
                  </div>
                  {m.subject && <div className="mb-1 font-medium">{m.subject}</div>}
                  <p className="whitespace-pre-wrap leading-relaxed">{m.body}</p>
                </div>
              ))}
            </div>
            <footer className="border-t border-ink-800 px-5 py-3 text-xs text-slate-500">
              Reply from your connected Gmail. Aireq threads recruiter responses here automatically.
            </footer>
          </>
        )}
      </section>
    </div>
  );
}
