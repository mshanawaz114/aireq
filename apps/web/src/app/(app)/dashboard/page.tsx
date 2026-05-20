import type { Metadata } from "next";
import { DbStatusTile } from "@/components/db-status-tile";

export const metadata: Metadata = { title: "Dashboard" };

export default function DashboardPage() {
  return (
    <div className="p-8">
      <header className="mb-6">
        <p className="text-xs text-slate-500">Workspace</p>
        <h1 className="text-xl font-semibold">Dashboard</h1>
      </header>

      <section
        aria-labelledby="welcome-heading"
        className="rounded-xl border border-ink-700 bg-ink-900 p-6"
      >
        <h2 id="welcome-heading" className="text-lg font-medium">
          Welcome to Aireq.
        </h2>
        <p className="mt-2 max-w-prose text-sm text-slate-400">
          Week&nbsp;1 of the MVP is live: sign in, build your{" "}
          <a href="/profile" className="text-brand-400 hover:underline">consultant profile</a>,
          and upload a resume — it&rsquo;s stored and parsed automatically in the background.
        </p>
        <p className="mt-2 max-w-prose text-sm text-slate-400">
          Job matching, per-job tailoring, auto-apply, and the recruiter inbox land across
          weeks&nbsp;2&ndash;4 — those nav items are placeholders until then. The badge top-right
          polls the API and its Neon Postgres connection; green means your stack is healthy.
        </p>
      </section>

      <section
        aria-labelledby="metrics-heading"
        className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4"
      >
        <h2 id="metrics-heading" className="sr-only">
          Top-line metrics
        </h2>
        <Card label="New matches" value="—" hint="seeded in AIRMVP1-204" />
        <Card label="Submissions" value="—" hint="enabled in AIRMVP1-303" />
        <Card label="Recruiter replies" value="—" hint="ingested in AIRMVP1-401" />
        <Card label="Avg ATS score" value="—" hint="computed in AIRMVP1-302" />
      </section>

      <div className="mt-6">
        <DbStatusTile />
      </div>

      <section
        aria-labelledby="next-steps-heading"
        className="mt-6 rounded-xl border border-ink-700 bg-ink-900 p-6"
      >
        <h2 id="next-steps-heading" className="font-medium">
          Try the resume loop
        </h2>
        <ol className="mt-3 list-decimal space-y-1.5 pl-5 text-sm text-slate-300">
          <li>
            Go to{" "}
            <a href="/profile" className="text-brand-400 hover:underline">Consultant Profile</a>{" "}
            and fill in your details, then Save.
          </li>
          <li>Upload a resume (PDF / DOC / DOCX / TXT, max 10&nbsp;MB).</li>
          <li>
            The upload is stored and a background job parses it into structured skills &amp;
            experience. Watch the worker logs or the{" "}
            <a href="http://localhost:5090/hangfire" className="text-brand-400 hover:underline">
              Hangfire dashboard
            </a>.
          </li>
          <li>
            The <strong>Resumes</strong> count in the schema tile above ticks up as you upload.
          </li>
        </ol>
      </section>
    </div>
  );
}

function Card({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <article className="rounded-xl border border-ink-700 bg-ink-900 p-5">
      <p className="text-xs text-slate-400">{label}</p>
      <p className="mt-1 text-3xl font-semibold tabular-nums">{value}</p>
      <p className="mt-1 text-[11px] text-slate-500">{hint}</p>
    </article>
  );
}
