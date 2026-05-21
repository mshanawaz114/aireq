"use client";

// Billing settings — shows the tenant's plan/trial state and links out to the
// Stripe-hosted checkout + customer portal. The actual subscription management
// (cards, invoices, cancellation) lives in Stripe's portal; this page is just
// the entry point + status. (AIRMVP1-406)

import { useEffect, useState } from "react";
import { api, ApiError, type BillingStatus } from "@/lib/api";

const STATUS_LABEL: Record<BillingStatus["status"], string> = {
  trialing: "Free trial",
  active: "Active",
  past_due: "Past due",
  canceled: "Canceled",
  trial_expired: "Trial expired",
  incomplete: "Incomplete",
};

export default function BillingPage() {
  const [status, setStatus] = useState<BillingStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<"checkout" | "portal" | null>(null);

  useEffect(() => {
    api.billing
      .status()
      .then(setStatus)
      .catch((e) => setError(e instanceof ApiError ? e.message : "Couldn't load billing."));
  }, []);

  async function go(kind: "checkout" | "portal") {
    setBusy(kind);
    setError(null);
    try {
      const res = kind === "checkout" ? await api.billing.checkout() : await api.billing.portal();
      window.location.href = res.url;
    } catch (e) {
      setError(
        e instanceof ApiError
          ? e.status === 503
            ? "Billing isn't configured on this server yet."
            : e.message
          : "Something went wrong.",
      );
      setBusy(null);
    }
  }

  const trialEnds = status?.trialEndsAt ? new Date(status.trialEndsAt) : null;
  const periodEnds = status?.currentPeriodEnd ? new Date(status.currentPeriodEnd) : null;

  return (
    <main id="main" className="mx-auto w-full max-w-2xl px-6 py-10">
      <h1 className="text-xl font-semibold">Billing</h1>
      <p className="mt-1 text-sm text-slate-400">Manage your subscription and view your plan.</p>

      {error && (
        <div
          role="alert"
          className="mt-6 rounded-md border border-bad-500/40 bg-bad-500/10 px-3 py-2 text-xs text-bad-500"
        >
          {error}
        </div>
      )}

      {status && (
        <div className="mt-6 rounded-xl border border-ink-700 bg-ink-900/60 p-6">
          <div className="flex items-center justify-between">
            <div>
              <div className="text-xs uppercase tracking-wide text-slate-500">Status</div>
              <div className="mt-1 text-lg font-semibold">{STATUS_LABEL[status.status]}</div>
            </div>
            <span
              className={`rounded-full px-3 py-1 text-xs font-medium ${
                status.entitled
                  ? "bg-good-500/15 text-good-500"
                  : "bg-bad-500/15 text-bad-500"
              }`}
            >
              {status.entitled ? "Active access" : "No access"}
            </span>
          </div>

          {status.status === "trialing" && trialEnds && (
            <p className="mt-3 text-sm text-slate-400">
              Your free trial ends on{" "}
              <span className="text-slate-200">{trialEnds.toLocaleDateString()}</span>.
            </p>
          )}
          {status.status === "active" && periodEnds && (
            <p className="mt-3 text-sm text-slate-400">
              Renews on <span className="text-slate-200">{periodEnds.toLocaleDateString()}</span>.
            </p>
          )}
          {status.status === "trial_expired" && (
            <p className="mt-3 text-sm text-slate-400">
              Your trial has ended. Subscribe to keep discovering and applying.
            </p>
          )}

          <div className="mt-6 flex flex-wrap gap-3">
            {!status.hasStripeCustomer || status.status !== "active" ? (
              <button
                onClick={() => go("checkout")}
                disabled={busy !== null}
                className="rounded-md bg-brand-500 px-4 py-2 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-60"
              >
                {busy === "checkout" ? "Redirecting…" : "Subscribe"}
              </button>
            ) : null}
            {status.hasStripeCustomer && (
              <button
                onClick={() => go("portal")}
                disabled={busy !== null}
                className="rounded-md border border-ink-700 bg-ink-800 px-4 py-2 text-sm font-medium text-slate-100 hover:bg-ink-700 disabled:opacity-60"
              >
                {busy === "portal" ? "Redirecting…" : "Manage billing"}
              </button>
            )}
          </div>
        </div>
      )}

      {!status && !error && <p className="mt-6 text-sm text-slate-500">Loading…</p>}
    </main>
  );
}
