import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = { title: "Settings" };

export default function SettingsPage() {
  return (
    <main id="main" className="mx-auto w-full max-w-2xl px-6 py-10">
      <h1 className="text-xl font-semibold">Settings</h1>
      <p className="mt-1 text-sm text-slate-400">
        Account, billing, sending-domain, and automation-approval preferences.
      </p>

      <nav className="mt-6 space-y-3">
        <Link
          href="/settings/billing"
          className="block rounded-xl border border-ink-700 bg-ink-900/60 p-5 hover:border-brand-500"
        >
          <div className="font-medium text-slate-100">Billing</div>
          <div className="mt-0.5 text-sm text-slate-400">
            Plan, free trial, subscription, and invoices.
          </div>
        </Link>
      </nav>
    </main>
  );
}
