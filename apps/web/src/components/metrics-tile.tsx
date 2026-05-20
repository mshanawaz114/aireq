"use client";

import { useEffect, useState } from "react";
import { api, ApiError, type Metrics } from "@/lib/api";

// Admin metrics: discovery-pipeline health + this tenant's matches/resumes/LLM
// spend. Polls every 30s. Renders the top-line cards on the dashboard and a
// detail panel (by-source breakdown + LLM cost).
//
// Refs: AIRMVP1-207
type State =
  | { kind: "loading" }
  | { kind: "ok"; data: Metrics }
  | { kind: "error"; message: string };

export function MetricsTile() {
  const [state, setState] = useState<State>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      try {
        const data = await api.adminMetrics();
        if (!cancelled) setState({ kind: "ok", data });
      } catch (e) {
        if (!cancelled) {
          setState({
            kind: "error",
            message: e instanceof ApiError ? `API ${e.status}` : "API unreachable",
          });
        }
      }
    };
    void tick();
    const id = setInterval(tick, 30_000);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);

  return (
    <section
      aria-labelledby="metrics-heading"
      className="rounded-xl border border-ink-700 bg-ink-900 p-6"
    >
      <h2 id="metrics-heading" className="font-medium">
        Pipeline metrics
      </h2>

      {state.kind === "loading" && (
        <p className="mt-2 text-sm text-slate-400" role="status" aria-live="polite">
          Loading…
        </p>
      )}

      {state.kind === "error" && (
        <p className="mt-2 text-sm text-bad-500" role="status" aria-live="polite">
          Can&rsquo;t reach the API ({state.message}).
        </p>
      )}

      {state.kind === "ok" && <MetricsBody data={state.data} />}
    </section>
  );
}

function MetricsBody({ data }: { data: Metrics }) {
  return (
    <>
      <div className="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Jobs (active)" value={`${data.jobs.active}`} hint={`${data.jobs.total} total`} />
        <Stat label="Embedded" value={`${data.jobs.embedded}`} hint="ready to match" />
        <Stat label="Matches" value={`${data.matches.total}`} hint={`${data.matches.reasoned} reasoned`} />
        <Stat
          label="Avg score"
          value={data.matches.total > 0 ? data.matches.avgScore.toFixed(0) : "—"}
          hint="across matches"
        />
      </div>

      <div className="mt-5 grid grid-cols-1 gap-5 sm:grid-cols-2">
        <Breakdown title="Jobs by source" entries={data.jobs.bySource} />
        <div>
          <p className="text-[11px] uppercase tracking-wide text-slate-500">LLM usage</p>
          <p className="mt-1.5 text-sm text-slate-300">
            {data.llm.calls.toLocaleString()} call{data.llm.calls === 1 ? "" : "s"} ·{" "}
            <span className="tabular-nums">${data.llm.costUsd.toFixed(4)}</span> est.
          </p>
          {Object.keys(data.llm.byPurpose).length > 0 && (
            <ul className="mt-1.5 space-y-0.5 text-xs text-slate-400">
              {Object.entries(data.llm.byPurpose).map(([purpose, n]) => (
                <li key={purpose} className="flex justify-between">
                  <span>{purpose}</span>
                  <span className="tabular-nums">{n}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>

      <p className="mt-4 text-[11px] text-slate-500">
        Updated {new Date(data.generatedAt).toLocaleTimeString()} · polls every 30s
      </p>
    </>
  );
}

function Stat({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <div className="rounded-lg border border-ink-800 bg-ink-950/40 p-3">
      <p className="text-[11px] text-slate-400">{label}</p>
      <p className="mt-0.5 text-2xl font-semibold tabular-nums text-slate-100">{value}</p>
      <p className="text-[10px] text-slate-500">{hint}</p>
    </div>
  );
}

function Breakdown({ title, entries }: { title: string; entries: Record<string, number> }) {
  const rows = Object.entries(entries).sort((a, b) => b[1] - a[1]);
  return (
    <div>
      <p className="text-[11px] uppercase tracking-wide text-slate-500">{title}</p>
      {rows.length === 0 ? (
        <p className="mt-1.5 text-xs text-slate-500">No data yet.</p>
      ) : (
        <ul className="mt-1.5 space-y-0.5 text-xs text-slate-400">
          {rows.map(([source, n]) => (
            <li key={source} className="flex justify-between">
              <span>{source}</span>
              <span className="tabular-nums">{n}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
