"use client";

// WaitlistForm — the landing-page email capture. Posts to the anonymous
// /api/waitlist endpoint and shows a friendly confirmation (idempotent server
// side, so a double-submit just says "you're already on the list").
//
// Refs: AIRMVP1-405

import { useState } from "react";
import { api, ApiError } from "@/lib/api";

type State = "idle" | "submitting" | "done" | "error";

export function WaitlistForm({ source = "landing" }: { source?: string }) {
  const [email, setEmail] = useState("");
  const [state, setState] = useState<State>("idle");
  const [message, setMessage] = useState<string | null>(null);

  async function onSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setState("submitting");
    setMessage(null);
    try {
      const res = await api.waitlist.join({ email, source });
      setState("done");
      setMessage(
        res.alreadyJoined
          ? "You're already on the list — we'll be in touch."
          : "You're in. We'll email you when your spot opens.",
      );
    } catch (err) {
      setState("error");
      setMessage(
        err instanceof ApiError && err.status === 400
          ? "Please enter a valid email address."
          : "Something went wrong. Please try again.",
      );
    }
  }

  if (state === "done") {
    return (
      <div
        role="status"
        aria-live="polite"
        className="rounded-lg border border-good-500/40 bg-good-500/10 px-4 py-3 text-sm text-good-500"
      >
        {message}
      </div>
    );
  }

  return (
    <form onSubmit={onSubmit} className="flex flex-col gap-3 sm:flex-row" noValidate>
      <label htmlFor="waitlist-email" className="sr-only">
        Work email
      </label>
      <input
        id="waitlist-email"
        name="email"
        type="email"
        inputMode="email"
        autoComplete="email"
        required
        placeholder="you@agency.com"
        value={email}
        onChange={(e) => setEmail(e.target.value)}
        className="min-w-0 flex-1 rounded-lg border border-ink-700 bg-ink-900 px-4 py-3 text-sm text-slate-100 placeholder:text-slate-500 focus:border-brand-500 focus:outline-none"
      />
      <button
        type="submit"
        disabled={state === "submitting"}
        className="rounded-lg bg-brand-500 px-6 py-3 text-sm font-semibold text-white hover:bg-brand-600 disabled:opacity-60"
      >
        {state === "submitting" ? "Joining…" : "Join the waitlist"}
      </button>
      {state === "error" && message && (
        <p role="alert" className="text-xs text-bad-500 sm:basis-full">
          {message}
        </p>
      )}
    </form>
  );
}
