"use client";

import { useEffect, useState } from "react";
import { api, ApiError, type DbStatusResponse } from "@/lib/api";
import { cn } from "@/lib/cn";

type State =
  | { kind: "loading" }
  | { kind: "ok"; data: DbStatusResponse }
  | { kind: "error"; message: string };

const TABLE_ORDER: Array<{ key: string; label: string; group: "core" | "match" | "ops" }> = [
  { key: "tenants",            label: "Tenants",            group: "core" },
  { key: "users",              label: "Users",              group: "core" },
  { key: "consultants",        label: "Consultants",        group: "core" },
  { key: "resumes",            label: "Resumes",            group: "core" },
  { key: "skills",             label: "Skills",             group: "core" },
  { key: "consultant_skills",  label: "Consultant skills",  group: "core" },
  { key: "jobs",               label: "Jobs",               group: "match" },
  { key: "matches",            label: "Matches",            group: "match" },
  { key: "tailored_resumes",   label: "Tailored resumes",   group: "match" },
  { key: "submissions",        label: "Submissions",        group: "ops" },
  { key: "recruiter_threads",  label: "Recruiter threads",  group: "ops" },
  { key: "messages",           label: "Messages",           group: "ops" },
  { key: "escalations",        label: "Escalations",        group: "ops" },
];

export function DbStatusTile() {
  const [state, setState] = useState<State>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      try {
        const data = await api.dbStatus();
        if (!cancelled) setState({ kind: "ok", data });
      } catch (e) {
        if (cancelled) return;
        const msg = e instanceof ApiError ? `API ${e.status}` : "API unreachable";
        setState({ kind: "error", message: msg });
      }
    };
    tick();
    const id = setInterval(tick, 30_000);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);

  if (state.kind === "loading") {
    return (
      <section
        aria-labelledby="db-status-heading"
        className="rounded-xl border border-ink-700 bg-ink-900 p-6"
      >
        <h2 id="db-status-heading" className="font-medium">
          Database schema
        </h2>
        <p className="mt-2 text-sm text-slate-400" role="status" aria-live="polite">
          Loading…
        </p>
      </section>
    );
  }

  if (state.kind === "error") {
    return (
      <section
        aria-labelledby="db-status-heading"
        className="rounded-xl border border-bad-500/40 bg-bad-500/5 p-6"
      >
        <h2 id="db-status-heading" className="font-medium">
          Database schema
        </h2>
        <p className="mt-2 text-sm text-bad-500" role="status" aria-live="polite">
          Can&rsquo;t reach the API ({state.message}). Start <code className="rounded bg-ink-800 px-1.5 py-0.5">make api</code>.
        </p>
      </section>
    );
  }

  const { appliedMigrations, pendingMigrations, rowCounts } = state.data;
  const allMigrated = pendingMigrations.length === 0;

  return (
    <section
      aria-labelledby="db-status-heading"
      className="rounded-xl border border-ink-700 bg-ink-900 p-6"
    >
      <div className="flex items-baseline justify-between">
        <h2 id="db-status-heading" className="font-medium">
          Database schema
        </h2>
        <span
          className={cn(
            "rounded px-2 py-0.5 text-[10px]",
            allMigrated
              ? "border border-good-500/40 bg-good-500/10 text-good-500"
              : "border border-warn-500/40 bg-warn-500/10 text-warn-500",
          )}
        >
          {allMigrated
            ? `${appliedMigrations.length} migration${appliedMigrations.length === 1 ? "" : "s"} applied`
            : `${pendingMigrations.length} pending`}
        </span>
      </div>

      <p className="mt-2 text-sm text-slate-400">
        Live row counts per table. Core tables fill in as you use the app;
        match &amp; ops tables populate across weeks&nbsp;2&ndash;4.
      </p>

      <div className="mt-4 grid grid-cols-1 gap-x-6 gap-y-1 sm:grid-cols-2 lg:grid-cols-3">
        {TABLE_ORDER.map(({ key, label, group }) => (
          <div
            key={key}
            className="flex items-center justify-between border-b border-ink-800/60 py-1.5"
          >
            <div className="flex items-center gap-2 text-sm">
              <span
                aria-hidden="true"
                className={cn(
                  "h-1.5 w-1.5 rounded-full",
                  group === "core" && "bg-brand-500",
                  group === "match" && "bg-purple-500",
                  group === "ops" && "bg-good-500",
                )}
              />
              <span className="text-slate-300">{label}</span>
            </div>
            <span className="font-mono text-sm tabular-nums text-slate-200">
              {(rowCounts[key] ?? 0).toLocaleString()}
            </span>
          </div>
        ))}
      </div>

      <p className="mt-4 text-[11px] text-slate-500">
        Updated {new Date(state.data.timestamp).toLocaleTimeString()} · polls every 30s
      </p>
    </section>
  );
}
