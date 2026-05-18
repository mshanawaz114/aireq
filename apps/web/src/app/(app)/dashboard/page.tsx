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
          You&rsquo;re looking at <code className="rounded bg-ink-800 px-1.5 py-0.5">AIRMVP1-101</code> —
          the freshly-scaffolded shell. The dashboard, profile, matches, tailor, submissions,
          inbox, and escalations pages fill in across the rest of week&nbsp;1
          (<code className="rounded bg-ink-800 px-1.5 py-0.5">AIRMVP1-102…107</code>).
        </p>
        <p className="mt-2 max-w-prose text-sm text-slate-400">
          The badge in the top-right polls the API every 15 seconds and shows live status of the
          backend plus its Neon Postgres connection. If it&rsquo;s green, your local stack is healthy.
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
          Next steps
        </h2>
        <ol className="mt-3 list-decimal space-y-1.5 pl-5 text-sm text-slate-300">
          <li>
            Open{" "}
            <a
              href="https://github.com/mshanawaz114/aireq/blob/main/SETUP_CLOUD.md"
              className="text-brand-400 hover:underline"
            >
              SETUP_CLOUD.md
            </a>{" "}
            and create your Neon project + Anthropic key.
          </li>
          <li>
            Copy <code className="rounded bg-ink-800 px-1.5 py-0.5">.env.example</code> to{" "}
            <code className="rounded bg-ink-800 px-1.5 py-0.5">.env.local</code> at the repo root and
            paste in the connection strings.
          </li>
          <li>
            Run <code className="rounded bg-ink-800 px-1.5 py-0.5">make dev</code> to boot all three
            processes (api, worker, web).
          </li>
          <li>The badge above turns green. You&rsquo;re ready for AIRMVP1-102.</li>
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
