import { Sidebar } from "@/components/sidebar";
import { HealthBadge } from "@/components/health-badge";

export default function AppLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <div className="flex flex-1 flex-col">
        <header
          role="banner"
          className="flex items-center justify-between border-b border-ink-800 bg-ink-900/40 px-8 py-3"
        >
          <div className="text-xs text-slate-500">
            Aireq · pre-alpha — story{" "}
            <a
              href="https://github.com/mshanawaz114/aireq/blob/main/PLAN.md#airmvp1-101"
              className="text-brand-400 hover:underline"
            >
              AIRMVP1-101
            </a>
          </div>
          <HealthBadge />
        </header>
        <main id="main" tabIndex={-1} className="flex-1 outline-none">
          {children}
        </main>
      </div>
    </div>
  );
}
