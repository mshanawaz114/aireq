"use client";

// DevSeedButton — DEV ONLY. Seeds demo recruiter-CRM data so the Inbox,
// Escalations, Follow-ups, and Notifications screens can be seen populated
// without a real Gmail reply. Renders nothing in production builds.
//
// Refs: AIRMVP1-408 (dev tooling)

import { useState } from "react";
import { api, ApiError } from "@/lib/api";

export function DevSeedButton() {
  const [state, setState] = useState<"idle" | "busy" | "done" | "error">("idle");
  const [msg, setMsg] = useState<string | null>(null);

  if (process.env.NODE_ENV === "production") return null;

  async function seed() {
    setState("busy");
    setMsg(null);
    try {
      const res = await api.dev.seedCrm();
      setState("done");
      setMsg(res.message);
    } catch (e) {
      setState("error");
      setMsg(e instanceof ApiError ? e.message : "Seed failed.");
    }
  }

  return (
    <section className="mt-6 rounded-xl border border-dashed border-ink-700 bg-ink-900/40 p-6">
      <h2 className="font-medium text-slate-200">Developer tools</h2>
      <p className="mt-1 text-sm text-slate-400">
        Populate the Inbox, Escalations, and Follow-ups screens with demo data (no real Gmail
        reply needed). Dev only.
      </p>
      <button
        onClick={seed}
        disabled={state === "busy"}
        className="mt-3 rounded-md bg-ink-800 px-4 py-2 text-sm font-medium text-slate-100 hover:bg-ink-700 disabled:opacity-60"
      >
        {state === "busy" ? "Seeding…" : "Seed demo CRM data"}
      </button>
      {msg && (
        <p
          role="status"
          className={`mt-3 text-xs ${state === "error" ? "text-bad-500" : "text-good-500"}`}
        >
          {msg}
        </p>
      )}
    </section>
  );
}
