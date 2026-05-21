// Marketing landing page (public, indexable). Lives at "/" and is the only
// page that overrides the app-wide robots:noindex with index:true.
//
// Server component — the only interactive island is <WaitlistForm/>.
//
// Refs: AIRMVP1-405

import type { Metadata } from "next";
import Link from "next/link";
import { WaitlistForm } from "@/components/waitlist-form";

const SITE_URL = process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000";

export const metadata: Metadata = {
  title: "Aireq — AI Operations Copilot for staffing & consultants",
  description:
    "Aireq discovers real openings, tailors each resume to beat the ATS, auto-applies, follows up, and only pings you when a human is needed. Join the waitlist.",
  alternates: { canonical: SITE_URL },
  robots: { index: true, follow: true },
  openGraph: {
    title: "Aireq — AI Operations Copilot",
    description:
      "Discover real openings, tailor resumes per role, auto-apply, follow up — human-in-the-loop only when it matters.",
    type: "website",
    url: SITE_URL,
  },
  twitter: { card: "summary_large_image", title: "Aireq", description: "AI Operations Copilot for staffing & consultants." },
};

const FEATURES = [
  {
    title: "Discover real openings",
    body: "Pulls live roles straight from employer ATS boards and job APIs — deduped and freshness-checked, never stale listings.",
  },
  {
    title: "Tailor to beat the ATS",
    body: "Rewrites each resume against the specific job description, closing keyword gaps so it clears the applicant tracking system.",
  },
  {
    title: "Auto-apply across channels",
    body: "Submits through portal APIs, automated forms, or a tracked email — falling back gracefully and logging every attempt.",
  },
  {
    title: "Follow up, then escalate",
    body: "Sends rate-limited nudges on quiet applications and classifies every recruiter reply — pinging you only when a human is needed.",
  },
];

const STEPS = [
  { n: "1", t: "Add a consultant", d: "Upload a resume; we parse and embed it." },
  { n: "2", t: "We match & tailor", d: "Real openings scored to fit, resume rewritten per role." },
  { n: "3", t: "We apply & follow up", d: "Auto-submit, nudge, and surface replies that need you." },
];

export default function LandingPage() {
  return (
    <main id="main" className="mx-auto w-full max-w-5xl px-6 py-16 sm:py-24">
      {/* Brand */}
      <header className="mb-16 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <div
            aria-hidden="true"
            className="flex h-9 w-9 items-center justify-center rounded-lg bg-gradient-to-br from-brand-500 to-purple-500 text-base font-bold"
          >
            A
          </div>
          <div>
            <div className="font-semibold">Aireq</div>
            <div className="-mt-0.5 text-[10px] text-slate-400">AI Operations Copilot</div>
          </div>
        </div>
        <nav className="flex items-center gap-4 text-sm">
          <Link href="/login" className="text-slate-300 hover:text-white">
            Sign in
          </Link>
          <Link
            href="/signup"
            className="rounded-md bg-ink-800 px-3 py-1.5 text-slate-100 hover:bg-ink-700"
          >
            Sign up
          </Link>
        </nav>
      </header>

      {/* Hero */}
      <section className="text-center">
        <p className="mb-3 text-xs font-medium uppercase tracking-widest text-brand-400">
          For staffing agencies &amp; independent consultants
        </p>
        <h1 className="mx-auto max-w-3xl text-balance text-4xl font-bold leading-tight sm:text-5xl">
          Your AI copilot for the whole apply-to-offer loop
        </h1>
        <p className="mx-auto mt-5 max-w-2xl text-pretty text-base text-slate-300 sm:text-lg">
          Aireq finds real openings, tailors every resume to beat the ATS, auto-applies, follows
          up, and escalates to you only when a human is genuinely needed.
        </p>

        <div className="mx-auto mt-8 max-w-xl">
          <WaitlistForm source="hero" />
          <p className="mt-3 text-xs text-slate-500">
            No spam. We&rsquo;ll email you when your spot opens.
          </p>
        </div>
      </section>

      {/* Features */}
      <section aria-labelledby="features-heading" className="mt-24">
        <h2 id="features-heading" className="sr-only">
          What Aireq does
        </h2>
        <div className="grid gap-4 sm:grid-cols-2">
          {FEATURES.map((f) => (
            <div key={f.title} className="rounded-xl border border-ink-700 bg-ink-900/60 p-6">
              <h3 className="text-base font-semibold text-white">{f.title}</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-400">{f.body}</p>
            </div>
          ))}
        </div>
      </section>

      {/* How it works */}
      <section aria-labelledby="how-heading" className="mt-24">
        <h2 id="how-heading" className="text-center text-2xl font-bold">
          How it works
        </h2>
        <ol className="mt-8 grid gap-6 sm:grid-cols-3">
          {STEPS.map((s) => (
            <li key={s.n} className="rounded-xl border border-ink-700 bg-ink-900/60 p-6 text-center">
              <div
                aria-hidden="true"
                className="mx-auto flex h-10 w-10 items-center justify-center rounded-full bg-brand-500/15 text-base font-bold text-brand-400"
              >
                {s.n}
              </div>
              <h3 className="mt-4 font-semibold text-white">{s.t}</h3>
              <p className="mt-1.5 text-sm text-slate-400">{s.d}</p>
            </li>
          ))}
        </ol>
      </section>

      {/* Final CTA */}
      <section className="mt-24 rounded-2xl border border-ink-700 bg-gradient-to-br from-ink-900 to-ink-800 p-10 text-center">
        <h2 className="text-2xl font-bold">Be first in line</h2>
        <p className="mx-auto mt-2 max-w-xl text-sm text-slate-300">
          Aireq is in early access. Join the waitlist and we&rsquo;ll onboard you as spots open.
        </p>
        <div className="mx-auto mt-6 max-w-xl">
          <WaitlistForm source="footer-cta" />
        </div>
      </section>

      {/* Footer */}
      <footer className="mt-20 flex flex-col items-center justify-between gap-3 border-t border-ink-800 pt-8 text-xs text-slate-500 sm:flex-row">
        <p>© {new Date().getFullYear()} Aireq. All rights reserved.</p>
        <nav className="flex gap-4">
          <Link href="/login" className="hover:text-slate-300">
            Sign in
          </Link>
          <Link href="/signup" className="hover:text-slate-300">
            Create account
          </Link>
        </nav>
      </footer>
    </main>
  );
}
