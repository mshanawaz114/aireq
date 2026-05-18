"use client";

import { useEffect, useState } from "react";
import { api, type HealthResponse, ApiError } from "@/lib/api";
import { cn } from "@/lib/cn";

type State =
  | { kind: "loading" }
  | { kind: "ok"; data: HealthResponse }
  | { kind: "error"; message: string };

export function HealthBadge() {
  const [state, setState] = useState<State>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      try {
        const data = await api.health();
        if (!cancelled) setState({ kind: "ok", data });
      } catch (e) {
        if (cancelled) return;
        const msg = e instanceof ApiError ? `API ${e.status}` : "API unreachable";
        setState({ kind: "error", message: msg });
      }
    };
    tick();
    const id = setInterval(tick, 15_000);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);

  if (state.kind === "loading") {
    return (
      <div
        role="status"
        aria-live="polite"
        className="inline-flex items-center gap-2 rounded-md border border-ink-700 bg-ink-900 px-3 py-1.5 text-xs text-slate-400"
      >
        <span className="h-2 w-2 animate-pulse rounded-full bg-slate-500" aria-hidden="true" />
        Checking API…
      </div>
    );
  }

  if (state.kind === "error") {
    return (
      <div
        role="status"
        aria-live="polite"
        className="inline-flex items-center gap-2 rounded-md border border-bad-500/40 bg-bad-500/10 px-3 py-1.5 text-xs text-bad-500"
      >
        <span className="h-2 w-2 rounded-full bg-bad-500" aria-hidden="true" />
        API down — {state.message}
      </div>
    );
  }

  const allHealthy =
    !state.data.dependenciesHealthy ||
    Object.values(state.data.dependenciesHealthy).every((v) => v);

  return (
    <div
      role="status"
      aria-live="polite"
      className={cn(
        "inline-flex items-center gap-2 rounded-md border px-3 py-1.5 text-xs",
        allHealthy
          ? "border-good-500/40 bg-good-500/10 text-good-500"
          : "border-warn-500/40 bg-warn-500/10 text-warn-500",
      )}
    >
      <span
        aria-hidden="true"
        className={cn("h-2 w-2 rounded-full", allHealthy ? "bg-good-500" : "bg-warn-500")}
      />
      API {state.data.status} · v{state.data.version}
      {state.data.dependenciesHealthy && (
        <span className="text-slate-400">
          ·{" "}
          {Object.entries(state.data.dependenciesHealthy)
            .map(([k, v]) => `${k}=${v ? "ok" : "down"}`)
            .join(", ")}
        </span>
      )}
    </div>
  );
}
